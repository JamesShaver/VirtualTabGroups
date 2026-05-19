using System;
using System.Runtime.InteropServices;

namespace VirtualTabGroups.Plugin.Npp
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct tTbData
    {
        public IntPtr hClient;
        public string pszName;
        public int dlgID;
        public NppTbMsg uMask;
        public IntPtr hIconTab;
        public string pszAddInfo;
        public RECT rcFloat;
        public int iPrevCont;
        public string pszModuleName;
    }
}
