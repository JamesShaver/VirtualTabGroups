using VirtualTabGroups.Core;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class AliasResolverTests
    {
        [Fact]
        public void EmptyFolder_ReturnsBareFilename()
        {
            var folder = new FolderNode("g");
            var name = AliasResolver.Resolve(folder, @"C:\a\User.php");
            Assert.Equal("User.php", name);
        }

        [Fact]
        public void OneCollision_ReturnsParenOne()
        {
            var folder = new FolderNode("g");
            folder.Children.Add(new FileNode("User.php", @"C:\a\User.php"));

            var name = AliasResolver.Resolve(folder, @"C:\b\User.php");
            Assert.Equal("User(1).php", name);
        }

        [Fact]
        public void TwoCollisions_ReturnsParenTwo()
        {
            var folder = new FolderNode("g");
            folder.Children.Add(new FileNode("User.php", @"C:\a\User.php"));
            folder.Children.Add(new FileNode("User(1).php", @"C:\b\User.php"));

            var name = AliasResolver.Resolve(folder, @"C:\c\User.php");
            Assert.Equal("User(2).php", name);
        }

        [Fact]
        public void SparseCollisions_FillsSmallestUnused()
        {
            var folder = new FolderNode("g");
            folder.Children.Add(new FileNode("User.php", @"C:\a\User.php"));
            folder.Children.Add(new FileNode("User(2).php", @"C:\b\User.php"));

            var name = AliasResolver.Resolve(folder, @"C:\c\User.php");
            Assert.Equal("User(1).php", name);
        }

        [Fact]
        public void NoExtension_AliasesCorrectly()
        {
            var folder = new FolderNode("g");
            folder.Children.Add(new FileNode("README", @"C:\a\README"));

            var name = AliasResolver.Resolve(folder, @"C:\b\README");
            Assert.Equal("README(1)", name);
        }

        [Fact]
        public void MultiDotName_TreatsLastDotAsExtension()
        {
            var folder = new FolderNode("g");
            folder.Children.Add(new FileNode("app.config.json", @"C:\a\app.config.json"));

            var name = AliasResolver.Resolve(folder, @"C:\b\app.config.json");
            Assert.Equal("app.config(1).json", name);
        }

        [Fact]
        public void FolderChildrenDoNotCount_OnlyFileSiblings()
        {
            var folder = new FolderNode("g");
            folder.Children.Add(new FolderNode("User.php"));   // folder with file-like name

            var name = AliasResolver.Resolve(folder, @"C:\a\User.php");
            Assert.Equal("User.php", name);
        }
    }
}
