# Build Scripts

This folder contains MSBuild configuration files used to build and package the DNN Newsletters module.

## Files

| File | Description |
|------|-------------|
| `BuildProperties.props` | Build properties (DNN bin path; Visual Studio version/path are auto-detected) |
| `ModulePackage.targets` | MSBuild targets for creating DNN install and source packages |
| `MSBuild.Community.Tasks.Targets` | Community tasks used by the packaging targets (Zip, XmlRead, etc.) |

## Configuration

Usually you only need to change `DnnBinRoot` in `BuildProperties.props`:

```xml
<!-- Path to your DNN installation's bin folder -->
<DnnBinRoot>C:\DNN\<Project.Name>\bin\</DnnBinRoot>
```

`VisualStudioVersion` and `VSToolsPath` are auto-detected from the running MSBuild engine
(works with Community/Pro/Enterprise/Build Tools, any version). You can still override them
via env var or `-p:` on the command line if needed.

## How to Build

### VS Code

Press `Ctrl+Shift+B` to run the default build task. This is configured in `.vscode/tasks.json` and runs MSBuild with `Release` configuration.

### Visual Studio

Open `<Project.Name>.sln` and build with `Ctrl+Shift+B` or **Build > Build Solution**.

### Command Line

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Dnn.Modules.RazorMvcHost.sln -p:Configuration=Release
```

## Packaging

When building in **Release** mode, the `ModulePackage.targets` automatically creates two DNN install packages in the `install/` folder:

- `<Project.Name>_<version>_Install.zip` - Install package (views, CSS, JS, resources, DLL, SQL providers, manifest)
- `<Project.Name>_<version>_Source.zip` - Source package (everything above plus source code, csproj, sln)

These zip files can be installed via the DNN **Extensions > Install Extension** page.
