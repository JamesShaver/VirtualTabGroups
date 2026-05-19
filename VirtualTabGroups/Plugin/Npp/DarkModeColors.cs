using System.Drawing;
using System.Runtime.InteropServices;

namespace VirtualTabGroups.Plugin.Npp
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DarkModeColors
    {
        public int Background;
        public int SofterBackground;
        public int HotBackground;
        public int PureBackground;
        public int ErrorBackground;
        public int TextColor;
        public int DarkerTextColor;
        public int DisabledTextColor;
        public int LinkTextColor;
        public int EdgeColor;
        public int HotEdgeColor;
        public int DisabledEdgeColor;

        public static Color FromColorRef(int colorRef) =>
            Color.FromArgb(colorRef & 0xFF, (colorRef >> 8) & 0xFF, (colorRef >> 16) & 0xFF);
    }
}
