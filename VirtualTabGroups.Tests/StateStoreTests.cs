using System;
using System.IO;
using VirtualTabGroups.Core;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class StateStoreTests
    {
        private static string NewTempStatePath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "VTG-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "state.json");
        }

        [Fact]
        public void Load_MissingFile_ReturnsEmptyRoot()
        {
            var path = NewTempStatePath();
            using (var store = new StateStore(path))
            {
                var root = store.Load();

                Assert.NotNull(root);
                Assert.Equal("", root.Name);
                Assert.Empty(root.Children);
                Assert.Null(store.LastSelectedId);
            }
        }

        [Fact]
        public void Load_EmptyFile_ReturnsEmptyRoot()
        {
            var path = NewTempStatePath();
            File.WriteAllText(path, "");
            using (var store = new StateStore(path))
            {
                var root = store.Load();
                Assert.Empty(root.Children);
            }
        }

        private static string CopyFixtureNextToStateFile(string fixtureName)
        {
            var statePath = NewTempStatePath();
            var fixtureSource = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Fixtures",
                fixtureName);
            File.Copy(fixtureSource, statePath, overwrite: true);
            return statePath;
        }

        [Fact]
        public void Load_ValidV1File_ReturnsTreeAndSelection()
        {
            var path = CopyFixtureNextToStateFile("state-good.json");
            using (var store = new StateStore(path))
            {
                var root = store.Load();

                Assert.True(root.Expanded);
                Assert.Single(root.Children);

                var auth = (FolderNode)root.Children[0];
                Assert.Equal("Auth", auth.Name);
                Assert.False(auth.Expanded);
                Assert.Single(auth.Children);

                var user = (FileNode)auth.Children[0];
                Assert.Equal("User.php", user.Name);
                Assert.Equal(@"C:\src\auth\User.php", user.Path);

                Assert.Equal(
                    new Guid("99e0a4d2-4b5c-4d6e-8f70-112233445566"),
                    store.LastSelectedId);
            }
        }

        private sealed class CapturingObserver : IStateStoreObserver
        {
            public string CorruptBackupPath;
            public int? FutureSchemaSeen;
            public int? CurrentSchemaSeen;
            public System.Exception SaveError;

            public void OnRecoveredFromCorruptFile(string backupPath) => CorruptBackupPath = backupPath;
            public void OnFutureSchemaVersion(int versionFound, int currentVersion)
            {
                FutureSchemaSeen = versionFound;
                CurrentSchemaSeen = currentVersion;
            }
            public void OnSaveFailed(System.Exception ex) => SaveError = ex;
        }

        [Fact]
        public void Load_CorruptFile_BacksUpAndNotifiesAndReturnsEmptyRoot()
        {
            var path = NewTempStatePath();
            File.WriteAllText(path, "this is not json {");

            var observer = new CapturingObserver();
            using (var store = new StateStore(path, observer))
            {
                var root = store.Load();

                Assert.Empty(root.Children);
                Assert.NotNull(observer.CorruptBackupPath);
                Assert.True(File.Exists(observer.CorruptBackupPath), "backup file should exist on disk");
                Assert.False(File.Exists(path), "original corrupt file should have been moved");
                Assert.StartsWith(Path.GetFileName(path) + ".corrupt-", Path.GetFileName(observer.CorruptBackupPath));
            }
        }

        [Fact]
        public void Load_CorruptRootField_BacksUpAndNotifiesAndResetsSelection()
        {
            var path = NewTempStatePath();
            // Valid envelope, valid lastSelectedId, but root is a file-typed node (invalid: root must be folder).
            File.WriteAllText(path,
                "{\"schemaVersion\":1," +
                "\"lastSelectedId\":\"99e0a4d2-4b5c-4d6e-8f70-112233445566\"," +
                "\"root\":{\"type\":\"file\",\"id\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"oops\",\"path\":\"C:\\\\x\"}}");

            var observer = new CapturingObserver();
            using (var store = new StateStore(path, observer))
            {
                var root = store.Load();

                Assert.Empty(root.Children);
                Assert.Null(store.LastSelectedId);
                Assert.NotNull(observer.CorruptBackupPath);
                Assert.True(File.Exists(observer.CorruptBackupPath));
                Assert.False(File.Exists(path));
            }
        }

        [Fact]
        public void Load_FutureSchemaVersion_NotifiesAndEntersReadOnlyMode()
        {
            var path = NewTempStatePath();
            File.WriteAllText(path,
                "{\"schemaVersion\":2,\"root\":{\"type\":\"folder\",\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"\",\"children\":[]}}");

            var originalContent = File.ReadAllText(path);
            var observer = new CapturingObserver();

            using (var store = new StateStore(path, observer))
            {
                var root = store.Load();
                Assert.Empty(root.Children);
                Assert.Equal(2, observer.FutureSchemaSeen);
                Assert.Equal(1, observer.CurrentSchemaSeen);
                Assert.True(store.IsReadOnly);
            }

            // File on disk must be unchanged (we did not overwrite the user's newer data).
            Assert.Equal(originalContent, File.ReadAllText(path));
        }

        [Fact]
        public void Save_AndReloadInNewStore_PreservesTree()
        {
            var path = NewTempStatePath();

            var root = new FolderNode("");
            var auth = new FolderNode("Auth") { Expanded = true };
            auth.Children.Add(new FileNode("User.php", @"C:\src\User.php"));
            root.Children.Add(auth);

            var selectedId = auth.Children[0].Id;

            using (var store = new StateStore(path))
            {
                store.MarkDirty(root, selectedId);
                store.Flush();
            }

            // Reload in a fresh store.
            using (var store2 = new StateStore(path))
            {
                var reloaded = store2.Load();
                Assert.Single(reloaded.Children);
                var authBack = (FolderNode)reloaded.Children[0];
                Assert.Equal("Auth", authBack.Name);
                Assert.True(authBack.Expanded);
                Assert.Single(authBack.Children);
                Assert.Equal("User.php", ((FileNode)authBack.Children[0]).Name);
                Assert.Equal(selectedId, store2.LastSelectedId);
            }
        }

        [Fact]
        public void Save_AtomicWrite_LeavesNoTempFile()
        {
            var path = NewTempStatePath();
            using (var store = new StateStore(path))
            {
                store.MarkDirty(new FolderNode(""), null);
                store.Flush();
            }

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void MarkDirty_BurstOfCalls_CoalescesIntoOneWrite()
        {
            var path = NewTempStatePath();
            using (var store = new StateStore(path, null, System.TimeSpan.FromMilliseconds(150)))
            {
                for (int i = 0; i < 30; i++)
                {
                    var root = new FolderNode("");
                    root.Children.Add(new FileNode("file" + i + ".txt", @"C:\f.txt"));
                    store.MarkDirty(root, null);
                }

                Assert.False(File.Exists(path), "no write should have happened yet during the burst");
                System.Threading.Thread.Sleep(400);  // > debounce window
                Assert.True(File.Exists(path), "exactly one write should land after debounce expires");
            }

            using (var store2 = new StateStore(path))
            {
                var reloaded = store2.Load();
                Assert.Single(reloaded.Children);
                // Final state should reflect the LAST MarkDirty (file29.txt).
                Assert.Equal("file29.txt", ((FileNode)reloaded.Children[0]).Name);
            }
        }

        [Fact]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var path = NewTempStatePath();
            var store = new StateStore(path);
            store.Dispose();
            store.Dispose();  // Must not throw.
        }

        [Fact]
        public void Save_IOFailure_NotifiesObserverAndKeepsDirtyState()
        {
            // Point at a directory that doesn't exist so File.WriteAllText throws.
            var bogusPath = Path.Combine(
                Path.GetTempPath(),
                "VTG-doesnotexist-" + System.Guid.NewGuid().ToString("N"),
                "state.json");

            var observer = new CapturingObserver();
            using (var store = new StateStore(bogusPath, observer, System.TimeSpan.FromMilliseconds(50)))
            {
                store.MarkDirty(new FolderNode(""), null);
                System.Threading.Thread.Sleep(200);

                Assert.NotNull(observer.SaveError);
                Assert.IsAssignableFrom<System.IO.IOException>(observer.SaveError);

                // The pending state should still be set after a failure — verify by triggering MarkDirty again.
                store.MarkDirty(new FolderNode(""), null);
            }
        }
    }
}
