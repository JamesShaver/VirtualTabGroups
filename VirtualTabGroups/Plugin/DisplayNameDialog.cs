using System.Drawing;
using System.Windows.Forms;
using VirtualTabGroups.Plugin.UI;

namespace VirtualTabGroups.Plugin
{
    internal sealed class DisplayNameDialog : Form
    {
        private readonly TextBox _nameBox;

        public string DisplayName => _nameBox.Text.Trim();

        public DisplayNameDialog(string currentName, ThemeManager theme)
        {
            Text = "Rename display label";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(390, 125);
            AutoScaleMode = AutoScaleMode.Dpi;

            var label = new Label
            {
                AutoSize = true,
                Location = new Point(16, 16),
                Text = "Virtual name (the file on disk is not changed):"
            };
            _nameBox = new TextBox
            {
                Location = new Point(16, 42),
                Size = new Size(358, 24),
                Text = currentName,
                TabIndex = 0
            };
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(284, 82),
                Size = new Size(90, 28)
            };
            var ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(188, 82),
                Size = new Size(90, 28)
            };

            Controls.AddRange(new Control[] { label, _nameBox, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;

            BackColor = theme.BackgroundDlg;
            ForeColor = theme.Text;
            foreach (Control control in Controls)
            {
                control.BackColor = control is TextBox ? theme.Surface : theme.BackgroundDlg;
                control.ForeColor = theme.Text;
            }
            ok.BackColor = theme.SelectionBackground;
            ok.ForeColor = theme.SelectionText;
            cancel.BackColor = theme.SurfaceAlt;

            Shown += (s, e) =>
            {
                _nameBox.SelectAll();
                _nameBox.Focus();
            };
        }
    }
}
