using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using RGiesecke.DllExport;
using VirtualTabGroups.Core;
using VirtualTabGroups.Plugin.Npp;
using VirtualTabGroups.Plugin.UI;

namespace VirtualTabGroups.Plugin
{
    /// <summary>
    /// Plugin entry point. Exports the six unmanaged functions required by Notepad++
    /// directly from this managed DLL via RGiesecke.DllExport IL post-processing,
    /// eliminating the need for a separate C++/CLI bridge loader.
    /// </summary>
    public static class PluginMain
    {
        internal const string PluginName = "Virtual Tab Groups";
        // Notepad++ menu command IDs (menuCmdID.h): IDM = 40000, IDM_FILE = IDM + 1000.
        private const int IdmFileSave = 41006;    // IDM_FILE_SAVE
        private const int IdmFileClose = 41003;   // IDM_FILE_CLOSE
        private const int IdmFileSaveAs = 41008;  // IDM_FILE_SAVEAS
        private static NppData _nppData;
        private static readonly Core.DirtyStateTracker _dirtyTracker = new Core.DirtyStateTracker();
        private static StateStore _stateStore;
        private static FolderNode _root;
        private static NotepadPlusPlusObserver _observer;
        private static VirtualTabGroupsPanel _panel;
        private static readonly object _pendingUnsavedLock = new object();
        private static readonly HashSet<IntPtr> _pendingUnsavedBufferIds = new HashSet<IntPtr>();
        private static System.Threading.Timer _pendingUnsavedTimer;

        internal static ThemeManager Theme { get; private set; }

        /// <summary>Dirty-state cache shared with the panel for unsaved-change indicators.</summary>
        internal static Core.DirtyStateTracker DirtyTracker => _dirtyTracker;

        // Menu item command IDs.
        private const int CmdId_ShowPanel = 0;
        private const int CmdId_About = 1;
        private const int FuncItemCount = 2;

        private static FuncItem[] _funcItems;
        private static IntPtr _funcItemsPtr = IntPtr.Zero;

        // Stable HGlobal pointer for the plugin name string. Notepad++ holds this pointer
        // for the lifetime of the process; a managed string reference would be moved/collected.
        private static IntPtr _namePtr = IntPtr.Zero;

        // Guards against installing the AssemblyResolve handler more than once.
        private static bool _resolverInstalled;

        // Cached delegates so GC doesn't collect them between Notepad++ menu invocations.
        private static Action _showPanelDelegate;
        private static Action _aboutDelegate;

        // 16x16 icon for the docked-panel tab. Stored statically so the HICON it
        // exposes stays valid for the plugin's lifetime — Notepad++ keeps a reference
        // to the HICON after RegisterAsDockedPanel returns.
        private static System.Drawing.Icon _tabIcon;
        private static IntPtr _tabIconHandle = IntPtr.Zero;

        // ---- Notepad++ unmanaged entry points ----

        /// <summary>
        /// Tells Notepad++ this plugin handles Unicode (UTF-16). Must return a 4-byte
        /// Windows BOOL (not a 1-byte C# bool) to match the BOOL ABI Notepad++ expects.
        /// </summary>
        [DllExport("isUnicode", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool isUnicode() => true;

        [DllExport("setInfo", CallingConvention = CallingConvention.Cdecl)]
        public static void setInfo(NppData nppData)
        {
            // Must run before any managed dependency (Newtonsoft.Json, etc.) is JIT-compiled.
            InstallAssemblyResolverOnce();
            try
            {
                _nppData = nppData;

                var configDirBuilder = new StringBuilder(512);
                Win32.SendMessageStringBuilder(
                    _nppData._nppHandle,
                    (int)NppMsg.NPPM_GETPLUGINSCONFIGDIR,
                    new IntPtr(configDirBuilder.Capacity),
                    configDirBuilder);

                var pluginConfigDir = Path.Combine(configDirBuilder.ToString(), "VirtualTabGroups");
                Directory.CreateDirectory(pluginConfigDir);

                CrashLog.Initialize(pluginConfigDir);
                CrashLog.Write("setInfo: configDir=" + pluginConfigDir);

                var stateFilePath = Path.Combine(pluginConfigDir, "state.json");
                _observer = new NotepadPlusPlusObserver(new WindowsMessageBoxProxy());
                _stateStore = new StateStore(stateFilePath, _observer);
            }
            catch (Exception ex)
            {
                ReportCrash("setInfo", ex);
            }
        }

        /// <summary>
        /// Returns a stable wchar_t* pointer to the plugin name. Notepad++ holds this pointer
        /// for the process lifetime so we must keep it in unmanaged HGlobal memory, not on the
        /// managed heap where the GC could move or collect it.
        /// </summary>
        [DllExport("getName", CallingConvention = CallingConvention.Cdecl)]
        public static IntPtr getName()
        {
            try
            {
                if (_namePtr == IntPtr.Zero)
                    _namePtr = Marshal.StringToHGlobalUni(PluginName);
                return _namePtr;
            }
            catch (Exception ex)
            {
                ReportCrash("getName", ex);
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Notepad++ calls getFuncsArray(int* nbF). We receive the pointer as IntPtr and
        /// write the count into it so Notepad++ knows how many menu items we registered.
        /// </summary>
        [DllExport("getFuncsArray", CallingConvention = CallingConvention.Cdecl)]
        public static IntPtr getFuncsArray(IntPtr nbFPtr)
        {
            try
            {
                if (_funcItemsPtr == IntPtr.Zero) BuildFuncItems();
                if (nbFPtr != IntPtr.Zero)
                    Marshal.WriteInt32(nbFPtr, FuncItemCount);
                return _funcItemsPtr;
            }
            catch (Exception ex)
            {
                ReportCrash("getFuncsArray", ex);
                if (nbFPtr != IntPtr.Zero) Marshal.WriteInt32(nbFPtr, 0);
                return IntPtr.Zero;
            }
        }

        [DllExport("beNotified", CallingConvention = CallingConvention.Cdecl)]
        public static void beNotified(IntPtr notifyCodePtr)
        {
            try
            {
                var notification = (SCNotification)Marshal.PtrToStructure(notifyCodePtr, typeof(SCNotification));
                switch ((NppNotif)notification.nmhdr.code)
                {
                    case NppNotif.NPPN_READY:
                        OnNppReady();
                        break;

                    case NppNotif.NPPN_FILEBEFORECLOSE:
                        // Virtual Tab Groups is a history manager: closing a Notepad++
                        // tab must not remove its entry from the user's virtual tree.
                        RememberPathBeforeClose(notification.nmhdr.idFrom);
                        OnFileClosed(notification.nmhdr.idFrom);
                        break;

                    case NppNotif.NPPN_FILECLOSED:
                        // The document is gone; an indicator it left behind would keep
                        // claiming unsaved changes for a buffer that no longer exists.
                        ClearDirtyAfterClose(notification.nmhdr.idFrom);
                        OnFileClosed(notification.nmhdr.idFrom);
                        break;

                    case NppNotif.NPPN_FILEOPENED:
                        // Auto-track completed saved-file opens only. A transient
                        // BUFFERACTIVATED notification can expose a placeholder name
                        // (for example "new 1") while Explorer opening is still in
                        // progress; never persist that placeholder here.
                        OnFileOpened(notification.nmhdr.idFrom);
                        break;

                    case (NppNotif)ScintillaNotif.SCN_SAVEPOINTREACHED:
                        // Scintilla notifications identify the editor by hwndFrom;
                        // idFrom is not a Notepad++ BufferID here.
                        OnScintillaDirtyNotification(notification.nmhdr.hwndFrom, isDirty: false);
                        break;

                    case (NppNotif)ScintillaNotif.SCN_SAVEPOINTLEFT:
                        OnScintillaDirtyNotification(notification.nmhdr.hwndFrom, isDirty: true);
                        break;

                    case (NppNotif)ScintillaNotif.SCN_MODIFIED:
                        // Save-point notifications are not emitted for every editing
                        // path in all Notepad++/Scintilla versions. Querying SCI_GETMODIFY
                        // on the originating editor makes the indicator reliable for
                        // typing, paste, undo and redo without polling.
                        OnScintillaDirtyNotification(notification.nmhdr.hwndFrom, isDirty: null);
                        break;

                    case NppNotif.NPPN_BUFFERACTIVATED:
                        // New unsaved buffers (Ctrl+N) never fire NPPN_FILEOPENED.
                        // BUFFERACTIVATED also fires transiently while Explorer opens
                        // a saved file, so tracking is delayed until the name settles.
                        OnBufferActivated(notification.nmhdr.idFrom);
                        // The tree follows the active tab. Read-only: no model change.
                        OnActiveBufferChanged(notification.nmhdr.idFrom);
                        break;

                    case NppNotif.NPPN_FILEBEFORESAVE:
                        // Save As replaces the buffer path silently, so the old one has
                        // to be captured before the write happens.
                        RememberPathBeforeSave(notification.nmhdr.idFrom);
                        break;

                    case NppNotif.NPPN_FILESAVED:
                        // A save can change the file name (Save As from the main window)
                        // and always confirms the on-disk path is current. Also syncs
                        // the dirty indicator (save point reached fires before FILESAVED
                        // but its idFrom is a buffer ID, not a path).
                        OnFileSavedOrRenamed(notification.nmhdr.idFrom);
                        break;

                    case NppNotif.NPPN_FILERENAMED:
                        // Rename from Notepad++'s main window. idFrom = bufferId.
                        OnFileSavedOrRenamed(notification.nmhdr.idFrom);
                        break;

                    case NppNotif.NPPN_DARKMODECHANGED:
                        Theme?.RefreshColors();
                        break;

                    case NppNotif.NPPN_BEFORESHUTDOWN:
                        CrashLog.Write("beNotified: NPPN_BEFORESHUTDOWN — preserving file history");
                        break;

                    case NppNotif.NPPN_CANCELSHUTDOWN:
                        CrashLog.Write("beNotified: NPPN_CANCELSHUTDOWN — file history remains unchanged");
                        break;

                    case NppNotif.NPPN_SHUTDOWN:
                        OnNppShutdown();
                        break;
                }
            }
            catch (Exception ex)
            {
                ReportCrash("beNotified", ex);
            }
        }

        [DllExport("messageProc", CallingConvention = CallingConvention.Cdecl)]
        public static IntPtr messageProc(uint msg, IntPtr wParam, IntPtr lParam)
        {
            try { return IntPtr.Zero; }
            catch (Exception ex) { ReportCrash("messageProc", ex); return IntPtr.Zero; }
        }

        // ---- State accessors (used by future phases) ----

        internal static IntPtr NppHandle => _nppData._nppHandle;
        internal static NppData NppData => _nppData;
        internal static StateStore StateStore => _stateStore;
        internal static FolderNode Root
        {
            get => _root;
            set => _root = value;
        }

        // ---- Notification handlers ----

        private static void OnNppReady()
        {
            if (_stateStore == null) return;
            _root = _stateStore.Load();

            // Older builds could add the same file twice (rename plus auto-track) and
            // that duplicate survives in state.json. Offer a one-time cleanup, but
            // never touch curated entries without the user's consent.
            RemoveDuplicateEntriesIfConfirmed();

            // File entries are history. Saved paths and unsaved buffer names remain in
            // the user's curated tree even when a buffer did not survive the restart.
            var openPaths = GetAllOpenFilePaths() ?? Array.Empty<string>();
            var liveBufferNames = new HashSet<string>(openPaths.Where(p => p != null), StringComparer.OrdinalIgnoreCase);
            int purgedStale = PurgeDeadUnsavedEntries(_root, liveBufferNames);
            if (purgedStale > 0)
            {
                CrashLog.Write("OnNppReady: preserved " + purgedStale + " history entry(ies); "
                    + liveBufferNames.Count + " live buffer(s)");
                _stateStore.MarkDirty(_root, _stateStore.LastSelectedId);
            }

            // Session restore and files passed on the command line fire NPPN_FILEOPENED
            // before NPPN_READY, when there is no tree yet. Sweep once here so no open
            // tab is missing from the panel at startup.
            ReconcileOpenDocuments();

            Theme = new ThemeManager(new Win32NppMessageSender(_nppData._nppHandle));
            Theme.Initialize();

            // Eagerly create and register the panel so Notepad++'s docking manager
            // can restore its last-session visibility from dockingMgr.xml. If we wait
            // until the user clicks the menu (lazy create), Notepad++ has already
            // finished applying its saved layout by then and our panel can never
            // be visible at startup.
            EnsurePanelRegistered();
        }

        private static void OnNppShutdown()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            CrashLog.Write("OnNppShutdown: entered");

            try { _stateStore?.Flush(); }
            catch (Exception ex) { CrashLog.Write("OnNppShutdown: state flush failed - " + ex.Message); }
            finally
            {
                lock (_pendingUnsavedLock)
                {
                    _pendingUnsavedBufferIds.Clear();
                    _pendingUnsavedTimer?.Dispose();
                    _pendingUnsavedTimer = null;
                }

                // Nothing can be saved or closed after this point; the captured
                // pre-save/pre-close paths must not outlive the session.
                _pathsBeforeSave.Clear();
                _pathsBeforeClose.Clear();

                try { _stateStore?.Dispose(); }
                catch (Exception ex) { CrashLog.Write("OnNppShutdown: state dispose failed - " + ex.Message); }
            }

            // Without explicit Dispose, the panel's owned resources (cached GDI objects,
            // cached GDI brushes/pens, the embedded TreeView) get finalized off-thread once
            // Notepad++ tears its window tree down. Finalization can hold the process alive
            // long enough that a quick re-launch of notepad++.exe sees the dying instance and
            // does nothing — making the user click again. Dispose synchronously here.
            try
            {
                if (_panel != null && !_panel.IsDisposed)
                {
                    _panel.Dispose();
                }
            }
            catch (Exception ex) { CrashLog.Write("OnNppShutdown: panel dispose failed - " + ex.Message); }
            finally { _panel = null; }

            // Release the 16x16 tab icon. The HICON is owned by the managed Icon, so
            // disposing the Icon releases the GDI handle (no separate DestroyIcon needed).
            try
            {
                _tabIcon?.Dispose();
            }
            catch (Exception ex) { CrashLog.Write("OnNppShutdown: icon dispose failed - " + ex.Message); }
            finally
            {
                _tabIcon = null;
                _tabIconHandle = IntPtr.Zero;
            }

            CrashLog.Write("OnNppShutdown: complete in " + sw.ElapsedMilliseconds + "ms");
        }

        // ---- Helpers ----

        /// <summary>
        /// Installs an AssemblyResolve handler that locates managed dependencies
        /// (Newtonsoft.Json.dll, etc.) in this DLL's own directory rather than the
        /// CLR's default probe path (which is the host EXE's directory — Notepad++'s
        /// install folder — and does NOT contain our plugin's dependencies).
        /// </summary>
        private static void InstallAssemblyResolverOnce()
        {
            if (_resolverInstalled) return;
            _resolverInstalled = true;

            string pluginDir;
            try { pluginDir = Path.GetDirectoryName(typeof(PluginMain).Assembly.Location); }
            catch { return; }

            if (string.IsNullOrEmpty(pluginDir)) return;

            AppDomain.CurrentDomain.AssemblyResolve += (object sender, ResolveEventArgs args) =>
            {
                try
                {
                    var requested = new AssemblyName(args.Name);
                    var candidate = Path.Combine(pluginDir, requested.Name + ".dll");
                    if (File.Exists(candidate))
                        return Assembly.LoadFrom(candidate);
                }
                catch { }
                return null;
            };
        }

        /// <summary>
        /// Shows a fatal-error MessageBox so the user knows what went wrong instead of
        /// Notepad++ crashing silently. Best-effort — uses the observer's save-failure
        /// path if available, otherwise falls back to a direct MessageBox call.
        /// </summary>
        private static void ReportCrash(string source, Exception ex)
        {
            CrashLog.WriteException(source, ex);
            try
            {
                var message = source + " failed: " + ex.GetType().Name + ": " + ex.Message
                              + "\n\n" + ex.StackTrace;
                if (_observer != null)
                {
                    _observer.OnSaveFailed(ex);
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show(
                        message,
                        "Virtual Tab Groups error",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
            catch { /* last-resort fallback */ }
        }

        /// <summary>
        /// Allocates a ShortcutKey struct in unmanaged memory and returns a pointer to it.
        /// Returns IntPtr.Zero for a zeroed (no-shortcut) key rather than allocating.
        /// The pointer is kept alive for the process lifetime — no FreeHGlobal.
        /// </summary>
        private static IntPtr AllocShortcutKey(ShortcutKey sk)
        {
            if (sk._key == 0 && sk._isCtrl == 0 && sk._isAlt == 0 && sk._isShift == 0)
                return IntPtr.Zero;
            IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ShortcutKey)));
            Marshal.StructureToPtr(sk, p, false);
            return p;
        }

        private static void BuildFuncItems()
        {
            _showPanelDelegate = OnShowPanel;
            _aboutDelegate = OnAbout;

            _funcItems = new FuncItem[FuncItemCount];

            _funcItems[CmdId_ShowPanel] = new FuncItem
            {
                _itemName = "Show Virtual Tab Groups",
                _pFunc = Marshal.GetFunctionPointerForDelegate(_showPanelDelegate),
                _cmdID = CmdId_ShowPanel,
                _init2Check = false,
                _pShKey = AllocShortcutKey(new ShortcutKey(ctrl: true, alt: false, shift: true, key: (byte)'T')),
            };

            _funcItems[CmdId_About] = new FuncItem
            {
                _itemName = "About Virtual Tab Groups",
                _pFunc = Marshal.GetFunctionPointerForDelegate(_aboutDelegate),
                _cmdID = CmdId_About,
                _init2Check = false,
                _pShKey = IntPtr.Zero,
            };

            int size = Marshal.SizeOf(typeof(FuncItem));
            _funcItemsPtr = Marshal.AllocHGlobal(size * _funcItems.Length);
            for (int i = 0; i < _funcItems.Length; i++)
            {
                IntPtr slot = new IntPtr(_funcItemsPtr.ToInt64() + i * size);
                Marshal.StructureToPtr(_funcItems[i], slot, false);
            }
        }

        /// <summary>
        /// Loads the 16x16 panel-tab icon from the embedded resource once.
        /// </summary>
        private static void LoadTabIconOnce()
        {
            if (_tabIconHandle != IntPtr.Zero) return;
            try
            {
                using (var stream = typeof(PluginMain).Assembly.GetManifestResourceStream(
                    "VirtualTabGroups.Plugin.Resources.plugin.ico"))
                {
                    if (stream != null)
                    {
                        _tabIcon = new System.Drawing.Icon(stream, new System.Drawing.Size(16, 16));
                        _tabIconHandle = _tabIcon.Handle;
                    }
                }
            }
            catch { /* missing icon resource is non-fatal */ }
        }

        /// <summary>
        /// Creates the panel form and registers it as a docked panel with Notepad++.
        /// Idempotent — subsequent calls are no-ops. Called eagerly from OnNppReady so
        /// Notepad++'s docking manager can restore the panel's last-session visibility
        /// before the user even sees the editor window.
        /// </summary>
        private static void EnsurePanelRegistered()
        {
            if (_panel != null) return;

            LoadTabIconOnce();

            _panel = new VirtualTabGroupsPanel();
            // Force handle creation without making the form visible — Notepad++'s
            // docking manager applies visibility from its saved layout right after
            // we register, so an explicit Show() here would just cause a brief flash.
            var _ = _panel.Handle;
            _panel.AttachTheme(Theme);

            _panel.RegisterAsDockedPanel(
                nppHandle: _nppData._nppHandle,
                moduleName: "VirtualTabGroups",
                caption: PluginName,
                cmdId: CmdId_ShowPanel,
                dockingFlags: NppTbMsg.DWS_DF_CONT_LEFT | NppTbMsg.DWS_ICONTAB,
                iconHandle: _tabIconHandle);

            Win32.SendMessage(_nppData._nppHandle,
                (int)NppMsg.NPPM_DARKMODESUBCLASSANDTHEME,
                new IntPtr(1),
                _panel.Handle);

            _panel.BindRoot(_root, _stateStore, _stateStore.LastSelectedId);

            AttachTabDragObserver();
        }

        /// <summary>
        /// Starts observing native tab-bar drags so the panel can accept a tab
        /// dropped onto it. Read-only observation: native tab reordering is untouched.
        /// </summary>
        private static void AttachTabDragObserver()
        {
            try
            {
                var observer = new NativeTabDragObserver(
                    _nppData._nppHandle,
                    index =>
                    {
                        var openPaths = GetAllOpenFilePaths();
                        if (openPaths == null || index < 0 || index >= openPaths.Length) return null;
                        return openPaths[index];
                    });
                _panel.AttachTabDragObserver(observer);
                CrashLog.Write("AttachTabDragObserver: native tab drag observation enabled");
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("AttachTabDragObserver", ex);
            }
        }

        private static void OnShowPanel()
        {
            // Menu callbacks run inside Notepad++'s message pump: an exception here
            // would take the host down, so everything is guarded.
            try
            {
                // Defensive: NPPN_READY normally runs before the user can click any menu
                // item, but if something goes wrong, lazy-create the panel here as a fallback.
                EnsurePanelRegistered();

                if (_panel == null) return;

                if (_panel.Visible)
                    _panel.HideDocked(_nppData._nppHandle);
                else
                    _panel.ShowDocked(_nppData._nppHandle);
            }
            catch (Exception ex) { ReportCrash("OnShowPanel", ex); }
        }

        /// <summary>
        /// Mirrors the active tab in the panel selection. Purely presentational: it
        /// never adds entries or opens files.
        /// </summary>
        private static void OnActiveBufferChanged(IntPtr bufferId)
        {
            if (_panel == null || _panel.IsDisposed || bufferId == IntPtr.Zero) return;

            try
            {
                var path = GetPathForBufferId(bufferId);
                if (string.IsNullOrEmpty(path)) return;

                // The tree stays a superset of the open tabs: activating a saved
                // document that is not tracked (opened at startup, or removed earlier)
                // brings its entry back before the selection sync runs.
                EnsureTrackedOpenDocument(path);

                if (!_panel.IsHandleCreated) return;
                _panel.SyncSelectionToPath(path);
            }
            catch (Exception ex) { CrashLog.WriteException("OnActiveBufferChanged", ex); }
        }

        private static void OnAbout()
        {
            using (var dlg = new AboutDialog(Theme))
                dlg.ShowDialog();
        }

        /// <summary>
        /// Returns the absolute path of Notepad++'s currently active document.
        ///
        /// Implementation note: this used to be <c>SendMessage(NPPM_GETFULLCURRENTPATH, ...)</c>,
        /// but that message crashes Notepad++ in current builds (confirmed by diagnostic probe
        /// 2026-05-19). The two-step <c>NPPM_GETCURRENTBUFFERID</c> + <c>NPPM_GETFULLPATHFROMBUFFERID</c>
        /// path produces the same data via a different handler that is stable.
        /// </summary>
        internal static string GetCurrentFullPath()
        {
            IntPtr bufferId = Win32.SendMessage(
                _nppData._nppHandle,
                (int)NppMsg.NPPM_GETCURRENTBUFFERID,
                IntPtr.Zero,
                IntPtr.Zero);

            if (bufferId == IntPtr.Zero) return string.Empty;

            return GetPathForBufferId(bufferId);
        }

        internal static string GetPathForBufferId(IntPtr bufferId)
        {
            const int bufSize = 512;
            IntPtr buffer = Marshal.AllocHGlobal(bufSize * 2);
            try
            {
                for (int i = 0; i < bufSize * 2; i++) Marshal.WriteByte(buffer, i, 0);
                Win32.SendMessage(
                    _nppData._nppHandle,
                    (int)NppMsg.NPPM_GETFULLPATHFROMBUFFERID,
                    bufferId,
                    buffer);
                return Marshal.PtrToStringUni(buffer) ?? string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Internal open used by plugin flows that already guarantee a live buffer
        /// (TryCloseFile, TrySaveAsUnsavedFile, TryRenameSavedFile). For a dead
        /// unsaved entry NPPM_SWITCHTOFILE returns FALSE; no error dialog here —
        /// UI flows must use TryOpenFile instead, which offers recovery options.
        /// </summary>
        internal static bool OpenFileUnchecked(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            bool isUnsaved = !System.IO.Path.IsPathRooted(path);
            IntPtr pathPtr = Marshal.StringToHGlobalUni(path);
            try
            {
                int msg = isUnsaved ? (int)NppMsg.NPPM_SWITCHTOFILE : (int)NppMsg.NPPM_DOOPEN;
                IntPtr result = Win32.SendMessage(_nppData._nppHandle, msg, IntPtr.Zero, pathPtr);
                bool ok = !isUnsaved || result != IntPtr.Zero;
                if (!ok)
                    CrashLog.Write("OpenFileUnchecked: buffer '" + path + "' not found");
                return ok;
            }
            finally { Marshal.FreeHGlobal(pathPtr); }
        }

        /// <summary>
        /// User-facing open. Returns false only when the entry is out of sync with
        /// reality: the unsaved buffer was closed without saving, or the saved file
        /// no longer exists on disk. The panel turns that into a recovery dialog;
        /// success means Notepad++ now shows the document.
        /// </summary>
        internal static bool TryOpenFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (!System.IO.Path.IsPathRooted(path))
                return IsPathOpen(path) && OpenFileUnchecked(path);

            if (!File.Exists(path)) return false;
            OpenFileUnchecked(path);
            return true;
        }

        /// <summary>Fire-and-forget open. UI flows should prefer TryOpenFile:
        /// it reports dead entries instead of silently doing nothing.</summary>
        internal static void OpenFile(string path) => OpenFileUnchecked(path);

        internal static bool TryCloseFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !IsPathOpen(path)) return false;

            OpenFileUnchecked(path);
            // wParam must be zero; the command id goes in lParam.
            Win32.SendMessage(_nppData._nppHandle,
                (int)NppMsg.NPPM_MENUCOMMAND,
                IntPtr.Zero,
                new IntPtr(IdmFileClose));

            // The native command displays Notepad++'s own save/discard/cancel dialog
            // for dirty buffers. A cancelled close leaves the path open, so it is not
            // removed from the virtual history.
            bool closed = !IsPathOpen(path);
            CrashLog.Write("TryCloseFile: path='" + path + "' closed=" + closed);
            return closed;
        }

        /// <summary>
        /// Saves one open document through Notepad++'s own save command, so encoding,
        /// line endings and the native error handling stay authoritative.
        /// </summary>
        internal static bool TrySaveFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !IsPathOpen(path)) return false;

            OpenFileUnchecked(path);
            Win32.SendMessage(_nppData._nppHandle,
                (int)NppMsg.NPPM_MENUCOMMAND,
                IntPtr.Zero,
                new IntPtr(IdmFileSave));
            return true;
        }

        internal static bool TrySaveAsUnsavedFile(string path, out string savedPath)
        {
            savedPath = null;
            if (string.IsNullOrEmpty(path) || System.IO.Path.IsPathRooted(path) || !IsPathOpen(path))
                return false;

            OpenFileUnchecked(path);
            // Same NPPM_MENUCOMMAND contract: wParam = 0, lParam = cmdID.
            Win32.SendMessage(_nppData._nppHandle,
                (int)NppMsg.NPPM_MENUCOMMAND,
                IntPtr.Zero,
                new IntPtr(IdmFileSaveAs));

            var currentPath = GetCurrentFullPath();
            if (string.IsNullOrEmpty(currentPath) || !System.IO.Path.IsPathRooted(currentPath))
                return false;

            savedPath = currentPath;
            return true;
        }

        internal static bool TryRenameSavedFile(string path, ThemeManager theme, out string renamedPath)
        {
            renamedPath = null;
            if (string.IsNullOrEmpty(path) || !System.IO.Path.IsPathRooted(path)
                || !File.Exists(path))
                return false;

            var directory = Path.GetDirectoryName(path);
            var currentName = Path.GetFileName(path);
            using (var dialog = new FileNameDialog(currentName, theme))
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return false;

                var newName = dialog.FileName;
                if (string.IsNullOrWhiteSpace(newName)
                    || string.Equals(newName, currentName, StringComparison.Ordinal))
                    return false;

                if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    System.Windows.Forms.MessageBox.Show(
                        "The file name contains characters that Windows does not allow.",
                        PluginName,
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                    return false;
                }

                renamedPath = Path.Combine(directory, newName);
                if (File.Exists(renamedPath))
                {
                    System.Windows.Forms.MessageBox.Show(
                        "A file with that name already exists in this folder.",
                        PluginName,
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                    renamedPath = null;
                    return false;
                }
            }

            bool wasOpen = IsPathOpen(path);
            if (wasOpen && !TryCloseFile(path))
            {
                renamedPath = null;
                return false;
            }

            try
            {
                File.Move(path, renamedPath);
                if (wasOpen)
                {
                    // Reopening raises NPPN_FILEOPENED; the caller relinks the
                    // existing entry afterwards, so auto-tracking stays off here.
                    SuppressAutoTrack = true;
                    try { OpenFileUnchecked(renamedPath); }
                    finally { SuppressAutoTrack = false; }
                }
                return true;
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("Rename file on disk", ex);
                System.Windows.Forms.MessageBox.Show(
                    "The file could not be renamed on disk.\n\n" + ex.Message,
                    PluginName,
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                renamedPath = null;
                return false;
            }
        }

        internal static bool IsPathOpen(string path)
        {
            var openPaths = GetAllOpenFilePaths() ?? Array.Empty<string>();
            return openPaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Entries repeating a path are pure noise: both rows open the same file, and
        /// older builds could create them when a rename auto-tracked the reopened file.
        /// Deleting the user's curated entries on our own is not acceptable, so the
        /// cleanup happens only after an explicit confirmation; declining keeps the
        /// tree exactly as it is for the rest of the session.
        /// </summary>
        private static void RemoveDuplicateEntriesIfConfirmed()
        {
            if (_root == null || _stateStore == null) return;

            try
            {
                var duplicates = TreeQuery.FindRedundantFileEntries(_root);
                if (duplicates.Count == 0) return;

                var preview = string.Join("\n", duplicates.Take(5).Select(f => f.Path));
                string message = "Virtual Tab Groups found " + duplicates.Count
                    + (duplicates.Count == 1 ? " duplicate entry" : " duplicate entries")
                    + " of the same file:\n\n" + preview
                    + (duplicates.Count > 5 ? "\n…" : "")
                    + "\n\nThe first entry of each file is kept and no file on disk is touched.";

                var answer = System.Windows.Forms.MessageBox.Show(
                    message,
                    PluginName,
                    System.Windows.Forms.MessageBoxButtons.YesNo,
                    System.Windows.Forms.MessageBoxIcon.Question,
                    System.Windows.Forms.MessageBoxDefaultButton.Button2);

                if (answer != System.Windows.Forms.DialogResult.Yes)
                {
                    CrashLog.Write("OnNppReady: " + duplicates.Count
                        + " duplicate entries kept by user choice");
                    return;
                }

                int removed = 0;
                foreach (var file in duplicates)
                {
                    var container = TreeMutator.FindContainer(_root, file);
                    if (container != null && container.Children.Remove(file)) removed++;
                }

                if (removed > 0)
                {
                    _stateStore.MarkDirty(_root, _stateStore.LastSelectedId);
                    CrashLog.Write("OnNppReady: removed " + removed + " duplicate entry(ies)");
                }
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("RemoveDuplicateEntriesIfConfirmed", ex);
            }
        }

        /// <summary>
        /// Keeps every virtual file entry across Notepad++ tab closes and restarts.
        /// A saved file can be reopened later through NPPM_DOOPEN. An unsaved buffer
        /// remains as history too; if its buffer no longer exists, OpenFile reports that
        /// it cannot be restored instead of silently deleting the user's entry.
        /// </summary>
        private static int PurgeDeadUnsavedEntries(FolderNode folder, HashSet<string> liveBufferNames)
        {
            // History is intentional. Do not purge unsaved entries merely because the
            // corresponding buffer did not survive a restart.
            return 0;
        }

        /// <summary>
        /// Sweeps every open saved document and adds the ones the tree does not know
        /// about yet. Startup is the main gap: NPPN_FILEOPENED fires before NPPN_READY
        /// for restored sessions and command-line files, when the tree does not exist.
        /// </summary>
        private static void ReconcileOpenDocuments()
        {
            if (_root == null || _stateStore == null) return;

            try
            {
                int added = 0;
                foreach (var path in GetAllOpenFilePaths() ?? Array.Empty<string>())
                {
                    if (EnsureTrackedOpenDocument(path)) added++;
                }

                if (added > 0)
                    CrashLog.Write("ReconcileOpenDocuments: added " + added
                        + " open tab(s) that were missing from the tree");
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("ReconcileOpenDocuments", ex);
            }
        }

        /// <summary>
        /// Adds one open saved document to the top of the tree when it is not tracked
        /// anywhere yet. Unsaved buffers are handled by the delayed activation path,
        /// because they have no path to compare.
        /// </summary>
        private static bool EnsureTrackedOpenDocument(string path)
        {
            if (_root == null || _stateStore == null) return false;
            if (string.IsNullOrEmpty(path) || !System.IO.Path.IsPathRooted(path)) return false;
            if (IsPathClaimed(_root, path)) return false;

            try
            {
                var added = TreeMutator.AddFile(_root, path, atTop: true);
                if (added == null) return false;

                _stateStore.MarkDirty(_root, _stateStore.LastSelectedId);
                _panel?.RefreshFromModel();
                CrashLog.Write("EnsureTrackedOpenDocument: added open tab: " + path);
                return true;
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("EnsureTrackedOpenDocument", ex);
                return false;
            }
        }

        /// <summary>
        /// Auto-tracking: when the user opens a saved file in Notepad++, the entry
        /// joins the top of the root list once — unless it is already tracked
        /// anywhere in the tree. Unsaved buffers are handled separately by the
        /// delayed OnBufferActivated path, because this notification is reserved
        /// for completed saved-file opens.
        /// NPPN_FILEOPENED fires before NPPN_READY during session restore, when
        /// _root is still null — so restored startup tabs never flood the list.
        /// </summary>
        private static void OnFileOpened(IntPtr bufferId)
        {
            if (_root == null || _stateStore == null) return;

            try
            {
                var path = GetPathForBufferId(bufferId);
                if (string.IsNullOrEmpty(path) || !System.IO.Path.IsPathRooted(path)) return;

                // A reopen after the plugin's own rename must not add a second entry;
                // the existing one is relinked by the caller.
                if (SuppressAutoTrack)
                {
                    CrashLog.Write("OnFileOpened: auto-track suppressed for " + path);
                    return;
                }

                // Already tracked anywhere in the tree → nothing to add.
                if (IsPathClaimed(_root, path)) return;

                // Unsaved buffers ("new 2", ...) have a non-rooted name stored in
                // Path, so IsPathClaimed already de-duplicates them.
                TreeMutator.AddFile(_root, path, atTop: true);

                _stateStore.MarkDirty(_root, _stateStore.LastSelectedId);
                _panel?.RefreshFromModel();
                CrashLog.Write("OnFileOpened: added at top: " + path);
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("OnFileOpened", ex);
            }
        }

        /// <summary>
        /// Dirty-state tracking: Scintilla fires SCN_SAVEPOINTLEFT when a document
        /// gains unsaved changes and SCN_SAVEPOINTREACHED when it returns to its
        /// saved state. Scintilla identifies the originating editor in hwndFrom;
        /// the current Notepad++ BufferID is queried separately. The key is the
        /// buffer's full path (saved) or buffer name (unsaved) —
        /// the same key the panel uses for FileNode lookups. The indicator lives
        /// only in the panel (redraw), never in state.json.
        /// </summary>
        private static void OnScintillaDirtyNotification(IntPtr scintillaHandle, bool? isDirty)
        {
            if (scintillaHandle == IntPtr.Zero) return;

            try
            {
                const int SciGetModify = 2159;
                bool dirty = isDirty ?? Win32.SendMessage(
                    scintillaHandle, SciGetModify, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero;

                // NPPM_GETCURRENTBUFFERID is the supported bridge from the active
                // Scintilla editor to Notepad++'s BufferID. Do not use SCNotification.idFrom.
                var bufferId = Win32.SendMessage(
                    _nppData._nppHandle,
                    (int)NppMsg.NPPM_GETCURRENTBUFFERID,
                    IntPtr.Zero,
                    IntPtr.Zero);
                if (bufferId == IntPtr.Zero) return;

                var key = GetPathForBufferId(bufferId);
                if (string.IsNullOrEmpty(key)) return;

                if (_dirtyTracker.SetDirty(key, dirty))
                {
                    CrashLog.Write("OnScintillaDirtyNotification: key='" + key + "' dirty=" + dirty);
                    if (_panel != null && !_panel.IsDisposed && _panel.IsHandleCreated)
                        _panel.BeginInvoke(new Action(() => _panel.RedrawFileRowByKey(key)));
                }
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("OnScintillaDirtyNotification", ex);
            }
        }

        /// <summary>
        /// Catches unsaved buffers ("новый 1", ...) that never fire NPPN_FILEOPENED.
        /// Notepad++ can emit BUFFERACTIVATED before an Explorer open has finished,
        /// while the new document still temporarily has a placeholder name. Delay the
        /// decision and query the buffer again: a real unsaved buffer remains non-rooted,
        /// while a file opened from Explorer has become an absolute path and is handled
        /// by NPPN_FILEOPENED.
        /// </summary>
        private static void OnBufferActivated(IntPtr bufferId)
        {
            if (_root == null || _stateStore == null || bufferId == IntPtr.Zero) return;

            lock (_pendingUnsavedLock)
            {
                _pendingUnsavedBufferIds.Add(bufferId);
                _pendingUnsavedTimer?.Dispose();
                _pendingUnsavedTimer = new System.Threading.Timer(
                    _ => FlushPendingUnsavedActivations(), null, 150, System.Threading.Timeout.Infinite);
            }
        }

        private static void FlushPendingUnsavedActivations()
        {
            IntPtr[] bufferIds;
            lock (_pendingUnsavedLock)
            {
                bufferIds = _pendingUnsavedBufferIds.ToArray();
                _pendingUnsavedBufferIds.Clear();
                _pendingUnsavedTimer?.Dispose();
                _pendingUnsavedTimer = null;
            }

            Action process = () =>
            {
                foreach (var bufferId in bufferIds)
                    AddStillUnsavedBuffer(bufferId);
            };

            try
            {
                if (_panel != null && !_panel.IsDisposed && _panel.IsHandleCreated)
                    _panel.BeginInvoke(process);
                else
                    process();
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("FlushPendingUnsavedActivations", ex);
            }
        }

        private static void AddStillUnsavedBuffer(IntPtr bufferId)
        {
            if (_root == null || _stateStore == null) return;

            try
            {
                var path = GetPathForBufferId(bufferId);
                if (string.IsNullOrEmpty(path) || System.IO.Path.IsPathRooted(path)) return;
                if (IsPathClaimed(_root, path)) return;

                TreeMutator.AddFile(_root, path, atTop: true);
                _stateStore.MarkDirty(_root, _stateStore.LastSelectedId);
                _panel?.RefreshFromModel();
                CrashLog.Write("OnBufferActivated: confirmed unsaved buffer at top: " + path);
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("AddStillUnsavedBuffer", ex);
            }
        }

        private static void OnFileClosed(IntPtr bufferId)
        {
            // Closing a tab changes its open/closed state, not its membership in the
            // user's virtual workspace. Keep the entry so it can be reopened later.
            CrashLog.Write("OnFileClosed: preserving history entry, bufferId="
                + bufferId.ToInt64().ToString("X"));
        }

        /// <summary>
        /// Paths captured at NPPN_FILEBEFORESAVE, keyed by buffer ID. The moment
        /// before the write is the only point where an entry can still be matched to
        /// its own buffer: afterwards Notepad++ reports just the new path, so a buffer
        /// saved from an unsaved name ("new 1") or a Save As that keeps the original
        /// file could never be relinked.
        /// </summary>
        private static readonly Dictionary<IntPtr, string> _pathsBeforeSave =
            new Dictionary<IntPtr, string>();

        /// <summary>
        /// Paths captured at NPPN_FILEBEFORECLOSE, keyed by buffer ID. FILECLOSED no
        /// longer resolves the buffer, so a discarded document's key is only readable
        /// while it is still open.
        /// </summary>
        private static readonly Dictionary<IntPtr, string> _pathsBeforeClose =
            new Dictionary<IntPtr, string>();

        /// <summary>
        /// Set while the plugin reopens a file it renamed itself: NPPN_FILEOPENED must
        /// not auto-track that buffer as an additional entry.
        /// </summary>
        internal static bool SuppressAutoTrack { get; set; }

        /// <summary>
        /// Syncs virtual history entries with Notepad++ after the user saved or
        /// renamed a file from the main window. Every FileNode whose stored path no
        /// longer exists on disk but whose buffer is still open with a new path gets
        /// its path and display name updated in place; then the panel refreshes.
        /// </summary>
        private static void OnFileSavedOrRenamed(IntPtr bufferId)
        {
            if (_root == null || _stateStore == null) return;

            try
            {
                var currentPath = GetPathForBufferId(bufferId);
                if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath)) return;

                string previousPath = null;
                bool hadPrevious = _pathsBeforeSave.TryGetValue(bufferId, out previousPath);
                if (hadPrevious) _pathsBeforeSave.Remove(bufferId);

                var openPaths = new HashSet<string>(
                    GetAllOpenFilePaths() ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

                // Exact relink first: the captured pre-save path identifies the entry
                // even when the buffer had no path yet or the old file still exists,
                // which is precisely when the on-disk heuristic below gives up.
                bool changed = RelinkEntry(previousPath, currentPath);
                if (!changed) changed = SyncNodePaths(_root, openPaths);

                if (changed)
                {
                    _stateStore.MarkDirty(_root, _stateStore.LastSelectedId);
                    _panel?.RefreshFromModel();

                }

                // The saved buffer is clean even when no history path needed syncing.
                // Keep this outside the changed block so ordinary Ctrl+S also clears
                // the indicator. A key for the old path/name must not linger either.
                if (hadPrevious && !SamePath(previousPath, currentPath))
                    _dirtyTracker.SetDirty(previousPath, false);
                _dirtyTracker.SetDirty(currentPath, false);

                CrashLog.Write("OnFileSavedOrRenamed: synced entries to current open paths (bufferId="
                    + bufferId.ToInt64().ToString("X") + ", path=" + currentPath + ")");
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("OnFileSavedOrRenamed", ex);
            }
        }

        private static void RememberPathBeforeSave(IntPtr bufferId)
        {
            if (_root == null || bufferId == IntPtr.Zero) return;

            try
            {
                var path = GetPathForBufferId(bufferId);
                if (string.IsNullOrEmpty(path)) _pathsBeforeSave.Remove(bufferId);
                else _pathsBeforeSave[bufferId] = path;
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("RememberPathBeforeSave", ex);
            }
        }

        private static void RememberPathBeforeClose(IntPtr bufferId)
        {
            if (bufferId == IntPtr.Zero) return;

            try
            {
                var path = GetPathForBufferId(bufferId);
                if (string.IsNullOrEmpty(path)) _pathsBeforeClose.Remove(bufferId);
                else _pathsBeforeClose[bufferId] = path;
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("RememberPathBeforeClose", ex);
            }
        }

        /// <summary>
        /// Clears the unsaved-changes indicator of a document that just closed. Without
        /// this a row kept its dot after a discarding close until the next restart,
        /// because only a save ever cleared the key. A cancelled close never reaches
        /// FILECLOSED, so a still-open dirty document keeps its indicator.
        /// </summary>
        private static void ClearDirtyAfterClose(IntPtr bufferId)
        {
            if (bufferId == IntPtr.Zero) return;

            string path;
            if (!_pathsBeforeClose.TryGetValue(bufferId, out path)) return;
            _pathsBeforeClose.Remove(bufferId);
            if (string.IsNullOrEmpty(path)) return;

            _dirtyTracker.SetDirty(path, false);
        }

        /// <summary>
        /// Moves the entry recorded at <paramref name="previousPath"/> to the path its
        /// buffer now has. Covers unsaved buffers that just got a real path and Save As
        /// onto a new name while the original file stays on disk.
        /// </summary>
        private static bool RelinkEntry(string previousPath, string currentPath)
        {
            if (string.IsNullOrEmpty(previousPath) || string.IsNullOrEmpty(currentPath)) return false;
            if (SamePath(previousPath, currentPath)) return false;

            var file = TreeQuery.FindFileByPath(_root, previousPath);
            if (file == null) return false;

            // Never leave two entries pointing at the same file.
            var existing = TreeQuery.FindFileByPath(_root, currentPath);
            if (existing != null && !ReferenceEquals(existing, file))
            {
                CrashLog.Write("RelinkEntry: '" + currentPath
                    + "' is already tracked — entry left unchanged");
                return false;
            }

            var folder = TreeMutator.FindContainer(_root, file);
            if (folder == null || !file.Relink(folder, currentPath)) return false;

            CrashLog.Write("RelinkEntry: '" + previousPath + "' -> '" + file.Path + "'");
            return true;
        }

        private static bool SamePath(string left, string right)
            => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Depth-first walk: for every FileNode whose Path is NOT among currently open
        /// paths AND whose old file no longer exists on disk, adopts an open buffer from
        /// the same directory — but only when there is exactly ONE unclaimed candidate.
        ///
        /// Notepad++ never reports the old path, so guessing between several candidates
        /// silently cross-linked entries to the wrong file. When the candidate is
        /// ambiguous the entry is left untouched and the panel's Locate action allows
        /// the user to resolve it explicitly.
        /// </summary>
        private static bool SyncNodePaths(FolderNode folder, HashSet<string> openPaths)
        {
            bool changed = false;

            foreach (var child in folder.Children)
            {
                if (child is FolderNode sub)
                {
                    changed |= SyncNodePaths(sub, openPaths);
                    continue;
                }
                if (child is not FileNode file) continue;

                bool isOpen = openPaths.Contains(file.Path);
                bool existsOnDisk = !string.IsNullOrEmpty(file.Path)
                    && System.IO.Path.IsPathRooted(file.Path)
                    && File.Exists(file.Path);

                if (isOpen || existsOnDisk) continue;

                string oldDir = null;
                try { oldDir = System.IO.Path.GetDirectoryName(file.Path); } catch { }
                if (string.IsNullOrEmpty(oldDir)) continue;

                var candidates = new List<string>();
                foreach (var openPath in openPaths)
                {
                    if (string.IsNullOrEmpty(openPath) || IsPathClaimed(_root, openPath)) continue;

                    string openDir = null;
                    try { openDir = System.IO.Path.GetDirectoryName(openPath); } catch { }
                    if (!string.Equals(openDir, oldDir, StringComparison.OrdinalIgnoreCase)) continue;

                    candidates.Add(openPath);
                }

                if (candidates.Count != 1)
                {
                    if (candidates.Count > 1)
                    {
                        CrashLog.Write("SyncNodePaths: ambiguous rename for '" + file.Name
                            + "' (" + candidates.Count + " candidates) — entry left unchanged");
                    }
                    continue;
                }

                // Relink re-resolves the label instead of resetting it to the file
                // name: an alias that disambiguated same-named files must survive.
                if (file.Relink(folder, candidates[0]))
                {
                    changed = true;
                    CrashLog.Write("SyncNodePaths: '" + file.Name + "' adopted new path " + file.Path);
                }
            }

            return changed;
        }

        private static bool IsPathClaimed(FolderNode folder, string path)
        {
            foreach (var child in folder.Children)
            {
                if (child is FileNode f
                    && string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (child is FolderNode sub && IsPathClaimed(sub, path))
                    return true;
            }
            return false;
        }

        internal static string[] GetAllOpenFilePaths()
        {
            int count = (int)Win32.SendMessage(
                _nppData._nppHandle,
                (int)NppMsg.NPPM_GETNBOPENFILES,
                IntPtr.Zero,
                IntPtr.Zero);

            if (count <= 0) return Array.Empty<string>();

            IntPtr arrayPtr = Marshal.AllocHGlobal(count * IntPtr.Size);
            var stringBuffers = new IntPtr[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    stringBuffers[i] = Marshal.AllocHGlobal(2048);
                    Marshal.WriteIntPtr(arrayPtr, i * IntPtr.Size, stringBuffers[i]);
                }

                Win32.SendMessage(
                    _nppData._nppHandle,
                    (int)NppMsg.NPPM_GETOPENFILENAMES,
                    arrayPtr,
                    new IntPtr(count));

                var result = new string[count];
                for (int i = 0; i < count; i++)
                    result[i] = Marshal.PtrToStringUni(stringBuffers[i]);
                return result;
            }
            finally
            {
                for (int i = 0; i < count; i++)
                    if (stringBuffers[i] != IntPtr.Zero) Marshal.FreeHGlobal(stringBuffers[i]);
                Marshal.FreeHGlobal(arrayPtr);
            }
        }
    }
}
