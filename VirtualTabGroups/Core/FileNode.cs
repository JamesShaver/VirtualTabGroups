using System;

namespace VirtualTabGroups.Core
{
    public sealed class FileNode : TreeNodeModel
    {
        public string Path { get; internal set; }
        public bool IsPinned { get; internal set; }

        public FileNode(string name, string path) : base(name)
        {
            Path = path;
        }

        /// <summary>
        /// Points the entry at a new path and re-resolves its display label inside
        /// <paramref name="parent"/>. The label is recalculated rather than reset to
        /// the bare file name, so an alias that disambiguated same-named files is
        /// released or re-applied correctly. Returns false when nothing changed.
        /// </summary>
        public bool Relink(FolderNode parent, string newPath)
        {
            if (parent == null || string.IsNullOrEmpty(newPath)) return false;
            if (string.Equals(Path, newPath, StringComparison.OrdinalIgnoreCase)) return false;

            Path = newPath;
            Name = AliasResolver.Resolve(parent, newPath);
            return true;
        }
    }
}
