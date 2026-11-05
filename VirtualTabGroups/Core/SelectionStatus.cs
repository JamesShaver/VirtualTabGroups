namespace VirtualTabGroups.Core
{
    /// <summary>
    /// Builds the status-bar description of the current selection. Files show
    /// their full path so it can be read and copied; unsaved buffers are marked
    /// because their "path" is only a buffer name.
    /// </summary>
    public static class SelectionStatus
    {
        public const string Idle = "Right-click for more actions";

        public static string Describe(TreeNodeModel selected)
        {
            if (selected is FolderNode folder) return "Group: " + folder.Name;

            if (selected is FileNode file)
            {
                return RemovePolicy.IsSavedToDisk(file)
                    ? "File: " + file.Path
                    : "File: " + file.Name + " (unsaved)";
            }

            return Idle;
        }
    }
}
