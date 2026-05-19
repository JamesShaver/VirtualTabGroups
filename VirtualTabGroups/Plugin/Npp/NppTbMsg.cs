using System;

namespace VirtualTabGroups.Plugin.Npp
{
    [Flags]
    public enum NppTbMsg : uint
    {
        DWS_ICONTAB = 0x00000001,
        DWS_ICONBAR = 0x00000002,
        DWS_ADDINFO = 0x00000004,
        DWS_PARAMSALL = DWS_ICONTAB | DWS_ICONBAR | DWS_ADDINFO,

        DWS_DF_CONT_LEFT = 0,
        DWS_DF_CONT_RIGHT = 1,
        DWS_DF_CONT_TOP = 2,
        DWS_DF_CONT_BOTTOM = 3,
        DWS_DF_FLOATING = 0x00000008,
    }
}
