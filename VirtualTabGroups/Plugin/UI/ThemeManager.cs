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

        // Semantic UI colors. Controls should use these roles instead of mixing
        // raw Notepad++ palette values with SystemColors.
        public Color Surface { get; private set; }
        public Color SurfaceAlt { get; private set; }
        public Color HoverBackground { get; private set; }
        public Color SelectionBackground { get; private set; }
        public Color SelectionText { get; private set; }
        public Color Accent { get; private set; }
        public Color Divider { get; private set; }

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
                Surface           = Background;
                SurfaceAlt        = BackgroundSofter;
                HoverBackground    = BackgroundSofter;
                SelectionBackground = BackgroundHotter;
                SelectionText      = Text;
                Accent             = HotEdge;
                Divider            = Edge;
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
                Surface           = SystemColors.Window;
                SurfaceAlt        = Color.FromArgb(248, 250, 252);
                HoverBackground    = Color.FromArgb(242, 246, 250);
                SelectionBackground = Color.FromArgb(225, 236, 255);
                SelectionText      = Color.FromArgb(24, 55, 92);
                Accent             = Color.FromArgb(0, 120, 212);
                Divider            = Color.FromArgb(225, 229, 234);
            }

            Changed?.Invoke();
        }
    }
}
