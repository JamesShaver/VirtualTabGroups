using System.Runtime.InteropServices;

namespace VirtualTabGroups.Plugin.Npp
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ShortcutKey
    {
        public byte _isCtrl;
        public byte _isAlt;
        public byte _isShift;
        public byte _key;

        public ShortcutKey(bool ctrl, bool alt, bool shift, byte key)
        {
            _isCtrl = (byte)(ctrl ? 1 : 0);
            _isAlt = (byte)(alt ? 1 : 0);
            _isShift = (byte)(shift ? 1 : 0);
            _key = key;
        }
    }
}
