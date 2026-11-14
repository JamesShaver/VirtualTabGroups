using System;
using System.Collections.Generic;

namespace VirtualTabGroups.Core
{
    /// <summary>
    /// Read-only queries over the virtual tree. Kept free of UI types so the
    /// panel's decision logic can be covered by unit tests.
    /// </summary>
    public static class TreeQuery
    {
        /// <summary>
        /// Finds the first file entry with the given path anywhere in the tree,
        /// or null when the path is not tracked.
        /// </summary>
        public static FileNode FindFileByPath(FolderNode root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;

            foreach (var child in root.Children)
            {
                if (child is FileNode file
                    && string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))
                    return file;

                if (child is FolderNode sub)
                {
                    var found = FindFileByPath(sub, path);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>
        /// True when the path is already tracked anywhere in the tree. On success
        /// <paramref name="container"/> is the folder that owns the entry, so the
        /// caller can tell the user where the file already lives.
        /// </summary>
        public static bool IsTracked(FolderNode root, string path, out FolderNode container)
        {
            container = null;
            var file = FindFileByPath(root, path);
            if (file == null) return false;

            container = TreeMutator.FindContainer(root, file);
            return true;
        }

        /// <summary>
        /// Resolves the folder a new entry belongs to: the selected folder itself,
        /// the folder containing a selected file, or the root when nothing usable
        /// is selected.
        /// </summary>
        public static FolderNode ResolveAddTarget(FolderNode root, TreeNodeModel selected)
        {
            if (root == null) return null;
            if (selected is FolderNode folder) return folder;
            if (selected is FileNode file) return TreeMutator.FindContainer(root, file) ?? root;
            return root;
        }

        /// <summary>
        /// Depth-first walk that returns true when <paramref name="candidate"/> sits
        /// anywhere inside <paramref name="folder"/>. Used to avoid removing a node
        /// twice when both a folder and one of its children are selected.
        /// </summary>
        public static bool IsInside(FolderNode folder, TreeNodeModel candidate)
        {
            if (folder == null || candidate == null) return false;

            foreach (var child in folder.Children)
            {
                if (ReferenceEquals(child, candidate)) return true;
                if (child is FolderNode sub && IsInside(sub, candidate)) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the file entries that repeat a path already seen earlier in
        /// depth-first order; the first entry of each path is the one to keep. An
        /// entry may only be listed once, so removing every returned node leaves no
        /// duplicate behind. Paths compare case-insensitively, matching the rest of
        /// the Windows-oriented code.
        /// </summary>
        public static List<FileNode> FindRedundantFileEntries(FolderNode root)
        {
            var redundant = new List<FileNode>();
            if (root == null) return redundant;

            CollectRedundant(root, new HashSet<string>(StringComparer.OrdinalIgnoreCase), redundant);
            return redundant;
        }

        private static void CollectRedundant(
            FolderNode folder, HashSet<string> seen, List<FileNode> redundant)
        {
            foreach (var child in folder.Children)
            {
                if (child is FolderNode sub)
                {
                    CollectRedundant(sub, seen, redundant);
                    continue;
                }
                if (!(child is FileNode file) || string.IsNullOrEmpty(file.Path)) continue;

                if (!seen.Add(file.Path)) redundant.Add(file);
            }
        }
    }
}
