namespace VirtualTabGroups.Plugin.Npp
{
    /// <summary>
    /// Scintilla notification codes relayed by Notepad++ to plugins through beNotified.
    /// Values from Scintilla.h (scintilla.org). For these notifications Notepad++
    /// sets nmhdr.idFrom to the affected document's buffer ID.
    /// </summary>
    public enum ScintillaNotif : uint
    {
        /// <summary>Document returned to its saved state (save point reached).</summary>
        SCN_SAVEPOINTREACHED = 2002,

        /// <summary>Document left its saved state (first modification since save).</summary>
        SCN_SAVEPOINTLEFT = 2003,

        /// <summary>Document content was modified.</summary>
        SCN_MODIFIED = 2008,
    }
}
