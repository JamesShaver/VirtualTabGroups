using System.Collections.Generic;

namespace VirtualTabGroups.Core
{
    public sealed class FolderNode : TreeNodeModel
    {
        public List<TreeNodeModel> Children { get; } = new List<TreeNodeModel>();

        public FolderNode(string name) : base(name) { }
    }
}
