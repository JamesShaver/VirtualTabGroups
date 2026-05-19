using System;
using System.IO;
using System.Text;

namespace VirtualTabGroups.Plugin
{
    /// <summary>
    /// Best-effort exception logger writing to %appdata%\Notepad++\plugins\config\VirtualTabGroups\plugin.log.
    /// Every Write call is wrapped in its own try/catch so logging failures never throw.
    /// </summary>
    internal static class CrashLog
    {
        private static string _logPath;

        public static void Initialize(string pluginConfigDir)
        {
            try
            {
                Directory.CreateDirectory(pluginConfigDir);
                _logPath = Path.Combine(pluginConfigDir, "plugin.log");
                Write("=== Plugin session started at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            }
            catch { _logPath = null; }
        }

        public static void Write(string line)
        {
            if (_logPath == null) return;
            try
            {
                File.AppendAllText(_logPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + line + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* swallow */ }
        }

        public static void WriteException(string source, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("EXCEPTION in " + source + ":");
                sb.AppendLine("  Type: " + ex.GetType().FullName);
                sb.AppendLine("  Message: " + ex.Message);
                sb.AppendLine("  Stack:");
                foreach (var line in (ex.StackTrace ?? string.Empty).Split('\n'))
                    sb.AppendLine("    " + line.TrimEnd('\r'));
                var inner = ex.InnerException;
                while (inner != null)
                {
                    sb.AppendLine("  Caused by " + inner.GetType().FullName + ": " + inner.Message);
                    foreach (var line in (inner.StackTrace ?? string.Empty).Split('\n'))
                        sb.AppendLine("    " + line.TrimEnd('\r'));
                    inner = inner.InnerException;
                }
                Write(sb.ToString());
            }
            catch { /* swallow */ }
        }
    }
}
