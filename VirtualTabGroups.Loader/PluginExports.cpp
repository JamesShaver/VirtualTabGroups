// Virtual Tab Groups — Notepad++ plugin entry-point exports.
//
// C++/CLI bridge: each export marshals arguments and forwards to the
// VirtualTabGroups.Plugin.PluginMain static class in the managed assembly.

#include <windows.h>
#include <cstdint>

// VirtualTabGroups.Managed.dll is already force-included via /FU by the
// ProjectReference in the vcxproj — no explicit #using directive needed here.

using namespace System;
using namespace System::Runtime::InteropServices;

// Native ABI structs Notepad++ uses. The managed side has parallel definitions
// in VirtualTabGroups.Plugin.Npp.NppData with [StructLayout(Sequential)] and
// IntPtr fields — same layout as this struct on Windows, so we can blittably
// pass it across the bridge.

struct NppData
{
    HWND nppHandle;
    HWND scintillaMainHandle;
    HWND scintillaSecondHandle;
};

struct FuncItem;          // Opaque from C ABI.
struct SCNotification;    // Opaque from C ABI.

// Cached pointer for the plugin-name string the bridge hands to Notepad++.
// Notepad++ keeps this pointer for the plugin's lifetime, so we marshal once.
static const wchar_t* g_cachedNamePtr = nullptr;

extern "C" __declspec(dllexport) bool isUnicode()
{
    return VirtualTabGroups::Plugin::PluginMain::IsUnicode();
}

extern "C" __declspec(dllexport) void setInfo(NppData notepadPlusData)
{
    VirtualTabGroups::Plugin::Npp::NppData managed;
    managed._nppHandle = IntPtr(notepadPlusData.nppHandle);
    managed._scintillaMainHandle = IntPtr(notepadPlusData.scintillaMainHandle);
    managed._scintillaSecondHandle = IntPtr(notepadPlusData.scintillaSecondHandle);
    VirtualTabGroups::Plugin::PluginMain::SetInfo(managed);
}

extern "C" __declspec(dllexport) const wchar_t* getName()
{
    if (g_cachedNamePtr == nullptr)
    {
        String^ name = VirtualTabGroups::Plugin::PluginMain::GetName();
        IntPtr ptr = Marshal::StringToHGlobalUni(name);
        g_cachedNamePtr = static_cast<const wchar_t*>(ptr.ToPointer());
    }
    return g_cachedNamePtr;
}

extern "C" __declspec(dllexport) FuncItem* getFuncsArray(int* nbF)
{
    int count = 0;
    IntPtr ptr = VirtualTabGroups::Plugin::PluginMain::GetFuncsArray(count);
    if (nbF) *nbF = count;
    return static_cast<FuncItem*>(ptr.ToPointer());
}

extern "C" __declspec(dllexport) void beNotified(SCNotification* notifyCode)
{
    VirtualTabGroups::Plugin::PluginMain::BeNotified(IntPtr(notifyCode));
}

extern "C" __declspec(dllexport) LRESULT messageProc(UINT msg, WPARAM wParam, LPARAM lParam)
{
    IntPtr result = VirtualTabGroups::Plugin::PluginMain::MessageProc(msg, IntPtr((void*)wParam), IntPtr((void*)lParam));
    return static_cast<LRESULT>(result.ToInt64());
}
