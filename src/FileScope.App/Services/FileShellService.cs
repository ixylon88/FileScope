using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileScope.App.Services;

/// <summary>Opens Windows properties or selects a file in Explorer; never invokes its default verb.</summary>
public sealed partial class FileShellService : IFileShellService
{
    public bool TryShowProperties(string fullPath)
    {
        // Property sheets belong to the UI thread and to the main window.
        Application.Current.Dispatcher.VerifyAccess();
        if (!IsExistingFile(fullPath)) return false;
        var owner = Application.Current.MainWindow;
        var handle = owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
        return Native.SHObjectProperties(handle, 0x2, fullPath, null); // SHOP_FILEPATH
    }

    public Task<bool> TryShowInExplorerAsync(string fullPath) => Task.Run(() =>
    {
        // Parsing shell paths (especially network paths) can block. Keep both
        // parsing and selection off the dispatcher, on one initialized COM thread.
        if (!IsExistingFile(fullPath)) return false;
        var initialized = Native.CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED
        if (initialized < 0) return false;
        var item = IntPtr.Zero;
        try
        {
            if (Native.SHParseDisplayName(fullPath, IntPtr.Zero, out item, 0, IntPtr.Zero) < 0
                || item == IntPtr.Zero)
            {
                return false;
            }
            // With cidl == 0 the PIDL identifies the FILE, not its folder.
            // Windows opens its parent and selects this exact item. No command
            // line quoting, /select parsing or file-association execution is involved.
            return Native.SHOpenFolderAndSelectItems(item, 0, IntPtr.Zero, 0) >= 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(item); // SHParseDisplayName transfers ownership.
            Native.CoUninitialize(); // Balance S_OK and S_FALSE on this same thread.
        }
    });

    private static bool IsExistingFile(string fullPath) =>
        !string.IsNullOrWhiteSpace(fullPath) && Path.IsPathFullyQualified(fullPath) && File.Exists(fullPath);

    private static partial class Native
    {
        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SHObjectProperties(IntPtr owner, uint objectType, string name, string? page);

        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int SHParseDisplayName(string name, IntPtr bindContext,
            out IntPtr item, uint requestedAttributes, IntPtr attributes);

        [LibraryImport("shell32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int SHOpenFolderAndSelectItems(IntPtr item, uint count, IntPtr children, uint flags);

        [LibraryImport("ole32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int CoInitializeEx(IntPtr reserved, uint apartment);

        [LibraryImport("ole32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial void CoUninitialize();
    }
}
