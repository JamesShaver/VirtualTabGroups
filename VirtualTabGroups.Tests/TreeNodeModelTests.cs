using System;
using VirtualTabGroups.Core;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class TreeNodeModelTests
    {
        [Fact]
        public void FolderNode_HasUniqueIdByDefault()
        {
            var a = new FolderNode("a");
            var b = new FolderNode("b");
            Assert.NotEqual(Guid.Empty, a.Id);
            Assert.NotEqual(a.Id, b.Id);
        }

        [Fact]
        public void FolderNode_StartsWithNoChildren()
        {
            var f = new FolderNode("f");
            Assert.Empty(f.Children);
        }

        [Fact]
        public void FileNode_StoresPath()
        {
            var f = new FileNode("User.php", @"C:\repo\User.php");
            Assert.Equal(@"C:\repo\User.php", f.Path);
        }

        [Fact]
        public void Node_ParentIsInitiallyNull()
        {
            Assert.Null(new FolderNode("f").Parent);
            Assert.Null(new FileNode("f", "p").Parent);
        }
    }
}
