# StateStore Design — v1.0.0

**Status:** approved
**Date:** 2026-05-19
**Scope:** v1.0.0 milestone of the Virtual Tab Groups plugin.

## Summary

StateStore is the durable persistence layer for the virtual tree. It owns the lifecycle of `state.json` — loading on startup, saving on mutation, recovering gracefully from corruption — while remaining pure C# with zero Notepad++ dependencies.

## Decisions locked

| Decision | Choice | Rationale |
| --- | --- | --- |
| JSON library | Newtonsoft.Json | Single-DLL deploy; ubiquitous in the Notepad++ plugin ecosystem |
| Type discriminator | Explicit `"type"` field | Format survives CLR refactors; human-readable for support |
| Save policy | Debounced (~500ms) + atomic write + shutdown flush | Coalesces bulk mutations; no partial-file corruption; no data loss on clean shutdown |
| Corrupt-file recovery | Backup + fresh start + observer notification | Preserves evidence; doesn't strand the user |
| UI state | Persisted in `state.json` (single file) | One atomic write; expanded/selection survive sessions |

## Scope

StateStore owns:

- Loading the tree from disk on plugin startup.
- Receiving change notifications from callers and writing safely to disk.
- Recovering from corrupt or future-version files without silent data loss.
- Surfacing failures to the host via an observer interface.

StateStore does **not**:

- Mutate the tree (callers are responsible — `AliasResolver`, drag-drop handlers, etc.).
- Render the tree (the WinForms panel will, when built).
- Talk to Notepad++ APIs (the plugin host computes paths and routes observer events).

This isolation makes StateStore fully unit-testable against a temp directory with no editor instance running.

## Public surface

StateStore exposes:

- `Load()` — called once on plugin init. Returns the root `FolderNode`. Never throws; recovery is internal.
- `LastSelectedId` — populated by `Load`. Indicates which node should be re-selected after load (or null).
- `MarkDirty(root, selectedId)` — caller signals a tree mutation. Returns immediately; schedules a debounced background write.
- `Flush()` — forces a pending save to complete synchronously. Called on `NPPN_SHUTDOWN`.
- `Dispose()` — calls `Flush`.

The observer interface (`IStateStoreObserver`) is the only seam between StateStore and the host. It exposes three callbacks:

- `OnRecoveredFromCorruptFile(backupPath)`
- `OnFutureSchemaVersion(versionFound)`
- `OnSaveFailed(exception)`

The plugin host implements this interface and routes events to Notepad++ notifications (status bar, message box). Tests use a no-op or capturing implementation.

## File layout

Managed by the plugin host, not StateStore. The host computes the path via `NPPM_GETPLUGINSCONFIGDIR` and passes it in.

- `%appdata%\Notepad++\plugins\config\VirtualTabGroups\state.json` — current state.
- `state.json.tmp` — transient atomic-write staging file; should not normally exist between sessions.
- `state.json.corrupt-yyyyMMdd-HHmmss` — preserved backup of any unparseable file.

## On-disk JSON schema (v1)

```json
{
  "schemaVersion": 1,
  "lastSelectedId": "99e0a4d2-...",
  "root": {
    "type": "folder",
    "id": "f1c2b3a4-...",
    "name": "",
    "expanded": true,
    "children": [
      {
        "type": "folder",
        "id": "a3b4c5d6-...",
        "name": "Auth",
        "expanded": false,
        "children": [
          {
            "type": "file",
            "id": "99e0a4d2-...",
            "name": "User.php",
            "path": "C:\\src\\auth\\User.php"
          }
        ]
      }
    ]
  }
}
```

**Top-level fields:**

- `schemaVersion` — integer; always `1` in v1.0.0. Older plugins reading newer schemas refuse to save (read-only fallback).
- `lastSelectedId` — GUID string or null; restored on load if the referenced node still exists.
- `root` — implicit hidden root folder. Its `name` is empty; it never renders.

**Folder node fields:**

- `type: "folder"` — explicit discriminator.
- `id` — stable GUID, generated once when the folder is created.
- `name` — display label.
- `expanded` — restored on load; missing or `false` means collapsed.
- `children` — ordered list of folder and file nodes; order is preserved.

**File node fields:**

- `type: "file"` — explicit discriminator.
- `id` — stable GUID.
- `name` — display label (possibly aliased, e.g. `"User(1).php"`).
- `path` — absolute filesystem path; used as the argument to `NPPM_DOOPEN`.

**Format rules:**

- Unknown fields are ignored on load (forward compatibility for benign additions).
- File is written indented and human-readable; UTF-8 without BOM.
- Path separators are platform-native backslashes on Windows, JSON-escaped per spec.

## Save mechanics

The save sequence:

1. Caller mutates the tree and invokes `MarkDirty(root, selectedId)` on the UI thread.
2. StateStore synchronously serializes the tree to a JSON string on the UI thread (milliseconds even for large trees) and stores it in a volatile field.
3. StateStore starts or restarts a 500ms debounce timer (`System.Threading.Timer`).
4. When the timer fires (on a thread-pool thread):
   1. Read the latest JSON string from the volatile field.
   2. Write it to `state.json.tmp` (UTF-8 without BOM).
   3. Atomically replace `state.json` using `File.Replace` (or `File.Move` on the first save when no prior file exists).
   4. On IO exception: invoke `observer.OnSaveFailed(ex)`. Do **not** clear the dirty state — the next `MarkDirty` retries.
5. `Flush()` cancels the timer and runs the save synchronously inline on the calling thread.

**Why the JSON string crosses thread boundaries instead of the tree:** the tree is held by the UI; serializing on the UI thread guarantees a consistent snapshot. Only an immutable string travels to the background writer, eliminating concurrent-modification hazards.

## Load mechanics

`Load()` returns a `FolderNode` regardless of file state.

| File state | Behavior |
| --- | --- |
| Missing or empty | Return new empty root. No observer call. |
| Parses, `schemaVersion: 1` | Build and return tree. Set `LastSelectedId`. Restore expanded flags. |
| Parses, `schemaVersion > 1` | Set internal read-only flag (`MarkDirty`/`Flush` become no-ops for the session). Return empty root. Invoke `observer.OnFutureSchemaVersion(found)`. |
| Does not parse / invalid type | Rename file to `state.json.corrupt-yyyyMMdd-HHmmss`. Return empty root. Invoke `observer.OnRecoveredFromCorruptFile(backupPath)`. |

Unknown fields on otherwise-valid nodes are silently ignored.

## Threading model

- All tree mutations happen on the UI thread (caller's responsibility).
- `MarkDirty` serializes synchronously on the calling (UI) thread; only the resulting string crosses to the background.
- Disk I/O runs on `System.Threading.Timer` thread-pool callbacks.
- `Flush` and `Dispose` run synchronously on the calling thread.

StateStore is safe to call from the UI thread without external locking; tests can call it directly on the test thread.

## Testing strategy

StateStore is testable in isolation using a temp directory. Concrete tests:

- **Round-trip:** build tree → `MarkDirty` → `Flush` → `Load` → assert structural equality.
- **Schema fixtures:** load known-good JSON files from a `Fixtures/` directory → assert tree shape, expanded flags, selection restoration.
- **Corrupt-file recovery:** seed garbage → `Load` → assert empty tree, backup file present, observer called.
- **Future schema:** seed `schemaVersion: 2` → `Load` → assert empty tree, read-only mode active, observer called, subsequent `MarkDirty` + `Flush` leaves disk untouched.
- **Debounce:** call `MarkDirty` N times in <500ms → assert exactly one disk write occurs.
- **Atomic write:** successful save → assert no `.tmp` file remains.
- **Save failure:** point at a read-only directory → `MarkDirty` + wait → observer notified, dirty state preserved.
- **Shutdown flush:** `MarkDirty` then immediately `Flush` → assert write completed synchronously before `Flush` returns.

## Explicit scope cut-offs for v1.0.0

The following are deliberately deferred:

- **Schema migrations.** Only `schemaVersion: 1` is read or written. Migration scaffolding lands when schema 2 ships.
- **Multi-instance coordination.** Two Notepad++ windows targeting the same `state.json` is undefined behavior. Real fix (file locks or per-instance config) is significant; deferred.
- **Backup rotation.** Accumulated `state.json.corrupt-*` files are not pruned. Trivial follow-up if it becomes a real problem.
- **Session-pending-change recovery.** Atomic write protects file integrity, not the buffer of unsaved changes since the last successful save. A persistent save-failure followed by exit will lose those changes.
- **UI / panel.** No WinForms code lands in this milestone; tests are the only caller until the dockable panel is built next.

## Dependencies

- `Newtonsoft.Json` (NuGet) — single-DLL dependency; deployed alongside the plugin.
- Existing types in `VirtualTabGroups.Core`: `TreeNodeModel`, `FolderNode`, `FileNode`.

## Open questions

None at spec time. All decisions are locked.
