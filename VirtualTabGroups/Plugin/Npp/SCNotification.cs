using System;
using System.Runtime.InteropServices;

namespace VirtualTabGroups.Plugin.Npp
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NotifyHeader
    {
        public IntPtr hwndFrom;
        public IntPtr idFrom;
        public uint code;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SCNotification
    {
        public NotifyHeader nmhdr;
        public IntPtr position;
        public int ch;
        public int modifiers;
        public int modificationType;
        public IntPtr text;
        public IntPtr length;
        public IntPtr linesAdded;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public IntPtr line;
        public int foldLevelNow;
        public int foldLevelPrev;
        public int margin;
        public int listType;
        public int x;
        public int y;
        public int token;
        public IntPtr annotationLinesAdded;
        public int updated;
        public int listCompletionMethod;
    }
}
