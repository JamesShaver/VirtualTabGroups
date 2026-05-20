using System;
using System.Drawing;
using System.Linq;
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
        private DarkAwareToolTip _tooltip;

        // Drag-drop: hover-expand state.
        private TreeNode _hoverNode;
        private readonly Timer _hoverTimer = new Timer { Interval = 500 };

        // Drag-drop: auto-scroll state.
        private readonly Timer _scrollTimer = new Timer { Interval = 100 };
        private int _scrollDirection;

        // Multi-selection set, populated by Ctrl/Shift+click. The set is in addition
        // to WinForms' single SelectedNode (which we treat as the "anchor").
        private readonly System.Collections.Generic.HashSet<TreeNode> _multiSelection
            = new System.Collections.Generic.HashSet<TreeNode>();
        private TreeNode _selectionAnchor;

        public DarkAwareTreeView Tree => _tree;

        /// <summary>
        /// Surfaces a MessageBox with the exception details instead of letting the
        /// exception propagate to Notepad++'s native message pump (which would crash
        /// the host). Safe to call from any UI callback.
        /// </summary>
        private void ReportError(string source, Exception ex)
        {
            VirtualTabGroups.Plugin.CrashLog.WriteException(source, ex);
            try
            {
                var message = $"{source} failed: {ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
                // Use no-owner MessageBox in case panel parenting is wrong or panel handle is bad.
                MessageBox.Show(message, "Virtual Tab Groups error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { /* last-resort fallback */ }
        }

        public void AttachTheme(ThemeManager theme)
        {
            _tree.AttachTheme(theme);
            _menu.Renderer = new VirtualTabGroups.Plugin.UI.DarkAwareToolStripRenderer(theme);

            _tooltip = new VirtualTabGroups.Plugin.UI.DarkAwareToolTip(theme);
            _tree.NodeMouseHover += (s, ev) =>
            {
                if (ev.Node?.Tag is FileNode f)
                    _tooltip.SetToolTip(_tree, f.Path);
            };
            // Hide the tooltip on any mouse-down so it can't sit in the click path
            // for a follow-up double-click. ToolTip windows are TOPMOST and can
            // briefly intercept clicks before they auto-dismiss.
            _tree.MouseDown += (s, ev) => { try { _tooltip?.Hide(_tree); } catch { } };
            _tree.MouseDown += Tree_MouseDownMultiSelect;
        }

        public VirtualTabGroupsPanel()
        {
            Text = "Virtual Tab Groups";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ClientSize = new Size(280, 480);
            StartPosition = FormStartPosition.Manual;

            _tree = new DarkAwareTreeView { Dock = DockStyle.Fill };
            _tree.IsExtraSelected = node => _multiSelection.Contains(node);
            _tree.EmptyStateText = "Right-click here to add open files";
            _tree.ImageList = _icons.Images;
            _tree.ImageIndex = _icons.FolderClosedIndex;
            _tree.SelectedImageIndex = _icons.FolderOpenIndex;
            _tree.AfterSelect += Tree_AfterSelect;
            _tree.AfterExpand += Tree_AfterExpand;
            _tree.AfterCollapse += Tree_AfterCollapse;
            _tree.AfterLabelEdit += Tree_AfterLabelEdit;
            // Use MouseDoubleClick + HitTest instead of NodeMouseDoubleClick.
            // NodeMouseDoubleClick only fires when the click hits WinForms' internal
            // idea of the node label, which doesn't match where we owner-draw the text.
            // MouseDoubleClick + HitTest(x, y) reliably resolves the clicked node
            // regardless of how/where the row is painted.
            _tree.MouseDoubleClick += (s, ev) =>
            {
                try
                {
                    var hit = _tree.HitTest(ev.X, ev.Y);
                    if (hit.Node?.Tag is FileNode f)
                        PluginMain.OpenFile(f.Path);
                }
                catch (Exception ex) { ReportError("Double-click open", ex); }
            };
            _tree.KeyDown += Tree_KeyDown;

            _tree.ContextMenuStrip = _menu;
            _menu.Opening += Menu_Opening;

            // Drag-drop wiring.
            _tree.ItemDrag += (s, ev) =>
            {
                try
                {
                    if (!(ev.Item is TreeNode source)) return;

                    TreeNode[] payload;
                    if (_multiSelection.Contains(source) && _multiSelection.Count > 1)
                    {
                        // Drag the entire multi-selection set, ordered by their position
                        // in the visible tree so insertion order is stable.
                        payload = OrderByVisiblePosition(_multiSelection).ToArray();
                    }
                    else
                    {
                        payload = new[] { source };
                    }

                    VirtualTabGroups.Plugin.CrashLog.Write("ItemDrag: starting drag of " + payload.Length + " node(s) (multiSelection.Count=" + _multiSelection.Count + ")");
                    _tree.DoDragDrop(payload, DragDropEffects.Move);
                }
                catch (Exception ex) { ReportError("Drag start", ex); }
            };
            _tree.DragEnter += (s, ev) =>
            {
                try
                {
                    ev.Effect = ev.Data.GetDataPresent(typeof(TreeNode[]))
                        ? DragDropEffects.Move
                        : DragDropEffects.None;
                }
                catch (Exception ex) { ReportError("Drag enter", ex); }
            };
            _tree.DragOver += Tree_DragOver;
            _tree.DragDrop += Tree_DragDrop;

            // Hover-expand timer.
            _hoverTimer.Tick += (s, ev) =>
            {
                try
                {
                    _hoverTimer.Stop();
                    if (_hoverNode != null && _hoverNode.Tag is FolderNode && !_hoverNode.IsExpanded)
                        _hoverNode.Expand();
                }
                catch (Exception ex) { ReportError("Hover auto-expand", ex); }
            };

            // Auto-scroll timer.
            _scrollTimer.Tick += (s, ev) =>
            {
                try
                {
                    if (_scrollDirection == 0 || _tree.Nodes.Count == 0) return;
                    var node = _tree.TopNode;
                    if (_scrollDirection < 0 && node?.PrevVisibleNode != null) _tree.TopNode = node.PrevVisibleNode;
                    else if (_scrollDirection > 0 && node?.NextVisibleNode != null) _tree.TopNode = node.NextVisibleNode;
                }
                catch (Exception ex) { ReportError("Auto-scroll", ex); }
            };

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

            // All WinForms TreeNode references are invalidated by the Nodes.Clear()
            // below. Drop them from the multi-selection set now so paint callbacks
            // and drag-start checks never see stale pointers.
            _multiSelection.Clear();
            _selectionAnchor = null;

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
            try
            {
                if (e.Node?.Tag is TreeNodeModel m)
                {
                    _currentSelectedId = m.Id;
                    if (_stateStore != null && _root != null)
                        _stateStore.MarkDirty(_root, _currentSelectedId);
                }
            }
            catch (Exception ex) { ReportError("Tree_AfterSelect", ex); }
        }

        private void Tree_AfterExpand(object sender, TreeViewEventArgs e)
        {
            try
            {
                if (e.Node?.Tag is FolderNode f)
                {
                    f.Expanded = true;
                    if (_stateStore != null && _root != null)
                        _stateStore.MarkDirty(_root, _currentSelectedId);
                }
            }
            catch (Exception ex) { ReportError("Tree_AfterExpand", ex); }
        }

        private void Tree_AfterCollapse(object sender, TreeViewEventArgs e)
        {
            try
            {
                if (e.Node?.Tag is FolderNode f)
                {
                    f.Expanded = false;
                    if (_stateStore != null && _root != null)
                        _stateStore.MarkDirty(_root, _currentSelectedId);
                }
            }
            catch (Exception ex) { ReportError("Tree_AfterCollapse", ex); }
        }

        private void Tree_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            try
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
            catch (Exception ex) { ReportError("Tree_AfterLabelEdit", ex); }
        }

        private void Menu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                VirtualTabGroups.Plugin.CrashLog.Write("Menu_Opening: entered");
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
                VirtualTabGroups.Plugin.CrashLog.Write("Menu_Opening: built " + _menu.Items.Count + " items");
            }
            catch (Exception ex)
            {
                ReportError("Building context menu", ex);
                e.Cancel = true;
            }
        }

        private void OnFileOpen(FileNode file)
        {
            try
            {
                PluginMain.OpenFile(file.Path);
            }
            catch (Exception ex)
            {
                ReportError("Open File", ex);
            }
        }
        private void OnAddActive(FolderNode targetFolder)
        {
            try
            {
                VirtualTabGroups.Plugin.CrashLog.Write("OnAddActive: entered, targetFolder=" + (targetFolder?.Name ?? "<null>"));
                if (targetFolder == null) return;

                var path = PluginMain.GetCurrentFullPath();
                VirtualTabGroups.Plugin.CrashLog.Write("OnAddActive: GetCurrentFullPath returned '" + (path ?? "<null>") + "'");
                if (string.IsNullOrEmpty(path)) return;

                var added = TreeMutator.AddFile(targetFolder, path);
                VirtualTabGroups.Plugin.CrashLog.Write("OnAddActive: AddFile returned " + (added != null ? added.Name : "<null>"));
                if (added == null) return;

                var tn = BuildTreeNode(added);
                VirtualTabGroups.Plugin.CrashLog.Write("OnAddActive: BuildTreeNode complete");

                var parentTn = FindByModelId(_tree.Nodes, targetFolder.Id);
                if (parentTn == null) _tree.Nodes.Add(tn);
                else parentTn.Nodes.Add(tn);
                VirtualTabGroups.Plugin.CrashLog.Write("OnAddActive: inserted into TreeView");

                _stateStore?.MarkDirty(_root, _currentSelectedId);
                VirtualTabGroups.Plugin.CrashLog.Write("OnAddActive: MarkDirty complete");
            }
            catch (Exception ex)
            {
                ReportError("Add Active File", ex);
            }
        }
        private void OnAddAllOpen(FolderNode targetFolder)
        {
            try
            {
                if (targetFolder == null) return;
                var paths = PluginMain.GetAllOpenFilePaths();
                if (paths.Length == 0) return;

                var parentTn = FindByModelId(_tree.Nodes, targetFolder.Id);
                bool any = false;

                foreach (var path in paths)
                {
                    if (string.IsNullOrEmpty(path))
                        continue;

                    var added = TreeMutator.AddFile(targetFolder, path);
                    if (added == null) continue;

                    any = true;
                    var tn = BuildTreeNode(added);
                    if (parentTn == null) _tree.Nodes.Add(tn);
                    else parentTn.Nodes.Add(tn);
                }

                if (any) _stateStore?.MarkDirty(_root, _currentSelectedId);
            }
            catch (Exception ex)
            {
                ReportError("Add All Open Files", ex);
            }
        }
        private void OnNewFolder(FolderNode targetFolder)
        {
            try
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
            catch (Exception ex)
            {
                ReportError("New Folder", ex);
            }
        }
        private void OnRemove(TreeNodeModel target)
        {
            try
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
            catch (Exception ex)
            {
                ReportError("Remove", ex);
            }
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

        private void Tree_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                var selected = _tree.SelectedNode;
                var selectedModel = selected?.Tag as TreeNodeModel;

                if (e.KeyCode == Keys.Enter && selectedModel is FileNode file)
                {
                    PluginMain.OpenFile(file.Path);
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.F2 && selected != null)
                {
                    selected.BeginEdit();
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.Delete && selectedModel != null)
                {
                    OnRemove(selectedModel);
                    e.Handled = true;
                    return;
                }

                if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.N)
                {
                    var target = (selectedModel as FolderNode) ?? _root;
                    OnNewFolder(target);
                    e.Handled = true;
                    return;
                }

                if (e.Control && e.Shift && !e.Alt && e.KeyCode == Keys.A)
                {
                    var target = (selectedModel as FolderNode) ?? _root;
                    OnAddActive(target);
                    e.Handled = true;
                    return;
                }

                if (e.Control && e.Shift && e.Alt && e.KeyCode == Keys.A)
                {
                    var target = (selectedModel as FolderNode) ?? _root;
                    OnAddAllOpen(target);
                    e.Handled = true;
                    return;
                }
            }
            catch (Exception ex) { ReportError("Tree_KeyDown", ex); }
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
        // Multi-selection support (Task 48)
        // ──────────────────────────────────────────────

        private void Tree_MouseDownMultiSelect(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.Button != MouseButtons.Left) return;
                var hit = _tree.HitTest(e.X, e.Y);
                if (hit.Node == null) return;

                bool ctrl  = (Control.ModifierKeys & Keys.Control) != 0;
                bool shift = (Control.ModifierKeys & Keys.Shift)   != 0;

                if (ctrl && !shift)
                {
                    // Toggle membership of the clicked node.
                    if (_multiSelection.Contains(hit.Node))
                        _multiSelection.Remove(hit.Node);
                    else
                        _multiSelection.Add(hit.Node);
                    _selectionAnchor = hit.Node;
                }
                else if (shift && !ctrl && _selectionAnchor != null)
                {
                    // Replace the multi-selection with all visible nodes between
                    // the current anchor and the clicked node, inclusive.
                    _multiSelection.Clear();
                    foreach (var rn in VisibleNodesBetween(_selectionAnchor, hit.Node))
                        _multiSelection.Add(rn);
                    // Note: we do NOT update _selectionAnchor on Shift+click so that
                    // successive Shift+clicks all extend from the original anchor.
                }
                else
                {
                    // Plain left-click. If the clicked node is ALREADY part of the
                    // multi-selection, preserve the set — otherwise a drag started
                    // from this MouseDown would have its selection wiped before the
                    // ItemDrag event fires, so only one file would move. Matches the
                    // file-manager convention. The set is only collapsed when the user
                    // clicks somewhere OUTSIDE the current multi-selection.
                    if (!_multiSelection.Contains(hit.Node))
                    {
                        _multiSelection.Clear();
                        _multiSelection.Add(hit.Node);
                    }
                    _selectionAnchor = hit.Node;
                }

                _tree.Invalidate();
            }
            catch (Exception ex) { ReportError("MouseDown multi-select", ex); }
        }

        /// <summary>
        /// Returns all visible nodes between <paramref name="a"/> and <paramref name="b"/>
        /// inclusive, walking NextVisibleNode to honour the user's expanded/collapsed state.
        /// Works regardless of which node appears earlier in the visible order.
        /// </summary>
        private System.Collections.Generic.IEnumerable<TreeNode> VisibleNodesBetween(TreeNode a, TreeNode b)
        {
            // Walk forward from a; if we find b, a is above b.
            for (var cur = a; cur != null; cur = cur.NextVisibleNode)
            {
                if (cur == b)
                {
                    // a is the top node.
                    for (var n = a; n != null; n = n.NextVisibleNode)
                    {
                        yield return n;
                        if (n == b) yield break;
                    }
                    yield break;
                }
            }

            // b is above a — walk from b down to a.
            for (var n = b; n != null; n = n.NextVisibleNode)
            {
                yield return n;
                if (n == a) yield break;
            }
        }

        /// <summary>
        /// Returns <paramref name="nodes"/> ordered by their position in the visible tree
        /// (top-to-bottom). Uses a single forward pass with a HashSet for O(n) membership
        /// tests, where n is the total visible node count.
        /// </summary>
        private System.Collections.Generic.IEnumerable<TreeNode> OrderByVisiblePosition(
            System.Collections.Generic.IEnumerable<TreeNode> nodes)
        {
            var set     = new System.Collections.Generic.HashSet<TreeNode>(nodes);
            var ordered = new System.Collections.Generic.List<TreeNode>(set.Count);

            var first = _tree.Nodes.Count > 0 ? _tree.Nodes[0] : null;
            for (var cur = first; cur != null; cur = cur.NextVisibleNode)
            {
                if (set.Contains(cur)) ordered.Add(cur);
            }
            return ordered;
        }

        // ──────────────────────────────────────────────
        // Drag-and-drop support (Tasks 32–34)
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
            try
            {
                if (!e.Data.GetDataPresent(typeof(TreeNode[])))
                {
                    e.Effect = DragDropEffects.None;
                    return;
                }

                var clientPoint = _tree.PointToClient(new Point(e.X, e.Y));
                var target = _tree.GetNodeAt(clientPoint);

                var draggedSet = (TreeNode[])e.Data.GetData(typeof(TreeNode[]));

                if (target != null)
                {
                    // Cyclic guard: check every dragged node against the target.
                    foreach (var dragged in draggedSet)
                    {
                        if (dragged == target)
                        {
                            e.Effect = DragDropEffects.None;
                            _hoverTimer.Stop();
                            _scrollTimer.Stop();
                            _tree.ClearInsertionLine();
                            return;
                        }
                        if (dragged.Tag is FolderNode draggedFolder
                            && target.Tag is TreeNodeModel targetModel
                            && TreeMutator.FindContainer(draggedFolder, targetModel) != null)
                        {
                            e.Effect = DragDropEffects.None;
                            _hoverTimer.Stop();
                            _scrollTimer.Stop();
                            _tree.ClearInsertionLine();
                            return;
                        }
                    }
                }

                e.Effect = DragDropEffects.Move;

                // Insertion-line indicator.
                if (target != null)
                {
                    var position = ComputeDropPosition(target, clientPoint);
                    switch (position)
                    {
                        case DropPosition.Above:
                            _tree.ShowInsertionLine(target.Bounds.Top);
                            break;
                        case DropPosition.Below:
                            _tree.ShowInsertionLine(target.Bounds.Bottom);
                            break;
                        default:
                            _tree.ClearInsertionLine();
                            break;
                    }
                }
                else
                {
                    _tree.ClearInsertionLine();
                }

                // Hover-timer management for auto-expand.
                if (target != null && target != _hoverNode)
                {
                    _hoverNode = target;
                    _hoverTimer.Stop();
                    if (target.Tag is FolderNode && !target.IsExpanded) _hoverTimer.Start();
                }
                else if (target == null)
                {
                    _hoverNode = null;
                    _hoverTimer.Stop();
                }

                // Auto-scroll near edges.
                const int margin = 20;
                if (clientPoint.Y < margin) { _scrollDirection = -1; _scrollTimer.Start(); }
                else if (clientPoint.Y > _tree.Height - margin) { _scrollDirection = 1; _scrollTimer.Start(); }
                else { _scrollDirection = 0; _scrollTimer.Stop(); }
            }
            catch (Exception ex) { ReportError("Tree_DragOver", ex); }
        }

        private void Tree_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                _hoverTimer.Stop();
                _hoverNode = null;
                _scrollTimer.Stop();
                _tree.ClearInsertionLine();

                if (!e.Data.GetDataPresent(typeof(TreeNode[]))) return;
                var draggedSet = (TreeNode[])e.Data.GetData(typeof(TreeNode[]));
                if (draggedSet.Length == 0) return;

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

                // Move each dragged node in turn. Track moved model IDs so we can
                // re-select them after the full tree rebuild.
                var movedModelIds = new System.Collections.Generic.List<Guid>();
                int currentIndex = insertionIndex;
                foreach (var draggedTn in draggedSet)
                {
                    if (!(draggedTn.Tag is TreeNodeModel draggedModel)) continue;
                    if (!TreeMutator.MoveNode(draggedModel, destinationFolder, currentIndex, _root))
                        continue;
                    movedModelIds.Add(draggedModel.Id);
                    currentIndex++;
                }

                if (movedModelIds.Count == 0) return;

                RefreshFromModel();

                // Re-select all moved nodes in the rebuilt tree.
                _multiSelection.Clear();
                foreach (var id in movedModelIds)
                {
                    var newTn = FindByModelId(_tree.Nodes, id);
                    if (newTn != null) _multiSelection.Add(newTn);
                }

                // Set WinForms selection and anchor to the first moved node.
                var firstTn = FindByModelId(_tree.Nodes, movedModelIds[0]);
                if (firstTn != null)
                {
                    _tree.SelectedNode = firstTn;
                    _selectionAnchor = firstTn;
                    firstTn.EnsureVisible();
                }

                _stateStore?.MarkDirty(_root, _currentSelectedId);
                _tree.Invalidate();
            }
            catch (Exception ex) { ReportError("Tree_DragDrop", ex); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _icons.Dispose();
                _hoverTimer.Dispose();
                _scrollTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
