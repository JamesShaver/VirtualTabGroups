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

        /// <summary>
        /// Moves a node from its current container to the destination folder at the given insertion position.
        /// Re-resolves the file's alias if it's a file and the destination has a name clash.
        /// Rejects cyclic moves (a folder cannot be moved into itself or any descendant).
        /// Returns true on success; false if rejected or node not in tree.
        /// </summary>
        public static bool MoveNode(TreeNodeModel node, FolderNode destination, int position, FolderNode root)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (root == null) throw new ArgumentNullException(nameof(root));

            // Cyclic check: dragged folder can't drop into itself or its descendants.
            if (node is FolderNode draggedFolder)
            {
                if (draggedFolder == destination) return false;
                if (IsDescendant(draggedFolder, destination)) return false;
            }

            var sourceContainer = FindContainer(root, node);
            if (sourceContainer == null) return false;

            sourceContainer.Children.Remove(node);

            // Re-resolve alias for files moving between folders.
            if (node is FileNode file && sourceContainer != destination)
            {
                file.Name = AliasResolver.Resolve(destination, file.Path);
            }

            position = Math.Max(0, Math.Min(position, destination.Children.Count));
            destination.Children.Insert(position, node);
            return true;
        }

        /// <summary>
        /// True if 'candidate' is anywhere inside 'ancestor's subtree (any depth).
        /// </summary>
        private static bool IsDescendant(FolderNode ancestor, TreeNodeModel candidate)
        {
            foreach (var child in ancestor.Children)
            {
                if (child == candidate) return true;
                if (child is FolderNode subFolder && IsDescendant(subFolder, candidate)) return true;
            }
            return false;
        }

        /// <summary>
        /// Recursively walks the tree, removing every FileNode whose Path matches (case-insensitive).
        /// Returns the count of removed nodes.
        /// </summary>
        public static int RemoveAllByPath(FolderNode root, string path)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(path)) return 0;

            int total = 0;
            // Walk children list in reverse so removals don't disrupt iteration.
            for (int i = root.Children.Count - 1; i >= 0; i--)
            {
                var child = root.Children[i];
                if (child is FileNode file && string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    root.Children.RemoveAt(i);
                    total++;
                }
                else if (child is FolderNode subFolder)
                {
                    total += RemoveAllByPath(subFolder, path);
                }
            }
            return total;
        }
    }
}
