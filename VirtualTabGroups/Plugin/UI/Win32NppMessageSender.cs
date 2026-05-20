using System;
using System.Runtime.InteropServices;
using VirtualTabGroups.Plugin.Npp;

namespace VirtualTabGroups.Plugin.UI
{
    public sealed class Win32NppMessageSender : INppMessageSender
    {
        private readonly IntPtr _nppHandle;

        public Win32NppMessageSender(IntPtr nppHandle)
        {
            _nppHandle = nppHandle;
        }

        public IntPtr SendMessage(int msg, IntPtr wParam, IntPtr lParam)
            => Win32.SendMessage(_nppHandle, msg, wParam, lParam);

        public DarkModeColors GetDarkModeColors()
        {
            var colors = new DarkModeColors();
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DarkModeColors)));
            try
            {
                Marshal.StructureToPtr(colors, ptr, false);
                Win32.SendMessage(_nppHandle, (int)NppMsg.NPPM_GETDARKMODECOLORS, new IntPtr(Marshal.SizeOf(typeof(DarkModeColors))), ptr);
                colors = (DarkModeColors)Marshal.PtrToStructure(ptr, typeof(DarkModeColors));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return colors;
        }
    }
}
