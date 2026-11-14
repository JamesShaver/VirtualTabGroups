using VirtualTabGroups.Core;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class FileNodeRelinkTests
    {
        [Fact]
        public void Relink_UnsavedBufferName_UpdatesPathAndLabel()
        {
            var folder = new FolderNode("Root");
            var file = new FileNode("new 1", "new 1");
            folder.Children.Add(file);

            bool changed = file.Relink(folder, @"C:\notes\Plan.txt");

            Assert.True(changed);
            Assert.Equal(@"C:\notes\Plan.txt", file.Path);
            Assert.Equal("Plan.txt", file.Name);
        }

        [Fact]
        public void Relink_SamePath_ReturnsFalseAndKeepsLabel()
        {
            var folder = new FolderNode("Root");
            var file = new FileNode("Plan.txt", @"C:\notes\Plan.txt");
            folder.Children.Add(file);

            Assert.False(file.Relink(folder, @"c:\NOTES\plan.txt"));
            Assert.Equal("Plan.txt", file.Name);
            Assert.Equal(@"C:\notes\Plan.txt", file.Path);
        }

        [Fact]
        public void Relink_NameTakenBySibling_GetsAlias()
        {
            var folder = new FolderNode("Root");
            folder.Children.Add(new FileNode("Plan.txt", @"C:\a\Plan.txt"));
            var file = new FileNode("new 1", "new 1");
            folder.Children.Add(file);

            Assert.True(file.Relink(folder, @"C:\b\Plan.txt"));
            Assert.Equal("Plan(1).txt", file.Name);
        }

        [Fact]
        public void Relink_ReleasesOldAlias_WhenNameNoLongerClashes()
        {
            var folder = new FolderNode("Root");
            folder.Children.Add(new FileNode("Plan.txt", @"C:\a\Plan.txt"));
            var file = new FileNode("Plan(1).txt", @"C:\b\Plan.txt");
            folder.Children.Add(file);

            Assert.True(file.Relink(folder, @"C:\b\Notes.txt"));
            Assert.Equal("Notes.txt", file.Name);
            Assert.Equal(@"C:\b\Notes.txt", file.Path);
        }

        [Fact]
        public void Relink_InvalidArguments_AreIgnored()
        {
            var file = new FileNode("Plan.txt", @"C:\notes\Plan.txt");

            Assert.False(file.Relink(null, @"C:\notes\Other.txt"));
            Assert.False(file.Relink(new FolderNode("Root"), ""));
            Assert.Equal("Plan.txt", file.Name);
            Assert.Equal(@"C:\notes\Plan.txt", file.Path);
        }
    }
}
