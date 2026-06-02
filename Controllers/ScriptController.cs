using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Hosting;
using System.Web.Mvc;
using DotNetNuke.Entities.Modules;
using DotNetNuke.Web.Mvc.Framework.ActionFilters;
using DotNetNuke.Web.Mvc.Framework.Controllers;

namespace Dnn.Modules.RazorMvcHost.Controllers
{
    /// <summary>
    /// Hosts an arbitrary Razor (.cshtml) MVC view located under
    /// /DesktopModules/MVC/Dnn.Modules.RazorMvcHost/Views/Scripts/.
    /// Each module instance picks one script via the Edit screen; the
    /// chosen relative path is stored in module settings ("ScriptPath").
    /// </summary>
    [DnnHandleError]
    public class ScriptController : DnnController
    {
        private const string SettingKey = "ScriptPath";
        private const string ScriptsVirtualRoot = "~/DesktopModules/MVC/Dnn.Modules.RazorMvcHost/Views/Scripts";

        [ModuleAction(ControlKey = "Edit", Title = "Pick Script")]
        public ActionResult Index()
        {
            var scriptPath = GetConfiguredScriptPath();

            if (string.IsNullOrEmpty(scriptPath))
            {
                ViewBag.Message = "No script selected. Open the module's Edit screen and choose a Razor script to render.";
                return View("NoScript");
            }

            if (!IsValidScript(scriptPath))
            {
                ViewBag.Message = "Configured script '" + scriptPath + "' was not found under " + ScriptsVirtualRoot + ".";
                return View("NoScript");
            }

            // Render the chosen .cshtml file directly. The view path is virtual
            // so the MVC view engine will resolve and compile it.
            return View(ScriptsVirtualRoot + "/" + scriptPath);
        }

        [HttpGet]
        public ActionResult Edit()
        {
            var selected  = GetConfiguredScriptPath() ?? string.Empty;
            var available = ListAvailableScripts();

            var allContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in available)
            {
                allContents[p] = ReadScriptContent(p);
            }

            ViewBag.AvailableScripts = available;
            ViewBag.Selected = selected;
            ViewBag.ScriptContent = ReadScriptContent(selected);
            ViewBag.AllContents = allContents;
            return View();
        }

        [HttpPost]
        public ActionResult Edit(string scriptPath, string scriptContent, string submitAction, string setActive)
        {
            // Bail out without writing anything if the user just hit "Return".
            if (string.Equals(submitAction, "return", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToDefaultRoute();
            }

            scriptPath = (scriptPath ?? string.Empty).Trim().Replace("\\", "/");
            var currentActive = GetConfiguredScriptPath() ?? string.Empty;

            if (scriptPath.Length > 0 && !IsValidScript(scriptPath))
            {
                ModelState.AddModelError("scriptPath", "Selected script is not available.");
                ViewBag.AvailableScripts = ListAvailableScripts();
                ViewBag.Selected = scriptPath;
                ViewBag.ScriptContent = scriptContent ?? string.Empty;
                return View();
            }

            // Save edited script content back to disk if both a valid path and content were posted.
            if (scriptPath.Length > 0 && scriptContent != null)
            {
                TryWriteScriptContent(scriptPath, scriptContent);
            }

            // Decide what to do with the module's "active script" setting:
            //   * checkbox checked   => make the dropdown selection the active script.
            //   * checkbox unchecked AND the dropdown selection IS the current active
            //                         => user explicitly turned the active one off; clear it.
            //   * checkbox unchecked AND the dropdown selection is something else
            //                         => user is just editing a non-active file; leave active alone.
            var isActive = !string.IsNullOrEmpty(setActive);
            if (isActive)
            {
                ModuleController.Instance.UpdateModuleSetting(ModuleContext.ModuleId, SettingKey, scriptPath);
            }
            else if (string.Equals(scriptPath, currentActive, StringComparison.OrdinalIgnoreCase))
            {
                ModuleController.Instance.UpdateModuleSetting(ModuleContext.ModuleId, SettingKey, string.Empty);
            }

            // "save" => stay on the Edit screen (re-GET via the same URL).
            // anything else ("saveReturn" or null) => leave back to the module's default view.
            if (string.Equals(submitAction, "save", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect(Request.RawUrl);
            }
            return RedirectToDefaultRoute();
        }

        // ---------- helpers ----------

        private string GetConfiguredScriptPath()
        {
            if (ModuleContext == null || ModuleContext.Settings == null) return null;
            var raw = ModuleContext.Settings[SettingKey] as string;
            return string.IsNullOrEmpty(raw) ? null : raw.Trim().Replace("\\", "/");
        }

        private static bool IsValidScript(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;
            if (relativePath.Contains("..")) return false;
            if (Path.IsPathRooted(relativePath)) return false;
            if (!relativePath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)) return false;

            var rootPhysical = HostingEnvironment.MapPath(ScriptsVirtualRoot);
            if (string.IsNullOrEmpty(rootPhysical) || !Directory.Exists(rootPhysical)) return false;

            var candidate = Path.GetFullPath(Path.Combine(rootPhysical, relativePath));
            var rootFull  = Path.GetFullPath(rootPhysical).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candidate);
        }

        private static string ReadScriptContent(string relativePath)
        {
            if (!IsValidScript(relativePath)) return string.Empty;
            var rootPhysical = HostingEnvironment.MapPath(ScriptsVirtualRoot);
            var fullPath = Path.GetFullPath(Path.Combine(rootPhysical, relativePath));
            try { return System.IO.File.ReadAllText(fullPath); }
            catch { return string.Empty; }
        }

        private static bool TryWriteScriptContent(string relativePath, string content)
        {
            if (!IsValidScript(relativePath)) return false;
            var rootPhysical = HostingEnvironment.MapPath(ScriptsVirtualRoot);
            var fullPath = Path.GetFullPath(Path.Combine(rootPhysical, relativePath));
            try { System.IO.File.WriteAllText(fullPath, content); return true; }
            catch { return false; }
        }

        private static List<string> ListAvailableScripts()
        {
            var rootPhysical = HostingEnvironment.MapPath(ScriptsVirtualRoot);
            if (string.IsNullOrEmpty(rootPhysical) || !Directory.Exists(rootPhysical))
            {
                return new List<string>();
            }

            var rootLen = rootPhysical.TrimEnd(Path.DirectorySeparatorChar).Length + 1;
            return Directory
                .EnumerateFiles(rootPhysical, "*.cshtml", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith("_"))
                .Select(f => f.Substring(rootLen).Replace("\\", "/"))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
