using System;
using System.Drawing;
using System.Windows.Forms;
using VirtualTabGroups.Core;
using VirtualTabGroups.Plugin.Npp;
using VirtualTabGroups.Plugin.UI;

namespace VirtualTabGroups.Plugin
{
    public sealed class VirtualTabGroupsPanel : DockingForm
    {
        private readonly DarkAwareTreeView _tree;
        private FolderNode _root;
        private StateStore _stateStore;
        private Guid? _currentSelectedId;

        public DarkAwareTreeView Tree => _tree;

        public VirtualTabGroupsPanel()
        {
            Text = "Virtual Tab Groups";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ClientSize = new Size(280, 480);
            StartPosition = FormStartPosition.Manual;

            _tree = new DarkAwareTreeView { Dock = DockStyle.Fill };
            _tree.AfterSelect += Tree_AfterSelect;
            _tree.AfterExpand += Tree_AfterExpand;
            _tree.AfterCollapse += Tree_AfterCollapse;
            _tree.AfterLabelEdit += Tree_AfterLabelEdit;

            Controls.Add(_tree);
        }

        public void BindRoot(FolderNode root, StateStore stateStore, Guid? lastSelected)
        {
            _root = root;
            _stateStore = stateStore;
            _currentSelectedId = lastSelected;

            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                foreach (var child in root.Children)
                {
                    var tn = BuildTreeNode(child);
                    _tree.Nodes.Add(tn);
                }
            }
            finally
            {
                _tree.EndUpdate();
            }

            if (lastSelected.HasValue)
            {
                var match = FindByModelId(_tree.Nodes, lastSelected.Value);
                if (match != null)
                {
                    _tree.SelectedNode = match;
                    match.EnsureVisible();
                }
            }
        }

        private TreeNode BuildTreeNode(TreeNodeModel model)
        {
            var tn = new TreeNode(model.Name) { Tag = model };

            if (model is FolderNode folder)
            {
                foreach (var child in folder.Children)
                {
                    tn.Nodes.Add(BuildTreeNode(child));
                }
                if (folder.Expanded) tn.Expand();
            }

            return tn;
        }

        private static TreeNode FindByModelId(TreeNodeCollection nodes, Guid id)
        {
            foreach (TreeNode tn in nodes)
            {
                if (tn.Tag is TreeNodeModel m && m.Id == id) return tn;
                var deeper = FindByModelId(tn.Nodes, id);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is TreeNodeModel m)
            {
                _currentSelectedId = m.Id;
                if (_stateStore != null && _root != null)
                    _stateStore.MarkDirty(_root, _currentSelectedId);
            }
        }

        private void Tree_AfterExpand(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is FolderNode f)
            {
                f.Expanded = true;
                if (_stateStore != null && _root != null)
                    _stateStore.MarkDirty(_root, _currentSelectedId);
            }
        }

        private void Tree_AfterCollapse(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is FolderNode f)
            {
                f.Expanded = false;
                if (_stateStore != null && _root != null)
                    _stateStore.MarkDirty(_root, _currentSelectedId);
            }
        }

        private void Tree_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (e.CancelEdit || string.IsNullOrWhiteSpace(e.Label))
            {
                e.CancelEdit = true;
                return;
            }

            if (e.Node?.Tag is TreeNodeModel m)
            {
                m.Name = e.Label;
                if (_stateStore != null && _root != null)
                    _stateStore.MarkDirty(_root, _currentSelectedId);
            }
        }
    }
}
