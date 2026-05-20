using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VirtualTabGroups.Plugin.UI
{
    /// <summary>
    /// Extracts per-extension shell icons via SHGetFileInfo, caches them in an ImageList
    /// for fast TreeView lookup.
    /// </summary>
    public sealed class IconCache : IDisposable
    {
        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_OPENICON = 0x000000002;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly Dictionary<string, int> _byExtension = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public ImageList Images { get; }
        public int FolderClosedIndex { get; private set; }
        public int FolderOpenIndex { get; private set; }
        public int FileGenericIndex { get; private set; }

        public IconCache()
        {
            Images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };

            FolderClosedIndex = AddIconForAttributes("folder", FILE_ATTRIBUTE_DIRECTORY, openFolder: false);
            FolderOpenIndex = AddIconForAttributes("folder", FILE_ATTRIBUTE_DIRECTORY, openFolder: true);
            FileGenericIndex = AddIconForExtension("");
        }

        public int GetIconIndexForFile(string path)
        {
            var ext = Path.GetExtension(path) ?? "";
            if (_byExtension.TryGetValue(ext, out int idx)) return idx;
            return AddIconForExtension(ext);
        }

        private int AddIconForExtension(string ext)
        {
            var fakePath = "dummy" + ext;
            var info = new SHFILEINFO();
            SHGetFileInfo(fakePath, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf(info),
                SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

            int newIndex = AddHIconToImageList(info.hIcon);
            _byExtension[ext] = newIndex;
            return newIndex;
        }

        private int AddIconForAttributes(string path, uint attributes, bool openFolder)
        {
            var info = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
            if (openFolder) flags |= SHGFI_OPENICON;
            SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf(info), flags);
            return AddHIconToImageList(info.hIcon);
        }

        private int AddHIconToImageList(IntPtr hIcon)
        {
            if (hIcon == IntPtr.Zero) return 0;
            using (var icon = (Icon)Icon.FromHandle(hIcon).Clone())
            {
                Images.Images.Add(icon);
                DestroyIcon(hIcon);
                return Images.Images.Count - 1;
            }
        }

        public void Dispose() => Images.Dispose();
    }
}
