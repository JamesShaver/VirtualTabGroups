namespace VirtualTabGroups.Plugin
{
    public enum MessageBoxKind
    {
        Info,
        Warning,
        Error,
    }

    public interface IMessageBoxProxy
    {
        void Show(string title, string body, MessageBoxKind kind);
    }
}
