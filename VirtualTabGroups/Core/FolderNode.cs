using System.Collections.Generic;

namespace VirtualTabGroups.Core
{
    public sealed class FolderNode : TreeNodeModel
    {
        public List<TreeNodeModel> Children { get; } = new List<TreeNodeModel>();

        /// <summary>
        /// Whether the folder is expanded in the tree view UI.
        /// Persisted to state.json so the UI restores its collapsed/expanded state.
        /// </summary>
        public bool Expanded { get; set; }

        public FolderNode(string name) : base(name) { }
    }
}
