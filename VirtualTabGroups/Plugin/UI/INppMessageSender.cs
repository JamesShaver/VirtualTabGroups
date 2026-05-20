using System;
using VirtualTabGroups.Plugin.Npp;

namespace VirtualTabGroups.Plugin.UI
{
    public interface INppMessageSender
    {
        IntPtr SendMessage(int msg, IntPtr wParam, IntPtr lParam);
        DarkModeColors GetDarkModeColors();
    }
}
