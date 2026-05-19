using System;
using System.Drawing;
using VirtualTabGroups.Plugin.Npp;
using VirtualTabGroups.Plugin.UI;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class ThemeManagerTests
    {
        private sealed class FakeNppSender : INppMessageSender
        {
            public bool DarkModeOn;
            public DarkModeColors Palette;

            public IntPtr SendMessage(int msg, IntPtr wParam, IntPtr lParam)
            {
                if (msg == (int)NppMsg.NPPM_ISDARKMODEENABLED)
                    return DarkModeOn ? new IntPtr(1) : IntPtr.Zero;
                return IntPtr.Zero;
            }

            public DarkModeColors GetDarkModeColors() => Palette;
        }

        [Fact]
        public void Initialize_WhenDarkModeOn_PopulatesFromPalette()
        {
            var fake = new FakeNppSender
            {
                DarkModeOn = true,
                Palette = new DarkModeColors
                {
                    Background = 0x002B2B2B,
                    TextColor = 0x00DDDDDD,
                    EdgeColor = 0x00555555,
                },
            };
            var tm = new ThemeManager(fake);

            tm.Initialize();

            Assert.True(tm.IsDark);
            Assert.Equal(Color.FromArgb(0x2B, 0x2B, 0x2B), tm.Background);
            Assert.Equal(Color.FromArgb(0xDD, 0xDD, 0xDD), tm.Text);
            Assert.Equal(Color.FromArgb(0x55, 0x55, 0x55), tm.Edge);
        }

        [Fact]
        public void Initialize_WhenLightMode_UsesSystemColors()
        {
            var fake = new FakeNppSender { DarkModeOn = false };
            var tm = new ThemeManager(fake);

            tm.Initialize();

            Assert.False(tm.IsDark);
            Assert.Equal(SystemColors.Window, tm.Background);
            Assert.Equal(SystemColors.ControlText, tm.Text);
        }
    }
}
