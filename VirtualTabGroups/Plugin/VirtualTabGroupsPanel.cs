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
        private ThemeManager _theme;
        private Panel _header;
        private Panel _statusBar;
        private Label _titleLabel;
        private Label _metaLabel;
        private Label _statusLabel;
        private FlowLayoutPanel _toolbar;
        private bool _suppressSingleClickOpen;

        // Non-blocking feedback channel: a message is shown for a moment, then the
        // status falls back to the description of the current selection.
        private readonly Timer _statusResetTimer = new Timer { Interval = 1500 };
        private bool _statusShowsTransient;

        // Long-press hint: announces what releasing will do without acting on the timer.
        private readonly Timer _pressHintTimer = new Timer();

        // Press gesture state (single click / double click / long press).
        // The long press is evaluated on MouseUp — never on a timer — so an OLE
        // drag (which swallows MouseUp) can never trigger the close action.
        private const int LongPressThresholdMs = 650;
        private TreeNode _pressNode;
        private Point _pressStart;
        private DateTime _pressAt;
        private TreeNode _pendingClickNode;
        private DateTime _pendingClickAt;
        private readonly Timer _doubleClickTimer = new Timer { Interval = SystemInformation.DoubleClickTime };

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

        // Native tab-bar drag observation (Notepad++ tab drags are not OLE drags).
        private Npp.NativeTabDragObserver _tabDragObserver;
        private readonly Timer _tabDragPollTimer = new Timer { Interval = 50 };
        private string _tabDragPath;
        private string _tabDropTargetFolderId;
        private bool _tabDragWasOverTree;

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
            _theme = theme;
            _tree.AttachTheme(theme);
            _menu.Renderer = new VirtualTabGroups.Plugin.UI.DarkAwareToolStripRenderer(theme);
            _theme.Changed += ApplyChromeTheme;

            _tree.MouseDown += Tree_MouseDownMultiSelect;
            ApplyChromeTheme();
        }

        /// <summary>
        /// Wires the native tab-bar drag observer. Called by PluginMain after the
        /// panel and theme are ready; safe to call again (replaces the observer).
        /// Internal: NativeTabDragObserver is an internal infrastructure type.
        /// </summary>
        internal void AttachTabDragObserver(Npp.NativeTabDragObserver observer)
        {
            _tabDragObserver = observer;
            _tabDragObserver.DragOverTabChanged += TabDrag_OverTabChanged;
            _tabDragObserver.DragReleased += TabDrag_Released;
            _tabDragObserver.DragCancelled += TabDrag_Cancelled;
            _tabDragPollTimer.Start();
        }

        private void TabDrag_OverTabChanged(string path)
        {
            _tabDragPath = path;
            UpdateTabDragFeedback();
        }

        /// <summary>
        /// While a native tab drag is in flight, the tab bar holds mouse capture, so
        /// WM_SETCURSOR never reaches our window and the host keeps resetting the
        /// cursor to its own no-drop image. The poll timer (the only channel we have)
        /// additionally sets the cursor and refreshes the drop indicator: "move"
        /// cursor + insertion line/highlight over the tree, plain arrow outside it.
        /// </summary>
        private void UpdateTabDragFeedback()
        {
            if (_tabDragPath == null)
            {
                if (_tabDragWasOverTree) { _tree.ClearDropIndicators(); _tabDragWasOverTree = false; }
                return;
            }

            var cursorPos = System.Windows.Forms.Cursor.Position;
            var client = _tree.PointToClient(cursorPos);
            bool overTree = _tree.ClientRectangle.Contains(client);

            if (!overTree)
            {
                if (_tabDragWasOverTree) { _tree.ClearDropIndicators(); _tabDragWasOverTree = false; }
                _tabDropTargetFolderId = null;
                // Neutral arrow while over other windows: the drag may still come back.
                Win32.SetCursor(Win32.LoadCursor(IntPtr.Zero, Win32.IDC_ARROW));
                _statusLabel.Text = "Drop to add: " + System.IO.Path.GetFileName(_tabDragPath);
                return;
            }

            // Over the tree: accept the drop. Show the same indicator the OLE path
            // uses and set the "move" cursor (the host cannot override it here).
            var target = _tree.GetNodeAt(client);
            if (target != null)
            {
                var position = ComputeDropPosition(target, client);
                switch (position)
                {
                    case DropPosition.Above: _tree.ShowInsertionLine(target.Bounds.Top); break;
                    case DropPosition.Below: _tree.ShowInsertionLine(target.Bounds.Bottom); break;
                    case DropPosition.Into:  _tree.ShowDropHighlight(target); break;
                }
            }
            else if (_tree.Nodes.Count == 0)
            {
                // Empty tree: highlight the placeholder card so the user sees
                // the drop is accepted into the root.
                _tree.ShowEmptyStateDropHighlight();
            }
            else
            {
                _tree.ClearDropIndicators();
            }

            var folder = GetDropFolder(target, client);
            _tabDropTargetFolderId = folder?.Id.ToString();
            _statusLabel.Text = "Drop to add: " + System.IO.Path.GetFileName(_tabDragPath)
                + (folder == null ? "" : "  →  " + folder.Name);

            Win32.SetCursor(System.Windows.Forms.Cursors.SizeAll.Handle);
            _tabDragWasOverTree = true;
        }

        private void TabDrag_Released(System.Drawing.Point screenPoint)
        {
            var path = _tabDragPath;
            _tabDragPath = null;
            _tabDropTargetFolderId = null;
            _tabDragWasOverTree = false;
            _tree.ClearDropIndicators();

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                // Only accept when the button was released inside the panel's tree.
                if (_tree.ClientRectangle.Contains(_tree.PointToClient(screenPoint)))
                {
                    var folder = ResolveDropFolderForScreenPoint(screenPoint);
                    if (folder != null)
                    {
                        AddExternalFiles(new[] { path }, folder);
                        _statusLabel.Text = "Added: " + System.IO.Path.GetFileName(path) + "  →  " + folder.Name;
                    }
                }
            }
            catch (Exception ex) { ReportError("Native tab drop", ex); }
        }

        private void TabDrag_Cancelled()
        {
            _tabDragPath = null;
            _tabDropTargetFolderId = null;
            _tree.ClearDropIndicators();
            _tabDragWasOverTree = false;
            UpdateChrome();
        }

        private FolderNode ResolveDropFolderForScreenPoint(System.Drawing.Point screenPoint)
        {
            var client = _tree.PointToClient(screenPoint);
            var target = _tree.GetNodeAt(client);
            return GetDropFolder(target, client);
        }

        public VirtualTabGroupsPanel()
        {
            Text = "Virtual Tab Groups";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(360, 620);
            MinimumSize = new Size(280, 320);
            StartPosition = FormStartPosition.Manual;

            _tree = new DarkAwareTreeView { Dock = DockStyle.Fill };
            _tree.IsExtraSelected = node => _multiSelection.Contains(node);
            _tree.IsDirtyIndicator = IsDirtyIndicator;
            _tree.EmptyStateText = "Right-click a group to add open files";
            _tree.IsPinnedIndicator = node => node?.Tag is FileNode pinned && pinned.IsPinned;
            _tree.ImageList = _icons.Images;
            _tree.ImageIndex = _icons.FolderClosedIndex;
            _tree.SelectedImageIndex = _icons.FolderOpenIndex;
            _tree.AfterSelect += Tree_AfterSelect;
            _tree.AfterExpand += Tree_AfterExpand;
            _tree.AfterCollapse += Tree_AfterCollapse;
            // Double-click is handled by the explicit MouseUp state machine below.
            // Do not subscribe to WinForms MouseDoubleClick: its native click state
            // can turn a fast release/long-press sequence into a false double-click.
            _doubleClickTimer.Tick += (s, e) => CommitPendingSingleClick();
            _statusResetTimer.Tick += (s, e) =>
            {
                _statusResetTimer.Stop();
                _statusShowsTransient = false;
                UpdateChrome();
            };
            _pressHintTimer.Interval = LongPressThresholdMs;
            _pressHintTimer.Tick += (s, e) =>
            {
                _pressHintTimer.Stop();
                if (_pressNode?.Tag is FileNode hinted)
                    _statusLabel.Text = "Release to close: " + hinted.Name;
            };
            _tree.MouseDown += Tree_MouseDownLongPress;
            _tree.MouseMove += Tree_MouseMoveLongPress;
            _tree.MouseUp += Tree_MouseUpLongPress;
            _tree.KeyDown += Tree_KeyDown;

            _tree.ContextMenuStrip = _menu;
            _menu.Opening += Menu_Opening;

            // Native tab drag-drop wiring (tab bar -> panel).
            _tabDragPollTimer.Tick += (s, ev) => _tabDragObserver?.Poll();

            // Drag-drop wiring.
            _tree.ItemDrag += (s, ev) =>
            {
                try
                {
                    if (!(ev.Item is TreeNode source)) return;

                    // A drag has started: it wins over the press gestures. MouseUp is
                    // swallowed by the OLE drag loop, so clear pending press/click
                    // state here — otherwise it would leak into the next real click.
                    _pressNode = null;
                    _pendingClickNode = null;
                    _doubleClickTimer.Stop();

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

                    _pressHintTimer.Stop();
                    _tree.DoDragDrop(payload, DragDropEffects.Move);
                }
                catch (Exception ex) { ReportError("Drag start", ex); }
            };
            _tree.DragEnter += (s, ev) =>
            {
                try
                {
                    string[] paths;
                    ev.Effect = ev.Data.GetDataPresent(typeof(TreeNode[]))
                        || TryGetExternalPaths(ev.Data, out paths)
                        ? DragDropEffects.Move
                        : DragDropEffects.None;
                }
                catch (Exception ex) { ReportError("Drag enter", ex); }
            };
            _tree.DragOver += Tree_DragOver;
            _tree.DragDrop += Tree_DragDrop;
            _tree.DragLeave += (s, ev) =>
            {
                try
                {
                    _hoverTimer.Stop();
                    _hoverNode = null;
                    _scrollTimer.Stop();
                    _scrollDirection = 0;
                    _tree.ClearDropIndicators();
                }
                catch (Exception ex) { ReportError("Drag leave", ex); }
            };

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

            BuildChrome();
            Controls.Add(_tree);
            Controls.Add(_statusBar);
            Controls.Add(_header);
        }

        private void BuildChrome()
        {
            _header = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(14, 10, 14, 8) };
            _titleLabel = new Label
            {
                AutoSize = true,
                Text = "Virtual Tab Groups",
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                Location = new Point(14, 9)
            };
            _metaLabel = new Label
            {
                AutoSize = true,
                Text = "No items",
                Font = new Font("Segoe UI", 8.25F),
                Location = new Point(15, 32)
            };
            _toolbar = new FlowLayoutPanel
            {
                Location = new Point(10, 54),
                Size = new Size(Width - 20, 30),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0),
                TabStop = false
            };
            AddToolbarButton("Save all", "Save every modified document", (s, e) => OnSaveAll());
            AddToolbarButton("New folder", "Create a virtual folder", (s, e) => OnNewFolder(GetTargetFolder()));
            AddToolbarButton("Collapse all", "Collapse every group", (s, e) => ExpandAllTree(false));

            _header.Controls.Add(_titleLabel);
            _header.Controls.Add(_metaLabel);
            _header.Controls.Add(_toolbar);

            _statusBar = new Panel { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(14, 4, 14, 4) };
            _statusLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.25F),
                Text = SelectionStatus.Idle,
                Cursor = Cursors.Hand
            };
            _statusLabel.Click += (s, e) => OnCopySelectedPath();
            _statusBar.Controls.Add(_statusLabel);
        }

        private Button AddToolbarButton(string text, string accessibilityDescription, EventHandler action)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25F),
                Margin = new Padding(0, 0, 6, 0),
                Padding = new Padding(8, 0, 8, 0),
                TabStop = false,
                AccessibleName = accessibilityDescription
            };
            button.FlatAppearance.BorderSize = 1;
            // Buttons never keep focus: the tree owns F2 / Delete / Ctrl+N, and a
            // focused button silently swallowed those shortcuts.
            button.Click += (s, e) =>
            {
                try { action(s, e); }
                catch (Exception ex) { ReportError("Toolbar: " + text, ex); }
                finally { if (!_tree.IsDisposed) _tree.Focus(); }
            };
            _toolbar.Controls.Add(button);
            return button;
        }

        private FolderNode GetTargetFolder()
        {
            return TreeQuery.ResolveAddTarget(_root, _tree.SelectedNode?.Tag as TreeNodeModel);
        }

        /// <summary>Children in display order: pinned files first, then model order.</summary>
        private static System.Collections.Generic.IEnumerable<TreeNodeModel> OrderedChildren(FolderNode folder)
        {
            if (folder == null) return Array.Empty<TreeNodeModel>();
            return folder.Children.OrderBy(node => node is FileNode file && file.IsPinned ? 0 : 1);
        }

        private void RebuildVisibleTree()
        {
            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                foreach (var child in OrderedChildren(_root))
                {
                    _tree.Nodes.Add(BuildTreeNode(child));
                }
            }
            finally
            {
                _tree.EndUpdate();
            }
            _tree.SelectedNode = null;
        }

        private void ApplyChromeTheme()
        {
            if (_theme == null || _header == null) return;
            BackColor = _theme.Surface;
            _header.BackColor = _theme.SurfaceAlt;
            _statusBar.BackColor = _theme.SurfaceAlt;
            _titleLabel.ForeColor = _theme.Text;
            _metaLabel.ForeColor = _theme.DarkerText;
            _statusLabel.ForeColor = _theme.DisabledText;
            foreach (Control control in _toolbar.Controls)
            {
                control.BackColor = _theme.SurfaceAlt;
                control.ForeColor = _theme.Text;
                if (control is Button button)
                {
                    button.FlatAppearance.BorderColor = _theme.Divider;
                    button.FlatAppearance.MouseOverBackColor = _theme.HoverBackground;
                    button.FlatAppearance.MouseDownBackColor = _theme.SelectionBackground;
                }
            }
            _header.Invalidate();
            _statusBar.Invalidate();
        }

        private void UpdateChrome()
        {
            if (_root == null || _metaLabel == null) return;
            int files = 0;
            int folders = 0;
            CountNodes(_root, ref files, ref folders);
            _titleLabel.Text = "Virtual Tab Groups";
            _metaLabel.Text = (_stateStore?.IsReadOnly == true ? "Read-only · " : "")
                + files + (files == 1 ? " file" : " files") + " · "
                + folders + (folders == 1 ? " group" : " groups");

            // Feedback for the last action wins until its timer elapses; otherwise the
            // status line describes the selection (full path, copyable).
            if (!_statusShowsTransient)
                _statusLabel.Text = SelectionStatus.Describe(_tree.SelectedNode?.Tag as TreeNodeModel);
        }

        private static void CountNodes(FolderNode folder, ref int files, ref int folders)
        {
            foreach (var child in folder.Children)
            {
                if (child is FolderNode nested)
                {
                    folders++;
                    CountNodes(nested, ref files, ref folders);
                }
                else if (child is FileNode)
                {
                    files++;
                }
            }
        }

        private bool _dirtySubscriptionWired;

        public void BindRoot(FolderNode root, StateStore stateStore, Guid? lastSelected)
        {
            _root = root;
            _stateStore = stateStore;
            _currentSelectedId = lastSelected;
            WireDirtySubscription();

            // All WinForms TreeNode references are invalidated by the Nodes.Clear()
            // below. Drop them from the multi-selection set now so paint callbacks
            // and drag-start checks never see stale pointers.
            _multiSelection.Clear();
            _selectionAnchor = null;

            _suppressSingleClickOpen = true;
            RebuildVisibleTree();

            if (lastSelected.HasValue)
            {
                var match = FindByModelId(_tree.Nodes, lastSelected.Value);
                if (match != null)
                {
                    _tree.SelectedNode = match;
                    match.EnsureVisible();
                }
            }
            _suppressSingleClickOpen = false;
            UpdateNodeToolTips();
            UpdateChrome();
        }

        /// <summary>
        /// Rebuilds the TreeView from the current model. Used when external code
        /// changes the model; closing a file does not remove history entries.
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
                foreach (var child in OrderedChildren(folder))
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

        /// <summary>
        /// Subscribes the tree to dirty-state changes once per panel lifetime.
        /// The tracker is a process-wide singleton owned by PluginMain.
        /// </summary>
        private void WireDirtySubscription()
        {
            if (_dirtySubscriptionWired) return;
            var tracker = PluginMain.DirtyTracker;
            if (tracker == null) return;
            _dirtySubscriptionWired = true;
            tracker.Changed += (key, isDirty) =>
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(() => RedrawFileRowByKey(key)));
            };
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

        /// <summary>
        /// Dirty indicator predicate for the tree painter: a file row shows the dot
        /// when its key (absolute path or unsaved buffer name) is marked dirty.
        /// </summary>
        private bool IsDirtyIndicator(TreeNode tn)
        {
            return tn?.Tag is FileNode file
                && PluginMain.DirtyTracker != null
                && PluginMain.DirtyTracker.IsDirty(file.Path);
        }

        /// <summary>
        /// Redraws the row(s) of every file node whose key matches after a
        /// dirty-state change — without rebuilding the tree (no flicker, keeps
        /// expand/selection state). Safe when the key is not visible in the tree.
        /// </summary>
        internal void RedrawFileRowByKey(string key)
        {
            if (_root == null || string.IsNullOrEmpty(key)) return;
            if (_tree.IsDisposed) return;
            try
            {
                RedrawFileRowsRecursive(_tree.Nodes, key);
            }
            catch (Exception ex)
            {
                ReportError("RedrawFileRowByKey", ex);
            }
        }

        private void RedrawFileRowsRecursive(TreeNodeCollection nodes, string key)
        {
            foreach (TreeNode tn in nodes)
            {
                if (tn.Tag is FileNode f
                    && string.Equals(f.Path, key, StringComparison.OrdinalIgnoreCase))
                {
                    _tree.RedrawNode(tn);
                }
                RedrawFileRowsRecursive(tn.Nodes, key);
            }
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
                    UpdateChrome();
                    // Single click only selects. Files open; folders do NOT switch the
                    // panel view — the folder workspace was removed by design.
                    if (!_suppressSingleClickOpen && m is FileNode file)
                        OpenFileWithRecovery(file);
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

        private void Menu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _menu.Items.Clear();

                var hit = _tree.HitTest(_tree.PointToClient(Cursor.Position));
                var target = hit.Node?.Tag as TreeNodeModel;
                var targetFolder = target as FolderNode ?? _root;

                if (target is FileNode fileTarget)
                {
                    // Saved files have rooted paths (e.g., "C:\src\foo.cs"). Unsaved buffers
                    // have names like "new 40" — there's no on-disk file to reveal or path
                    // worth copying, so those items are shown but grayed out.
                    bool isOnDisk = System.IO.Path.IsPathRooted(fileTarget.Path);

                    _menu.Items.Add(new ToolStripMenuItem("Open", null, (s, ev) => OnFileOpen(fileTarget)));
                    _menu.Items.Add(new ToolStripSeparator());

                    var revealItem = new ToolStripMenuItem("Reveal in Explorer", null,
                        (s, ev) => OnRevealInExplorer(fileTarget)) { Enabled = isOnDisk };
                    _menu.Items.Add(revealItem);

                    var copyPathItem = new ToolStripMenuItem("Copy full path", null,
                        (s, ev) => OnCopyFullPath(fileTarget)) { Enabled = isOnDisk };
                    _menu.Items.Add(copyPathItem);

                    _menu.Items.Add(new ToolStripSeparator());
                    _menu.Items.Add(new ToolStripMenuItem("Rename display label", null, (s, ev) => RenameDisplayNode(hit.Node)));
                    _menu.Items.Add(new ToolStripMenuItem(
                        fileTarget.IsPinned ? "Unpin from top" : "Pin to top",
                        null,
                        (s, ev) => OnTogglePin(fileTarget)));
                    _menu.Items.Add(new ToolStripMenuItem("Remove from history…", null, (s, ev) => OnRemove(fileTarget)));
                }
                else
                {
                    _menu.Items.Add(new ToolStripMenuItem("Add Active File", null, (s, ev) => OnAddActive(targetFolder)));
                    _menu.Items.Add(new ToolStripMenuItem("Add All Open Files", null, (s, ev) => OnAddAllOpen(targetFolder)));
                    _menu.Items.Add(new ToolStripMenuItem("New Folder", null, (s, ev) => OnNewFolder(targetFolder)));

                    if (target is FolderNode existingFolder)
                    {
                        _menu.Items.Add(new ToolStripSeparator());
                        _menu.Items.Add(new ToolStripMenuItem("Rename", null, (s, ev) => RenameDisplayNode(hit.Node)));
                        _menu.Items.Add(new ToolStripMenuItem("Remove…", null, (s, ev) => OnRemove(existingFolder)));
                        _menu.Items.Add(new ToolStripSeparator());
                        _menu.Items.Add(new ToolStripMenuItem("Expand All", null, (s, ev) => ExpandAllUnder(hit.Node, true)));
                        _menu.Items.Add(new ToolStripMenuItem("Collapse All", null, (s, ev) => ExpandAllUnder(hit.Node, false)));
                        _menu.Items.Add(new ToolStripSeparator());
                        // Replaces the old double-click gesture, which closed every
                        // unpinned tab without asking.
                        _menu.Items.Add(new ToolStripMenuItem("Replace workspace with folder files…", null,
                            (s, ev) => SwapWorkspaceToFolder(existingFolder)));
                    }
                }
            }
            catch (Exception ex)
            {
                ReportError("Building context menu", ex);
                e.Cancel = true;
            }
        }

        private void RenameDisplayNode(TreeNode node)
        {
            if (node?.Tag is not TreeNodeModel model) return;
            using (var dialog = new DisplayNameDialog(model.Name, PluginMain.Theme))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK
                    || string.IsNullOrWhiteSpace(dialog.DisplayName))
                    return;
                model.Name = dialog.DisplayName;
                node.Text = model.Name;
                _stateStore?.MarkDirty(_root, _currentSelectedId);
                UpdateChrome();
            }
        }

        private void OnTogglePin(FileNode file)
        {
            if (file == null) return;
            file.IsPinned = !file.IsPinned;
            _stateStore?.MarkDirty(_root, _currentSelectedId);
            // Rebuild keeps ordering consistent on every level and restores the
            // selection from _currentSelectedId, so pinning never drops the row.
            RefreshFromModel();
            ShowTransientStatus(file.IsPinned ? "Pinned: " + file.Name : "Unpinned: " + file.Name);
        }

        private void OnFileCloseAndRemove(FileNode file)
        {
            if (file == null) return;

            // Saved file with an open tab: native close (save prompt if dirty),
            // then remove the history entry only after the tab actually closed.
            if (System.IO.Path.IsPathRooted(file.Path) && PluginMain.IsPathOpen(file.Path))
            {
                // The native close already owns the save/discard/cancel prompt, so a
                // second history-removal dialog would ask the same question twice.
                if (!PluginMain.TryCloseFile(file.Path)) return;
                RemoveEntry(file, skipConfirmation: true);
                return;
            }

            // History entry whose tab is already closed (or unsaved buffer that no
            // longer exists): there is nothing to close in Notepad++. The long press
            // still means "close and remove", so drop the entry after confirmation.
            OnRemove(file);
        }

        /// <summary>Renames a saved file on disk or saves an unsaved buffer via the
        /// native Save As dialog. Returns true when the entry was updated.</summary>
        private bool RenameOrSaveFile(FileNode file, TreeNode node, string source)
        {
            if (file == null) return false;

            try
            {
                string updatedPath;
                bool changed;
                if (System.IO.Path.IsPathRooted(file.Path))
                    changed = PluginMain.TryRenameSavedFile(file.Path, PluginMain.Theme, out updatedPath);
                else
                    changed = PluginMain.TrySaveAsUnsavedFile(file.Path, out updatedPath);

                if (!changed || string.IsNullOrEmpty(updatedPath)) return false;

                // Relink keeps alias handling in the model: a renamed sibling may
                // free or need a "Name(1).ext" label.
                var container = _root != null
                    ? VirtualTabGroups.Core.TreeMutator.FindContainer(_root, file)
                    : null;
                if (container == null || !file.Relink(container, updatedPath))
                {
                    file.Path = updatedPath;
                    file.Name = System.IO.Path.GetFileName(updatedPath);
                }
                if (node != null) node.Text = file.Name;
                _stateStore?.MarkDirty(_root, _currentSelectedId);
                UpdateChrome();
                return true;
            }
            catch (Exception ex) { ReportError(source, ex); return false; }
        }

        private void Tree_MouseDownLongPress(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var hit = _tree.HitTest(e.Location);
            if (hit.Node == null) { _pressNode = null; return; }

            _pressNode = hit.Node;
            _pressStart = e.Location;
            _pressAt = DateTime.UtcNow;

            // Hint only: the action still runs on release, so a press that turns
            // into a drag (or ends outside the tree) can never close anything.
            _pressHintTimer.Stop();
            if (hit.Node.Tag is FileNode) _pressHintTimer.Start();
        }

        private void Tree_MouseMoveLongPress(object sender, MouseEventArgs e)
        {
            // Movement beyond the drag threshold means the press has become a drag
            // (WinForms raises ItemDrag, which also clears this state) — the press
            // gestures must not fire.
            if (_pressNode == null) return;
            var dragSize = SystemInformation.DragSize;
            if (Math.Abs(e.X - _pressStart.X) > dragSize.Width
                || Math.Abs(e.Y - _pressStart.Y) > dragSize.Height)
            {
                _pressNode = null;
                _pressHintTimer.Stop();
            }
        }

        private void Tree_MouseUpLongPress(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var pressNode = _pressNode;
            _pressNode = null;
            _pressHintTimer.Stop();

            // Long press: button held on one spot for the threshold, evaluated on
            // release. A real drag never reaches this branch — the OLE drag loop
            // swallows MouseUp, and ItemDrag/MouseMove already cleared _pressNode.
            if (pressNode != null
                && (DateTime.UtcNow - _pressAt).TotalMilliseconds >= LongPressThresholdMs)
            {
                _doubleClickTimer.Stop();
                _pendingClickNode = null;
                if (pressNode.Tag is FileNode file)
                {
                    // Long press is the destructive/closing gesture. The native
                    // Notepad++ close command owns the save/cancel prompt; history
                    // is removed only after the close succeeds.
                    try { OnFileCloseAndRemove(file); }
                    catch (Exception ex) { ReportError("Long-press close/remove", ex); }
                }
                return;
            }

            if (pressNode == null) return;

            var now = DateTime.UtcNow;
            if (_pendingClickNode == pressNode
                && (now - _pendingClickAt).TotalMilliseconds <= SystemInformation.DoubleClickTime)
            {
                _doubleClickTimer.Stop();
                _pendingClickNode = null;
                OnFileDoubleClick(pressNode);
                return;
            }

            _doubleClickTimer.Stop();
            _pendingClickNode = pressNode;
            _pendingClickAt = now;
            _doubleClickTimer.Start();
        }

        private void CommitPendingSingleClick()
        {
            _doubleClickTimer.Stop();
            var node = _pendingClickNode;
            _pendingClickNode = null;
            if (node == null || node.TreeView != _tree) return;

            _tree.SelectedNode = node;
        }

        private void OnFileDoubleClick(TreeNode node)
        {
            try
            {
                if (node?.Tag is FileNode file)
                {
                    RenameOrSaveFile(file, node, "Double-click rename/save");
                }
                else if (node?.Tag is FolderNode)
                {
                    // Double-click on a folder is now harmless: replacing the whole tab
                    // set moved to an explicit, confirmed context-menu command.
                    if (node.IsExpanded) node.Collapse(); else node.Expand();
                }
            }
            catch (Exception ex) { ReportError("Double-click action", ex); }
        }

        /// <summary>
        /// Replaces the tabs shown in Notepad++'s main window with the folder's files.
        /// Every open tab is closed except files pinned anywhere in the virtual tree
        /// and unsaved buffers (non-rooted paths, e.g. "new 2") whose content only
        /// exists in Notepad++ memory; then the folder's files are opened.
        /// Closed-by-swap tabs stay in virtual history — history removal remains an
        /// explicit gesture.
        /// </summary>
        private void SwapWorkspaceToFolder(FolderNode folder)
        {
            if (folder == null || _root == null) return;


            var targetPaths = folder.Children.OfType<FileNode>()
                .Select(f => f.Path)
                .Where(p => !string.IsNullOrEmpty(p) && System.IO.File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();


            var pinnedPaths = CollectPinnedPaths(_root)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);


            var openPaths = (PluginMain.GetAllOpenFilePaths() ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Destructive part first: confirm before any tab is closed.
            var closable = openPaths
                .Where(p => !pinnedPaths.Contains(p) && System.IO.Path.IsPathRooted(p))
                .ToList();
            var preview = string.Join("\n", closable.Take(5).Select(System.IO.Path.GetFileName));
            var confirmation = MessageBox.Show(
                this,
                "Close " + closable.Count + " tab(s) and open " + targetPaths.Count
                    + " file(s) from '" + folder.Name + "'?\n\n"
                    + (closable.Count > 0
                        ? "Tabs to close:\n" + preview + (closable.Count > 5 ? "\n…" : "") + "\n\n"
                        : "No tabs will be closed.\n\n")
                    + "Pinned files and unsaved buffers stay open. History entries stay in the tree.",
                "Replace workspace",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (confirmation != DialogResult.OK) return;


            // Close everything that is not pinned and not an unsaved buffer.
            // Unsaved buffers ("new 2", ...) have no file on disk: closing them would
            // destroy the user's in-memory work, so the swap keeps them open.
            // Saved dirty buffers show Notepad++'s native save/discard/cancel prompt;
            // a cancelled close aborts the swap so the user never loses their working
            // set half-way.
            foreach (var openPath in openPaths)
            {
                if (pinnedPaths.Contains(openPath)) continue;
                // Unsaved buffers (non-rooted names like "new 2") always survive the swap.
                if (!System.IO.Path.IsPathRooted(openPath)) continue;
                if (!PluginMain.TryCloseFile(openPath))
                {
                    _statusLabel.Text = "Workspace swap cancelled — tab close was cancelled.";
                    return;
                }
            }


            foreach (var path in targetPaths)
            {
                PluginMain.OpenFileUnchecked(path);
            }


            _statusLabel.Text = "Workspace: " + folder.Name
                + " (" + targetPaths.Count + (targetPaths.Count == 1 ? " file" : " files") + ")";
        }


        private static System.Collections.Generic.IEnumerable<string> CollectPinnedPaths(FolderNode folder)
        {
            foreach (var child in folder.Children)
            {
                if (child is FileNode f && f.IsPinned && !string.IsNullOrEmpty(f.Path))
                    yield return f.Path;
                else if (child is FolderNode sub)
                {
                    foreach (var path in CollectPinnedPaths(sub))
                        yield return path;
                }
            }
        }

        private void OnFileOpen(FileNode file)
        {
            try
            {
                OpenFileWithRecovery(file);
            }
            catch (Exception ex)
            {
                ReportError("Open File", ex);
            }
        }

        /// <summary>
        /// Single entry point for opening a virtual entry. If the entry is out of
        /// sync with reality (unsaved buffer closed without saving, file deleted or
        /// moved on disk), shows a recovery dialog instead of failing silently.
        /// The entry is never removed or modified without an explicit user choice.
        /// </summary>
        private void OpenFileWithRecovery(FileNode file)
        {
            if (file == null || string.IsNullOrEmpty(file.Path)) return;

            if (PluginMain.TryOpenFile(file.Path)) return;

            try
            {
                bool unsaved = !System.IO.Path.IsPathRooted(file.Path);
                string reason = unsaved
                    ? "The unsaved buffer '" + file.Name + "' was closed without saving. "
                      + "Its content existed only in Notepad++ memory and is gone."
                    : "The file was moved, renamed or deleted outside Notepad++, "
                      + "or is on an unavailable drive:\n" + file.Path;

                using (var dialog = new MissingFileDialog(file.Name, reason, PluginMain.Theme))
                {
                    var result = dialog.ShowDialog(this);
                    if (result == DialogResult.Cancel) return;

                    switch (dialog.Action)
                    {
                        case MissingFileAction.Remove:
                            OnRemove(file);
                            _statusLabel.Text = "Entry removed: " + file.Name;
                            break;

                        case MissingFileAction.Locate:
                            if (unsaved)
                            {
                                // Nothing on disk to locate: offer Save As instead, so the
                                // entry gets a real path. Declining keeps the entry untouched.
                                if (PluginMain.IsPathOpen(file.Path)
                                    && RenameOrSaveFile(file, FindByModelId(_tree.Nodes, file.Id), "Save unsaved buffer"))
                                {
                                    _statusLabel.Text = "Buffer saved: " + file.Name;
                                }
                            }
                            else
                            {
                                using (var dialog2 = new OpenFileDialog
                                {
                                    Title = "Locate '" + file.Name + "'",
                                    InitialDirectory = SafeInitialDirectory(file.Path),
                                    CheckFileExists = true
                                })
                                {
                                    if (dialog2.ShowDialog(this) == DialogResult.OK
                                        && !string.Equals(dialog2.FileName, file.Path, StringComparison.OrdinalIgnoreCase))
                                    {
                                        file.Path = dialog2.FileName;
                                        file.Name = System.IO.Path.GetFileName(dialog2.FileName);
                                        var node = FindByModelId(_tree.Nodes, file.Id);
                                        if (node != null) node.Text = file.Name;
                                        _stateStore?.MarkDirty(_root, _currentSelectedId);
                                        UpdateChrome();
                                        PluginMain.OpenFileUnchecked(file.Path);
                                        _statusLabel.Text = "Entry re-linked: " + file.Name;
                                    }
                                }
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                ReportError("Open entry", ex);
            }
        }

        private static string SafeInitialDirectory(string path)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                return !string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir) ? dir : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Opens Windows Explorer at the file's parent folder with the file selected.
        /// No-op for unsaved buffers (non-rooted paths) — the menu item is grayed out
        /// in that case, but we re-check here for defense-in-depth.
        /// </summary>
        private void OnRevealInExplorer(FileNode file)
        {
            try
            {
                if (file == null || !System.IO.Path.IsPathRooted(file.Path)) return;
                // /select,"<path>" opens Explorer at the parent folder with the file highlighted.
                // Filenames can't contain " on Windows (NTFS forbids it), so simple quoting suffices.
                // Wrap in using() so the returned Process handle is disposed — otherwise each
                // invocation leaks a Win32 process handle for the lifetime of the plugin.
                using (System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file.Path + "\"")) { }
            }
            catch (Exception ex) { ReportError("Reveal in Explorer", ex); }
        }

        /// <summary>
        /// Copies the file's absolute path to the clipboard. No-op for unsaved buffers
        /// (non-rooted paths) — the menu item is grayed out in that case.
        /// </summary>
        private void OnCopyFullPath(FileNode file)
        {
            try
            {
                if (file == null || !System.IO.Path.IsPathRooted(file.Path)) return;
                Clipboard.SetText(file.Path);
            }
            catch (Exception ex) { ReportError("Copy full path", ex); }
        }

        private void OnAddActive(FolderNode targetFolder)
        {
            try
            {
                if (targetFolder == null) return;

                var path = PluginMain.GetCurrentFullPath();
                if (string.IsNullOrEmpty(path))
                {
                    ShowTransientStatus("No active document to add.");
                    return;
                }

                AddPathToFolder(path, targetFolder);
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
                if (paths == null || paths.Length == 0)
                {
                    ShowTransientStatus("No open documents to add.");
                    return;
                }

                int added = 0;
                foreach (var path in paths)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    if (AddPathToFolder(path, targetFolder, quiet: true)) added++;
                }

                if (added == 0)
                {
                    ShowTransientStatus("Every open document is already tracked.");
                    return;
                }

                _stateStore?.MarkDirty(_root, _currentSelectedId);
                UpdateChrome();
                ShowTransientStatus("Added " + added + " file(s) → " + targetFolder.Name);
            }
            catch (Exception ex)
            {
                ReportError("Add All Open Files", ex);
            }
        }

        /// <summary>
        /// Adds one path to a folder. The tree holds at most one entry per path, so a
        /// file that is already tracked is reported instead of being duplicated.
        /// </summary>
        private bool AddPathToFolder(string path, FolderNode destination, bool quiet = false)
        {
            if (string.IsNullOrEmpty(path) || destination == null) return false;

            if (TreeQuery.IsTracked(_root, path, out var owner))
            {
                if (!quiet)
                {
                    ShowTransientStatus(owner == destination
                        ? "Already in '" + destination.Name + "': " + System.IO.Path.GetFileName(path)
                        : "Already in '" + (owner?.Name ?? "the tree") + "': "
                          + System.IO.Path.GetFileName(path));
                }
                return false;
            }

            var added = TreeMutator.AddFile(destination, path);
            if (added == null) return false;

            InsertNodeIncrementally(added, destination);
            _stateStore?.MarkDirty(_root, _currentSelectedId);
            UpdateChrome();
            if (!quiet) ShowTransientStatus("Added: " + added.Name + " → " + destination.Name);
            return true;
        }

        /// <summary>
        /// Inserts a single node where the display order expects it, without
        /// rebuilding the tree — the scroll position, expansion and selection of the
        /// rest of the tree survive an external add.
        /// </summary>
        private TreeNode InsertNodeIncrementally(TreeNodeModel model, FolderNode parentFolder)
        {
            var parentTn = ReferenceEquals(parentFolder, _root)
                ? null
                : FindByModelId(_tree.Nodes, parentFolder.Id);
            var collection = parentTn?.Nodes ?? _tree.Nodes;
            var tn = BuildTreeNode(model);

            int index = 0;
            foreach (var sibling in OrderedChildren(parentFolder))
            {
                if (ReferenceEquals(sibling, model)) break;
                index++;
            }

            if (index >= collection.Count) collection.Add(tn);
            else collection.Insert(index, tn);

            if (parentTn != null) parentTn.Expand();
            return tn;
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
                RenameDisplayNode(tn);

                _stateStore?.MarkDirty(_root, _currentSelectedId);
                UpdateChrome();
            }
            catch (Exception ex)
            {
                ReportError("New Folder", ex);
            }
        }        private void OnRemove(TreeNodeModel target) => RemoveEntry(target, skipConfirmation: false);

        /// <summary>
        /// Removes one entry. Confirmation follows RemovePolicy: saved and unmodified
        /// files disappear silently, while unsaved buffers, modified files and
        /// non-empty folders ask first.
        /// </summary>
        private void RemoveEntry(TreeNodeModel target, bool skipConfirmation)
        {
            try
            {
                if (target == null) return;

                int descendants = target is FolderNode folder ? CountDescendants(folder) : 0;
                bool dirty = target is FileNode file
                    && PluginMain.DirtyTracker != null
                    && PluginMain.DirtyTracker.IsDirty(file.Path);

                if (!skipConfirmation && RemovePolicy.RequiresConfirmation(target, dirty, descendants))
                {
                    var result = MessageBox.Show(
                        this,
                        RemovePolicy.Describe(target, descendants),
                        "Virtual Tab Groups",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Warning);
                    if (result != DialogResult.OK) return;
                }

                if (!TreeMutator.RemoveNode(target, _root)) return;

                var tn = FindByModelId(_tree.Nodes, target.Id);
                if (tn != null)
                {
                    _multiSelection.Remove(tn);
                    if (_selectionAnchor == tn) _selectionAnchor = null;
                    tn.Remove();
                }
                if (_currentSelectedId == target.Id) _currentSelectedId = null;

                _stateStore?.MarkDirty(_root, _currentSelectedId);
                UpdateChrome();
                ShowTransientStatus("Removed: " + target.Name);
            }
            catch (Exception ex)
            {
                ReportError("Remove", ex);
            }
        }

        /// <summary>
        /// Deletes every selected entry with a single confirmation, so a multi-select
        /// removal cannot turn into N dialogs. Children of a selected folder are
        /// skipped — they disappear with their parent.
        /// </summary>
        private void OnRemoveSelection()
        {
            try
            {
                var models = new System.Collections.Generic.List<TreeNodeModel>();
                var nodes = new System.Collections.Generic.List<TreeNode>();
                if (_tree.SelectedNode != null) nodes.Add(_tree.SelectedNode);
                foreach (var tn in OrderByVisiblePosition(_multiSelection)) nodes.Add(tn);

                foreach (var tn in nodes)
                {
                    if (tn?.Tag is not TreeNodeModel model || models.Contains(model)) continue;
                    if (models.Any(existing => existing is FolderNode f && TreeQuery.IsInside(f, model))) continue;
                    models.Add(model);
                }

                if (models.Count == 0) return;
                if (models.Count == 1) { OnRemove(models[0]); return; }

                int fileCount = models.Count(m => m is FileNode);
                int folderCount = models.Count - fileCount;
                var result = MessageBox.Show(
                    this,
                    "Remove " + fileCount + " file(s) and " + folderCount + " group(s) from the tree?\n\n"
                        + "Files on disk are not deleted. Unsaved buffers stay open in Notepad++.",
                    "Virtual Tab Groups",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.OK) return;

                int removed = 0;
                foreach (var model in models)
                {
                    if (!TreeMutator.RemoveNode(model, _root)) continue;
                    var tn = FindByModelId(_tree.Nodes, model.Id);
                    if (tn != null) tn.Remove();
                    if (_currentSelectedId == model.Id) _currentSelectedId = null;
                    removed++;
                }

                _multiSelection.Clear();
                _selectionAnchor = null;
                _stateStore?.MarkDirty(_root, _currentSelectedId);
                UpdateChrome();
                ShowTransientStatus("Removed " + removed + " entries.");
            }
            catch (Exception ex) { ReportError("Remove selection", ex); }
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
                    OpenFileWithRecovery(file);
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.F2 && selected != null)
                {
                    RenameDisplayNode(selected);
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.Delete && selectedModel != null)
                {
                    OnRemoveSelection();
                    e.Handled = true;
                    return;
                }

                if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.N)
                {
                    OnNewFolder(GetTargetFolder());
                    e.Handled = true;
                    return;
                }

                if (e.Control && e.Shift && !e.Alt && e.KeyCode == Keys.A)
                {
                    OnAddActive(GetTargetFolder());
                    e.Handled = true;
                    return;
                }

                if (e.Control && e.Shift && e.Alt && e.KeyCode == Keys.A)
                {
                    OnAddAllOpen(GetTargetFolder());
                    e.Handled = true;
                    return;
                }
            }
            catch (Exception ex) { ReportError("Tree_KeyDown", ex); }
        }

        private void ExpandAllTree(bool expand)
        {
            foreach (TreeNode node in _tree.Nodes)
                ExpandAllUnder(node, expand);
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

        /// <summary>
        /// Maps the cursor to a drop position. Files split the row into halves
        /// (top = above, bottom = below) so siblings can be freely reordered.
        /// Folders keep the middle half as "into", with the outer quarters as
        /// above/below reorder zones.
        /// </summary>
        private DropPosition ComputeDropPosition(TreeNode target, Point clientPoint)
        {
            if (target == null) return DropPosition.None;
            var bounds = target.Bounds;
            if (target.Tag is FolderNode)
            {
                int quarter = Math.Max(1, bounds.Height / 4);
                if (clientPoint.Y < bounds.Top + quarter) return DropPosition.Above;
                if (clientPoint.Y > bounds.Bottom - quarter) return DropPosition.Below;
                return DropPosition.Into;
            }
            if (clientPoint.Y < bounds.Top + bounds.Height / 2) return DropPosition.Above;
            return DropPosition.Below;
        }

        private void Tree_DragOver(object sender, DragEventArgs e)
        {
            try
            {
                var clientPoint = _tree.PointToClient(new Point(e.X, e.Y));
                string[] externalPaths;
                if (TryGetExternalPaths(e.Data, out externalPaths))
                {
                    e.Effect = DragDropEffects.Copy;
                    // Empty tree: highlight the placeholder drop target instead
                    // of clearing indicators.
                    if (_tree.Nodes.Count == 0) _tree.ShowEmptyStateDropHighlight();
                    else _tree.ClearDropIndicators();
                    return;
                }
                if (!e.Data.GetDataPresent(typeof(TreeNode[])))
                {
                    e.Effect = DragDropEffects.None;
                    return;
                }

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
                            _tree.ClearDropIndicators();
                            return;
                        }
                        if (dragged.Tag is FolderNode draggedFolder
                            && target.Tag is TreeNodeModel targetModel
                            && TreeMutator.FindContainer(draggedFolder, targetModel) != null)
                        {
                            e.Effect = DragDropEffects.None;
                            _hoverTimer.Stop();
                            _scrollTimer.Stop();
                            _tree.ClearDropIndicators();
                            return;
                        }
                    }
                }

                e.Effect = DragDropEffects.Move;

                // Drop indicator: insertion line for reorder, row highlight for "into".
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
                        case DropPosition.Into:
                            _tree.ShowDropHighlight(target);
                            break;
                    }
                }
                else if (_tree.Nodes.Count == 0)
                {
                    _tree.ShowEmptyStateDropHighlight();
                }
                else
                {
                    _tree.ClearDropIndicators();
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

        private bool TryGetExternalPaths(IDataObject data, out string[] paths)
        {
            paths = null;
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                paths = ((string[])data.GetData(DataFormats.FileDrop))
                    .Where(System.IO.File.Exists)
                    .ToArray();
                return paths.Length > 0;
            }

            if (data.GetDataPresent(DataFormats.UnicodeText)
                || data.GetDataPresent(DataFormats.Text))
            {
                var text = (data.GetDataPresent(DataFormats.UnicodeText)
                    ? data.GetData(DataFormats.UnicodeText)
                    : data.GetData(DataFormats.Text)) as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    paths = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim().Trim('"'))
                        .Where(System.IO.File.Exists)
                        .ToArray();
                    return paths.Length > 0;
                }
            }
            return false;
        }

        private FolderNode GetDropFolder(TreeNode target, Point clientPoint)
        {
            if (target == null) return _root;
            var position = ComputeDropPosition(target, clientPoint);
            if (position == DropPosition.Into && target.Tag is FolderNode folder)
                return folder;
            return TreeMutator.FindContainer(_root, target.Tag as TreeNodeModel)
                ?? _root;
        }

        private void AddExternalFiles(string[] paths, FolderNode destination)
        {
            if (destination == null || paths == null) return;

            int added = 0;
            int duplicates = 0;
            foreach (var path in paths)
            {
                if (TreeQuery.IsTracked(_root, path, out _)) { duplicates++; continue; }

                var node = TreeMutator.AddFile(destination, path);
                if (node == null) continue;
                InsertNodeIncrementally(node, destination);
                added++;
            }

            if (added == 0)
            {
                if (duplicates > 0)
                    ShowTransientStatus("Already tracked: " + duplicates + " file(s).");
                return;
            }

            _stateStore?.MarkDirty(_root, _currentSelectedId);
            UpdateChrome();
            ShowTransientStatus("Added " + added + " file(s) → " + destination.Name);
        }

        private void Tree_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                _hoverTimer.Stop();
                _hoverNode = null;
                _scrollTimer.Stop();
                _tree.ClearDropIndicators();

                var clientPoint = _tree.PointToClient(new Point(e.X, e.Y));
                string[] externalPaths;
                if (TryGetExternalPaths(e.Data, out externalPaths))
                {
                    AddExternalFiles(externalPaths, GetDropFolder(_tree.GetNodeAt(clientPoint), clientPoint));
                    return;
                }

                if (!e.Data.GetDataPresent(typeof(TreeNode[]))) return;
                var draggedSet = (TreeNode[])e.Data.GetData(typeof(TreeNode[]));
                if (draggedSet.Length == 0) return;

                var target = _tree.GetNodeAt(clientPoint);
                var position = ComputeDropPosition(target, clientPoint);

                FolderNode destinationFolder;
                int insertionIndex;

                if (target == null)
                {
                    destinationFolder = _root;
                    insertionIndex = destinationFolder.Children.Count;
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

        /// <summary>
        /// The panel is registered with Notepad++'s docking manager for its whole
        /// lifetime, so a user close of the floating window must hide it instead of
        /// disposing it — otherwise the next show reaches a disposed object.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                try
                {
                    if (PluginMain.NppHandle != IntPtr.Zero) HideDocked(PluginMain.NppHandle);
                }
                catch (Exception ex) { ReportError("Hide panel", ex); }
                return;
            }
            base.OnFormClosing(e);
        }

        /// <summary>
        /// Shows a message for a moment, then restores the selection description.
        /// Keeps feedback from being overwritten by the next UpdateChrome call.
        /// </summary>
        private void ShowTransientStatus(string message)
        {
            if (_statusLabel == null || _statusLabel.IsDisposed) return;
            _statusResetTimer.Stop();
            _statusShowsTransient = true;
            _statusLabel.Text = message;
            _statusResetTimer.Start();
        }

        /// <summary>Copies the selected file's full path; the status confirms it.</summary>
        private void OnCopySelectedPath()
        {
            try
            {
                var selected = _tree.SelectedNode?.Tag as FileNode;
                if (selected == null || !System.IO.Path.IsPathRooted(selected.Path))
                {
                    ShowTransientStatus("Nothing to copy — select a saved file.");
                    return;
                }

                Clipboard.SetText(selected.Path);
                ShowTransientStatus("Copied");
            }
            catch (Exception ex) { ReportError("Copy path", ex); }
        }

        /// <summary>Saves every modified document; untouched buffers are skipped.</summary>
        private void OnSaveAll()
        {
            try
            {
                var paths = PluginMain.GetAllOpenFilePaths() ?? Array.Empty<string>();
                int saved = 0;
                foreach (var path in paths)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    if (PluginMain.DirtyTracker == null || !PluginMain.DirtyTracker.IsDirty(path)) continue;
                    if (PluginMain.TrySaveFile(path)) saved++;
                }

                ShowTransientStatus(saved == 0
                    ? "Nothing to save — no modified documents."
                    : "Saved " + saved + (saved == 1 ? " document." : " documents."));
            }
            catch (Exception ex) { ReportError("Save all", ex); }
        }

        /// <summary>
        /// Follows the active Notepad++ buffer with the tree selection. Opening is
        /// suppressed, so switching tabs never re-opens the file or moves focus.
        /// </summary>
        internal void SyncSelectionToPath(string path)
        {
            if (_root == null || string.IsNullOrEmpty(path)) return;
            if (IsDisposed || _tree.IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(SyncSelectionToPath), path); return; }

            try
            {
                var file = TreeQuery.FindFileByPath(_root, path);
                if (file == null) return;

                var tn = FindByModelId(_tree.Nodes, file.Id);
                if (tn == null || tn == _tree.SelectedNode) return;

                _suppressSingleClickOpen = true;
                try
                {
                    _tree.SelectedNode = tn;
                    tn.EnsureVisible();
                }
                finally { _suppressSingleClickOpen = false; }
            }
            catch (Exception ex) { ReportError("Sync selection", ex); }
        }

        /// <summary>
        /// Native tooltips only for labels that do not fit the row, so the full path
        /// is reachable for elided names without drawing overlays over the tree.
        /// </summary>
        private void UpdateNodeToolTips()
        {
            if (_tree.IsDisposed || _tree.Nodes.Count == 0) return;
            _tree.BeginUpdate();
            try
            {
                foreach (TreeNode tn in _tree.Nodes) ApplyToolTip(tn);
            }
            finally { _tree.EndUpdate(); }
        }

        private void ApplyToolTip(TreeNode tn)
        {
            if (tn.Tag is FileNode file && System.IO.Path.IsPathRooted(file.Path))
            {
                int indent = tn.Level * _tree.Indent + 2 + 14 + 18;
                int available = Math.Max(0, _tree.ClientSize.Width - indent - 8);
                int needed = TextRenderer.MeasureText(tn.Text, _tree.Font).Width;
                // Only elided labels get a tooltip; a short name needs no hint.
                tn.ToolTipText = needed > available ? file.Path : string.Empty;
            }

            foreach (TreeNode child in tn.Nodes) ApplyToolTip(child);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_theme != null) _theme.Changed -= ApplyChromeTheme;
                _icons.Dispose();
                _hoverTimer.Dispose();
                _scrollTimer.Dispose();
                _doubleClickTimer.Dispose();
                _statusResetTimer.Dispose();
                _pressHintTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
