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
    }
}
