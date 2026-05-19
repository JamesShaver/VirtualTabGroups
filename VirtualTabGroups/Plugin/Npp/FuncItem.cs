using System;
using System.Runtime.InteropServices;

namespace VirtualTabGroups.Plugin.Npp
{
    // Mirrors the C++ FuncItem struct from the Notepad++ SDK exactly.
    // _pShKey is a ShortcutKey* pointer (8 bytes on x64) — NOT an embedded ShortcutKey
    // value (4 bytes). Using IntPtr.Zero means no shortcut. Callers must allocate
    // ShortcutKey structs on the unmanaged heap (Marshal.AllocHGlobal) so the pointer
    // remains valid for the lifetime of the process.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FuncItem
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string _itemName;

        public IntPtr _pFunc;
        public int _cmdID;
        public bool _init2Check;
        public IntPtr _pShKey;  // ShortcutKey* — IntPtr.Zero means no shortcut
    }
}
