using System.Windows.Forms;

namespace VirtualTabGroups.Plugin
{
    public sealed class WindowsMessageBoxProxy : IMessageBoxProxy
    {
        public void Show(string title, string body, MessageBoxKind kind)
        {
            var icon = kind switch
            {
                MessageBoxKind.Warning => MessageBoxIcon.Warning,
                MessageBoxKind.Error => MessageBoxIcon.Error,
                _ => MessageBoxIcon.Information,
            };

            Form host = null;
            foreach (Form f in Application.OpenForms)
            {
                if (f.IsHandleCreated) { host = f; break; }
            }

            if (host != null && host.InvokeRequired)
            {
                host.BeginInvoke(new System.Action(() =>
                    MessageBox.Show(host, body, title, MessageBoxButtons.OK, icon)));
            }
            else if (host != null)
            {
                MessageBox.Show(host, body, title, MessageBoxButtons.OK, icon);
            }
            else
            {
                MessageBox.Show(body, title, MessageBoxButtons.OK, icon);
            }
        }
    }
}
