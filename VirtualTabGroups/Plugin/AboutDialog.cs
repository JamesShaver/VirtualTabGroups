using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using VirtualTabGroups.Plugin.UI;

namespace VirtualTabGroups.Plugin
{
    public sealed class AboutDialog : Form
    {
        private const string RepositoryUrl = "https://github.com/shavertech/VirtualTabGroups";

        public AboutDialog(ThemeManager theme)
        {
            Text = "About Virtual Tab Groups";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(360, 220);

            var header = new Label
            {
                Text = "Virtual Tab Groups",
                Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 20),
            };

            var version = new Label
            {
                Text = "Version " + Assembly.GetExecutingAssembly().GetName().Version,
                AutoSize = true,
                Location = new Point(22, 60),
            };

            var description = new Label
            {
                Text = "A logical workspace manager for Notepad++ — organize your open files into virtual folders that persist across sessions.",
                Location = new Point(20, 90),
                Size = new Size(320, 40),
            };

            var repoLink = new LinkLabel
            {
                Text = RepositoryUrl,
                AutoSize = true,
                Location = new Point(20, 135),
            };
            repoLink.LinkClicked += (s, e) => Process.Start(RepositoryUrl);

            var attribution = new Label
            {
                Text = "Uses Newtonsoft.Json (MIT license).",
                AutoSize = true,
                Location = new Point(20, 160),
            };

            var license = new Label
            {
                Text = "Virtual Tab Groups is released under the MIT license.",
                AutoSize = true,
                Location = new Point(20, 180),
            };

            var close = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Location = new Point(264, 184),
                Size = new Size(80, 24),
            };

            Controls.AddRange(new Control[] { header, version, description, repoLink, attribution, license, close });
            AcceptButton = close;
            CancelButton = close;

            ApplyTheme(theme);
        }

        private void ApplyTheme(ThemeManager theme)
        {
            BackColor = theme.BackgroundDlg;
            ForeColor = theme.Text;
            foreach (Control c in Controls)
            {
                c.BackColor = theme.BackgroundDlg;
                c.ForeColor = c is LinkLabel ? theme.LinkText : theme.Text;
            }
        }
    }
}
