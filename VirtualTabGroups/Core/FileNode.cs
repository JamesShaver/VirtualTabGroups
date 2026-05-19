namespace VirtualTabGroups.Core
{
    public sealed class FileNode : TreeNodeModel
    {
        public string Path { get; set; }

        public FileNode(string name, string path) : base(name)
        {
            Path = path;
        }
    }
}
