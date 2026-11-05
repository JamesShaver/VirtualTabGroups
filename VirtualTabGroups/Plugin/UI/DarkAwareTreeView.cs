using System;
using System.Drawing;
using System.Windows.Forms;
using VirtualTabGroups.Plugin;

namespace VirtualTabGroups.Plugin.UI
{
    public sealed class DarkAwareTreeView : TreeView
    {
        private ThemeManager _theme;

        // Cached GDI objects — rebuilt in ApplyTheme, disposed in DisposeCachedGdi.
        private SolidBrush _bgBrush;
        private SolidBrush _bgHotterBrush;
        private SolidBrush _hoverBrush;
        private SolidBrush _accentBrush;
        private SolidBrush _textBrush;
        private Pen _edgeDottedPen;
        private Pen _hotEdgePen;
        private Brush _chevronBrush;

        private string _emptyStateText;
        private TreeNode _hotNode;
        public string EmptyStateText
        {
            get => _emptyStateText;
            set { _emptyStateText = value; Invalidate(); }
        }

        /// <summary>
        /// Optional predicate the panel sets to extend selection visuals beyond
        /// the single WinForms SelectedNode. Used to highlight multi-selected nodes.
        /// </summary>
        public Func<TreeNode, bool> IsExtraSelected { get; set; }

        /// <summary>
        /// Optional predicate the panel sets to mark file rows with unsaved changes.
        /// When it returns true, an accent dot is painted before the row text (like
        /// a modern editor's modified-file marker).
        /// </summary>
        public Func<TreeNode, bool> IsDirtyIndicator { get; set; }

        /// <summary>
        /// Optional predicate the panel sets to mark pinned file rows. Pinned rows are
        /// ordered first and get a slim accent bar at the right edge.
        /// </summary>
        public Func<TreeNode, bool> IsPinnedIndicator { get; set; }

        public DarkAwareTreeView()
        {
            DrawMode = TreeViewDrawMode.OwnerDrawAll;
            ShowLines = false;       // we paint our own connector lines (Task 37)
            ShowPlusMinus = false;   // we paint our own chevrons
            ShowRootLines = false;
            // Native tooltips carry the full path for labels that do not fit the row.
            ShowNodeToolTips = true;
            HideSelection = false;
            // Disable the native inline editor. It creates a transient textbox that
            // conflicts with single/double/long-click gestures and can visually flicker.
            LabelEdit = false;
            // Defense in depth: cancel any native label-edit request as well.
            // This prevents the transient WinForms textbox even if a keyboard or
            // mouse gesture reaches the underlying TreeView control.
            BeforeLabelEdit += (s, e) => e.CancelEdit = true;
            AllowDrop = true;
            // FullRowSelect = true so WinForms' hit-test treats the entire row as
            // clickable. Our owner-draw paints text at a custom x-position (after the
            // chevron + icon); with FullRowSelect=false the default hit-test thinks the
            // text is at its default position and clicks on the visible text miss.
            FullRowSelect = true;
            ItemHeight = 28;
            Indent = 20;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            MouseMove += HandleMouseMove;
            MouseLeave += (s, e) => SetHotNode(null);

            // Double-buffer the TreeView to reduce flicker during owner-draw paint passes.
            // TreeView's DoubleBuffered is protected — set via the control-styles API.
            SetStyle(System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer
                   | System.Windows.Forms.ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // TreeView creates its native handle lazily. Reassert the setting after
            // handle creation so no native edit control can be requested later.
            LabelEdit = false;

            // Scale row metrics with the monitor DPI: fixed pixels clipped text and
            // misaligned icons at 125/150 %.
            float scale = DeviceDpi / 96f;
            ItemHeight = (int)Math.Round(28 * scale);
            Indent = (int)Math.Round(20 * scale);
        }

        protected override void OnBeforeLabelEdit(NodeLabelEditEventArgs e)
        {
            e.CancelEdit = true;
            base.OnBeforeLabelEdit(e);
        }

        public void AttachTheme(ThemeManager theme)
        {
            _theme = theme;
            _theme.Changed += ApplyTheme;
            ApplyTheme();
        }

        public void DetachTheme()
        {
            if (_theme != null) _theme.Changed -= ApplyTheme;
            _theme = null;
        }

        private void ApplyTheme()
        {
            if (_theme == null) return;
            BackColor = _theme.Surface;
            ForeColor = _theme.Text;
            LineColor = _theme.Edge;

            DisposeCachedGdi();
            _bgBrush        = new SolidBrush(_theme.Surface);
            _bgHotterBrush  = new SolidBrush(_theme.SelectionBackground);
            _hoverBrush     = new SolidBrush(_theme.HoverBackground);
            _accentBrush    = new SolidBrush(_theme.Accent);
            _textBrush      = new SolidBrush(_theme.Text);
            _chevronBrush   = new SolidBrush(_theme.Text);
            _edgeDottedPen  = new Pen(_theme.Edge) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            _hotEdgePen     = new Pen(_theme.HotEdge, 2);

            Invalidate();
        }

        private void DisposeCachedGdi()
        {
            _bgBrush?.Dispose();       _bgBrush       = null;
            _bgHotterBrush?.Dispose(); _bgHotterBrush = null;
            _hoverBrush?.Dispose();    _hoverBrush    = null;
            _accentBrush?.Dispose();   _accentBrush   = null;
            _textBrush?.Dispose();     _textBrush     = null;
            _chevronBrush?.Dispose();  _chevronBrush  = null;
            _edgeDottedPen?.Dispose(); _edgeDottedPen = null;
            _hotEdgePen?.Dispose();    _hotEdgePen    = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachTheme();
                DisposeCachedGdi();
            }
            base.Dispose(disposing);
        }

        protected override void OnDrawNode(DrawTreeNodeEventArgs e)
        {
            try
            {
                if (_theme == null) { base.OnDrawNode(e); return; }

                // Fill background using cached brushes — no per-call allocation.
                // A node is highlighted if WinForms considers it selected, OR if the
                // panel's multi-selection predicate claims it (Ctrl/Shift+click set).
                bool isSelected = (e.State & TreeNodeStates.Selected) != 0
                                  || (IsExtraSelected != null && IsExtraSelected(e.Node));
                bool isDropTarget = _dropHighlightNode == e.Node;
                bool isHovered = _hotNode == e.Node && !isSelected && !isDropTarget;
                var brush = isSelected ? _bgHotterBrush
                    : isDropTarget ? _hoverBrush
                    : isHovered ? _hoverBrush
                    : _bgBrush;
                e.Graphics.FillRectangle(brush, new Rectangle(0, e.Bounds.Top, Width, e.Bounds.Height));

                // Accent frame marks the "drop into folder" target — visually distinct
                // from selection (filled accent strip) and from the insertion line.
                if (isDropTarget && _hotEdgePen != null)
                    e.Graphics.DrawRectangle(_hotEdgePen,
                        new Rectangle(1, e.Bounds.Top + 1, Width - 3, e.Bounds.Height - 3));

                if (isSelected && _accentBrush != null)
                    e.Graphics.FillRectangle(_accentBrush, new Rectangle(0, e.Bounds.Top, 3, e.Bounds.Height));

                int indent = e.Node.Level * Indent + 2;

                // Connector lines for child nodes — uses cached dotted pen.
                if (e.Node.Level > 0 && _edgeDottedPen != null)
                {
                    int parentX = (e.Node.Level - 1) * Indent + 6;
                    int midY    = e.Bounds.Top + e.Bounds.Height / 2;
                    e.Graphics.DrawLine(_edgeDottedPen, parentX, e.Bounds.Top, parentX, midY);
                    e.Graphics.DrawLine(_edgeDottedPen, parentX, midY, parentX + Indent, midY);
                }

                if (e.Node.Nodes.Count > 0)
                {
                    var glyphRect = new Rectangle(indent, e.Bounds.Top + (e.Bounds.Height - 8) / 2, 8, 8);
                    DrawChevron(e.Graphics, glyphRect, e.Node.IsExpanded);
                }
                indent += 14;

                if (ImageList != null && e.Node.ImageIndex >= 0 && e.Node.ImageIndex < ImageList.Images.Count)
                {
                    var img = ImageList.Images[e.Node.ImageIndex];
                    e.Graphics.DrawImage(img, indent, e.Bounds.Top + (e.Bounds.Height - 16) / 2, 16, 16);
                    indent += 18;
                }

                // Unsaved-changes dot: a small accent circle before the text, the same
                // visual language modern editors use for modified documents. Rows shift
                // text only when the dot is actually shown — no jitter for clean files.
                if (IsDirtyIndicator != null && IsDirtyIndicator(e.Node) && _accentBrush != null)
                {
                    int dotY = e.Bounds.Top + (e.Bounds.Height - 6) / 2;
                    e.Graphics.FillEllipse(_accentBrush, indent, dotY, 6, 6);
                    indent += 10;
                }

                // Pinned marker: a slim accent bar at the right edge, visually separate
                // from the selection strip on the left and from the dirty dot.
                if (IsPinnedIndicator != null && IsPinnedIndicator(e.Node) && _accentBrush != null)
                {
                    int barHeight = Math.Max(6, e.Bounds.Height / 2);
                    e.Graphics.FillRectangle(_accentBrush,
                        new Rectangle(Width - 7, e.Bounds.Top + (e.Bounds.Height - barHeight) / 2, 3, barHeight));
                }

                var textRect = new Rectangle(indent, e.Bounds.Top, Math.Max(0, Width - indent), e.Bounds.Height);
                // TextRenderer.DrawText accepts Color directly — no GDI allocation needed.
                TextRenderer.DrawText(e.Graphics, e.Node.Text, Font, textRect,
                    isSelected ? _theme.SelectionText : _theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                e.DrawDefault = false;
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("DarkAwareTreeView.OnDrawNode", ex);
                e.DrawDefault = true;
            }
        }

        // Draws the expand/collapse chevron using the cached chevron brush.
        private void DrawChevron(System.Drawing.Graphics g, Rectangle r, bool expanded)
        {
            if (_chevronBrush == null) return;
            if (expanded)
            {
                var pts = new[] { new Point(r.Left, r.Top + 2), new Point(r.Right, r.Top + 2), new Point(r.Left + r.Width / 2, r.Bottom - 1) };
                g.FillPolygon(_chevronBrush, pts);
            }
            else
            {
                var pts = new[] { new Point(r.Left + 2, r.Top), new Point(r.Left + 2, r.Bottom), new Point(r.Right - 1, r.Top + r.Height / 2) };
                g.FillPolygon(_chevronBrush, pts);
            }
        }

        private Rectangle? _insertionLine;
        private TreeNode _dropHighlightNode;

        // True while a drag hovers over an empty tree: the empty-state card is
        // drawn with accent highlight so the user sees the drop is accepted.
        private bool _emptyStateDropHighlighted;

        // Last client rectangle painted for the empty-state card, used to
        // invalidate precisely when the highlight toggles.
        private Rectangle _emptyStateCardRect;

        /// <summary>
        /// Draws a horizontal insertion line at the given y — the drop indicator for
        /// reordering (drop above/below a row). Clears any folder highlight.
        /// </summary>
        public void ShowInsertionLine(int y)
        {
            if (_dropHighlightNode != null)
            {
                var stale = _dropHighlightNode;
                _dropHighlightNode = null;
                InvalidateNode(stale);
            }

            // Skip redundant invalidates when the line position hasn't changed.
            if (_insertionLine.HasValue && _insertionLine.Value.Top == y)
                return;

            var oldY = _insertionLine?.Top;
            _insertionLine = new Rectangle(0, y, Width, 1);

            // Invalidate only the narrow strips around the new and old line positions
            // instead of the entire client area, cutting repaint work at 30-60 Hz.
            const int strip = 3;
            Invalidate(new Rectangle(0, y - strip, Width, strip * 2 + 1));
            if (oldY.HasValue)
                Invalidate(new Rectangle(0, oldY.Value - strip, Width, strip * 2 + 1));
        }

        /// <summary>
        /// Highlights a whole row — the drop indicator for "drop into folder".
        /// Clears any insertion line.
        /// </summary>
        public void ShowDropHighlight(TreeNode node)
        {
            if (node == null || node.TreeView != this) return;
            if (_insertionLine != null) ClearInsertionLine();
            if (_dropHighlightNode == node) return;

            var previous = _dropHighlightNode;
            _dropHighlightNode = node;
            InvalidateNode(previous);
            InvalidateNode(_dropHighlightNode);
        }

        /// <summary>
        /// Highlights the empty-state placeholder card while a drag hovers over
        /// an empty tree: accent border + hover background instead of neutral.
        /// </summary>
        public void ShowEmptyStateDropHighlight()
        {
            if (_emptyStateDropHighlighted) return;
            _emptyStateDropHighlighted = true;
            Invalidate(GetCardInvalidateRect());
        }

        /// <summary>
        /// Removes all drag indicators (insertion line, row highlight and the
        /// empty-state placeholder highlight). Safe to call when nothing is shown.
        /// </summary>
        public void ClearDropIndicators()
        {
            ClearInsertionLine();
            if (_dropHighlightNode != null)
            {
                var previous = _dropHighlightNode;
                _dropHighlightNode = null;
                InvalidateNode(previous);
            }
            if (_emptyStateDropHighlighted)
            {
                _emptyStateDropHighlighted = false;
                Invalidate(GetCardInvalidateRect());
            }
        }

        /// <summary>Client rectangle to invalidate when the card visual toggles.</summary>
        private Rectangle GetCardInvalidateRect()
        {
            // Prefer the rectangle measured during the last paint; before the first
            // paint fall back to the static card geometry from ClientRectangle.
            var bounds = _emptyStateCardRect;
            if (bounds.IsEmpty)
            {
                var client = ClientRectangle;
                bounds = new Rectangle(client.Left + 18, client.Top + client.Height / 3 - 18,
                    Math.Max(0, client.Width - 36), 64);
            }
            bounds.Inflate(4, 4);
            return bounds;
        }

        /// <summary>
        /// Clears the insertion line if one is shown; no-op otherwise.
        /// </summary>
        public void ClearInsertionLine()
        {
            if (_insertionLine == null) return;
            var oldY = _insertionLine.Value.Top;
            _insertionLine = null;
            const int strip = 3;
            Invalidate(new Rectangle(0, oldY - strip, Width, strip * 2 + 1));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                base.OnPaint(e);
                if (_insertionLine != null && _hotEdgePen != null)
                {
                    e.Graphics.DrawLine(_hotEdgePen,
                        _insertionLine.Value.Left,  _insertionLine.Value.Top,
                        _insertionLine.Value.Right, _insertionLine.Value.Top);
                }

                if (!string.IsNullOrEmpty(_emptyStateText) && Nodes.Count == 0 && _theme != null)
                {
                    var bounds   = ClientRectangle;
                    var card = new Rectangle(bounds.Left + 18, bounds.Top + bounds.Height / 3 - 18,
                        Math.Max(0, bounds.Width - 36), 64);
                    _emptyStateCardRect = card;

                    if (_emptyStateDropHighlighted)
                    {
                        // Drop accepted: hover background + accent frame (same visual
                        // language as the folder drop highlight above).
                        if (_hoverBrush != null)
                            e.Graphics.FillRectangle(_hoverBrush, card);
                        if (_hotEdgePen != null)
                            e.Graphics.DrawRectangle(_hotEdgePen,
                                new Rectangle(card.Left + 1, card.Top + 1, card.Width - 3, card.Height - 3));
                    }
                    else
                    {
                        using (var cardBrush = new SolidBrush(_theme.SurfaceAlt))
                            e.Graphics.FillRectangle(cardBrush, card);
                        using (var border = new Pen(_theme.Divider))
                            e.Graphics.DrawRectangle(border, card);
                    }

                    var textRect = new Rectangle(card.Left + 8, card.Top + 10, card.Width - 16, 42);
                    TextRenderer.DrawText(e.Graphics, _emptyStateText, Font, textRect,
                        _emptyStateDropHighlighted ? _theme.Text : _theme.DisabledText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
            }
            catch (Exception ex) { CrashLog.WriteException("DarkAwareTreeView.OnPaint", ex); }
        }

        private void HandleMouseMove(object sender, MouseEventArgs e)
        {
            SetHotNode(GetNodeAt(e.X, e.Y));
        }

        private void SetHotNode(TreeNode node)
        {
            if (_hotNode == node) return;

            var previous = _hotNode;
            _hotNode = node;
            InvalidateNode(previous);
            InvalidateNode(_hotNode);
        }

        private void InvalidateNode(TreeNode node)
        {
            if (node == null || node.TreeView != this) return;
            // OnDrawNode paints the FULL row (0..Width): row background, hover,
            // accent frame — all wider than node.Bounds, which covers only the
            // text area starting after the indent. Invalidating node.Bounds left
            // stale pixels on the row edges — the torn/partial drop highlight.
            // Invalidate exactly what will be painted: the full row.
            var bounds = node.Bounds;
            var row = new Rectangle(0, bounds.Top, Width, bounds.Height);
            row.Inflate(0, 2);
            Invalidate(row);
        }

        /// <summary>
        /// Public wrapper so the panel can repaint a single row after a dirty-state
        /// change (dot appears/disappears) without rebuilding the tree.
        /// </summary>
        public void RedrawNode(TreeNode node) => InvalidateNode(node);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            try
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;

                var node = GetNodeAt(e.X, e.Y);
                if (node == null || node.Nodes.Count == 0) return;

                int indent = node.Level * Indent + 2;
                var glyphRect = new Rectangle(indent, node.Bounds.Top + (node.Bounds.Height - 8) / 2, 8, 8);
                if (glyphRect.Contains(e.X, e.Y))
                {
                    if (node.IsExpanded) node.Collapse(); else node.Expand();
                }
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("DarkAwareTreeView.OnMouseDown", ex);
            }
        }
    }
}
