using System.Collections.Generic;

namespace VirtualTabGroups.Core
{
    public sealed class FolderNode : TreeNodeModel
    {
        public List<TreeNodeModel> Children { get; set; } = new List<TreeNodeModel>();

        public FolderNode(string name) : base(name) { }
    }
}
