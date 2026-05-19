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
        private readonly System.Threading.Timer _debounceTimer;
        private readonly TimeSpan _debounceInterval;
        private volatile bool _disposed;

        public StateStore(string stateFilePath, IStateStoreObserver observer = null)
            : this(stateFilePath, observer, TimeSpan.FromMilliseconds(500)) { }

        internal StateStore(string stateFilePath, IStateStoreObserver observer, TimeSpan debounce)
        {
            _stateFilePath = stateFilePath ?? throw new ArgumentNullException(nameof(stateFilePath));
            _observer = observer;
            _debounceInterval = debounce;
            _debounceTimer = new System.Threading.Timer(_ => OnDebounceFired(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
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
                    try { _observer?.OnRecoveredFromCorruptFile(backupPath); }
                    catch { /* swallow observer exceptions — never let host code propagate through Load(). */ }
                }
                return new FolderNode("");
            }

            var schemaVersion = (int?)envelope["schemaVersion"] ?? CurrentSchemaVersion;
            if (schemaVersion > CurrentSchemaVersion)
            {
                IsReadOnly = true;
                try { _observer?.OnFutureSchemaVersion(schemaVersion, CurrentSchemaVersion); }
                catch { /* swallow observer exceptions — never let host code propagate through Load(). */ }
                return new FolderNode("");
            }

            LastSelectedId = null;
            var lastSelectedToken = (string)envelope["lastSelectedId"];
            if (lastSelectedToken != null && Guid.TryParse(lastSelectedToken, out var parsedId))
            {
                LastSelectedId = parsedId;
            }
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
                    try { _observer?.OnRecoveredFromCorruptFile(backupPath); }
                    catch { /* swallow observer exceptions — never let host code propagate through Load(). */ }
                }
                LastSelectedId = null;
                return new FolderNode("");
            }
        }

        private string _pendingJson;
        private readonly object _writeLock = new object();

        public void MarkDirty(FolderNode root, Guid? selectedId = null)
        {
            if (_disposed) return;
            if (IsReadOnly) return;
            if (root == null) throw new ArgumentNullException(nameof(root));

            var settings = new JsonSerializerSettings
            {
                Converters = { new NodeJsonConverter() },
            };

            var envelope = new JObject
            {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["lastSelectedId"] = selectedId.HasValue ? selectedId.Value.ToString() : null,
                ["root"] = JToken.FromObject(root, JsonSerializer.Create(settings)),
            };

            var json = envelope.ToString(Formatting.Indented);
            lock (_writeLock)
            {
                _pendingJson = json;
            }

            _debounceTimer.Change(_debounceInterval, System.Threading.Timeout.InfiniteTimeSpan);
        }

        private void WritePendingNow()
        {
            string json;
            lock (_writeLock)
            {
                json = _pendingJson;
                if (json == null) return;
                // Do NOT clear _pendingJson yet — only clear on successful write.
            }

            try
            {
                var tmpPath = _stateFilePath + ".tmp";
                File.WriteAllText(tmpPath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                if (File.Exists(_stateFilePath))
                {
                    File.Replace(tmpPath, _stateFilePath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tmpPath, _stateFilePath);
                }

                lock (_writeLock)
                {
                    // Only clear if no new MarkDirty raced in with a newer string.
                    if (_pendingJson == json) _pendingJson = null;
                }
            }
            catch (Exception ex)
            {
                try { _observer?.OnSaveFailed(ex); }
                catch { /* swallow observer exceptions — never let host code crash the timer thread. */ }
                // Leave _pendingJson set so the next MarkDirty or Flush retries.
            }
        }

        private void OnDebounceFired() => WritePendingNow();

        public void Flush()
        {
            if (_disposed) return;
            if (IsReadOnly) return;
            _debounceTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            WritePendingNow();
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

        public void Dispose()
        {
            if (_disposed) return;
            try { Flush(); }
            finally
            {
                _disposed = true;
                _debounceTimer.Dispose();
            }
        }
    }
}
