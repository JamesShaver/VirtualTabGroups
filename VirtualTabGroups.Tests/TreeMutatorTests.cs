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

        [Fact]
        public void RemoveNode_FileInFolder_RemovesFromContainer()
        {
            var root = new FolderNode("");
            var auth = new FolderNode("Auth");
            var user = new FileNode("User.php", @"C:\a\User.php");
            auth.Children.Add(user);
            root.Children.Add(auth);

            bool removed = TreeMutator.RemoveNode(user, root);

            Assert.True(removed);
            Assert.Empty(auth.Children);
        }

        [Fact]
        public void RemoveNode_FolderInRoot_RemovesEntireSubtree()
        {
            var root = new FolderNode("");
            var auth = new FolderNode("Auth");
            auth.Children.Add(new FileNode("User.php", @"C:\a\User.php"));
            root.Children.Add(auth);

            bool removed = TreeMutator.RemoveNode(auth, root);

            Assert.True(removed);
            Assert.Empty(root.Children);
        }

        [Fact]
        public void RemoveNode_NodeNotInTree_ReturnsFalse()
        {
            var root = new FolderNode("");
            var orphan = new FileNode("X.txt", @"C:\X.txt");

            bool removed = TreeMutator.RemoveNode(orphan, root);

            Assert.False(removed);
        }

        [Fact]
        public void MoveNode_FileToOtherFolder_NoNameClash_PreservesName()
        {
            var root = new FolderNode("");
            var a = new FolderNode("A");
            var b = new FolderNode("B");
            var user = new FileNode("User.php", @"C:\src\User.php");
            a.Children.Add(user);
            root.Children.Add(a);
            root.Children.Add(b);

            bool moved = TreeMutator.MoveNode(user, b, position: 0, root);

            Assert.True(moved);
            Assert.Empty(a.Children);
            Assert.Single(b.Children);
            Assert.Equal("User.php", user.Name);
        }

        [Fact]
        public void MoveNode_FileToFolderWithClashingName_ReResolvesAlias()
        {
            var root = new FolderNode("");
            var a = new FolderNode("A");
            var b = new FolderNode("B");
            a.Children.Add(new FileNode("User.php", @"C:\b\User.php"));
            b.Children.Add(new FileNode("User.php", @"C:\b1\User.php"));
            var moving = (FileNode)a.Children[0];
            root.Children.Add(a);
            root.Children.Add(b);

            TreeMutator.MoveNode(moving, b, position: 1, root);

            Assert.Equal("User(1).php", moving.Name);
            Assert.Equal(2, b.Children.Count);
        }

        [Fact]
        public void MoveNode_FolderIntoItself_IsRejected()
        {
            var root = new FolderNode("");
            var a = new FolderNode("A");
            root.Children.Add(a);

            bool moved = TreeMutator.MoveNode(a, a, position: 0, root);

            Assert.False(moved);
            Assert.Single(root.Children);
        }

        [Fact]
        public void MoveNode_FolderIntoDescendant_IsRejected()
        {
            var root = new FolderNode("");
            var outer = new FolderNode("Outer");
            var inner = new FolderNode("Inner");
            outer.Children.Add(inner);
            root.Children.Add(outer);

            bool moved = TreeMutator.MoveNode(outer, inner, position: 0, root);

            Assert.False(moved);
        }
    }
}
