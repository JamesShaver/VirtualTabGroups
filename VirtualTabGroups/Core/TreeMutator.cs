using System;
using System.Linq;

namespace VirtualTabGroups.Core
{
    public static class TreeMutator
    {
        /// <summary>
        /// Adds a file to the target folder. If a FileNode with the same Path already exists
        /// as a direct child, returns null (no duplicate added). Otherwise uses AliasResolver
        /// to compute a display name unique within the folder, appends, and returns the new node.
        /// </summary>
        public static FileNode AddFile(FolderNode folder, string absolutePath)
        {
            if (folder == null) throw new ArgumentNullException(nameof(folder));
            if (string.IsNullOrEmpty(absolutePath)) throw new ArgumentException("path required", nameof(absolutePath));

            bool alreadyHere = folder.Children
                .OfType<FileNode>()
                .Any(f => string.Equals(f.Path, absolutePath, StringComparison.OrdinalIgnoreCase));
            if (alreadyHere) return null;

            var displayName = AliasResolver.Resolve(folder, absolutePath);
            var fileNode = new FileNode(displayName, absolutePath);
            folder.Children.Add(fileNode);
            return fileNode;
        }

        /// <summary>
        /// Removes the given node from whichever folder contains it. Walks from root.
        /// Returns true if the node was found and removed; false if not in the tree.
        /// </summary>
        public static bool RemoveNode(TreeNodeModel node, FolderNode root)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (root == null) throw new ArgumentNullException(nameof(root));

            var container = FindContainer(root, node);
            if (container == null) return false;

            container.Children.Remove(node);
            return true;
        }

        /// <summary>
        /// Walks the tree from root, returning the folder that directly contains the target node, or null if not found.
        /// </summary>
        public static FolderNode FindContainer(FolderNode root, TreeNodeModel target)
        {
            if (root.Children.Contains(target)) return root;
            foreach (var child in root.Children)
            {
                if (child is FolderNode subFolder)
                {
                    var found = FindContainer(subFolder, target);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
