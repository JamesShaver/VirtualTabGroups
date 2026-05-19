using VirtualTabGroups.Core;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class TreeMutatorTests
    {
        [Fact]
        public void AddFile_EmptyFolder_AppendsFileWithBareName()
        {
            var folder = new FolderNode("Auth");
            var added = TreeMutator.AddFile(folder, @"C:\src\User.php");

            Assert.NotNull(added);
            Assert.Equal("User.php", added.Name);
            Assert.Equal(@"C:\src\User.php", added.Path);
            Assert.Single(folder.Children);
            Assert.Same(added, folder.Children[0]);
        }

        [Fact]
        public void AddFile_DuplicateNameDifferentPath_AppendsWithAlias()
        {
            var folder = new FolderNode("Auth");
            folder.Children.Add(new FileNode("User.php", @"C:\a\User.php"));

            var added = TreeMutator.AddFile(folder, @"C:\b\User.php");

            Assert.Equal("User(1).php", added.Name);
            Assert.Equal(@"C:\b\User.php", added.Path);
            Assert.Equal(2, folder.Children.Count);
        }

        [Fact]
        public void AddFile_SamePathAlreadyPresent_ReturnsNullAndDoesNotDuplicate()
        {
            var folder = new FolderNode("Auth");
            folder.Children.Add(new FileNode("User.php", @"C:\a\User.php"));

            var added = TreeMutator.AddFile(folder, @"C:\a\User.php");

            Assert.Null(added);
            Assert.Single(folder.Children);
        }
    }
}
