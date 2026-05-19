using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

            var json = File.ReadAllText(_stateFilePath);
            var settings = new JsonSerializerSettings
            {
                Converters = { new NodeJsonConverter() },
            };

            var envelope = JsonConvert.DeserializeObject<JObject>(json);

            // Tasks 7 and 8 add corrupt/future-version handling.
            // For now, parse v1 only.
            LastSelectedId = (Guid?)envelope["lastSelectedId"];
            var root = envelope["root"].ToObject<TreeNodeModel>(JsonSerializer.Create(settings));
            return (FolderNode)root;
        }

        public void Dispose() { }
    }
}
