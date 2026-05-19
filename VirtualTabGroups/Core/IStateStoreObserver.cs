using System;

namespace VirtualTabGroups.Core
{
    /// <summary>
    /// Receives notifications from <see cref="StateStore"/> when load/save events warrant
    /// surfacing to the user (e.g. status bar messages or modal dialogs in the host plugin).
    /// All callbacks may be invoked from a background thread; implementations should marshal
    /// to the UI thread if they touch UI state.
    /// </summary>
    public interface IStateStoreObserver
    {
        /// <summary>
        /// Called once on Load when state.json could not be parsed and was renamed to
        /// <paramref name="backupPath"/>. The store has returned an empty tree; the user's
        /// data is preserved at the backup path for inspection or recovery.
        /// </summary>
        void OnRecoveredFromCorruptFile(string backupPath);

        /// <summary>
        /// Called once on Load when state.json declares a schemaVersion higher than this
        /// plugin can read. The store has entered read-only mode for the session: subsequent
        /// MarkDirty/Flush calls are no-ops so the user's newer data is never overwritten.
        /// </summary>
        /// <param name="versionFound">The schemaVersion declared in the file on disk.</param>
        /// <param name="currentVersion">The highest schemaVersion this plugin supports.</param>
        void OnFutureSchemaVersion(int versionFound, int currentVersion);

        /// <summary>
        /// Called from the background timer thread when a save attempt failed (disk full,
        /// permission denied, file locked, etc.). The dirty state is preserved in memory;
        /// the next MarkDirty or Flush will retry. Implementations must not throw.
        /// </summary>
        void OnSaveFailed(Exception ex);
    }
}
