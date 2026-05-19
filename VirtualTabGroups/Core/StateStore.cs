using System;
using System.IO;

namespace VirtualTabGroups.Core
{
    public sealed class StateStore : IDisposable
    {
        private readonly string _stateFilePath;
        private readonly IStateStoreObserver _observer;

        public StateStore(string stateFilePath, IStateStoreObserver observer = null)
        {
            _stateFilePath = stateFilePath ?? throw new ArgumentNullException(nameof(stateFilePath));
            _observer = observer;
        }

        public Guid? LastSelectedId { get; private set; }

        public FolderNode Load()
        {
            if (!File.Exists(_stateFilePath) || new FileInfo(_stateFilePath).Length == 0)
            {
                return new FolderNode("");
            }
            // Parsing comes in Task 6.
            throw new NotImplementedException();
        }

        public void Dispose() { }
    }
}
