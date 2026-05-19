using System;

namespace VirtualTabGroups.Core
{
    public abstract class TreeNodeModel
    {
        public Guid Id { get; internal set; } = Guid.NewGuid();
        public string Name { get; internal set; }

        protected TreeNodeModel(string name)
        {
            Name = name;
        }
    }
}
