using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DuplicateFinder.Core;

/// <summary>Reads identity, link count and named data streams from the opened Windows file.</summary>
internal sealed partial class WindowsFileFacts : IFileFacts
{
    public unsafe FileStamp Read(FileStream file)
    {
        var handle = file.SafeFileHandle;
        // FILE_ID_INFO contains a volume serial and a 128-bit ID (including on ReFS).
        FileId id = default;
        if (!GetFileInformationByHandleEx(handle, 18, &id, (uint)sizeof(FileId)))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is not (1 or 50 or 87)) throw new Win32Exception(error);

            // Older filesystem providers may expose only the 64-bit file index.
            if (!GetFileInformationByHandle(handle, out var legacy)) throw new Win32Exception();
            Span<byte> bytes = stackalloc byte[16];
            bytes.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, legacy.IndexLow);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], legacy.IndexHigh);
            id = new FileId { Volume = legacy.Volume, Id = new Guid(bytes) };
        }

        // FILE_BASIC_INFO and FILE_STANDARD_INFO are read through the same handle.
        // ChangeTime also changes on metadata/stream edits that preserve LastWriteTime.
        BasicInfo basic = default;
        StandardInfo standard = default;
        if (!GetFileInformationByHandleEx(handle, 0, &basic, (uint)sizeof(BasicInfo))
            || !GetFileInformationByHandleEx(handle, 1, &standard, (uint)sizeof(StandardInfo)))
        {
            throw new Win32Exception();
        }

        if (id.Id == Guid.Empty || standard.Links == 0 || standard.DeletePending != 0
            || (basic.Attributes & ((uint)FileAttributes.ReparsePoint | (uint)FileAttributes.Directory)) != 0)
        {
            throw new IOException("An ordinary file with a stable identity is required.");
        }

        return new(new(id.Volume, id.Id), standard.EndOfFile, DateTime.FromFileTimeUtc(basic.Written),
            basic.Changed, standard.Links);
    }

    public unsafe IReadOnlyList<NamedStream> Streams(FileStream file)
    {
        // Filesystems without FILE_NAMED_STREAMS (e.g. FAT32) have an empty
        // manifest. An error querying the volume or the stream list is unknown.
        if (!GetVolumeInformationByHandleW(file.SafeFileHandle, null, 0, null, null, out var flags, null, 0))
        {
            throw new Win32Exception();
        }
        if ((flags & 0x40000) == 0) return [];

        for (var size = 4096; size <= 16 * 1024 * 1024; size *= 2)
        {
            var buffer = new byte[size];
            fixed (byte* data = buffer)
            {
                if (GetFileInformationByHandleEx(file.SafeFileHandle, 7, data, (uint)size))
                {
                    return ParseStreams(buffer);
                }

                var error = Marshal.GetLastPInvokeError();
                if (error == 38) return []; // ERROR_HANDLE_EOF: no stream entries.
                if (error is not (234 or 122 or 24)) throw new Win32Exception(error);
            }
        }

        throw new IOException("The stream list exceeds the supported buffer.");
    }

    internal static IReadOnlyList<NamedStream> ParseStreams(ReadOnlySpan<byte> buffer)
    {
        var streams = new List<NamedStream>();
        while (buffer.Length >= 24)
        {
            var next = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            var bytes = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
            var size = BinaryPrimitives.ReadInt64LittleEndian(buffer[8..]);
            if (size < 0 || bytes == 0 || bytes % 2 != 0 || bytes > buffer.Length - 24)
            {
                break;
            }

            var name = Encoding.Unicode.GetString(buffer.Slice(24, (int)bytes));
            if (name != "::$DATA")
            {
                // Only append a kernel-supplied single stream name to the verified path.
                if (!name.StartsWith(':') || !name.EndsWith(":$DATA", StringComparison.Ordinal)
                    || name.Length <= 7 || name.AsSpan(1, name.Length - 7).ContainsAny(":\\/\0"))
                {
                    throw new IOException("Unexpected file stream name.");
                }

                streams.Add(new(name, size));
            }

            if (next == 0) return streams.OrderBy(s => s.Suffix, StringComparer.Ordinal).ToArray();
            if (next < 24 + bytes || next > buffer.Length) break;
            buffer = buffer[(int)next..];
        }

        throw new IOException("Incomplete file stream information.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LegacyInfo
    {
        internal uint Attributes, CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out LegacyInfo information);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileId { internal ulong Volume; internal Guid Id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInfo { internal long Created, Accessed, Written, Changed; internal uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct StandardInfo
    {
        internal long Allocation, EndOfFile;
        internal uint Links;
        internal byte DeletePending, Directory;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, void* data, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetVolumeInformationByHandleW(SafeFileHandle file, char* label, uint labelSize,
        uint* serial, uint* componentLength, out uint flags, char* fs, uint fsSize);
}
