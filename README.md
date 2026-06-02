# Dnn.Modules.RazorMvcHost

DotNetNuke 9.x MVC desktop module that hosts arbitrary Razor (`.cshtml`)
view scripts. The MVC counterpart to the legacy "Razor Host" module
(which runs on ASP.NET WebPages under `DesktopModules/RazorModules/RazorHost/`).

- **Package name (manifest):** `Dnn.Modules.RazorMvcHost`
- **Package type:** `Module` (DNN MVC)
- **Assembly / namespace:** `Dnn.Modules.RazorMvcHost`
- **Version:** `01.00.00`
- **DNN core dependency:** `08.00.00+`
- **Target framework:** `.NET Framework 4.8`

---

## How it works

- Drop `.cshtml` files into `Views/Scripts/` (subfolders are allowed).
- After adding the module to a page, click **Edit** to pick which
  script the instance renders. The chosen relative path is stored in
  `ModuleSettings["ScriptPath"]`.
- On render, `ScriptController.Index()` resolves the configured path
  and returns it as the view; the MVC view engine compiles the
  `.cshtml` on the fly. The script has full access to `ViewBag`,
  `ViewData`, partials, layouts and any namespace declared in
  `Views/Web.config`.
- Per-instance setting means **the same module DLL can render N
  different page components on the same site** without rebuilding /
  reinstalling.

`Views/_ViewStart.cshtml` sets `Layout = null` by default, so each
hosted script renders inline inside the DNN skin. A script can opt into
a layout by setting `Layout = "..."` itself.

The **Edit** screen doubles as a small in-browser code editor: it lists
every script under `Views/Scripts/` and lets you edit and save its
content directly to disk, mark a script as the active one for the
module instance, or just save without activating.

---

## Project layout

```
Dnn.Modules.RazorMvcHost/
├── Controllers/
│   └── ScriptController.cs            # Host controller (Index, Edit GET/POST)
├── Components/
│   ├── BusinessController.cs          # DNN lifecycle (IUpgradeable)
│   └── RazorLanguageService.cs        # Helper used by the Edit editor
├── Views/
│   ├── _ViewStart.cshtml              # Layout = null by default
│   ├── Web.config                     # Razor namespaces / web.pages config
│   ├── Script/                        # The host's own MVC views
│   │   ├── Edit.cshtml                # Script picker + in-browser editor
│   │   └── NoScript.cshtml            # Fallback when no script is configured
│   ├── Scripts/                       # Hosted page components live here
│   │   └── Hello.cshtml               # Starter sample
│   └── Shared/                        # Shared partials available to hosted scripts
│       └── README.md                  # How to copy reusable partials in
├── App_LocalResources/
│   ├── Edit.resx
│   └── NoScript.resx
├── Resources/
│   └── css/edit.css                   # Styles for the Edit screen
├── BuildScripts/                      # MSBuild props/targets (install zip)
├── RouteConfig.cs                     # IServiceRouteMapper (Web API plumbing)
├── manifest.dnn                       # DNN install manifest
├── Web.config
└── README.md / ReleaseNotes.txt / License.txt
```

> The `Views/Shared/` folder currently ships empty (just a README).
> Drop reusable partials in there - e.g. copy
> `Dnn.Modules.TableDZP/Views/Shared/_Select.cshtml` - and call them
> from any hosted script via `@Html.Partial("_PartialName", ...)`. The
> README in that folder has the canonical copy-and-go example.

---

## Adding a new hosted page component

1. Create `Views/Scripts/MyThing.cshtml`:

   ```cshtml
   @{
       Layout = null;
   }
   <section class="p-4">
       <h2 class="text-xl font-semibold">My thing</h2>
       <p>Rendered by RazorMvcHost.</p>
   </section>
   ```

2. On a DNN page, add a `RazorMvcHost` module instance and pick
   `MyThing.cshtml` in its **Edit** screen.
3. Reload the page. No rebuild, no re-install.

You can also edit the script's content directly from the Edit screen
and click **Save** - the file is written back to disk under
`Views/Scripts/`.

---

## Security model

`ScriptController` validates the configured script path before resolving
it to a view:

- Path must be **relative** - no `..`, no rooted paths.
- Must end with `.cshtml`.
- Must resolve under the physical `Views/Scripts/` folder.
- Files starting with `_` are excluded from the dropdown (treated as
  partials, not selectable components).

This prevents directory traversal via crafted `ScriptPath` module
settings or hand-edited posts to the Edit form.

---

## Build

This project is built with MSBuild (the targets in `BuildScripts/`
produce the install zip on Release builds).

- **VS Code:** `Ctrl+Shift+B`.
- **Visual Studio:** open `Dnn.Modules.RazorMvcHost.sln` and build.
- **CLI:**
  `MSBuild.exe Dnn.Modules.RazorMvcHost.sln -p:Configuration=Release`
  using the MSBuild shipped with Visual Studio.

Release output:

- `bin/Release/Dnn.Modules.RazorMvcHost.dll`
- `install/Dnn.Modules.RazorMvcHost_01.00.00_Install.zip`
- `install/Dnn.Modules.RazorMvcHost_01.00.00_Source.zip`

See [`BuildScripts/README.md`](BuildScripts/README.md) for the
`DnnBinRoot` setting and packaging detail.

The build also bundles all `.cshtml`, `.css`, `.js`, `.html`, `.resx`
and `.txt` files into `Resources.zip` inside the install package.

---

## Install

1. **Settings -> Extensions -> Install Extension** in DNN.
2. Upload `install/Dnn.Modules.RazorMvcHost_<version>_Install.zip`.
3. The manifest deploys files to
   `/DesktopModules/MVC/Dnn.Modules.RazorMvcHost/`.

---

## When to use this module (and when not to)

Use `RazorMvcHost` when you want:

- A page-level component / widget / mini-form / dashboard tile.
- A view that mostly renders data and posts back through standard MVC
  or fetch/AJAX calls.
- Quick iteration: edit `.cshtml` on the server, reload the page.

Build a dedicated DNN MVC module instead when you need:

- A complex domain with many controllers, services, DI registrations,
  background jobs, scheduled tasks, or a full settings UI.
- Module-level permissions / ModuleSettings beyond what a hosted view
  needs.
- Distribution as an installable package to other DNN sites.

---

## Conventions & pitfalls

| Topic                   | Detail                                                                          |
| ----------------------- | ------------------------------------------------------------------------------- |
| `Layout = null` default | `Views/_ViewStart.cshtml` sets it; hosted scripts opt in explicitly             |
| Namespaces              | Configure in `Views/Web.config` (`<pages><namespaces>`), not per-view           |
| Partials naming         | Files starting with `_` are excluded from the picker - reserve them for partials|
| Not the legacy RazorHost | This is **MVC**, not ASP.NET WebPages under `RazorModules/RazorHost/`          |
| Distribution            | Scripts under `Views/Scripts/` are part of this repo; copy/port to re-use them  |

---

## Deep-dive docs

For the handover-prompt voice and the broader DZP integration notes,
see `c:\DNN\dzp\workspace\docs\Dnn.Modules.RazorMvcHost.md` in the DZP
portal workspace.
