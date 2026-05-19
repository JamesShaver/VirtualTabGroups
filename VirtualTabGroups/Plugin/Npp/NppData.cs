using System;
using System.Runtime.InteropServices;

namespace VirtualTabGroups.Plugin.Npp
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NppData
    {
        public IntPtr _nppHandle;
        public IntPtr _scintillaMainHandle;
        public IntPtr _scintillaSecondHandle;
    }
}
