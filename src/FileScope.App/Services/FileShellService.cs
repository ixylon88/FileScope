using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileScope.App.Services;

/// <summary>Opens Windows properties or selects a file in Explorer; never invokes its default verb.</summary>
public sealed partial class FileShellService : IFileShellService
{
    /// <inheritdoc/>
    /// <remarks>
    /// The WPF dispatcher owns this UI operation. The main window's HWND is
    /// borrowed for dialog ownership; this service neither creates nor closes it.
    /// Windows chooses the default property page and may read the target file.
    /// </remarks>
    public bool TryShowProperties(string fullPath)
    {
        // Property sheets belong to the UI thread and to the main window.
        Application.Current.Dispatcher.VerifyAccess();
        if (!IsExistingFile(fullPath)) return false;
        var owner = Application.Current.MainWindow;
        var handle = owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
        return Native.SHObjectProperties(handle, 0x2, fullPath, null); // SHOP_FILEPATH
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The delegate contains no await: COM initialization, path resolution,
    /// selection and cleanup all run on the same worker thread. A negative
    /// HRESULT becomes false; these imports do not use a GetLastError contract.
    /// Shell requests are separate from the analysis stream and may access files.
    /// </remarks>
    public Task<bool> TryShowInExplorerAsync(string fullPath) => Task.Run(() =>
    {
        // Parsing shell paths (especially network paths) can block. Keep both
        // parsing and selection off the dispatcher, on one initialized COM thread.
        if (!IsExistingFile(fullPath)) return false;
        var initialized = Native.CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED
        // Unlike the metadata type lookup, this worker requires successful MTA
        // initialization. RPC_E_CHANGED_MODE also aborts without CoUninitialize.
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
            // The PIDL belongs to the caller and uses the COM task allocator.
            // Free it after use, including later Shell failures; zero is allowed.
            Marshal.FreeCoTaskMem(item);
            Native.CoUninitialize(); // Balance S_OK and S_FALSE on this same thread.
        }
    });

    private static bool IsExistingFile(string fullPath) =>
        !string.IsNullOrWhiteSpace(fullPath) && Path.IsPathFullyQualified(fullPath) && File.Exists(fullPath);

    /// <summary>Windows Shell ABI declarations; only system DLLs are resolved.</summary>
    /// <remarks>BOOL/DWORD/HRESULT are 32-bit values; HWND and PIDL remain pointer-sized on x64 and ARM64.</remarks>
    private static partial class Native
    {
        /// <summary>Invokes a Shell object's Properties command.</summary>
        /// <param name="owner">Borrowed parent HWND, or zero for no owner.</param>
        /// <param name="objectType">SHOP_FILEPATH (0x2) makes name a fully qualified file path.</param>
        /// <param name="name">UTF-16 object name; FileScope supplies the file path.</param>
        /// <param name="page">Initial property-page name; null selects the default page.</param>
        /// <returns>Native BOOL: nonzero means the command was invoked successfully.</returns>
        /// <remarks>The return is marshalled as a 32-bit Windows BOOL, not an HRESULT.</remarks>
        /// <seealso href="https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shobjectproperties"/>
        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SHObjectProperties(IntPtr owner, uint objectType, string name, string? page);

        /// <summary>Resolves a Shell display name to an absolute item identifier list (PIDL).</summary>
        /// <param name="name">UTF-16 name to resolve; FileScope uses a fully qualified file path.</param>
        /// <param name="bindContext">Optional IBindCtx pointer; zero uses the default parsing context.</param>
        /// <param name="item">Receives the PIDL; the caller must release it with Marshal.FreeCoTaskMem.</param>
        /// <param name="requestedAttributes">SFGAO attribute mask; zero requests no attributes.</param>
        /// <param name="attributes">Optional output pointer to a 32-bit SFGAO mask; zero when unused.</param>
        /// <returns>S_OK on success, otherwise an HRESULT error code.</returns>
        /// <remarks>Path resolution can block, so this service calls it on the background worker.</remarks>
        /// <seealso href="https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shparsedisplayname"/>
        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int SHParseDisplayName(string name, IntPtr bindContext,
            out IntPtr item, uint requestedAttributes, IntPtr attributes);

        /// <summary>Opens Explorer and selects the item identified by a borrowed PIDL.</summary>
        /// <param name="item">Absolute PIDL of the file itself when count is zero.</param>
        /// <param name="count">Zero selects item in its parent folder; otherwise the child-array length.</param>
        /// <param name="children">Optional child-PIDL array; zero for this service's single-file request.</param>
        /// <param name="flags">Zero requests ordinary selection, without rename or desktop-selection mode.</param>
        /// <returns>S_OK on success, otherwise an HRESULT error code.</returns>
        /// <remarks>COM must be initialized on this thread. PIDL ownership remains with the caller.</remarks>
        /// <seealso href="https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems"/>
        [LibraryImport("shell32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int SHOpenFolderAndSelectItems(IntPtr item, uint count, IntPtr children, uint flags);

        /// <summary>Initializes COM for the calling worker thread.</summary>
        /// <param name="reserved">Reserved by Windows; must be zero.</param>
        /// <param name="apartment">COINIT flags; this service passes zero for COINIT_MULTITHREADED (MTA).</param>
        /// <returns>S_OK for a new initialization, S_FALSE for an existing compatible one, or a failing HRESULT.</returns>
        /// <remarks>Both success codes need a matching CoUninitialize on this thread. Failed calls acquire no such obligation.</remarks>
        /// <seealso href="https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-coinitializeex"/>
        [LibraryImport("ole32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int CoInitializeEx(IntPtr reserved, uint apartment);

        /// <summary>Balances one successful COM initialization on the calling thread.</summary>
        /// <remarks>Called in finally after PIDL cleanup, including when parsing or selection failed; never after failed initialization.</remarks>
        /// <seealso href="https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-couninitialize"/>
        [LibraryImport("ole32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial void CoUninitialize();
    }
}
