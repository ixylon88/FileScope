using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;

namespace FileScope.Core.Metadata;

public sealed class FilePropertiesInspector : IFilePropertiesInspector
{
    public FilePropertiesResult Inspect(string fullPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FilePropertiesInfo();
        try
        {
            var extension = Path.GetExtension(fullPath);
            info = info with { FileType = OperatingSystem.IsWindows() ? ReadWindowsType(extension) : extension };
            // Reads VERSIONINFO resources, never loads the file as executable code.
            // The analysis service keeps its read lock open throughout this call.
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

    [SupportedOSPlatform("windows")]
    private static string ReadWindowsType(string extension)
    {
        if (extension.Length >= 260) return extension;
        // SHGetFileInfo requires COM on its calling thread. Balance successful
        // initialization, including S_FALSE; an existing STA is already usable.
        var initialized = Native.CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) return extension; // RPC_E_CHANGED_MODE
        // Query the extension's registered type only. No target-file access,
        // icon extraction, shell preview handler, or target program execution.
        try
        {
            var result = Native.SHGetFileInfoW(extension.Length == 0 ? "file" : extension,
                0x80, out var info, (uint)Marshal.SizeOf<Native.ShellFileInfo>(), 0x400 | 0x10);
            return result != IntPtr.Zero && !string.IsNullOrWhiteSpace(info.TypeName) ? info.TypeName : extension;
        }
        finally { if (initialized >= 0) Native.CoUninitialize(); }
    }

    [SupportedOSPlatform("windows")]
    private static class Native
    {
        [DllImport("ole32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int CoInitializeEx(IntPtr reserved, uint apartment);

        [DllImport("ole32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void CoUninitialize();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ShellFileInfo
        {
            public IntPtr Icon;
            public int IconIndex;
            public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr SHGetFileInfoW(string path, uint attributes,
            out ShellFileInfo info, uint size, uint flags);
    }
}
