using System.Buffers.Binary;
using FileScope.Core.Models;

namespace FileScope.Core.Metadata;

// The NE layout is separate from PE/COFF: it has no COFF Machine field or CLR header.
internal static class NeHeaderReader
{
    public static PeHeaderInfo Read(Stream stream, long offset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Original OS/2 headers end at 0x3c; Windows adds two more WORDs.
        if (stream.Length - offset < 60) throw new BadImageFormatException("Truncated NE header.");
        stream.Position = offset;
        Span<byte> header = stackalloc byte[60];
        stream.ReadExactly(header);
        var target = header[0x36];
        var headerSize = target is 2 or 4 ? 64 : 60;
        if (stream.Length - offset < headerSize) throw new BadImageFormatException("Truncated Windows NE header.");
        var entryOffset = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        var entryLength = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
        var segmentCount = BinaryPrimitives.ReadUInt16LittleEndian(header[0x1c..]);
        var segmentOffset = BinaryPrimitives.ReadUInt16LittleEndian(header[0x22..]);
        ValidateTable(entryOffset, entryLength);
        ValidateTable(segmentOffset, segmentCount * 8L);
        cancellationToken.ThrowIfCancellationRequested();
        var format = target switch
        {
            1 => "NE (OS/2)",
            2 => "NE (Win16)",
            3 => "NE (DOS 4.x)",
            4 => "NE (Windows/386)",
            _ => $"NE (target 0x{target:X2})"
        };
        // Windows/386 and unknown targets do not establish a 16-bit code model.
        var machine = target is 1 or 2 or 3 ? "x86 (16-bit)" : "x86";
        return new(null, machine, format, ManagedPlatform.None);

        void ValidateTable(int relativeOffset, long length)
        {
            if (length != 0 && (relativeOffset < headerSize || offset + relativeOffset > stream.Length - length))
                throw new BadImageFormatException("NE header table is outside the file or overlaps the header.");
        }
    }
}
