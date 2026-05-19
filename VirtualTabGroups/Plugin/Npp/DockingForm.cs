using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VirtualTabGroups.Plugin.Npp
{
    public abstract class DockingForm : Form
    {
        protected bool _isDocked;

        public void RegisterAsDockedPanel(IntPtr nppHandle, string moduleName, string caption, int cmdId, NppTbMsg dockingFlags, IntPtr iconHandle)
        {
            if (_isDocked) return;

            var tbData = new tTbData
            {
                hClient = this.Handle,
                pszName = caption,
                dlgID = cmdId,
                uMask = dockingFlags,
                hIconTab = iconHandle,
                pszAddInfo = string.Empty,
                rcFloat = new RECT(),
                iPrevCont = -1,
                pszModuleName = moduleName,
            };

            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(tbData));
            try
            {
                Marshal.StructureToPtr(tbData, ptr, false);
                Win32.SendMessage(nppHandle, (int)NppMsg.NPPM_DMMREGASDCKDLG, IntPtr.Zero, ptr);
                _isDocked = true;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public void ShowDocked(IntPtr nppHandle)
        {
            Win32.SendMessage(nppHandle, (int)NppMsg.NPPM_DMMSHOW, IntPtr.Zero, this.Handle);
        }

        public void HideDocked(IntPtr nppHandle)
        {
            Win32.SendMessage(nppHandle, (int)NppMsg.NPPM_DMMHIDE, IntPtr.Zero, this.Handle);
        }
    }
}
