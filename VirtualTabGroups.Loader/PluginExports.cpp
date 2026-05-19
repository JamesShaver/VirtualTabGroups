// Virtual Tab Groups — Notepad++ plugin entry-point exports.
//
// C++/CLI bridge: each export marshals arguments and forwards to the
// VirtualTabGroups.Plugin.PluginMain static class in the managed assembly.
//
// Assembly resolution: the CLR's default probing path is the host EXE's directory
// (Notepad++.exe's directory), NOT this DLL's directory. We install an
// AppDomain.AssemblyResolve handler at first export entry so the runtime can find
// VirtualTabGroups.Managed.dll (and Newtonsoft.Json.dll) in our plugin folder.
//
// JIT-laziness guarantee: InstallAssemblyResolverOnce() only references mscorlib
// types (AppDomain, ResolveEventHandler). The ManagedBridge ref class methods that
// reference VirtualTabGroups.Managed.dll are in separate methods; the JIT compiles
// each method body lazily on first call. By the time ManagedBridge::* is JIT-
// compiled and invoked, the AssemblyResolve handler is already in place.

#include <windows.h>
#include <cstdint>

// VirtualTabGroups.Managed.dll is already force-included via /FU by the
// ProjectReference in the vcxproj — no explicit #using directive needed here.

using namespace System;
using namespace System::Reflection;
using namespace System::IO;
using namespace System::Runtime::InteropServices;

// ---- Assembly resolution helper ----
//
// GetModuleHandleExW with GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS is used to
// locate *this* DLL's path at runtime, independent of the working directory.

static Assembly^ ResolveFromPluginDirectory(Object^ /*sender*/, ResolveEventArgs^ args)
{
    wchar_t modulePath[MAX_PATH] = { 0 };
    HMODULE hSelf = nullptr;
    GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&ResolveFromPluginDirectory),
        &hSelf);
    if (hSelf == nullptr) return nullptr;

    GetModuleFileNameW(hSelf, modulePath, MAX_PATH);
    String^ moduleDir = Path::GetDirectoryName(gcnew String(modulePath));

    AssemblyName^ requested = gcnew AssemblyName(args->Name);
    String^ candidate = Path::Combine(moduleDir, requested->Name + ".dll");

    if (File::Exists(candidate))
    {
        try { return Assembly::LoadFrom(candidate); }
        catch (Exception^) { return nullptr; }
    }
    return nullptr;
}

static bool s_resolverInstalled = false;

static void InstallAssemblyResolverOnce()
{
    if (s_resolverInstalled) return;
    AppDomain::CurrentDomain->AssemblyResolve += gcnew ResolveEventHandler(&ResolveFromPluginDirectory);
    s_resolverInstalled = true;
}

// ---- Managed bridge ----
//
// All references to VirtualTabGroups.Managed.dll are confined to this ref class.
// Keeping them here (separate methods) ensures the JIT doesn't attempt to resolve
// the managed assembly until these methods are individually called — after the
// AssemblyResolve handler is already installed.

ref class ManagedBridge abstract sealed
{
public:
    static bool IsUnicode()
    {
        return VirtualTabGroups::Plugin::PluginMain::IsUnicode();
    }

    static void SetInfo(IntPtr nppHandle, IntPtr scintillaMain, IntPtr scintillaSecond)
    {
        VirtualTabGroups::Plugin::Npp::NppData managed;
        managed._nppHandle = nppHandle;
        managed._scintillaMainHandle = scintillaMain;
        managed._scintillaSecondHandle = scintillaSecond;
        VirtualTabGroups::Plugin::PluginMain::SetInfo(managed);
    }

    static String^ GetName()
    {
        return VirtualTabGroups::Plugin::PluginMain::GetName();
    }

    static IntPtr GetFuncsArray([Runtime::InteropServices::Out] int% count)
    {
        int c;
        IntPtr p = VirtualTabGroups::Plugin::PluginMain::GetFuncsArray(c);
        count = c;
        return p;
    }

    static void BeNotified(IntPtr notifyCodePtr)
    {
        VirtualTabGroups::Plugin::PluginMain::BeNotified(notifyCodePtr);
    }

    static IntPtr MessageProc(unsigned int msg, IntPtr wParam, IntPtr lParam)
    {
        return VirtualTabGroups::Plugin::PluginMain::MessageProc(msg, wParam, lParam);
    }
};

// ---- Native ABI structs ----

struct NppData
{
    HWND nppHandle;
    HWND scintillaMainHandle;
    HWND scintillaSecondHandle;
};

struct FuncItem;          // Opaque from C ABI.
struct SCNotification;    // Opaque from C ABI.

// Cached pointer for the plugin-name string. Notepad++ keeps this pointer for
// the plugin's lifetime; we marshal once and never free.
static const wchar_t* g_cachedNamePtr = nullptr;

// ---- Exported entry points ----
//
// isUnicode() now returns BOOL (Win32 4-byte int) instead of C++ bool (1 byte).
// Notepad++ checks the return value as a BOOL; returning bool risked reading
// garbage in the high 3 bytes on some calling conventions.

extern "C" __declspec(dllexport) BOOL isUnicode()
{
    InstallAssemblyResolverOnce();
    return ManagedBridge::IsUnicode() ? TRUE : FALSE;
}

extern "C" __declspec(dllexport) void setInfo(NppData notepadPlusData)
{
    InstallAssemblyResolverOnce();
    ManagedBridge::SetInfo(
        IntPtr(notepadPlusData.nppHandle),
        IntPtr(notepadPlusData.scintillaMainHandle),
        IntPtr(notepadPlusData.scintillaSecondHandle));
}

extern "C" __declspec(dllexport) const wchar_t* getName()
{
    InstallAssemblyResolverOnce();
    if (g_cachedNamePtr == nullptr)
    {
        String^ name = ManagedBridge::GetName();
        IntPtr ptr = Marshal::StringToHGlobalUni(name);
        g_cachedNamePtr = static_cast<const wchar_t*>(ptr.ToPointer());
    }
    return g_cachedNamePtr;
}

extern "C" __declspec(dllexport) FuncItem* getFuncsArray(int* nbF)
{
    InstallAssemblyResolverOnce();
    int count = 0;
    IntPtr ptr = ManagedBridge::GetFuncsArray(count);
    if (nbF) *nbF = count;
    return static_cast<FuncItem*>(ptr.ToPointer());
}

extern "C" __declspec(dllexport) void beNotified(SCNotification* notifyCode)
{
    InstallAssemblyResolverOnce();
    ManagedBridge::BeNotified(IntPtr(notifyCode));
}

extern "C" __declspec(dllexport) LRESULT messageProc(UINT msg, WPARAM wParam, LPARAM lParam)
{
    InstallAssemblyResolverOnce();
    IntPtr result = ManagedBridge::MessageProc(msg, IntPtr((void*)wParam), IntPtr((void*)lParam));
    return static_cast<LRESULT>(result.ToInt64());
}
