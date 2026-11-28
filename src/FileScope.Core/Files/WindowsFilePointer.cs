using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace FileScope.Core.Files;

/// <summary>Reads and sets the native position of a borrowed handle around Windows API calls.</summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsFilePointer
{
    internal static long GetPosition(SafeFileHandle handle)
    {
        if (!SetFilePointerEx(handle, 0, out var position, SeekOrigin.Current))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        return position;
    }

    internal static void Rewind(SafeFileHandle handle)
    {
        if (!TrySetPosition(handle, 0, out var error)) throw new Win32Exception(error);
    }

    // Capture the error here so callers can report a failed restore without masking a pending exception.
    internal static bool TrySetPosition(SafeFileHandle handle, long position, out int error)
    {
        var success = SetFilePointerEx(handle, position, out _, SeekOrigin.Begin);
        error = success ? 0 : Marshal.GetLastPInvokeError();
        return success;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFilePointerEx(SafeFileHandle handle, long distance, out long position, SeekOrigin origin);
}
