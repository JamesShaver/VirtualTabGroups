using System;
using System.Drawing;
using System.Windows.Forms;
using VirtualTabGroups.Plugin.Npp;
using VirtualTabGroups.Plugin.UI;

namespace VirtualTabGroups.Plugin
{
    public sealed class VirtualTabGroupsPanel : DockingForm
    {
        private readonly DarkAwareTreeView _tree;

        public DarkAwareTreeView Tree => _tree;

        public VirtualTabGroupsPanel()
        {
            Text = "Virtual Tab Groups";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ClientSize = new Size(280, 480);
            StartPosition = FormStartPosition.Manual;

            _tree = new DarkAwareTreeView
            {
                Dock = DockStyle.Fill,
            };
            Controls.Add(_tree);
        }
    }
}
