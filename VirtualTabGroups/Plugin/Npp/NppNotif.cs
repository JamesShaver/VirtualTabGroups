namespace VirtualTabGroups.Plugin.Npp
{
    public enum NppNotif : uint
    {
        NPPN_FIRST = 1000,
        NPPN_READY = NPPN_FIRST + 1,
        NPPN_TBMODIFICATION = NPPN_FIRST + 2,
        NPPN_FILECLOSED = NPPN_FIRST + 3,
        NPPN_FILEOPENED = NPPN_FIRST + 4,
        NPPN_FILEBEFORECLOSE = NPPN_FIRST + 5,
        NPPN_FILEBEFOREOPEN = NPPN_FIRST + 6,
        NPPN_FILEBEFORESAVE = NPPN_FIRST + 7,
        NPPN_FILESAVED = NPPN_FIRST + 8,
        NPPN_SHUTDOWN = NPPN_FIRST + 9,
        NPPN_BUFFERACTIVATED = NPPN_FIRST + 10,
        NPPN_LANGCHANGED = NPPN_FIRST + 11,
        NPPN_WORDSTYLESUPDATED = NPPN_FIRST + 12,
        NPPN_SHORTCUTREMAPPED = NPPN_FIRST + 13,
        // Fires when Notepad++ has decided to shut down but BEFORE per-file
        // close notifications start. Use it to suppress per-file cleanup that
        // would otherwise strip every open document out of the virtual tree
        // (since each open buffer closes during the shutdown sequence).
        NPPN_BEFORESHUTDOWN = NPPN_FIRST + 19,
        // Fires if the user backs out of shutdown (e.g., they hit Cancel on the
        // "Save dirty files?" dialog). Used to clear the shutdown flag.
        NPPN_CANCELSHUTDOWN = NPPN_FIRST + 20,
        NPPN_DARKMODECHANGED = NPPN_FIRST + 27,
    }
}
