using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;

namespace FileScope.Core.Metadata;

public sealed class PeHeaderInspector : IPeHeaderInspector
{
    public PeHeaderResult Inspect(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanRead || !stream.CanSeek) return new(null, "PE inspection requires a readable, seekable stream.");
        var originalPosition = stream.Position;
        try
        {
            // Distinguish PE images from ordinary data, DOS/NE images and COFF
            // object files before using the framework's bounded header parser.
            if (stream.Length < 64) return new(null);
            stream.Position = 0;
            Span<byte> dosHeader = stackalloc byte[64];
            stream.ReadExactly(dosHeader);
            if (dosHeader[0] != 'M' || dosHeader[1] != 'Z') return new(null);
            var peOffset = BinaryPrimitives.ReadUInt32LittleEndian(dosHeader[60..]);
            if (peOffset < 64 || peOffset > stream.Length - 4) return new(null);
            stream.Position = peOffset;
            Span<byte> signature = stackalloc byte[4];
            stream.ReadExactly(signature);
            if (!signature.SequenceEqual("PE\0\0"u8)) return new(null);
            cancellationToken.ThrowIfCancellationRequested();
            stream.Position = 0;
            // Header-only, lazy reading; do not prefetch or load/execute an assembly.
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            var headers = reader.PEHeaders;
            if (headers.PEHeader is not { } pe) return new(null);
            var machine = unchecked((ushort)headers.CoffHeader.Machine);
            var platform = GetManagedPlatform(headers);
            cancellationToken.ThrowIfCancellationRequested();
            return new(new PeHeaderInfo(machine, MachineName(machine),
                pe.Magic == PEMagic.PE32Plus ? "PE32+" : "PE32", platform));
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or ArgumentException or NotSupportedException)
        {
            return new(null, ex.Message);
        }
        finally { stream.Position = originalPosition; }
    }

    private static ManagedPlatform GetManagedPlatform(PEHeaders headers)
    {
        var cor = headers.CorHeader;
        if (cor is null) return ManagedPlatform.None;
        if (headers.CoffHeader.Machine != Machine.I386 || headers.PEHeader?.Magic != PEMagic.PE32
            || !cor.Flags.HasFlag(CorFlags.ILOnly) || cor.Flags.HasFlag(CorFlags.NativeEntryPoint)
            || cor.ManagedNativeHeaderDirectory.Size != 0 || cor.ManagedNativeHeaderDirectory.RelativeVirtualAddress != 0)
        {
            return ManagedPlatform.MachineSpecific;
        }
        // Prefer-32-bit may coexist with Requires32Bit for older CLR versions.
        if (cor.Flags.HasFlag(CorFlags.Prefers32Bit))
        {
            return headers.CoffHeader.Characteristics.HasFlag(Characteristics.Dll)
                ? ManagedPlatform.MachineSpecific : ManagedPlatform.Prefer32Bit;
        }

        return cor.Flags.HasFlag(CorFlags.Requires32Bit) ? ManagedPlatform.MachineSpecific : ManagedPlatform.AnyCpu;
    }

    // Preserve unrecognised numeric machine values rather than guessing an architecture.
    internal static string MachineName(ushort code) => code switch
    {
        0x0000 => "UNKNOWN",
        0x014c => "i386",
        0x0160 => "R3000 (BE)",
        0x0162 => "R3000",
        0x0166 => "R4000",
        0x0168 => "R10000",
        0x0169 => "MIPS WCE v2",
        0x0184 => "Alpha",
        0x01a2 => "SH3",
        0x01a3 => "SH3 DSP",
        0x01a6 => "SH4",
        0x01a8 => "SH5",
        0x01c0 => "ARM",
        0x01c2 => "Thumb",
        0x01c4 => "ARMNT (Thumb-2)",
        0x01d3 => "AM33",
        0x01f0 => "PowerPC",
        0x01f1 => "PowerPC FP",
        0x0200 => "IA64",
        0x0266 => "MIPS16",
        0x0284 => "Alpha64",
        0x0366 => "MIPS FPU",
        0x0466 => "MIPS16 FPU",
        0x0ebc => "EBC",
        0x5032 => "RISC-V 32",
        0x5064 => "RISC-V 64",
        0x5128 => "RISC-V 128",
        0x6232 => "LoongArch32",
        0x6264 => "LoongArch64",
        0x8664 => "AMD64 (x64)",
        0x9041 => "M32R",
        0xa641 => "ARM64EC",
        0xa64e => "ARM64X",
        0xaa64 => "ARM64",
        _ => $"0x{code:X4}"
    };
}
