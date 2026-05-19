using System;
using System.Runtime.InteropServices;

namespace VirtualTabGroups.Plugin.Npp
{
    internal static class Win32
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessageStringBuilder(IntPtr hWnd, int Msg, IntPtr wParam, System.Text.StringBuilder lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessageRefInt(IntPtr hWnd, int Msg, IntPtr wParam, ref int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    }
}
