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
    }
}
