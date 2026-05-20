using System;
using System.IO;
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
        private static NppData _nppData;
        private static StateStore _stateStore;
        private static FolderNode _root;
        private static NotepadPlusPlusObserver _observer;
        private static VirtualTabGroupsPanel _panel;

        internal static ThemeManager Theme { get; private set; }

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
                        // Fires BEFORE the buffer is destroyed, so NPPM_GETFULLPATHFROMBUFFERID
                        // can still resolve the path. NPPN_FILECLOSED fires after destruction
                        // and the lookup returns empty.
                        OnFileClosed(notification.nmhdr.idFrom);
                        break;

                    case NppNotif.NPPN_FILECLOSED:
                        // Kept as a fallback for buffer IDs we somehow didn't catch in
                        // BEFORECLOSE — best-effort, may no-op if the buffer is gone.
                        OnFileClosed(notification.nmhdr.idFrom);
                        break;

                    case NppNotif.NPPN_DARKMODECHANGED:
                        Theme?.RefreshColors();
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

            Theme = new ThemeManager(new Win32NppMessageSender(_nppData._nppHandle));
            Theme.Initialize();
        }

        private static void OnNppShutdown()
        {
            try { _stateStore?.Flush(); }
            finally { _stateStore?.Dispose(); }
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

        private static void OnShowPanel()
        {
            if (_panel == null)
            {
                _panel = new VirtualTabGroupsPanel();
                _panel.Show();
                _panel.AttachTheme(Theme);

                _panel.RegisterAsDockedPanel(
                    nppHandle: _nppData._nppHandle,
                    moduleName: "VirtualTabGroups",
                    caption: PluginName,
                    cmdId: CmdId_ShowPanel,
                    dockingFlags: NppTbMsg.DWS_DF_CONT_LEFT,
                    iconHandle: IntPtr.Zero);

                Win32.SendMessage(_nppData._nppHandle,
                    (int)NppMsg.NPPM_DARKMODESUBCLASSANDTHEME,
                    new IntPtr(1),
                    _panel.Handle);

                _panel.BindRoot(_root, _stateStore, _stateStore.LastSelectedId);
                return;
            }

            if (_panel.Visible)
                _panel.HideDocked(_nppData._nppHandle);
            else
                _panel.ShowDocked(_nppData._nppHandle);
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

        internal static void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            IntPtr pathPtr = Marshal.StringToHGlobalUni(path);
            try
            {
                Win32.SendMessage(_nppData._nppHandle, (int)NppMsg.NPPM_DOOPEN, IntPtr.Zero, pathPtr);
            }
            finally { Marshal.FreeHGlobal(pathPtr); }
        }

        private static void OnFileClosed(IntPtr bufferId)
        {
            CrashLog.Write("OnFileClosed: entered, bufferId=" + bufferId.ToInt64().ToString("X"));
            if (_root == null || _stateStore == null)
            {
                CrashLog.Write("OnFileClosed: bailout - _root or _stateStore null");
                return;
            }

            var path = GetPathForBufferId(bufferId);
            CrashLog.Write("OnFileClosed: GetPathForBufferId returned '" + (path ?? "<null>") + "'");
            if (string.IsNullOrEmpty(path))
            {
                CrashLog.Write("OnFileClosed: bailout - empty path (buffer likely already destroyed)");
                return;
            }

            string canonical;
            try { canonical = System.IO.Path.GetFullPath(path); }
            catch { canonical = path; }
            CrashLog.Write("OnFileClosed: canonical='" + canonical + "'");

            int removed = VirtualTabGroups.Core.TreeMutator.RemoveAllByPath(_root, canonical);
            CrashLog.Write("OnFileClosed: RemoveAllByPath removed " + removed + " node(s)");
            if (removed == 0) return;

            _panel?.RefreshFromModel();
            _stateStore.MarkDirty(_root, null);
            CrashLog.Write("OnFileClosed: panel refreshed and MarkDirty called");
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
