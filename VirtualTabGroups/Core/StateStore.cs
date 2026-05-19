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
        public bool IsReadOnly { get; private set; }
        private const int CurrentSchemaVersion = 1;

        public FolderNode Load()
        {
            if (!File.Exists(_stateFilePath) || new FileInfo(_stateFilePath).Length == 0)
            {
                return new FolderNode("");
            }

            var settings = new JsonSerializerSettings { Converters = { new NodeJsonConverter() } };

            JObject envelope;
            try
            {
                var json = File.ReadAllText(_stateFilePath);
                envelope = JsonConvert.DeserializeObject<JObject>(json);
                if (envelope == null) throw new JsonSerializationException("empty envelope");
            }
            catch (Exception)
            {
                var backupPath = BackupCorruptFile();
                if (backupPath != null)
                {
                    _observer?.OnRecoveredFromCorruptFile(backupPath);
                }
                return new FolderNode("");
            }

            var schemaVersion = (int?)envelope["schemaVersion"] ?? CurrentSchemaVersion;
            if (schemaVersion > CurrentSchemaVersion)
            {
                IsReadOnly = true;
                _observer?.OnFutureSchemaVersion(schemaVersion, CurrentSchemaVersion);
                return new FolderNode("");
            }

            LastSelectedId = (Guid?)envelope["lastSelectedId"];
            try
            {
                var rootToken = envelope["root"]
                    ?? throw new JsonException("state.json envelope is missing required 'root' property.");
                var rootNode = rootToken.ToObject<TreeNodeModel>(JsonSerializer.Create(settings));
                if (!(rootNode is FolderNode folder))
                {
                    throw new JsonException("state.json root must be a folder node, not a file.");
                }
                return folder;
            }
            catch (Exception)
            {
                var backupPath = BackupCorruptFile();
                if (backupPath != null)
                {
                    _observer?.OnRecoveredFromCorruptFile(backupPath);
                }
                LastSelectedId = null;
                return new FolderNode("");
            }
        }

        private string BackupCorruptFile()
        {
            var suffix = ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssZ");
            var backupPath = _stateFilePath + suffix;

            // Disambiguate if two corruption events land in the same second.
            int n = 0;
            while (File.Exists(backupPath))
            {
                n++;
                backupPath = _stateFilePath + suffix + "-" + n;
            }

            try
            {
                File.Move(_stateFilePath, backupPath);
                return backupPath;
            }
            catch (Exception)
            {
                // Locked file, denied permissions, etc. — recovery still proceeds without a backup.
                return null;
            }
        }

        public void Dispose() { }
    }
}
