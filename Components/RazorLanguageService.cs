using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Mvc;
using System.Web.Razor;
using System.Web.Razor.Parser;
using System.Web.Razor.Parser.SyntaxTree;

namespace Dnn.Modules.RazorMvcHost.Components
{
    /// <summary>
    /// Tiny "language service" for ASP.NET Razor (.cshtml) used by the
    /// in-browser Monaco editor on the module's Edit screen. We deliberately
    /// only use what already ships in DNN's bin folder:
    ///
    ///   * <c>System.Web.Razor.dll</c>          -> structural Razor parsing
    ///   * <c>System.Web.Mvc.dll</c>            -> reflection over Html / Url helpers
    ///   * <c>System.Web.WebPages.dll</c>       -> reflection over WebViewPage members
    ///
    /// No Roslyn / no rzls / no extra installs. The result is:
    ///
    ///   * Diagnostics for malformed Razor (unbalanced @{ }, bad directives,
    ///     dangling expression terminators, etc.) - everything the Razor
    ///     parser itself complains about.
    ///   * A static completion catalog: Razor directives + common MVC helpers
    ///     reflected once and cached.
    ///
    /// What we explicitly DON'T provide (would need Roslyn): semantic C#
    /// completions for arbitrary expressions like @Model.PropertyName.
    /// </summary>
    public static class RazorLanguageService
    {
        // ----- Diagnostics -----------------------------------------------

        /// <summary>
        /// Parses <paramref name="cshtmlText"/> through the same Razor 3
        /// parser ASP.NET MVC 5 uses and converts every <see cref="RazorError"/>
        /// into a Monaco-friendly <see cref="DiagnosticItem"/>.
        /// Lines / columns are 1-based to match Monaco.
        /// </summary>
        public static List<DiagnosticItem> Parse(string cshtmlText)
        {
            var items = new List<DiagnosticItem>();
            if (string.IsNullOrEmpty(cshtmlText)) return items;

            try
            {
                var host = new RazorEngineHost(new CSharpRazorCodeLanguage())
                {
                    DefaultBaseClass = "System.Web.Mvc.WebViewPage",
                };

                var engine = new RazorTemplateEngine(host);
                GeneratorResults gen;
                using (var reader = new StringReader(cshtmlText))
                {
                    gen = engine.GenerateCode(reader);
                }

                if (gen != null && gen.ParserErrors != null)
                {
                    foreach (var err in gen.ParserErrors)
                    {
                        items.Add(ToDiagnostic(err, cshtmlText));
                    }
                }
            }
            catch (Exception ex)
            {
                // Never let a parser failure surface as a 500 to the editor;
                // turn it into a single info marker instead.
                items.Add(new DiagnosticItem
                {
                    Line = 1,
                    Col = 1,
                    EndLine = 1,
                    EndCol = 2,
                    Severity = "info",
                    Message = "Razor parser threw: " + ex.Message
                });
            }

            // Plus a few cheap structural checks the Razor parser doesn't
            // always report explicitly (paired-brace count is the most useful
            // smoke test for "I forgot a closing }").
            AddBraceBalanceCheck(cshtmlText, items);

            return items;
        }

        private static DiagnosticItem ToDiagnostic(RazorError err, string text)
        {
            // RazorError.Location is 0-based; Monaco wants 1-based.
            var line = (err.Location.LineIndex < 0 ? 0 : err.Location.LineIndex) + 1;
            var col  = (err.Location.CharacterIndex < 0 ? 0 : err.Location.CharacterIndex) + 1;
            var len  = err.Length > 0 ? err.Length : 1;

            // Compute end position by walking <len> characters from the absolute
            // index, honoring newlines so multi-line errors get the right span.
            var abs  = err.Location.AbsoluteIndex;
            var endLine = line;
            var endCol  = col + len;
            if (abs >= 0 && abs + len <= text.Length)
            {
                int l = line, c = col;
                for (int i = 0; i < len; i++)
                {
                    var ch = text[abs + i];
                    if (ch == '\n') { l++; c = 1; }
                    else            { c++;        }
                }
                endLine = l;
                endCol  = c;
            }

            return new DiagnosticItem
            {
                Line = line,
                Col = col,
                EndLine = endLine,
                EndCol = endCol,
                Severity = "error",
                Message = err.Message
            };
        }

        private static void AddBraceBalanceCheck(string text, List<DiagnosticItem> items)
        {
            int open = 0, close = 0;
            bool inString = false;
            char stringQuote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (inString)
                {
                    if (ch == '\\' && i + 1 < text.Length) { i++; continue; }
                    if (ch == stringQuote) inString = false;
                    continue;
                }
                if (ch == '"' || ch == '\'') { inString = true; stringQuote = ch; continue; }
                if (ch == '{') open++;
                else if (ch == '}') close++;
            }

            if (open != close)
            {
                items.Add(new DiagnosticItem
                {
                    Line = 1, Col = 1, EndLine = 1, EndCol = 2,
                    Severity = "warning",
                    Message = "Unbalanced braces: " + open + " '{' vs " + close + " '}'."
                });
            }
        }

        // ----- Completions ----------------------------------------------

        private static List<CompletionItem> _cache;
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Returns the cached completion catalog. Built once on first call.
        /// </summary>
        public static List<CompletionItem> GetCompletions()
        {
            if (_cache != null) return _cache;
            lock (_cacheLock)
            {
                if (_cache != null) return _cache;
                _cache = BuildCompletions();
                return _cache;
            }
        }

        private static List<CompletionItem> BuildCompletions()
        {
            var list = new List<CompletionItem>();

            // --- Razor directives & control keywords --------------------
            AddSnippet(list, "model",     "@model ${1:Type}",                              "Razor @model directive");
            AddSnippet(list, "using",     "@using ${1:Namespace}",                         "Razor @using directive");
            AddSnippet(list, "inherits",  "@inherits ${1:BaseType}",                       "Razor @inherits directive");
            AddSnippet(list, "functions", "@functions {\n\t$0\n}",                         "Razor @functions block");
            AddSnippet(list, "section",   "@section ${1:Name} {\n\t$0\n}",                 "Razor @section block");
            AddSnippet(list, "helper",    "@helper ${1:Name}(${2:args}) {\n\t$0\n}",       "Razor @helper block");
            AddSnippet(list, "if",        "@if (${1:condition})\n{\n\t$0\n}",              "Razor @if block");
            AddSnippet(list, "for",       "@for (int ${1:i} = 0; ${1:i} < ${2:n}; ${1:i}++)\n{\n\t$0\n}", "Razor @for loop");
            AddSnippet(list, "foreach",   "@foreach (var ${1:item} in ${2:items})\n{\n\t$0\n}",          "Razor @foreach loop");
            AddSnippet(list, "while",     "@while (${1:condition})\n{\n\t$0\n}",           "Razor @while loop");
            AddSnippet(list, "switch",    "@switch (${1:expr})\n{\n\tcase ${2:value}:\n\t\t$0\n\t\tbreak;\n}", "Razor @switch block");
            AddSnippet(list, "try",       "@try\n{\n\t$0\n}\ncatch (${1:Exception} ${2:ex})\n{\n}",     "Razor @try/catch");
            AddSnippet(list, "do",        "@do\n{\n\t$0\n} while (${1:condition});",      "Razor @do/while");
            AddSnippet(list, "lock",      "@lock (${1:obj})\n{\n\t$0\n}",                  "Razor @lock block");
            AddSnippet(list, "using-block","@using (${1:Html.BeginForm()})\n{\n\t$0\n}",   "Razor @using statement (disposable)");
            AddSnippet(list, "razor-comment", "@* $0 *@",                                  "Razor comment");
            AddSnippet(list, "line",      "@: $0",                                          "Razor single-line text marker");

            // --- Implicit page members (DnnWebViewPage / WebViewPage) ----
            AddText(list, "Model",      "Razor @Model implicit property");
            AddText(list, "ViewBag",    "Razor @ViewBag implicit property");
            AddText(list, "ViewData",   "Razor @ViewData implicit property");
            AddText(list, "Html",       "HtmlHelper instance");
            AddText(list, "Url",        "UrlHelper instance");
            AddText(list, "Layout",     "Razor layout path property");
            AddText(list, "Context",    "Current HttpContext");
            AddText(list, "Request",    "Current HttpRequest");
            AddText(list, "Response",   "Current HttpResponse");
            AddText(list, "Server",     "Current HttpServerUtility");
            AddText(list, "Session",    "Current HttpSessionState");
            AddText(list, "User",       "Current IPrincipal");
            AddText(list, "TempData",   "Temp data dictionary");

            // --- Reflected MVC helpers ----------------------------------
            ReflectStaticAndInstanceMethods(list, typeof(HtmlHelper),       "Html.",  isStatic: false);
            ReflectStaticAndInstanceMethods(list, typeof(UrlHelper),        "Url.",   isStatic: false);

            // System.Web.Mvc.Html.* extension types (LinkExtensions, FormExtensions, etc.)
            try
            {
                var mvcAsm = typeof(HtmlHelper).Assembly;
                foreach (var t in mvcAsm.GetTypes()
                                        .Where(t => t.IsPublic && t.IsAbstract && t.IsSealed
                                                    && t.Namespace == "System.Web.Mvc.Html"))
                {
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (!m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)) continue;
                        var ps = m.GetParameters();
                        if (ps.Length == 0) continue;
                        var first = ps[0].ParameterType;
                        if (!typeof(HtmlHelper).IsAssignableFrom(first) && !first.IsGenericType) continue;
                        AddMethod(list, "Html." + m.Name, m, t.Name);
                    }
                }
            }
            catch { /* never let reflection problems break completions */ }

            // De-duplicate by label, keep the first occurrence.
            return list
                .GroupBy(c => c.Label, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void ReflectStaticAndInstanceMethods(List<CompletionItem> list, Type type, string prefix, bool isStatic)
        {
            try
            {
                var flags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
                foreach (var m in type.GetMethods(flags).Where(m => !m.IsSpecialName))
                {
                    AddMethod(list, prefix + m.Name, m, type.Name);
                }
                foreach (var p in type.GetProperties(flags))
                {
                    list.Add(new CompletionItem
                    {
                        Label = prefix + p.Name,
                        Kind = "property",
                        Detail = type.Name + "." + p.Name,
                        InsertText = prefix + p.Name
                    });
                }
            }
            catch { /* swallow */ }
        }

        private static void AddMethod(List<CompletionItem> list, string label, MethodInfo m, string declaringName)
        {
            var ps = m.GetParameters();
            // Skip the implicit "this HtmlHelper" parameter for extension methods.
            int skip = m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false) ? 1 : 0;
            var args = string.Join(", ",
                ps.Skip(skip).Select((p, i) => "${" + (i + 1) + ":" + p.Name + "}"));

            var sig = string.Join(", ",
                ps.Skip(skip).Select(p => FriendlyTypeName(p.ParameterType) + " " + p.Name));

            list.Add(new CompletionItem
            {
                Label = label,
                Kind = "method",
                Detail = declaringName + "." + m.Name + "(" + sig + ")",
                InsertText = label + "(" + args + ")",
                IsSnippet = true
            });
        }

        private static void AddSnippet(List<CompletionItem> list, string label, string body, string detail)
        {
            list.Add(new CompletionItem
            {
                Label = label,
                Kind = "snippet",
                Detail = detail,
                InsertText = body,
                IsSnippet = true
            });
        }

        private static void AddText(List<CompletionItem> list, string label, string detail)
        {
            list.Add(new CompletionItem
            {
                Label = label,
                Kind = "variable",
                Detail = detail,
                InsertText = label
            });
        }

        private static string FriendlyTypeName(Type t)
        {
            if (t == null) return "object";
            if (!t.IsGenericType) return t.Name;
            var defName = t.Name;
            var tick = defName.IndexOf('`');
            if (tick > 0) defName = defName.Substring(0, tick);
            var args = string.Join(", ", t.GetGenericArguments().Select(FriendlyTypeName));
            return defName + "<" + args + ">";
        }
    }

    /// <summary>JSON contract: a single editor diagnostic.</summary>
    public class DiagnosticItem
    {
        public int Line { get; set; }
        public int Col { get; set; }
        public int EndLine { get; set; }
        public int EndCol { get; set; }
        public string Severity { get; set; }   // "error" | "warning" | "info"
        public string Message { get; set; }
    }

    /// <summary>JSON contract: a single completion item.</summary>
    public class CompletionItem
    {
        public string Label { get; set; }
        public string Kind { get; set; }       // "snippet" | "method" | "property" | "variable"
        public string Detail { get; set; }
        public string InsertText { get; set; }
        public bool IsSnippet { get; set; }
    }
}
