using System;
using System.Runtime.InteropServices;

namespace VirtualTabGroups.Plugin.Npp
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FuncItem
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string _itemName;

        public IntPtr _pFunc;
        public int _cmdID;
        public bool _init2Check;
        public ShortcutKey _pShKey;
    }
}
