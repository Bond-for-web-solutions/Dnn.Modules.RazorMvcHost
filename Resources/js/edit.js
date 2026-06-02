/*
 * Razor MVC Host - Edit screen client behavior.
 *
 * The page exposes a JSON blob `window.__rmhEdit.contents` (path -> source text)
 * before this file loads. This file:
 *   - switches the textarea + Monaco editor when the user picks a different
 *     script from the dropdown,
 *   - mounts the Monaco editor via the AMD loader emitted by the .cshtml,
 *   - mirrors Monaco's value into the (hidden) textarea on every change and
 *     before form submit so the POST carries the latest source,
 *   - handles the fullscreen toggle button and Escape key,
 *   - updates the line count in the titlebar and the position / selection
 *     items in the status bar.
 *
 * The textarea is the source-of-truth that gets POSTed back. Monaco is purely
 * a presentation layer and falls back to a plain visible textarea if Monaco
 * fails to load (e.g. the CDN is blocked).
 */
(function () {
    var data        = window.__rmhEdit || {};
    var contents    = data.contents || {};
    var monacoBase  = data.monacoBase || 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs';

    var sel      = document.getElementById('scriptPath');
    var ta       = document.getElementById('scriptContent');
    var wrap     = document.querySelector('.rmh-edit__editor-wrap');
    var host     = document.getElementById('rmh-monaco');
    var hintPath = document.querySelector('.rmh-edit__hint-path');
    var editorNm = document.querySelector('.rmh-edit__editor-name');
    var setAct   = document.getElementById('setActive');
    var form     = wrap ? wrap.closest('form') : null;
    var btnFs    = document.querySelector('.rmh-edit__btn-fs');
    var linesEl  = document.querySelector('.rmh-edit__lines');
    var statusEl = document.querySelector('.rmh-edit__status');
    var posEl    = statusEl ? statusEl.querySelector('[data-role="position"]') : null;
    var selEl    = statusEl ? statusEl.querySelector('[data-role="selection"]') : null;
    var currentActive = (form && form.getAttribute('data-current-active')) || '';
    if (!sel || !ta || !wrap) { return; }

    var editor = null;

    function updateLines(text) {
        if (!linesEl) { return; }
        var n = text ? (text.match(/\n/g) || []).length + 1 : 0;
        linesEl.setAttribute('data-lines', String(n));
        linesEl.textContent = n + (n === 1 ? ' line' : ' lines');
    }

    function setEditorValue(v) {
        ta.value = v || '';
        if (editor) { editor.setValue(v || ''); }
        updateLines(v || '');
    }

    function applySelection() {
        var path = sel.value || '';
        if (path && Object.prototype.hasOwnProperty.call(contents, path)) {
            setEditorValue(contents[path] || '');
            wrap.style.display = '';
            if (hintPath) { hintPath.textContent = path; }
            if (editorNm) { editorNm.textContent = path; }
            // "Currently active" reflects the SAVED setting, not the dropdown
            // selection - leave it untouched until the user saves.
            // Set Active = checked only when the picked script IS the currently-active script.
            if (setAct) {
                setAct.checked = (path.toLowerCase() === currentActive.toLowerCase());
            }
            if (editor) { editor.layout(); }
        } else {
            setEditorValue('');
            wrap.style.display = 'none';
            if (editorNm) { editorNm.textContent = ''; }
            if (setAct) { setAct.checked = false; }
        }
    }

    sel.addEventListener('change', applySelection);

    if (form) {
        form.addEventListener('submit', function () {
            if (editor) { ta.value = editor.getValue(); }
        });
    }

    if (btnFs && wrap) {
        btnFs.addEventListener('click', function () {
            wrap.classList.toggle('rmh-edit__editor-wrap--fullscreen');
            if (editor) { setTimeout(function () { editor.layout(); }, 0); }
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && wrap.classList.contains('rmh-edit__editor-wrap--fullscreen')) {
                wrap.classList.remove('rmh-edit__editor-wrap--fullscreen');
                if (editor) { setTimeout(function () { editor.layout(); }, 0); }
            }
        });
    }

    updateLines(ta.value);

    function mountMonaco() {
        if (!host) { return; }

        // Cross-origin web-worker shim. Monaco normally tries to spawn its
        // worker directly from the CDN URL, but browsers (and Edge tracking
        // prevention in particular) block that as a third-party request and
        // the editor then crashes inside editor.main.js with errors like
        // "Cannot read properties of undefined (reading 'get')" because its
        // internal services can't reach the worker. Returning a same-origin
        // blob: URL whose body just importScripts() the CDN worker bypasses
        // the cross-origin worker restriction entirely.
        if (!window.MonacoEnvironment || typeof window.MonacoEnvironment.getWorkerUrl !== 'function') {
            // Worker's baseUrl must be the PARENT of the 'vs/' folder, because
            // worker requests like 'vs/language/html/htmlWorker.js' are joined
            // to it. monacoBase ends in '/vs', so strip that to avoid the
            // doubled '/vs/vs/…' path.
            var monacoParent = monacoBase.replace(/\/vs\/?$/, '') + '/';
            window.MonacoEnvironment = {
                getWorkerUrl: function (_workerId, _label) {
                    var src =
                        'self.MonacoEnvironment = { baseUrl: ' + JSON.stringify(monacoParent) + ' };' +
                        'importScripts(' + JSON.stringify(monacoBase + '/base/worker/workerMain.js') + ');';
                    try {
                        return URL.createObjectURL(new Blob([src], { type: 'application/javascript' }));
                    } catch (e) {
                        return 'data:text/javascript;charset=utf-8,' + encodeURIComponent(src);
                    }
                }
            };
        }

        // DNN admin pages (PersonaBar) load their own RequireJS late, which
        // overwrites window.define/window.require and collides with Monaco's
        // AMD loader (duplicate module definitions, empty editor). The standard
        // Monaco-with-RequireJS coexistence pattern: snapshot the existing
        // globals, hide them so Monaco's loader.js installs cleanly, then
        // restore them once Monaco has finished loading all its modules.
        var prevRequire = window.require;
        var prevDefine  = window.define;
        try { window.require = undefined; } catch (e) {}
        try { window.define  = undefined; } catch (e) {}

        function fallback() {
            // Restore globals; show the plain textarea instead.
            window.require = prevRequire;
            window.define  = prevDefine;
            ta.style.display = 'block';
            if (host) { host.style.display = 'none'; }
        }

        var loaderScript = document.createElement('script');
        loaderScript.src = monacoBase + '/loader.js';
        loaderScript.async = true;
        loaderScript.onload = function () {
            var monacoRequire = window.require;
            if (!monacoRequire || typeof monacoRequire.config !== 'function') {
                fallback();
                return;
            }
            try {
                monacoRequire.config({ paths: { vs: monacoBase } });
                // Two-phase load: first 'vs/editor/editor.main', then every
                // language file razor depends on. Doing both phases inside the
                // Monaco-define window means each language script's define()
                // fires before we restore the page's pre-Monaco define/require
                // globals; otherwise the lazy razor/html/css loads later crash
                // with "define is not a function". (Monaco 0.55.x reorganises
                // these into hash-named lazy bundles and breaks this approach,
                // so we stay on 0.52.x.)
                monacoRequire(['vs/editor/editor.main'], function () {
                    monacoRequire([
                        'vs/basic-languages/monaco.contribution',
                        'vs/language/html/monaco.contribution',
                        'vs/basic-languages/razor/razor',
                        'vs/basic-languages/html/html',
                        'vs/basic-languages/css/css',
                        'vs/basic-languages/javascript/javascript',
                        'vs/language/html/htmlMode'
                    ], function () {
                        // Restore the pre-Monaco AMD globals so DNN's RequireJS
                        // (and anything else on the page) keeps working.
                        window.require = prevRequire;
                        window.define  = prevDefine;

                        editor = monaco.editor.create(host, {
                            value: ta.value,
                            language: 'razor',
                            theme: 'vs',
                            automaticLayout: true,
                            minimap: { enabled: false },
                            fontSize: 13,
                            tabSize: 4,
                            wordWrap: 'off',
                            scrollBeyondLastLine: false
                        });
                        editor.onDidChangeModelContent(function () {
                            ta.value = editor.getValue();
                            updateLines(ta.value);
                        });
                        editor.onDidChangeCursorPosition(function (e) {
                            if (posEl) { posEl.textContent = 'Ln ' + e.position.lineNumber + ', Col ' + e.position.column; }
                        });
                        editor.onDidChangeCursorSelection(function (e) {
                            if (!selEl) { return; }
                            var s = e.selection;
                            if (s.isEmpty()) { selEl.textContent = ''; return; }
                            var txt = editor.getModel().getValueInRange(s);
                            var chars = txt.length;
                            var lines = (txt.match(/\n/g) || []).length + 1;
                            selEl.textContent = '(' + chars + ' selected' + (lines > 1 ? ', ' + lines + ' lines' : '') + ')';
                        });
                    });
                });
            } catch (e) {
                fallback();
            }
        };
        loaderScript.onerror = fallback;
        document.head.appendChild(loaderScript);
    }
    mountMonaco();
})();
