using System.Drawing;
using System.Windows.Forms;
using VirtualTabGroups.Plugin.UI;

namespace VirtualTabGroups.Plugin
{
    internal sealed class FileNameDialog : Form
    {
        private readonly TextBox _nameBox;

        public string FileName => _nameBox.Text.Trim();

        public FileNameDialog(string currentName, ThemeManager theme)
        {
            Text = "Rename file on disk";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 180);
            AutoScaleMode = AutoScaleMode.Dpi;

            var warning = new Label
            {
                AutoSize = false,
                Location = new Point(16, 14),
                Size = new Size(398, 48),
                Text = "This changes the real file name on disk.\nThe file remains in the same directory."
            };
            var nameLabel = new Label
            {
                AutoSize = true,
                Location = new Point(16, 76),
                Text = "New file name:"
            };
            _nameBox = new TextBox
            {
                Location = new Point(16, 98),
                Size = new Size(398, 24),
                Text = currentName,
                TabIndex = 0
            };

            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(324, 137),
                Size = new Size(90, 28)
            };
            var rename = new Button
            {
                Text = "Rename",
                DialogResult = DialogResult.OK,
                Location = new Point(228, 137),
                Size = new Size(90, 28)
            };

            Controls.AddRange(new Control[] { warning, nameLabel, _nameBox, rename, cancel });
            AcceptButton = rename;
            CancelButton = cancel;

            BackColor = theme.BackgroundDlg;
            ForeColor = theme.Text;
            foreach (Control control in Controls)
            {
                control.BackColor = control is TextBox ? theme.Surface : theme.BackgroundDlg;
                control.ForeColor = theme.Text;
            }
            rename.BackColor = theme.SelectionBackground;
            rename.ForeColor = theme.SelectionText;
            cancel.BackColor = theme.SurfaceAlt;

            Shown += (s, e) =>
            {
                _nameBox.SelectAll();
                _nameBox.Focus();
            };
        }
    }
}
