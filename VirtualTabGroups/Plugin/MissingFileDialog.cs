using System;
using System.Drawing;
using System.Windows.Forms;
using VirtualTabGroups.Plugin.UI;

namespace VirtualTabGroups.Plugin
{
    /// <summary>Recovery choice for an out-of-sync virtual entry.</summary>
    internal enum MissingFileAction
    {
        Keep,
        Locate,
        Remove,
    }

    /// <summary>
    /// Shown when a virtual entry cannot be opened because reality moved on:
    /// the unsaved buffer was closed without saving, or the file no longer
    /// exists on disk. Offers recovery paths without any silent data loss:
    /// the entry itself is never destroyed automatically.
    /// </summary>
    internal sealed class MissingFileDialog : Form
    {
        private readonly Label _message;

        public MissingFileAction Action { get; private set; } = MissingFileAction.Keep;

        public MissingFileDialog(string entryName, string reason, ThemeManager theme)
        {
            Text = "Virtual Tab Groups";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(470, 210);
            AutoScaleMode = AutoScaleMode.Dpi;

            var title = new Label
            {
                AutoSize = false,
                Location = new Point(16, 14),
                Size = new Size(438, 22),
                Text = "Can't open '" + entryName + "'",
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold)
            };
            _message = new Label
            {
                AutoSize = false,
                Location = new Point(16, 42),
                Size = new Size(438, 60),
                Text = reason
            };
            var hint = new Label
            {
                AutoSize = false,
                Location = new Point(16, 106),
                Size = new Size(438, 34),
                Text = "The entry stays in the list until you remove it yourself."
            };

            var keep = new Button
            {
                Text = "Keep entry",
                DialogResult = DialogResult.No,
                Location = new Point(16, 168),
                Size = new Size(100, 28)
            };
            var locate = new Button
            {
                Text = "Locate on disk…",
                DialogResult = DialogResult.Yes,
                Location = new Point(240, 168),
                Size = new Size(120, 28)
            };
            var remove = new Button
            {
                Text = "Remove entry",
                DialogResult = DialogResult.OK,
                Location = new Point(366, 168),
                Size = new Size(88, 28)
            };

            Controls.AddRange(new Control[] { title, _message, hint, keep, locate, remove });
            AcceptButton = keep;
            CancelButton = keep;

            BackColor = theme.BackgroundDlg;
            ForeColor = theme.Text;
            foreach (Control control in Controls)
            {
                control.BackColor = theme.BackgroundDlg;
                control.ForeColor = theme.Text;
            }
            keep.BackColor = theme.SelectionBackground;
            keep.ForeColor = theme.SelectionText;
            locate.BackColor = theme.SurfaceAlt;
            remove.BackColor = theme.SurfaceAlt;

            Shown += (s, e) => keep.Focus();
        }

        /// <summary>DialogResult No → Keep, Yes → Locate, OK → Remove.</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (DialogResult == DialogResult.Yes) Action = MissingFileAction.Locate;
            else if (DialogResult == DialogResult.OK) Action = MissingFileAction.Remove;
            else Action = MissingFileAction.Keep;
            base.OnFormClosed(e);
        }
    }
}
