# Virtual Tab Groups for Notepad++

> A logical workspace manager that lets you organize open files into virtual folders that persist across sessions.

Virtual Tab Groups adds a dockable panel to Notepad++ where you curate a hierarchical view of *your* open documents — grouped however your brain wants them grouped, not how the filesystem happens to be organized. Working on a feature that touches an auth module, a couple of view templates, a migration, and some test fixtures? Drag them into an "Auth Feature" folder in the panel. Restart Notepad++ a week later — they're still there, ready to double-click open.

The panel is **purely virtual**. It does not scan your disk, mirror folders, or watch the filesystem. It only knows about files *you* add to it.

---

## Table of contents

- [Features](#features)
- [Install](#install)
- [Using the plugin](#using-the-plugin)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Data storage](#data-storage)
- [Dark mode](#dark-mode)
- [Building from source](#building-from-source)
- [Architecture overview](#architecture-overview)
- [Roadmap](#roadmap)
- [Troubleshooting](#troubleshooting)
- [Contributing](#contributing)
- [License](#license)

---

## Features

### Organization
- **Hierarchical virtual folders.** Nest folders as deep as you like. Drag files (or whole folders) between them.
- **Multi-select drag-and-drop.** `Ctrl+click` to pick several files, `Shift+click` to extend a range, then drag the whole set in one motion.
- **Smart drop targeting.** Hover near the top or bottom edge of a node to insert above/below as a sibling; hover the middle of a folder to drop into it. A live insertion-line shows exactly where the drop will land.
- **Auto-expand on hover.** Dragging over a collapsed folder for ~500ms expands it so you can drop into descendants.
- **Auto-scroll near edges.** Drag near the top or bottom of the panel and the view scrolls to reveal more nodes.
- **Cyclic-drop prevention.** You can't drop a folder into itself or any of its descendants (it would create a loop in the tree).
- **Aliasing for same-named files.** Add two different `User.php` files to the same folder and the second becomes `User(1).php` in the display label — actual disk files are untouched. The aliases re-resolve automatically when you drag across folders.

### File operations
- **Add Active File** — adds the currently focused Notepad++ document to a folder.
- **Add All Open Files** — bulk-adds every open document in one click.
- **New Folder** — creates an empty group with immediate in-place rename.
- **Rename** — `F2` or context menu, edits the *display label* only; the actual file on disk is never renamed.
- **Remove** — removes the entry from the panel; **does not delete the file on disk**. Folder removals prompt for confirmation when non-empty.
- **Open / activate** — double-click a file (or press `Enter` on it) to switch to its tab in Notepad++. If the file isn't currently open, it's reopened from disk. If it was an unsaved scratch buffer that's since been closed, you get a friendly "buffer no longer open" message instead of trying to create a file at a path that doesn't exist.
- **Reveal in Explorer** — right-click a file → opens Windows Explorer at the file's parent folder with the file highlighted. Disabled for unsaved buffers (nothing to reveal).
- **Copy full path** — right-click a file → puts the file's absolute path on the clipboard. Disabled for unsaved buffers.
- **Expand All / Collapse All** — context menu actions for any folder, applies recursively to its subtree.

### Persistence
- **State survives restarts.** Your virtual tree (folders, files, expanded state, last selection) is saved to a JSON file in Notepad++'s plugin config directory and loaded again on next launch.
- **Auto-removal when files close.** Closing a file via Notepad++'s tab `X` removes it from any virtual folder it was in. Saves you from accumulating stale references.
- **Unsaved buffers are session-only.** You can absolutely add `new 17` or any other unsaved scratch buffer to a folder — useful for organizing work-in-progress alongside saved files. On Notepad++ restart, references to unsaved buffers are silently cleaned up (their data didn't survive shutdown, so the references would be dead anyway).
- **Corrupt-state recovery.** If something hand-edits or corrupts the state file, the plugin backs it up to `state.json.corrupt-yyyyMMdd-HHmmssZ-<random>` and starts with a clean tree rather than crashing. A dialog tells you where the backup landed.
- **Future-version safety.** If a future version of the plugin writes a newer schema, an older plugin reading that file enters read-only mode for the session rather than overwriting newer data.

### Visual polish
- **Tier-3 dark mode integration.** When Notepad++'s dark mode is on, the panel matches it pixel-for-pixel — including custom-drawn expand chevrons, dotted connector lines between siblings, themed context-menu chrome, and a dark-themed tooltip. The transition is instant when you toggle Notepad++'s dark mode at runtime.
- **Per-extension shell icons.** File nodes show whatever icon Windows associates with the file's extension — same icons you see in Explorer. Folders use the standard yellow folder icons.
- **Empty-state hint.** When the panel is empty, a centered hint reads *"Right-click here to add open files"* so new users have a starting point.
- **Tooltips with absolute paths.** Hover any file node to see its full path in a theme-aware tooltip.
- **Smooth drag rendering.** Double-buffered TreeView with cached GDI objects and regional invalidates — no flicker or stutter even when you're dragging through a long list.
- **Tab icon.** The panel shows a 16×16 icon next to its name in the Notepad++ docking tab bar, consistent with built-in panels.

### Stability
- **No-crash crash guards.** Every UI callback (menu actions, drag handlers, paint cycles, timer ticks, plugin entry points) is wrapped in exception isolation. A bug in plugin code surfaces as a MessageBox you can read, not a host-process crash that loses your work.
- **File-based crash log.** `plugin.log` in the plugin config directory records every notable event — initialization, lifecycle hooks, exceptions with stack traces, and breadcrumbs through user actions. If something does go wrong, the log usually pinpoints the failure in one read.
- **Save-failure deduplication.** If a disk-write fails, you get one informative dialog — not one per debounced save while the issue persists.

---

## Install

### From a release zip

1. Download `VirtualTabGroups_x64.zip` from the [Releases](https://github.com/JamesShaver/VirtualTabGroups/releases) page (use `_x86` if you're running 32-bit Notepad++).
2. Extract into `C:\Program Files\Notepad++\plugins\VirtualTabGroups\` (create the folder if needed). The directory should contain:
   ```
   VirtualTabGroups.dll
   Newtonsoft.Json.dll
   ```
3. Restart Notepad++.
4. Open the panel via *Plugins → Virtual Tab Groups → Show Virtual Tab Groups* (or `Ctrl+Shift+T` if you haven't rebound it).

### From source

See [Building from source](#building-from-source).

---

## Using the plugin

### Open the panel

*Plugins → Virtual Tab Groups → Show Virtual Tab Groups* — it docks to the left pane by default. Drag the title bar to float it, dock it to another edge, or attach it as a tab to an existing docked panel group. Notepad++ remembers the position across launches.

### Add files

Open a few documents in Notepad++. Right-click anywhere in the empty panel (or on a folder) and pick *Add Active File* — the currently focused tab appears in the panel. Or *Add All Open Files* to bring everything across at once. Repeat for the docs you want to group.

### Create folders

Right-click → *New Folder* (or `Ctrl+N`). A folder with the placeholder name *"New Folder"* appears with the rename cursor active — type a name, press Enter.

### Move things around

Drag files or folders between folders. Multi-select with `Ctrl+click` to pick several at once, then drag any one of them to move the whole set together. Folders move with all their descendants intact.

### Rename and remove

`F2` or right-click → *Rename* to edit a folder name or a file's display label. The actual file on disk is never renamed — the panel is purely a virtual organization layer.

`Delete` or right-click → *Remove* to take an entry out of the panel. Files on disk are never deleted — *Remove* only affects the virtual entry. Folders with contents prompt for confirmation.

### Switch to a file

Double-click a file node (or press `Enter` on it) to switch Notepad++'s focus to that file's tab. If the file is no longer open, it's reopened from disk.

### Auto-removal

When you close a file via Notepad++'s tab bar (clicking the `X` or `Ctrl+W`), it disappears from any virtual folder it was in. The folder itself stays — only file entries get cleaned up.

---

## Keyboard shortcuts

| Shortcut | Where | Action |
|---|---|---|
| `Ctrl+Shift+T` | Anywhere in Notepad++ | Toggle the Virtual Tab Groups panel |
| `Enter` | On a file node in the panel | Open / switch to the file |
| `F2` | On any node in the panel | Rename (display label only) |
| `Delete` | On any node in the panel | Remove from the panel (confirms for non-empty folders) |
| `Ctrl+N` | In the panel | New folder under the current selection (or root) |
| `Ctrl+Shift+A` | In the panel | Add the currently-active Notepad++ file |
| `Ctrl+Shift+Alt+A` | In the panel | Add all currently-open files |
| Arrow keys, `Home`/`End`, `Page Up`/`Down` | In the panel | Standard TreeView navigation |

---

## Data storage

Everything the plugin persists lives under your Notepad++ plugin config directory — typically:

```
%appdata%\Notepad++\plugins\config\VirtualTabGroups\
```

| File | Purpose |
|---|---|
| `state.json` | The virtual tree (folders, files, expanded state, last selection). Human-readable JSON. |
| `state.json.corrupt-yyyyMMdd-HHmmssZ-<random>` | Backups of state files the plugin couldn't parse. Created automatically if `state.json` is ever malformed. Safe to delete once you've inspected them. |
| `plugin.log` | Diagnostic log written by every plugin session. Useful if something misbehaves. |

The plugin **never** writes anywhere else, never reads files outside this directory, and never touches the actual file content of anything you add to virtual folders. The displayed icons come from Windows shell APIs; everything else is text.

### Resetting

To start fresh with an empty tree: close Notepad++, delete (or move) `state.json`, restart Notepad++. The plugin will create a new empty state file on first save.

---

## Dark mode

The plugin reads Notepad++'s active theme via the standard `NPPM_GETDARKMODECOLORS` API and applies it to:

- Panel background and tree text
- Selection / hover highlight
- Expand-collapse chevrons
- Dotted connector lines between sibling nodes
- Context menu (full theming, including separators and arrows)
- Tooltips (replaces the OS yellow with a themed dark-style popup)
- Drag-drop insertion-line indicator

Toggling Notepad++'s dark mode at runtime is handled live — the panel re-themes within a frame, no restart needed.

---

## Building from source

### Prerequisites

- **Windows 10 or 11**
- **Visual Studio 2022 Build Tools** (or the full IDE) with the *Desktop development with .NET Framework* workload, including:
  - .NET Framework 4.8 SDK
  - .NET Framework 4.8 targeting pack
- **NuGet** CLI on PATH (or use the `nuget.exe` checked into the repo root).

### Building

```powershell
git clone https://github.com/JamesShaver/VirtualTabGroups.git
cd VirtualTabGroups
nuget restore VirtualTabGroups.sln
msbuild VirtualTabGroups.sln /p:Configuration=Release /p:Platform=x64
```

Output lands in `VirtualTabGroups\bin\Release\x64\`. The two files needed for deployment are `VirtualTabGroups.dll` and `Newtonsoft.Json.dll`.

For `x86` or `ARM64`, swap the `/p:Platform=` value.

### Running tests

```powershell
dotnet test tests\VirtualTabGroups.Tests\VirtualTabGroups.Tests.csproj --nologo
```

The test project covers the model layer (StateStore, TreeMutator, NodeJsonConverter, AliasResolver, theme manager, observer) — 54 unit tests at the time of writing. UI behavior is verified manually inside Notepad++ since WinForms doesn't lend itself to automated testing without a real message loop.

### Local install for testing

```powershell
Copy-Item -Force "VirtualTabGroups\bin\Release\x64\VirtualTabGroups.dll" "C:\Program Files\Notepad++\plugins\VirtualTabGroups\"
Copy-Item -Force "VirtualTabGroups\bin\Release\x64\Newtonsoft.Json.dll" "C:\Program Files\Notepad++\plugins\VirtualTabGroups\"
```

(Run an elevated PowerShell — `Program Files` requires admin to write to.)

---

## Architecture overview

The plugin is a single managed assembly (`VirtualTabGroups.dll`) built against .NET Framework 4.8. Unmanaged C entry points required by Notepad++ are produced via [DllExport (3F)](https://github.com/3F/DllExport) — the package patches the built assembly to add export thunks for the required functions (`isUnicode`, `setInfo`, `getName`, `getFuncsArray`, `beNotified`, `messageProc`).

The codebase is split into:

- **`Core/`** — pure library code with no Notepad++ dependency:
  - `TreeNodeModel` / `FolderNode` / `FileNode` — the virtual tree.
  - `AliasResolver` — generates `Name(N).ext` aliases for duplicate-name files in a folder.
  - `TreeMutator` — testable add/remove/move primitives.
  - `StateStore` — persistence with debounced atomic writes, observer notifications, corrupt-file recovery, future-version read-only fallback.
  - `NodeJsonConverter` — Newtonsoft.Json custom converter with an explicit `"type"` discriminator.
  - `IStateStoreObserver` — host-notification seam.
- **`Plugin/`** — Notepad++ integration:
  - `PluginMain` — the exported entry points + lifecycle (`setInfo` → `NPPN_READY` → `NPPN_SHUTDOWN`).
  - `NotepadPlusPlusObserver` — implements `IStateStoreObserver` over `WindowsMessageBoxProxy`.
  - `VirtualTabGroupsPanel` — the dockable WinForms form, owns the TreeView and context menu.
  - `AboutDialog` — the About box (Plugins → Virtual Tab Groups → About).
  - `CrashLog` — file-based diagnostic logger.
  - **`Plugin/Npp/`** — vendored interop types: `Win32`, `NppData`, `FuncItem`, `ShortcutKey`, `NppMsg`, `NppNotif`, `SCNotification`, `tTbData`, `DockingForm`, `DarkModeColors`.
  - **`Plugin/UI/`** — `ThemeManager`, `DarkAwareTreeView`, `DarkAwareToolStripRenderer`, `DarkAwareToolTip`, `IconCache`.
  - **`Plugin/Resources/`** — embedded `plugin.ico`.

The model layer (`Core/`) is fully unit-tested without any Notepad++ instance. The UI layer (`Plugin/`) is exercised through manual smoke testing.

### The `tests/` directory

`tests/VirtualTabGroups.Tests/` is a separate xUnit project that covers everything under `Core/` plus the testable seams in `Plugin/` (the observer dedup logic, the theme manager). It is **never shipped** — the release zip contains only `VirtualTabGroups.dll` and `Newtonsoft.Json.dll`. The test project exists for CI (every PR and push to `main` runs `dotnet test`) and for refactoring safety. See [CONTRIBUTING.md](CONTRIBUTING.md) for a detailed breakdown of what's covered.

---

## Roadmap

These would all fit naturally with what the plugin already does. None are committed — but each one would slot in cleanly.

### Likely-soon
- **Search / filter box** above the tree to quickly find a file in a large workspace.
- **Customizable keybindings** — currently the menu shortcuts are baked in. (Note: the *Show panel* shortcut `Ctrl+Shift+T` is already rebindable via *Notepad++ → Settings → Shortcut Mapper → Plugin commands*. This roadmap item is specifically about the in-panel shortcuts: `F2`, `Delete`, `Ctrl+N`, etc.)
- **`Ctrl+C` to copy the selected file's path** without opening the context menu.

### Bigger ideas
- **Multiple workspaces.** A dropdown above the tree to switch between, say, "Work project A", "Personal scripts", "Documentation cleanup" — each with its own root tree, saved to its own state file. Useful when you context-switch between unrelated projects.
- **Group templates.** Predefined folder structures you can apply to a fresh workspace ("Web feature scaffold", "Migration set", etc.).
- **Per-folder color or icon overrides.** Mark some folders red, others blue, attach a custom icon — quick visual scanning of a large tree.
- **External drag-and-drop.** Drop files from Windows Explorer onto the panel to add them. Drop tabs from Notepad++'s tab bar (this last one is constrained by Notepad++'s plugin API but worth investigating).
- **Cut / Copy / Paste in the context menu.** Already reachable via drag-drop, but some users prefer keyboard-driven workflows.
- **Sync across machines.** Detect that `state.json` lives in a synced folder (OneDrive, Dropbox) and behave correctly with concurrent edits — currently undefined behavior.
- **Per-file notes.** Attach a short note to a file entry visible in the tooltip — explanations for future-you of why this file is in this folder.

### Maybe-someday
- **Tab-bar integration.** Group indicators directly in Notepad++'s native tab bar showing which tabs belong to which virtual folder. Requires deeper Notepad++ API access than currently available.
- **Visual diff between workspaces.** "What's in workspace A but not in workspace B?"
- **Workspace export / import** as a portable file format you can share or version-control.

If you're interested in any of these, an issue or PR is welcome — see [Contributing](#contributing).

---

## Troubleshooting

### Plugin doesn't appear in the Plugins menu
- Confirm both DLLs landed in `C:\Program Files\Notepad++\plugins\VirtualTabGroups\`:
  - `VirtualTabGroups.dll`
  - `Newtonsoft.Json.dll`
- Confirm Notepad++ is 64-bit (the `Program Files` install, not `Program Files (x86)`) and you have the 64-bit build of the plugin. Use the `_x86` zip for 32-bit Notepad++.
- Check `Notepad++ → ? → Debug Info` to confirm Notepad++'s architecture matches.

### "Plugin is not compatible" error on launch
- The most common cause is deploying a Debug build (links against `VCRUNTIME140D.dll`, a non-redistributable). Always deploy from `bin\Release\x64\`, not `bin\Debug\`.

### Things break in unexpected ways
- Open `%appdata%\Notepad++\plugins\config\VirtualTabGroups\plugin.log`. The plugin logs every notable event (startup, dialogs, exceptions with stack traces, breadcrumbs through user actions). Most issues are visible in one read.
- If you file an issue, attaching the relevant section of `plugin.log` makes a fix dramatically faster.

### Resetting everything
- Close Notepad++.
- Delete (or move) `%appdata%\Notepad++\plugins\config\VirtualTabGroups\state.json`.
- Reopen Notepad++ — the plugin starts with an empty tree and creates a fresh state file on the next save.

---

## Contributing

Bug reports, feature requests, and PRs are all welcome via [GitHub Issues](https://github.com/JamesShaver/VirtualTabGroups/issues).

See [CONTRIBUTING.md](CONTRIBUTING.md) for the project structure, build steps, and details on what the test project covers vs. what gets manual smoke-tested. When filing a bug, including the contents of `plugin.log` (or the relevant lines near the failure) helps enormously.

---

## License

Virtual Tab Groups is released under the [MIT License](LICENSE).

### Third-party dependencies

- **[Newtonsoft.Json](https://www.newtonsoft.com/json)** (MIT) — JSON serialization. Ships alongside the plugin DLL.
- **[3F.DllExport](https://github.com/3F/DllExport)** (MIT) — IL post-processor that produces the unmanaged exports Notepad++ requires. Build-time only; not shipped.

### Acknowledgements

Inspired by every developer who's ever ended a workday with thirty tabs open across four unrelated codebases.
