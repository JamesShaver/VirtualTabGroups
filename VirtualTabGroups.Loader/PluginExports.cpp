// Virtual Tab Groups — Notepad++ plugin entry-point exports.
//
// This file is the unmanaged C ABI surface Notepad++ calls into.
// Each export forwards to the managed VirtualTabGroups.Plugin.PluginMain class.
// Bodies are stubbed out in this commit; Phase 4 wires the real forwarding.

#include <cstdint>
#include <cstring>

// Minimal struct definitions matching Notepad++'s C ABI for the entry points
// that take parameters. Real marshaling happens via VirtualTabGroups.Managed.

struct NppData
{
    void* nppHandle;
    void* scintillaMainHandle;
    void* scintillaSecondHandle;
};

extern "C" __declspec(dllexport) bool __cdecl isUnicode()
{
    // Notepad++ has been Unicode-only since v6.
    return true;
}

extern "C" __declspec(dllexport) void __cdecl setInfo(NppData /*notepadPlusData*/)
{
    // Phase 4 forwards to VirtualTabGroups::Plugin::PluginMain::SetInfo.
}

extern "C" __declspec(dllexport) const wchar_t* __cdecl getName()
{
    return L"Virtual Tab Groups";
}

extern "C" __declspec(dllexport) void* __cdecl getFuncsArray(int* nbF)
{
    if (nbF) *nbF = 0;
    return nullptr;
}

extern "C" __declspec(dllexport) void __cdecl beNotified(void* /*notifyCodePtr*/)
{
    // Phase 4 forwards.
}

extern "C" __declspec(dllexport) void* __cdecl messageProc(unsigned int /*msg*/, void* /*wParam*/, void* /*lParam*/)
{
    return nullptr;
}
