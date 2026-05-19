using System;

namespace VirtualTabGroups.Core
{
    public abstract class TreeNodeModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; }
        public TreeNodeModel Parent { get; internal set; }

        protected TreeNodeModel(string name)
        {
            Name = name;
        }
    }
}
