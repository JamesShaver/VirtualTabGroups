using System;
using System.Drawing;
using VirtualTabGroups.Plugin.Npp;

namespace VirtualTabGroups.Plugin.UI
{
    public sealed class ThemeManager
    {
        private readonly INppMessageSender _sender;

        public ThemeManager(INppMessageSender sender)
        {
            _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        }

        public bool IsDark { get; private set; }

        public Color Background { get; private set; }
        public Color BackgroundSofter { get; private set; }
        public Color BackgroundHotter { get; private set; }
        public Color BackgroundDlg { get; private set; }
        public Color BackgroundError { get; private set; }
        public Color Text { get; private set; }
        public Color DarkerText { get; private set; }
        public Color DisabledText { get; private set; }
        public Color LinkText { get; private set; }
        public Color Edge { get; private set; }
        public Color HotEdge { get; private set; }
        public Color DisabledEdge { get; private set; }

        public event Action Changed;

        public void Initialize() => RefreshColors();

        public void RefreshColors()
        {
            IsDark = _sender.SendMessage((int)NppMsg.NPPM_ISDARKMODEENABLED, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero;

            if (IsDark)
            {
                var p = _sender.GetDarkModeColors();
                Background       = DarkModeColors.FromColorRef(p.Background);
                BackgroundSofter = DarkModeColors.FromColorRef(p.SofterBackground);
                BackgroundHotter = DarkModeColors.FromColorRef(p.HotBackground);
                BackgroundDlg    = DarkModeColors.FromColorRef(p.PureBackground);
                BackgroundError  = DarkModeColors.FromColorRef(p.ErrorBackground);
                Text             = DarkModeColors.FromColorRef(p.TextColor);
                DarkerText       = DarkModeColors.FromColorRef(p.DarkerTextColor);
                DisabledText     = DarkModeColors.FromColorRef(p.DisabledTextColor);
                LinkText         = DarkModeColors.FromColorRef(p.LinkTextColor);
                Edge             = DarkModeColors.FromColorRef(p.EdgeColor);
                HotEdge          = DarkModeColors.FromColorRef(p.HotEdgeColor);
                DisabledEdge     = DarkModeColors.FromColorRef(p.DisabledEdgeColor);
            }
            else
            {
                Background       = SystemColors.Window;
                BackgroundSofter = SystemColors.Control;
                BackgroundHotter = SystemColors.Highlight;
                BackgroundDlg    = SystemColors.Control;
                BackgroundError  = Color.MistyRose;
                Text             = SystemColors.ControlText;
                DarkerText       = SystemColors.WindowText;
                DisabledText     = SystemColors.GrayText;
                LinkText         = SystemColors.HotTrack;
                Edge             = SystemColors.ControlDark;
                HotEdge          = SystemColors.Highlight;
                DisabledEdge     = SystemColors.ControlDarkDark;
            }

            Changed?.Invoke();
        }
    }
}
