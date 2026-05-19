using System;

namespace VirtualTabGroups.Core
{
    public interface IStateStoreObserver
    {
        void OnRecoveredFromCorruptFile(string backupPath);
        void OnFutureSchemaVersion(int versionFound);
        void OnSaveFailed(Exception ex);
    }
}
