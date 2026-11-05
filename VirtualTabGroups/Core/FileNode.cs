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
    }
}
