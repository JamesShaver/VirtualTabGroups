# Contributing to Virtual Tab Groups

Thanks for considering a contribution. This file orients you to the codebase so you can get from `git clone` to a useful change without surprises.

## Project structure

```
VirtualTabGroups.sln              ← Solution file
VirtualTabGroups/                 ← Shipping plugin (the only project users install)
  Core/                           ← Pure C# library, no Notepad++ dependency
  Plugin/                         ← Notepad++ integration: entry points, UI, interop
  Plugin/Npp/                     ← Vendored Notepad++ interop types
  Plugin/UI/                      ← WinForms custom controls + theme manager
  Plugin/Resources/               ← Embedded resources (plugin icon)
  Properties/                     ← AssemblyInfo, etc.
  packages.config                 ← Legacy-style NuGet manifest
  VirtualTabGroups.csproj
tests/
  VirtualTabGroups.Tests/         ← Unit tests — never shipped
    *Tests.cs
    Fixtures/                     ← Known-good JSON used by StateStore tests
    VirtualTabGroups.Tests.csproj ← SDK-style csproj, references the main project
.github/workflows/                ← CI: validate.yml (PR/main) + release.yml (tag v*)
docs/                             ← (none in repo — see README)
README.md
CONTRIBUTING.md                   ← (this file)
LICENSE
```

**Only the `VirtualTabGroups/` project ships.** Users get `VirtualTabGroups.dll` + `Newtonsoft.Json.dll` in the release zip — nothing from `tests/` or build artifacts.

## Building locally

### Prerequisites

- Windows 10 or 11
- Visual Studio 2022 Build Tools (or full IDE) with .NET Framework 4.8 SDK + targeting pack
- NuGet on PATH (or use the `nuget.exe` checked into the repo root)

### Build commands

```powershell
# Restore packages first time (or after pulling someone else's package changes)
nuget restore VirtualTabGroups.sln

# Build the plugin (Release x64 is the standard test target)
msbuild VirtualTabGroups.sln /p:Configuration=Release /p:Platform=x64

# Run the test suite
dotnet test tests/VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj --nologo

# Deploy locally for manual testing (elevated PowerShell)
Copy-Item -Force "VirtualTabGroups\bin\Release\x64\VirtualTabGroups.dll" "C:\Program Files\Notepad++\plugins\VirtualTabGroups\"
Copy-Item -Force "VirtualTabGroups\bin\Release\x64\Newtonsoft.Json.dll" "C:\Program Files\Notepad++\plugins\VirtualTabGroups\"
```

## What the test project covers (and what it doesn't)

`tests/VirtualTabGroups.Tests/` covers the **model layer** — code under `VirtualTabGroups/Core/` plus a couple of test seams in `Plugin/` (the observer dedup logic, the theme manager). Specifically:

| Test file | What it verifies |
|---|---|
| `AliasResolverTests` | Display-name aliasing (`User.php`, `User(1).php`, sparse-fill, no-extension files, multi-dot names) |
| `NodeJsonConverterTests` | JSON round-trip, polymorphic folder/file discriminator, unknown-field forward compat, error paths |
| `StateStoreTests` | Load (missing/empty/valid/corrupt/future-schema), save (debounced atomic write, failure observer notification, dirty-state preservation, shutdown semantics, observer-throws isolation) |
| `TreeMutatorTests` | Add with alias collision, Remove, Move with cyclic prevention, RemoveAllByPath case-insensitive |
| `NotepadPlusPlusObserverTests` | Save-failure dedup, MessageBox routing via `IMessageBoxProxy` seam |
| `ThemeManagerTests` | Dark vs light palette resolution via `INppMessageSender` fake |

Tests run against a **temp directory** (no shared state) and a couple of fake interfaces (`IMessageBoxProxy`, `INppMessageSender`) so they never touch real Notepad++ or pop dialogs.

**What's NOT covered by automated tests** — anything that needs a running Notepad++:
- The dockable panel rendering and docking behavior
- Custom owner-draw output (chevrons, connector lines, insertion lines, theming)
- Drag-and-drop interaction
- Context menu structure
- Keyboard shortcuts
- File-close auto-removal (the `NPPN_FILEBEFORECLOSE` plumbing)
- Double-click open via `NPPM_DOOPEN` / `NPPM_SWITCHTOFILE`

These get **manual smoke-tested** inside Notepad++. The plugin writes a diagnostic log (`%appdata%\Notepad++\plugins\config\VirtualTabGroups\plugin.log`) that breadcrumbs every notable action, so reproducing a bug usually means doing the action and checking the log.

## Filing a bug

A good bug report has three things:

1. **What you did** — the steps to reproduce, in order.
2. **What happened vs. what you expected.**
3. **The relevant section of `plugin.log`** — the file lives at `%appdata%\Notepad++\plugins\config\VirtualTabGroups\plugin.log`. Paste the last few entries that bracket the failure.

With those three, most bugs become a one-round fix.

## Pull requests

- Run `dotnet test` locally before opening the PR. The suite is fast (~1 second) and catches most regressions in the model layer.
- Keep PRs focused on one logical change. A "fix bug X + refactor Y + add feature Z" PR is harder to review than three separate ones.
- For changes to `Core/`, add or update a test alongside the change. The test suite is your safety net during the next refactor.
- For UI changes, include before/after notes in the PR description (a screenshot is great if relevant). UI bugs that pass code review but fail in Notepad++ are common; the PR description should help the reviewer mentally simulate the change.
- `[DllExport]`-decorated methods in `PluginMain` are the unmanaged interface Notepad++ calls. **Don't rename them** (the export names are referenced by Notepad++) and **don't add untyped exceptions to their signatures** — any uncaught exception there crashes the host. Wrap their bodies in try/catch and route through `ReportCrash`.

## License

By contributing, you agree your contribution is licensed under the [MIT License](LICENSE) — same as the rest of the project.
