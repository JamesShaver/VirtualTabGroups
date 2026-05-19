// Virtual Tab Groups — Notepad++ plugin entry-point exports.
//
// This file is the unmanaged C ABI surface Notepad++ calls into.
// Each export forwards to the managed VirtualTabGroups.Plugin.PluginMain class.
// Bodies are stubbed out in this commit; Phase 4 wires the real forwarding.

#include <windows.h>  // for LRESULT, UINT, WPARAM, LPARAM
#include <cstdint>

// Minimal forward declarations of the Notepad++ ABI types that appear
// in our export signatures. Real layouts live in the managed bridge;
// from C the types are opaque pointers.

struct NppData
{
    HWND nppHandle;
    HWND scintillaMainHandle;
    HWND scintillaSecondHandle;
};

struct FuncItem;          // Opaque from C ABI; the managed side fills the array.
struct SCNotification;    // Opaque from C ABI.

extern "C" __declspec(dllexport) bool isUnicode()
{
    // Notepad++ has been Unicode-only since v6.
    return true;
}

extern "C" __declspec(dllexport) void setInfo(NppData /*notepadPlusData*/)
{
    // Phase 4 forwards to VirtualTabGroups::Plugin::PluginMain::SetInfo.
}

extern "C" __declspec(dllexport) const wchar_t* getName()
{
    return L"Virtual Tab Groups";
}

extern "C" __declspec(dllexport) FuncItem* getFuncsArray(int* nbF)
{
    if (nbF) *nbF = 0;
    return nullptr;
}

extern "C" __declspec(dllexport) void beNotified(SCNotification* /*notifyCode*/)
{
    // Phase 4 forwards to VirtualTabGroups::Plugin::PluginMain::BeNotified.
}

extern "C" __declspec(dllexport) LRESULT messageProc(UINT /*msg*/, WPARAM /*wParam*/, LPARAM /*lParam*/)
{
    return 0;
}
