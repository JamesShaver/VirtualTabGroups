using Newtonsoft.Json;
using VirtualTabGroups.Core;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class NodeJsonConverterTests
    {
        private static JsonSerializerSettings Settings() => new JsonSerializerSettings
        {
            Converters = { new NodeJsonConverter() },
            Formatting = Formatting.None,
        };

        [Fact]
        public void WriteFile_EmitsTypeIdNamePath()
        {
            var file = new FileNode("User.php", @"C:\src\User.php");
            file.Id = new System.Guid("11111111-1111-1111-1111-111111111111");

            var json = JsonConvert.SerializeObject(file, Settings());

            Assert.Equal(
                "{\"type\":\"file\",\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"User.php\",\"path\":\"C:\\\\src\\\\User.php\"}",
                json);
        }

        [Fact]
        public void WriteFolder_EmitsTypeIdNameExpandedAndChildren()
        {
            var folder = new FolderNode("Auth");
            folder.Id = new System.Guid("22222222-2222-2222-2222-222222222222");
            folder.Expanded = true;

            var file = new FileNode("User.php", @"C:\src\User.php");
            file.Id = new System.Guid("33333333-3333-3333-3333-333333333333");
            folder.Children.Add(file);

            var json = JsonConvert.SerializeObject(folder, Settings());

            Assert.Equal(
                "{\"type\":\"folder\",\"id\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"Auth\",\"expanded\":true,\"children\":[" +
                "{\"type\":\"file\",\"id\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"User.php\",\"path\":\"C:\\\\src\\\\User.php\"}" +
                "]}",
                json);
        }

        [Fact]
        public void RoundTrip_NestedTree_PreservesStructure()
        {
            var root = new FolderNode("");
            root.Id = System.Guid.NewGuid();
            root.Expanded = true;

            var auth = new FolderNode("Auth");
            auth.Id = System.Guid.NewGuid();
            auth.Expanded = false;
            auth.Children.Add(new FileNode("User.php", @"C:\src\auth\User.php"));
            root.Children.Add(auth);

            root.Children.Add(new FileNode("README", @"C:\src\README"));

            var json = JsonConvert.SerializeObject(root, Settings());
            var rehydrated = (FolderNode)JsonConvert.DeserializeObject<TreeNodeModel>(json, Settings());

            Assert.Equal(root.Id, rehydrated.Id);
            Assert.Equal(root.Name, rehydrated.Name);
            Assert.True(rehydrated.Expanded);
            Assert.Equal(2, rehydrated.Children.Count);

            var authBack = (FolderNode)rehydrated.Children[0];
            Assert.Equal("Auth", authBack.Name);
            Assert.False(authBack.Expanded);
            Assert.Single(authBack.Children);

            var userBack = (FileNode)authBack.Children[0];
            Assert.Equal("User.php", userBack.Name);
            Assert.Equal(@"C:\src\auth\User.php", userBack.Path);

            var readmeBack = (FileNode)rehydrated.Children[1];
            Assert.Equal("README", readmeBack.Name);
        }

        [Fact]
        public void ReadFolder_MissingExpandedField_DefaultsToFalse()
        {
            var json = "{\"type\":\"folder\",\"id\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"X\",\"children\":[]}";

            var folder = (FolderNode)JsonConvert.DeserializeObject<TreeNodeModel>(json, Settings());

            Assert.False(folder.Expanded);
            Assert.Empty(folder.Children);
        }
    }
}
