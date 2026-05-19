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
    }
}
