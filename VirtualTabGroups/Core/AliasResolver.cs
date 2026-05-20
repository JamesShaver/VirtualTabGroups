using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace VirtualTabGroups.Core
{
    public static class AliasResolver
    {
        public static string Resolve(FolderNode folder, string incomingPath)
        {
            if (folder == null) throw new ArgumentNullException(nameof(folder));
            if (string.IsNullOrEmpty(incomingPath)) throw new ArgumentException("path required", nameof(incomingPath));

            string baseName = Path.GetFileName(incomingPath);
            string stem = Path.GetFileNameWithoutExtension(baseName);
            string ext = Path.GetExtension(baseName); // includes leading dot, or "" if none

            var takenAliases = new HashSet<int>();
            bool baseIsTaken = false;

            // Regex matches "{stem}({N}){ext}" or "{stem}{ext}".
            // We use Regex.Escape on stem/ext so dots in either are literal.
            var pattern = new Regex(
                "^" + Regex.Escape(stem) + @"(?:\((?<n>\d+)\))?" + Regex.Escape(ext) + "$",
                RegexOptions.CultureInvariant);

            foreach (var child in folder.Children)
            {
                if (!(child is FileNode file)) continue;
                var m = pattern.Match(file.Name);
                if (!m.Success) continue;

                if (m.Groups["n"].Success)
                {
                    if (int.TryParse(m.Groups["n"].Value, out int n)) takenAliases.Add(n);
                }
                else
                {
                    baseIsTaken = true;
                }
            }

            if (!baseIsTaken) return baseName;

            for (int n = 1; ; n++)
            {
                if (!takenAliases.Contains(n))
                {
                    return stem + "(" + n + ")" + ext;
                }
            }
        }
    }
}
