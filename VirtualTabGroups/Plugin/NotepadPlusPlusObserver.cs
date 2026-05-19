using System;
using VirtualTabGroups.Core;

namespace VirtualTabGroups.Plugin
{
    public sealed class NotepadPlusPlusObserver : IStateStoreObserver
    {
        private const string Title = "Virtual Tab Groups";

        private readonly IMessageBoxProxy _box;
        private bool _saveFailureNotified;

        public NotepadPlusPlusObserver(IMessageBoxProxy box)
        {
            _box = box ?? throw new ArgumentNullException(nameof(box));
        }

        public void OnRecoveredFromCorruptFile(string backupPath)
        {
            var body = $"Your Virtual Tab Groups state file could not be read and has been backed up to {backupPath}. Starting with an empty tree.";
            _box.Show(Title, body, MessageBoxKind.Warning);
        }

        public void OnFutureSchemaVersion(int versionFound, int currentVersion)
        {
            var body = $"Your state file was created by a newer version of Virtual Tab Groups (schema {versionFound}; this plugin supports up to {currentVersion}). The plugin is running in read-only mode this session to avoid clobbering your data. Please upgrade the plugin.";
            _box.Show(Title, body, MessageBoxKind.Warning);
        }

        public void OnSaveFailed(Exception ex)
        {
            if (_saveFailureNotified) return;
            _saveFailureNotified = true;

            var body = $"Saving your Virtual Tab Groups state failed: {ex.Message}. Subsequent save failures this session will be suppressed.";
            _box.Show(Title, body, MessageBoxKind.Error);
        }
    }
}
