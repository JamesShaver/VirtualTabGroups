using System;

namespace VirtualTabGroups.Core
{
    /// <summary>
    /// Decides whether removing an entry must be confirmed first.
    ///
    /// A saved, unmodified file is cheap to re-add (it still exists on disk), so
    /// removing its entry is not destructive. An unsaved buffer, a file with
    /// uncommitted edits, or a folder with content loses curated state, so those
    /// are always confirmed.
    /// </summary>
    public static class RemovePolicy
    {
        public static bool RequiresConfirmation(TreeNodeModel target, bool isDirty, int descendantCount)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (target is FolderNode) return descendantCount > 0;
            if (target is FileNode file) return !IsSavedToDisk(file) || isDirty;
            return false;
        }

        /// <summary>
        /// Confirmation text for the given target. Always states that files on disk
        /// are untouched, because the removal only affects the virtual tree.
        /// </summary>
        public static string Describe(TreeNodeModel target, int descendantCount)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (target is FolderNode folder)
            {
                return "Remove '" + folder.Name + "' and " + descendantCount + " item(s)?\n\n"
                    + "Files on disk will not be deleted.";
            }

            if (target is FileNode file)
            {
                return IsSavedToDisk(file)
                    ? "Remove '" + file.Name + "' from the tree?\n\n"
                      + "The file stays on disk. Unsaved changes in Notepad++ are not discarded."
                    : "Remove '" + file.Name + "' from the tree?\n\n"
                      + "This unsaved buffer exists only in Notepad++ memory. Its content is not touched.";
            }

            return null;
        }

        /// <summary>Saved entries carry an absolute path; unsaved buffers do not.</summary>
        public static bool IsSavedToDisk(FileNode file)
            => file != null && !string.IsNullOrEmpty(file.Path) && System.IO.Path.IsPathRooted(file.Path);
    }
}
