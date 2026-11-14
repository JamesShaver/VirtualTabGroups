using VirtualTabGroups.Core;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class TreeQueryTests
    {
        private static FolderNode BuildTree()
        {
            var root = new FolderNode("");
            var project = new FolderNode("Project");

            var file = new FileNode("a.cs", @"C:\src\a.cs");
            var nested = new FileNode("b.cs", @"C:\src\b.cs");

            project.Children.Add(nested);
            root.Children.Add(project);
            root.Children.Add(file);
            return root;
        }

        [Fact]
        public void FindFileByPath_FindsNestedEntry_CaseInsensitive()
        {
            var root = BuildTree();

            var found = TreeQuery.FindFileByPath(root, @"c:\SRC\B.CS");

            Assert.NotNull(found);
            Assert.Equal("b.cs", found.Name);
        }

        [Fact]
        public void FindFileByPath_ReturnsNull_ForUntrackedPath()
        {
            Assert.Null(TreeQuery.FindFileByPath(BuildTree(), @"C:\src\zzz.cs"));
        }

        [Fact]
        public void IsTracked_ReportsTheOwningFolder()
        {
            var root = BuildTree();

            Assert.True(TreeQuery.IsTracked(root, @"C:\src\b.cs", out var container));
            Assert.Equal("Project", container.Name);
        }

        [Fact]
        public void IsTracked_ReturnsFalse_WithoutContainer()
        {
            Assert.False(TreeQuery.IsTracked(BuildTree(), @"C:\src\zzz.cs", out var container));
            Assert.Null(container);
        }

        [Fact]
        public void ResolveAddTarget_UsesSelectedFolder()
        {
            var root = BuildTree();
            var project = (FolderNode)root.Children[0];

            Assert.Same(project, TreeQuery.ResolveAddTarget(root, project));
        }

        [Fact]
        public void ResolveAddTarget_UsesParentOfSelectedFile()
        {
            var root = BuildTree();
            var nested = (FileNode)((FolderNode)root.Children[0]).Children[0];

            var target = TreeQuery.ResolveAddTarget(root, nested);

            Assert.Equal("Project", target.Name);
        }

        [Fact]
        public void ResolveAddTarget_FallsBackToRoot_WhenNothingSelected()
        {
            var root = BuildTree();

            Assert.Same(root, TreeQuery.ResolveAddTarget(root, null));
        }

        [Fact]
        public void IsInside_DetectsDescendantsOnly()
        {
            var root = BuildTree();
            var project = (FolderNode)root.Children[0];
            var nested = (FileNode)project.Children[0];
            var sibling = (FileNode)root.Children[1];

            Assert.True(TreeQuery.IsInside(project, nested));
            Assert.False(TreeQuery.IsInside(project, sibling));
            Assert.False(TreeQuery.IsInside(project, project));
        }
    }

    public class RemovePolicyTests
    {
        [Fact]
        public void SavedCleanFile_NeedsNoConfirmation()
        {
            var file = new FileNode("a.cs", @"C:\src\a.cs");

            Assert.False(RemovePolicy.RequiresConfirmation(file, isDirty: false, descendantCount: 0));
        }

        [Fact]
        public void SavedDirtyFile_NeedsConfirmation()
        {
            var file = new FileNode("a.cs", @"C:\src\a.cs");

            Assert.True(RemovePolicy.RequiresConfirmation(file, isDirty: true, descendantCount: 0));
        }

        [Fact]
        public void UnsavedBuffer_NeedsConfirmation_EvenWhenClean()
        {
            var file = new FileNode("new 3", "new 3");

            Assert.True(RemovePolicy.RequiresConfirmation(file, isDirty: false, descendantCount: 0));
        }

        [Fact]
        public void EmptyFolder_NeedsNoConfirmation()
        {
            Assert.False(RemovePolicy.RequiresConfirmation(new FolderNode("Empty"), false, 0));
        }

        [Fact]
        public void NonEmptyFolder_NeedsConfirmation()
        {
            Assert.True(RemovePolicy.RequiresConfirmation(new FolderNode("Full"), false, 3));
        }

        [Fact]
        public void Describe_StatesThatFilesStayOnDisk()
        {
            var text = RemovePolicy.Describe(new FileNode("a.cs", @"C:\src\a.cs"), 0);

            Assert.Contains("stays on disk", text);
        }

        [Fact]
        public void Describe_WarnsAboutUnsavedBuffer()
        {
            var text = RemovePolicy.Describe(new FileNode("new 3", "new 3"), 0);

            Assert.Contains("unsaved buffer", text);
        }
    }

    public class SelectionStatusTests
    {
        [Fact]
        public void SavedFile_ShowsFullPath()
        {
            var status = SelectionStatus.Describe(new FileNode("a.cs", @"C:\src\a.cs"));

            Assert.Equal(@"File: C:\src\a.cs", status);
        }

        [Fact]
        public void UnsavedBuffer_IsMarkedUnsaved()
        {
            var status = SelectionStatus.Describe(new FileNode("new 3", "new 3"));

            Assert.Equal("File: new 3 (unsaved)", status);
        }

        [Fact]
        public void Folder_ShowsGroupName()
        {
            Assert.Equal("Group: Project", SelectionStatus.Describe(new FolderNode("Project")));
        }

        [Fact]
        public void NoSelection_ShowsHint()
        {
            Assert.Equal(SelectionStatus.Idle, SelectionStatus.Describe(null));
        }
    }
}
