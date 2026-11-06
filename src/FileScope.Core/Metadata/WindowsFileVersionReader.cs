using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FileScope.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace FileScope.Core.Metadata;

// Windows owns PE/NE resource decoding and MUI lookup. No executable is loaded as code.
[SupportedOSPlatform("windows")]
internal static partial class WindowsFileVersionReader
{
    private static readonly Lazy<GetVersionByHandle?> ReadFunction = new(LoadReadFunction);
    internal static bool IsAvailable => ReadFunction.Value is not null;

    internal static bool TryRead(FileStream file, out FilePropertiesInfo fields, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        fields = new();
        if (ReadFunction.Value is not { } read) return false;
        var managedPosition = file.Position;
        // SafeFileHandle flushes buffering and synchronizes the native pointer with
        // FileStream.Position on every access. Save that synchronized native position.
        var handle = file.SafeFileHandle;
        if (!Native.SetFilePointerEx(handle, 0, out var nativePosition, 1))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr buffer = IntPtr.Zero;
        var restored = true;
        var restoreError = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Match FileVersionInfo's localized/neutral resource policy (flags 1 | 2).
            var success = read(3, handle, ref buffer, out var size);
            var error = Marshal.GetLastWin32Error();
            cancellationToken.ThrowIfCancellationRequested();
            if (success)
            {
                if (buffer == IntPtr.Zero || size == 0)
                    throw new InvalidDataException("Windows returned an empty version-information buffer.");
                fields = ReadFields(buffer, size);
            }
            // Missing resources are normal for DOS images and ordinary data files.
            else if (error is not (1812 or 1813 or 1814 or 1815 or 193))
                throw new Win32Exception(error);
        }
        finally
        {
            // A failed call can still allocate a buffer; release it in both cases.
            if (buffer != IntPtr.Zero) _ = Native.LocalFree(buffer);
            try { if (file.Position != managedPosition) file.Position = managedPosition; }
            finally
            {
                // NE reads move the native pointer without updating FileStream.Position.
                // Preserve a pending inspection/cancellation exception if restoration fails.
                restored = Native.SetFilePointerEx(handle, nativePosition, out _, 0);
                if (!restored) restoreError = Marshal.GetLastWin32Error();
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!restored) throw new Win32Exception(restoreError);
        return true;
    }

    private static FilePropertiesInfo ReadFields(IntPtr buffer, uint size)
    {
        uint translation = 0x040904e4;
        if (Native.VerQueryValue(buffer, "\\VarFileInfo\\Translation", out var languages, out var length)
            && languages != IntPtr.Zero && length >= 4)
        {
            ValidateRange(buffer, size, languages, 4);
            translation = ((uint)unchecked((ushort)Marshal.ReadInt16(languages)) << 16)
                | unchecked((ushort)Marshal.ReadInt16(languages, 2));
        }
        var language = LanguageName(translation >> 16);
        var result = ReadTable(translation);
        // Preserve the existing .NET language/codepage fallback order.
        foreach (var fallback in (ReadOnlySpan<uint>)[0x040904b0, 0x040904e4, 0x04090000])
        {
            if (!string.IsNullOrEmpty(result.FileVersion)) break;
            if (fallback != translation) result = ReadTable(fallback);
        }
        return result;

        FilePropertiesInfo ReadTable(uint id)
        {
            var prefix = "\\StringFileInfo\\" + id.ToString("X8", CultureInfo.InvariantCulture) + "\\";
            string Read(string field)
            {
                if (!Native.VerQueryValue(buffer, prefix + field, out var text, out var characters)
                    || text == IntPtr.Zero || characters == 0) return "";
                ValidateRange(buffer, size, text, (ulong)characters * 2);
                var value = Marshal.PtrToStringUni(text, checked((int)characters)) ?? "";
                var terminator = value.IndexOf('\0');
                return terminator < 0 ? value : value[..terminator];
            }
            return new()
            {
                Language = language, FileDescription = Read("FileDescription"), FileVersion = Read("FileVersion"),
                ProductName = Read("ProductName"), ProductVersion = Read("ProductVersion"), CompanyName = Read("CompanyName"),
                OriginalFilename = Read("OriginalFilename"), InternalName = Read("InternalName"),
                Copyright = Read("LegalCopyright"), Trademarks = Read("LegalTrademarks"), Comments = Read("Comments")
            };
        }
    }

    private static void ValidateRange(IntPtr buffer, uint size, IntPtr value, ulong bytes)
    {
        var offset = unchecked((ulong)value.ToInt64()) - unchecked((ulong)buffer.ToInt64());
        if (offset > size || bytes > size - offset)
            throw new InvalidDataException("Windows returned version data outside its buffer.");
    }

    private static unsafe string LanguageName(uint language)
    {
        const uint capacity = 256;
        var text = stackalloc char[(int)capacity];
        var count = Native.VerLanguageName(language, text, capacity);
        if (count >= capacity) throw new InvalidDataException("Windows returned an invalid language-name length.");
        return new string(text, 0, (int)count);
    }

    private static GetVersionByHandle? LoadReadFunction()
    {
        if (!NativeLibrary.TryLoad("version.dll", typeof(WindowsFileVersionReader).Assembly,
            DllImportSearchPath.System32, out var module)) return null;
        var retained = false;
        try
        {
            if (!NativeLibrary.TryGetExport(module, "GetFileVersionInfoByHandle", out var address)) return null;
            var read = Marshal.GetDelegateForFunctionPointer<GetVersionByHandle>(address);
            // Keep one system-library reference for the lifetime of the cached delegate.
            retained = true;
            return read;
        }
        finally { if (!retained) NativeLibrary.Free(module); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool GetVersionByHandle(uint flags, SafeFileHandle file, ref IntPtr data, out uint size);

    private static partial class Native
    {
        [LibraryImport("version.dll", EntryPoint = "VerQueryValueW", StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool VerQueryValue(IntPtr block, string subBlock, out IntPtr value, out uint length);

        [LibraryImport("kernel32.dll", EntryPoint = "VerLanguageNameW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static unsafe partial uint VerLanguageName(uint language, char* buffer, uint capacity);

        [LibraryImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial IntPtr LocalFree(IntPtr memory);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetFilePointerEx(SafeFileHandle file, long distance, out long position, uint method);
    }
}
