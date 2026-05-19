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
        private readonly IconCache _icons = new IconCache();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
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
            _tree.ImageList = _icons.Images;
            _tree.ImageIndex = _icons.FolderClosedIndex;
            _tree.SelectedImageIndex = _icons.FolderOpenIndex;
            _tree.AfterSelect += Tree_AfterSelect;
            _tree.AfterExpand += Tree_AfterExpand;
            _tree.AfterCollapse += Tree_AfterCollapse;
            _tree.AfterLabelEdit += Tree_AfterLabelEdit;
            _tree.NodeMouseDoubleClick += (s, ev) =>
            {
                if (ev.Node?.Tag is FileNode f)
                    PluginMain.OpenFile(f.Path);
            };

            _tree.ContextMenuStrip = _menu;
            _menu.Opening += Menu_Opening;

            // Drag-drop wiring.
            _tree.ItemDrag += (s, ev) =>
            {
                if (ev.Item is TreeNode tn) _tree.DoDragDrop(tn, DragDropEffects.Move);
            };
            _tree.DragEnter += (s, ev) =>
            {
                ev.Effect = ev.Data.GetDataPresent(typeof(TreeNode))
                    ? DragDropEffects.Move
                    : DragDropEffects.None;
            };
            _tree.DragOver += Tree_DragOver;
            _tree.DragDrop += Tree_DragDrop;

            try
            {
                using (var stream = GetType().Assembly.GetManifestResourceStream(
                    "VirtualTabGroups.Plugin.Resources.plugin.ico"))
                {
                    if (stream != null) Icon = new System.Drawing.Icon(stream);
                }
            }
            catch { /* missing icon resource is non-fatal */ }

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

        /// <summary>
        /// Rebuilds the TreeView from the current model. Used when external code
        /// (auto-removal on file close) mutates the model directly.
        /// </summary>
        public void RefreshFromModel()
        {
            if (_root == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshFromModel));
                return;
            }
            BindRoot(_root, _stateStore, _currentSelectedId);
        }

        private TreeNode BuildTreeNode(TreeNodeModel model)
        {
            var tn = new TreeNode(model.Name) { Tag = model };

            if (model is FolderNode folder)
            {
                tn.ImageIndex = _icons.FolderClosedIndex;
                tn.SelectedImageIndex = _icons.FolderOpenIndex;
                foreach (var child in folder.Children)
                {
                    tn.Nodes.Add(BuildTreeNode(child));
                }
                if (folder.Expanded) tn.Expand();
            }
            else if (model is FileNode file)
            {
                int idx = _icons.GetIconIndexForFile(file.Path);
                tn.ImageIndex = idx;
                tn.SelectedImageIndex = idx;
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

        private void Menu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _menu.Items.Clear();

            var hit = _tree.HitTest(_tree.PointToClient(Cursor.Position));
            var target = hit.Node?.Tag as TreeNodeModel;
            var targetFolder = target as FolderNode ?? _root;

            if (target is FileNode fileTarget)
            {
                _menu.Items.Add(new ToolStripMenuItem("Open", null, (s, ev) => OnFileOpen(fileTarget)));
                _menu.Items.Add(new ToolStripSeparator());
                _menu.Items.Add(new ToolStripMenuItem("Rename", null, (s, ev) => hit.Node.BeginEdit()));
                _menu.Items.Add(new ToolStripMenuItem("Remove…", null, (s, ev) => OnRemove(fileTarget)));
            }
            else
            {
                _menu.Items.Add(new ToolStripMenuItem("Add Active File", null, (s, ev) => OnAddActive(targetFolder)));
                _menu.Items.Add(new ToolStripMenuItem("Add All Open Files", null, (s, ev) => OnAddAllOpen(targetFolder)));
                _menu.Items.Add(new ToolStripMenuItem("New Folder", null, (s, ev) => OnNewFolder(targetFolder)));

                if (target is FolderNode existingFolder)
                {
                    _menu.Items.Add(new ToolStripSeparator());
                    _menu.Items.Add(new ToolStripMenuItem("Rename", null, (s, ev) => hit.Node.BeginEdit()));
                    _menu.Items.Add(new ToolStripMenuItem("Remove…", null, (s, ev) => OnRemove(existingFolder)));
                    _menu.Items.Add(new ToolStripSeparator());
                    _menu.Items.Add(new ToolStripMenuItem("Expand All", null, (s, ev) => ExpandAllUnder(hit.Node, true)));
                    _menu.Items.Add(new ToolStripMenuItem("Collapse All", null, (s, ev) => ExpandAllUnder(hit.Node, false)));
                }
            }
        }

        // Action stubs — wired in Tasks 26-31.
        private void OnFileOpen(FileNode file)
        {
            PluginMain.OpenFile(file.Path);
        }
        private void OnAddActive(FolderNode targetFolder)
        {
            if (targetFolder == null) return;
            var path = PluginMain.GetCurrentFullPath();
            if (string.IsNullOrEmpty(path)) return;

            var added = TreeMutator.AddFile(targetFolder, path);
            if (added == null) return;

            var tn = BuildTreeNode(added);
            var parentTn = FindByModelId(_tree.Nodes, targetFolder.Id);
            if (parentTn == null) _tree.Nodes.Add(tn);
            else parentTn.Nodes.Add(tn);

            _stateStore?.MarkDirty(_root, _currentSelectedId);
        }
        private void OnAddAllOpen(FolderNode targetFolder)
        {
            if (targetFolder == null) return;
            var paths = PluginMain.GetAllOpenFilePaths();
            if (paths.Length == 0) return;

            var parentTn = FindByModelId(_tree.Nodes, targetFolder.Id);
            bool any = false;

            foreach (var path in paths)
            {
                var added = TreeMutator.AddFile(targetFolder, path);
                if (added == null) continue;

                any = true;
                var tn = BuildTreeNode(added);
                if (parentTn == null) _tree.Nodes.Add(tn);
                else parentTn.Nodes.Add(tn);
            }

            if (any) _stateStore?.MarkDirty(_root, _currentSelectedId);
        }
        private void OnNewFolder(FolderNode targetFolder)
        {
            if (targetFolder == null) return;
            var newFolder = new FolderNode("New Folder");
            targetFolder.Children.Add(newFolder);

            var tn = BuildTreeNode(newFolder);
            var parentTn = FindByModelId(_tree.Nodes, targetFolder.Id);
            if (parentTn == null) _tree.Nodes.Add(tn);
            else { parentTn.Nodes.Add(tn); parentTn.Expand(); }

            _tree.SelectedNode = tn;
            tn.BeginEdit();

            _stateStore?.MarkDirty(_root, _currentSelectedId);
        }
        private void OnRemove(TreeNodeModel target)
        {
            if (target == null) return;

            if (target is FolderNode folder && folder.Children.Count > 0)
            {
                int count = CountDescendants(folder);
                var result = MessageBox.Show(
                    this,
                    $"Remove '{folder.Name}' and {count} item(s)?\n\nFiles on disk will not be deleted.",
                    "Virtual Tab Groups",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.OK) return;
            }

            if (!TreeMutator.RemoveNode(target, _root)) return;

            var tn = FindByModelId(_tree.Nodes, target.Id);
            if (tn != null) tn.Remove();

            _stateStore?.MarkDirty(_root, _currentSelectedId);
        }

        private static int CountDescendants(FolderNode folder)
        {
            int total = 0;
            foreach (var child in folder.Children)
            {
                total++;
                if (child is FolderNode sub) total += CountDescendants(sub);
            }
            return total;
        }

        private void ExpandAllUnder(TreeNode tn, bool expand)
        {
            void Recurse(TreeNode n)
            {
                if (expand) n.Expand(); else n.Collapse();
                foreach (TreeNode c in n.Nodes) Recurse(c);
            }
            Recurse(tn);
        }

        // ──────────────────────────────────────────────
        // Drag-and-drop support (Task 32)
        // ──────────────────────────────────────────────

        private enum DropPosition { Above, Into, Below, None }

        private DropPosition ComputeDropPosition(TreeNode target, Point clientPoint)
        {
            if (target == null) return DropPosition.None;
            var bounds = target.Bounds;
            int third = bounds.Height / 4;
            if (clientPoint.Y < bounds.Top + third) return DropPosition.Above;
            if (clientPoint.Y > bounds.Bottom - third) return DropPosition.Below;
            return target.Tag is FolderNode ? DropPosition.Into : DropPosition.Below;
        }

        private void Tree_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(TreeNode))) { e.Effect = DragDropEffects.None; return; }

            var clientPoint = _tree.PointToClient(new Point(e.X, e.Y));
            var target = _tree.GetNodeAt(clientPoint);

            var dragged = (TreeNode)e.Data.GetData(typeof(TreeNode));
            if (target == null) { e.Effect = DragDropEffects.Move; return; }

            if (dragged.Tag is FolderNode draggedFolder && target.Tag is TreeNodeModel targetModel)
            {
                if (target == dragged) { e.Effect = DragDropEffects.None; return; }
                if (TreeMutator.FindContainer(draggedFolder, targetModel) != null)
                {
                    e.Effect = DragDropEffects.None;
                    return;
                }
            }

            e.Effect = DragDropEffects.Move;
        }

        private void Tree_DragDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(TreeNode))) return;

            var dragged = (TreeNode)e.Data.GetData(typeof(TreeNode));
            var draggedModel = dragged.Tag as TreeNodeModel;
            if (draggedModel == null) return;

            var clientPoint = _tree.PointToClient(new Point(e.X, e.Y));
            var target = _tree.GetNodeAt(clientPoint);
            var position = ComputeDropPosition(target, clientPoint);

            FolderNode destinationFolder;
            int insertionIndex;

            if (target == null)
            {
                destinationFolder = _root;
                insertionIndex = _root.Children.Count;
            }
            else
            {
                var targetModel = (TreeNodeModel)target.Tag;

                switch (position)
                {
                    case DropPosition.Above:
                        destinationFolder = TreeMutator.FindContainer(_root, targetModel) ?? _root;
                        insertionIndex = destinationFolder.Children.IndexOf(targetModel);
                        break;
                    case DropPosition.Below:
                        destinationFolder = TreeMutator.FindContainer(_root, targetModel) ?? _root;
                        insertionIndex = destinationFolder.Children.IndexOf(targetModel) + 1;
                        break;
                    case DropPosition.Into:
                    default:
                        destinationFolder = (FolderNode)targetModel;
                        insertionIndex = destinationFolder.Children.Count;
                        break;
                }
            }

            if (!TreeMutator.MoveNode(draggedModel, destinationFolder, insertionIndex, _root))
                return;

            RefreshFromModel();

            var newTn = FindByModelId(_tree.Nodes, draggedModel.Id);
            if (newTn != null) { _tree.SelectedNode = newTn; newTn.EnsureVisible(); }

            _stateStore?.MarkDirty(_root, draggedModel.Id);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _icons.Dispose();
            base.Dispose(disposing);
        }
    }
}
