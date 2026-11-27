using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;

namespace FileScope.Core.Metadata;

/// <summary>Combines the extension's registered type with VERSIONINFO from the borrowed file.</summary>
/// <remarks>
/// Shell type lookup and resource decoding are independent: a type name does not validate file content.
/// Expected metadata errors preserve the type information and return a diagnostic; cancellation propagates.
/// </remarks>
public sealed partial class FilePropertiesInspector : IFilePropertiesInspector
{
    public FilePropertiesResult Inspect(FileStream file, string fullPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FilePropertiesInfo();
        try
        {
            var extension = Path.GetExtension(fullPath);
            info = info with { FileType = OperatingSystem.IsWindows() ? ReadWindowsType(extension) : extension };
            if (OperatingSystem.IsWindows() && WindowsFileVersionReader.TryRead(file, out var fields, cancellationToken))
                return new(fields with { FileType = info.FileType });
            // Compatibility fallback for other platforms or a missing Windows export.
            // Actual handle-API errors do not trigger a second, path-based attempt.
            var version = FileVersionInfo.GetVersionInfo(fullPath);
            cancellationToken.ThrowIfCancellationRequested();
            return new(info with
            {
                FileDescription = version.FileDescription,
                Language = version.Language,
                FileVersion = version.FileVersion,
                ProductName = version.ProductName,
                ProductVersion = version.ProductVersion,
                CompanyName = version.CompanyName,
                OriginalFilename = version.OriginalFilename,
                InternalName = version.InternalName,
                Copyright = version.LegalCopyright,
                Trademarks = version.LegalTrademarks,
                Comments = version.Comments
            });
        }
        catch (BadImageFormatException) { return new(info); } // No version resource in this format.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or Win32Exception or ArgumentException or NotSupportedException)
        {
            return new(info, ex.Message);
        }
    }

    /// <summary>Queries the Shell association for an extension without opening the target file.</summary>
    /// <remarks>COM initialization and cleanup must both occur on this calling thread.</remarks>
    /// <seealso href="https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow"/>
    [SupportedOSPlatform("windows")]
    private static string ReadWindowsType(string extension)
    {
        if (extension.Length >= 260) return extension;
        // SHGetFileInfo requires COM on its calling thread. Balance successful
        // initialization, including S_FALSE; an existing STA is already usable.
        var initialized = Native.CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED
        if (initialized is < 0 and not unchecked((int)0x80010106)) return extension; // RPC_E_CHANGED_MODE
        // Query the extension's registered type only. No target-file access,
        // icon extraction, shell preview handler, or target program execution.
        try
        {
            // FILE_ATTRIBUTE_NORMAL (0x80), SHGFI_TYPENAME (0x400), SHGFI_USEFILEATTRIBUTES (0x10).
            // These flags request no icon handle that the caller would have to destroy.
            var result = Native.SHGetFileInfoW(extension.Length == 0 ? "file" : extension,
                0x80, out var info, (uint)Marshal.SizeOf<Native.ShellFileInfo>(), 0x400 | 0x10);
            return result != IntPtr.Zero && !string.IsNullOrWhiteSpace(info.TypeName) ? info.TypeName : extension;
        }
        // S_OK and S_FALSE both acquire a COM initialization reference; RPC_E_CHANGED_MODE does not.
        finally { if (initialized >= 0) Native.CoUninitialize(); }
    }

    [SupportedOSPlatform("windows")]
    private static partial class Native
    {
        [LibraryImport("ole32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int CoInitializeEx(IntPtr reserved, uint apartment);

        [LibraryImport("ole32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial void CoUninitialize();

        // SHFILEINFOW contains inline WCHAR arrays, not pointers to separately allocated strings.
        // Preserve their capacities and platform-default alignment when calculating cbFileInfo.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ShellFileInfo
        {
            public IntPtr Icon;
            public int IconIndex;
            public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
        }

        // The type 'FileScope.Core.Metadata.FilePropertiesInspector.Native.ShellFileInfo' is not supported by source-generated P/Invokes. The generated source will not handle marshalling of parameter 'info'. Hence we keep DllImport.
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr SHGetFileInfoW(string path, uint attributes,
            out ShellFileInfo info, uint size, uint flags);
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
    }
}
