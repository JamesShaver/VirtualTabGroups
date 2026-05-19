using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using VirtualTabGroups.Core;
using VirtualTabGroups.Plugin.Npp;

namespace VirtualTabGroups.Plugin
{
    /// <summary>
    /// Managed plugin orchestrator. Called by VirtualTabGroups.Loader's C++/CLI bridge
    /// from the unmanaged Notepad++ entry-point exports.
    /// </summary>
    public static class PluginMain
    {
        internal const string PluginName = "Virtual Tab Groups";
        private static NppData _nppData;
        private static StateStore _stateStore;
        private static FolderNode _root;

        // Menu item command IDs.
        private const int CmdId_ShowPanel = 0;
        private const int CmdId_About = 1;
        private const int FuncItemCount = 2;

        private static FuncItem[] _funcItems;
        private static IntPtr _funcItemsPtr = IntPtr.Zero;

        // Cached delegates so GC doesn't collect them between Notepad++ menu invocations.
        private static Action _showPanelDelegate;
        private static Action _aboutDelegate;

        // ---- Entry points called by the C++/CLI bridge ----

        public static bool IsUnicode() => true;

        public static void SetInfo(NppData nppData)
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

            var stateFilePath = Path.Combine(pluginConfigDir, "state.json");
            _stateStore = new StateStore(stateFilePath, observer: null);
            // Observer assignment lands in Phase 5.
        }

        public static string GetName() => PluginName;

        /// <summary>
        /// Builds the menu items lazily, marshals them into unmanaged memory, returns
        /// the pointer + count via out parameter. The pointer is held by the C++/CLI bridge
        /// in a static cache so Notepad++ can keep the pointer for the plugin's lifetime.
        /// </summary>
        public static IntPtr GetFuncsArray(out int count)
        {
            if (_funcItemsPtr == IntPtr.Zero) BuildFuncItems();
            count = FuncItemCount;
            return _funcItemsPtr;
        }

        public static void BeNotified(IntPtr notifyCodePtr)
        {
            // Phase 4 Task 15 wires the notification dispatch here.
            _ = notifyCodePtr;
        }

        public static IntPtr MessageProc(uint msg, IntPtr wParam, IntPtr lParam) => IntPtr.Zero;

        // ---- State accessors (used by future phases) ----

        internal static IntPtr NppHandle => _nppData._nppHandle;
        internal static NppData NppData => _nppData;
        internal static StateStore StateStore => _stateStore;
        internal static FolderNode Root
        {
            get => _root;
            set => _root = value;
        }

        // ---- Helpers ----

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
                _pShKey = new ShortcutKey(ctrl: true, alt: false, shift: true, key: (byte)'T'),
            };

            _funcItems[CmdId_About] = new FuncItem
            {
                _itemName = "About Virtual Tab Groups",
                _pFunc = Marshal.GetFunctionPointerForDelegate(_aboutDelegate),
                _cmdID = CmdId_About,
                _init2Check = false,
                _pShKey = new ShortcutKey(false, false, false, 0),
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
            // Phase 7 Task 22 wires the panel toggle.
        }

        private static void OnAbout()
        {
            // Phase 13 Task 41 wires the About dialog.
        }
    }
}
