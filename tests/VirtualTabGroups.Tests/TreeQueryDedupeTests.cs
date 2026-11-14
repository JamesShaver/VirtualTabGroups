using System.Collections.Generic;
using VirtualTabGroups.Core;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class TreeQueryDedupeTests
    {
        [Fact]
        public void FindRedundantFileEntries_UniquePaths_ReturnsNothing()
        {
            var root = new FolderNode("");
            root.Children.Add(new FileNode("A.txt", @"C:\a\A.txt"));
            root.Children.Add(new FileNode("B.txt", @"C:\b\B.txt"));

            Assert.Empty(TreeQuery.FindRedundantFileEntries(root));
        }

        [Fact]
        public void FindRedundantFileEntries_SamePathTwice_ReturnsSecondEntryOnly()
        {
            var root = new FolderNode("");
            var first = new FileNode("identity.md", @"D:\openclaw\projects\identity.md");
            var second = new FileNode("identity.md", @"D:\openclaw\projects\identity.md");
            root.Children.Add(first);
            root.Children.Add(second);

            var redundant = TreeQuery.FindRedundantFileEntries(root);

            Assert.Single(redundant);
            Assert.Same(second, redundant[0]);
        }

        [Fact]
        public void FindRedundantFileEntries_PathDiffersOnlyByCase_IsDuplicate()
        {
            var root = new FolderNode("");
            root.Children.Add(new FileNode("A.txt", @"C:\a\A.txt"));
            root.Children.Add(new FileNode("a.txt", @"c:\A\a.TXT"));

            Assert.Single(TreeQuery.FindRedundantFileEntries(root));
        }

        [Fact]
        public void FindRedundantFileEntries_NestedFolders_KeepsFirstDepthFirstOccurrence()
        {
            var root = new FolderNode("");
            var work = new FolderNode("Work");
            var nested = new FileNode("Plan.txt", @"C:\notes\Plan.txt");
            root.Children.Add(new FileNode("Plan.txt", @"C:\notes\Plan.txt"));
            root.Children.Add(work);
            work.Children.Add(nested);
            work.Children.Add(new FileNode("Plan.txt", @"C:\notes\Plan.txt"));

            var redundant = TreeQuery.FindRedundantFileEntries(root);

            Assert.Equal(2, redundant.Count);
            Assert.Same(nested, redundant[0]);
        }

        [Fact]
        public void FindRedundantFileEntries_EmptyPaths_AreIgnored()
        {
            var root = new FolderNode("");
            root.Children.Add(new FileNode("empty", null));
            root.Children.Add(new FileNode("empty", ""));

            Assert.Empty(TreeQuery.FindRedundantFileEntries(root));
        }

        [Fact]
        public void FindRedundantFileEntries_RemovingResultLeavesNoDuplicates()
        {
            var root = new FolderNode("");
            for (int i = 0; i < 3; i++)
                root.Children.Add(new FileNode("Plan.txt", @"C:\notes\Plan.txt"));

            var redundant = TreeQuery.FindRedundantFileEntries(root);
            Assert.Equal(2, redundant.Count);

            foreach (var node in redundant) root.Children.Remove(node);

            Assert.Empty(TreeQuery.FindRedundantFileEntries(root));
            Assert.Single(root.Children);
        }
    }
}
