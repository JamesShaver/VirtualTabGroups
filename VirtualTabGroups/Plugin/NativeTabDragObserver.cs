using System;
using System.Runtime.InteropServices;

namespace VirtualTabGroups.Plugin.Npp
{
    /// <summary>
    /// Detects a native tab drag on Notepad++'s main tab bar and hands the dragged
    /// tab's buffer path to the panel so it can accept the drop.
    ///
    /// HOW IT WORKS (and why it is safe):
    /// Notepad++'s tab bar implements tab reordering with mouse capture inside its own
    /// wndProc — it does NOT use the OLE IDataObject protocol, so RegisterDragDrop on
    /// the panel never sees those drags. Any way to "receive" them therefore has to
    /// observe the native mouse state while the drag is in progress. This class does
    /// exactly that, read-only:
    ///   - GetAsyncKeyState(VK_LBUTTON) tells us the button is still held;
    ///   - WindowFromPoint tells us which window the cursor is over;
    ///   - if that window is the tab bar, TCM_GETCURSEL + TCM_GETITEM count tell us
    ///     which tab is being dragged and whether it moved.
    ///
    /// It never sends messages into Notepad++'s controls, never subclasses or hooks
    /// their wndProc, and never synthesizes input, so native tab reordering keeps
    /// working exactly as before. The user cancels by simply releasing the button
    /// outside the panel.
    /// </summary>
    internal sealed class NativeTabDragObserver : IDisposable
    {
        // sysc32 (common controls) tab messages.
        private const int TCM_FIRST = 0x1300;
        private const int TCM_GETITEMCOUNT = TCM_FIRST + 4;
        private const int TCM_GETCURSEL = TCM_FIRST + 11;

        // The main tab bar is a plain SysTabControl32; its class name is stable.
        private const string TabBarClassName = "SysTabControl32";

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;
        private const int VK_ESCAPE = 0x1B;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(System.Drawing.Point p);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        private readonly IntPtr _nppHandle;
        private readonly Func<int, string> _pathByIndexProvider;

        private bool _dragActive;
        private int _lastTabIndex = -1;

        /// <summary>
        /// Raised once per tab change while a native tab drag is in progress and the
        /// cursor is over the tab bar. Parameter: full path of the dragged buffer.
        /// </summary>
        public event Action<string> DragOverTabChanged;

        /// <summary>
        /// Raised exactly once when the held button is released while a native tab
        /// drag was in progress. Parameter: cursor position in screen coordinates.
        /// The tab bar holds mouse capture during the drag, so the panel never sees
        /// WM_LBUTTONUP itself — the release must be observed here.
        /// </summary>
        public event Action<System.Drawing.Point> DragReleased;

        /// <summary>Raised once when the user aborts the gesture with Escape.</summary>
        public event Action DragCancelled;

        public NativeTabDragObserver(
            IntPtr nppHandle,
            Func<int, string> pathByIndexProvider)
        {
            _nppHandle = nppHandle;
            _pathByIndexProvider = pathByIndexProvider;
        }

        /// <summary>
        /// Called by the panel on a timer (~every 50 ms). Polls the async key and
        /// window state; raises events on state transitions only.
        /// </summary>
        public void Poll()
        {
            try
            {
                bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                bool anyDown = leftDown
                    || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
                    || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;

                if (!anyDown)
                {
                    if (_dragActive)
                    {
                        _dragActive = false;
                        _lastTabIndex = -1;
                        DragReleased?.Invoke(System.Windows.Forms.Cursor.Position);
                    }
                    return;
                }

                if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0 && _dragActive)
                {
                    _dragActive = false;
                    _lastTabIndex = -1;
                    DragCancelled?.Invoke();
                    return;
                }

                var cursor = System.Windows.Forms.Cursor.Position;
                var window = WindowFromPoint(cursor);
                if (window == IntPtr.Zero)
                {
                    if (_dragActive) return; // cursor over a popup — keep current state
                    return;
                }

                if (!IsTabBar(window))
                {
                    if (_dragActive)
                    {
                        // Cursor left the tab bar: drag continues toward the panel
                        // (or anywhere else). Keep _dragActive so the user can
                        // complete the gesture; nothing to report yet.
                    }
                    return;
                }

                // Cursor is over the tab bar while the button is held — this is the
                // beginning (or continuation) of a native tab drag.
                if (!_dragActive)
                {
                    _dragActive = true;
                    _lastTabIndex = -1;
                }

                int tabCount = (int)Win32.SendMessage(window, TCM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (tabCount <= 0) return;

                int currentIndex = (int)Win32.SendMessage(window, TCM_GETCURSEL, IntPtr.Zero, IntPtr.Zero);

                // During Notepad++'s drag the highlighted tab typically follows the
                // cursor; report only when it changes so the panel gets one event per
                // tab, not a flood.
                if (currentIndex == _lastTabIndex) return;
                _lastTabIndex = currentIndex;

                if (currentIndex >= 0 && currentIndex < tabCount)
                {
                    var path = _pathByIndexProvider?.Invoke(currentIndex);
                    if (!string.IsNullOrEmpty(path))
                        DragOverTabChanged?.Invoke(path);
                }
            }
            catch
            {
                // Observation is best-effort; never let it disturb the host.
            }
        }

        private bool IsTabBar(IntPtr window)
        {
            if (window == IntPtr.Zero) return false;
            var className = new System.Text.StringBuilder(64);
            GetClassName(window, className, className.Capacity);
            if (string.Equals(className.ToString(), TabBarClassName, StringComparison.OrdinalIgnoreCase))
            {
                // Confirm it belongs to Notepad++'s main window, not some other app.
                return GetParent(GetParent(window)) == _nppHandle || GetParent(window) == _nppHandle;
            }
            return false;
        }

        public void Dispose()
        {
            DragOverTabChanged = null;
            DragReleased = null;
            DragCancelled = null;
        }
    }
}
