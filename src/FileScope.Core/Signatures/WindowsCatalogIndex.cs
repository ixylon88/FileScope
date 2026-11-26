using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using FileScope.Core.Models;

namespace FileScope.Core.Signatures;

public sealed record CatalogMatch(string Path, string Member, bool Registered, SignatureResult Trust);
public sealed record CatalogLookup(IReadOnlyList<CatalogMatch> Matches, IReadOnlyList<string> Issues, bool IndexComplete);

/// <summary>Read-only index of loose catalogs in the search roots plus the Windows catalog database.</summary>
/// <remarks>Member digests come from the subject interface package for the file type. A flat file checksum
/// is not a substitute. Index matches are candidates until WinVerifyTrust verifies the actual member.</remarks>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsCatalogIndex
{
    private sealed record Member(string Path, string Tag);
    private readonly Dictionary<(string Algorithm, string Digest), List<Member>> _loose = [];
    private readonly HashSet<string> _algorithms = ["SHA1", "SHA256"];
    private readonly bool _complete = true;

    public WindowsCatalogIndex(IEnumerable<string> catalogs, Action<string, string> report, CancellationToken token = default)
    {
        foreach (var path in catalogs)
        {
            token.ThrowIfCancellationRequested();
            try { ReadCatalog(path, token); }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _complete = false;
                report(path, ex.Message);
            }
        }
    }

    private void ReadCatalog(string path, CancellationToken token)
    {
        var catalog = CryptCATOpen(path, 4, 0, 0, 0); // CRYPTCAT_OPEN_EXISTING, never create/install.
        if (catalog is -1 or 0) throw new Win32Exception();

        try
        {
            nint previous = 0;
            var sawLookupAlias = false;
            var sawMemberDigest = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                previous = CryptCATEnumerateMember(catalog, previous);
                if (previous == 0)
                {
                    ThrowIfEnumerationFailed();
                    if (sawLookupAlias && !sawMemberDigest)
                    {
                        throw new InvalidDataException("Catalog contains lookup tags without member digests.");
                    }
                    return;
                }

                // Both the member and its SIP_INDIRECT_DATA are borrowed until CryptCATClose.
                var member = (MemberPrefix*)previous;
                var indirect = member->Indirect;
                var tag = Marshal.PtrToStringUni(member->Tag);
                // MakeCat V2 can also expose a SHA-1 lookup tag with no indirect
                // data. Only entries carrying a member digest establish membership.
                if (indirect == null && member->EncodedIndirect.Length == 0
                    && tag is { Length: 40 } && tag.All(char.IsAsciiHexDigit))
                {
                    sawLookupAlias = true;
                    continue;
                }
                if (indirect == null || tag == null) throw new InvalidDataException("Catalog member data unavailable.");
                var algorithm = Marshal.PtrToStringAnsi(indirect->Algorithm.Oid) switch
                {
                    "1.3.14.3.2.26" => "SHA1",
                    "2.16.840.1.101.3.4.2.1" => "SHA256",
                    "2.16.840.1.101.3.4.2.2" => "SHA384",
                    "2.16.840.1.101.3.4.2.3" => "SHA512",
                    _ => throw new InvalidDataException("Unsupported catalog member digest algorithm.")
                };
                if (indirect->Digest.Length is 0 or > 64 || indirect->Digest.Bytes == null)
                {
                    throw new InvalidDataException("Invalid catalog member digest.");
                }

                _algorithms.Add(algorithm);
                var digest = Convert.ToHexString(new ReadOnlySpan<byte>(indirect->Digest.Bytes, (int)indirect->Digest.Length));
                if (!_loose.TryGetValue((algorithm, digest), out var entries))
                {
                    _loose.Add((algorithm, digest), entries = []);
                }
                entries.Add(new(path, tag));
                sawMemberDigest = true;
            }
        }
        finally { _ = CryptCATClose(catalog); }
    }

    public CatalogLookup Inspect(FileStream file, RevocationMode mode, CancellationToken token = default)
    {
        var matches = new List<CatalogMatch>();
        var issues = new List<string>();
        var handle = file.SafeFileHandle;
        if (SetFilePointerEx(handle, 0, out var position, 1) == 0) throw new Win32Exception();
        try
        {
            foreach (var algorithm in _algorithms)
            {
                token.ThrowIfCancellationRequested();
                try { InspectAlgorithm(file, algorithm, mode, matches, token); }
                catch (Exception ex) when (ex is Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    issues.Add($"{algorithm}: {ex.Message}");
                }
            }
        }
        finally
        {
            // The SIP calls use the native handle and can move its file pointer.
            if (SetFilePointerEx(handle, position, out _, 0) == 0) issues.Add(new Win32Exception().Message);
        }

        return new(matches.AsReadOnly(), issues.AsReadOnly(), _complete);
    }

    private void InspectAlgorithm(FileStream file, string algorithm, RevocationMode mode,
        List<CatalogMatch> matches, CancellationToken token)
    {
        if (CryptCATAdminAcquireContext2(out var admin, 0, algorithm, 0, 0) == 0) throw new Win32Exception();

        try
        {
            var handle = file.SafeFileHandle;
            var descriptor = handle.DangerousGetHandle();
            if (SetFilePointerEx(handle, 0, out _, 0) == 0) throw new Win32Exception();
            byte* digest = stackalloc byte[64];
            uint size = algorithm switch
            {
                "SHA1" => 20,
                "SHA256" => 32,
                "SHA384" => 48,
                "SHA512" => 64,
                _ => throw new InvalidDataException("Unsupported catalog member hash.")
            };
            if (CryptCATAdminCalcHashFromFileHandle2(admin, descriptor, ref size, digest, 0) == 0)
            {
                throw new Win32Exception();
            }
            if (size is 0 or > 64) throw new InvalidDataException("Invalid Windows catalog hash length.");
            var hex = Convert.ToHexString(new ReadOnlySpan<byte>(digest, (int)size));

            if (_loose.TryGetValue((algorithm, hex), out var members))
            {
                foreach (var member in members) Verify(member.Path, member.Tag, false);
            }

            nint previous = 0;
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    // Windows consumes the previous context on the next call, including on exhaustion.
                    // The first call takes a null pointer. Later calls take the address of the previous handle.
                    previous = CryptCATAdminEnumCatalogFromHash(admin, digest, size, 0, previous == 0 ? null : &previous);
                    if (previous == 0) { ThrowIfEnumerationFailed(); break; }

                    var info = new CatalogInfo { Size = (uint)sizeof(CatalogInfo) };
                    if (CryptCATCatalogInfoFromContext(previous, &info, 0) == 0) throw new Win32Exception();
                    var path = new string(info.Path);
                    Verify(path, hex, true);
                }
            }
            finally
            {
                if (previous != 0) _ = CryptCATAdminReleaseCatalogContext(admin, previous, 0);
            }

            void Verify(string path, string tag, bool registered)
            {
                token.ThrowIfCancellationRequested();
                var index = matches.FindIndex(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(m.Member, tag, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    if (registered) matches[index] = matches[index] with { Registered = true };
                    return;
                }

                if (SetFilePointerEx(handle, 0, out _, 0) == 0) throw new Win32Exception();
                var trust = WindowsAuthenticodeInspector.VerifyMember(file, descriptor, path, tag, mode, admin, digest, size);
                matches.Add(new(path, tag, registered, trust));
            }
        }
        finally
        {
            _ = CryptCATAdminReleaseContext(admin, 0);
            GC.KeepAlive(file);
        }
    }

    private static void ThrowIfEnumerationFailed()
    {
        var error = Marshal.GetLastPInvokeError();
        if (error is not (0 or 259 or 1168 or unchecked((int)0x80092004))) throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public uint Length; public byte* Bytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Attribute { public nint Oid; public Blob Value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IndirectData { public Attribute Data; public Attribute Algorithm; public Blob Digest; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemberPrefix
    {
        public uint Size;
        public nint Tag, Name;
        public Guid Subject;
        public uint Flags;
        public IndirectData* Indirect;
        public uint CertVersion, Reserved;
        public nint ReservedHandle;
        public Blob EncodedIndirect;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CatalogInfo { public uint Size; public fixed char Path[260]; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetFilePointerEx(SafeFileHandle file, long distance, out long position, uint origin);

    [LibraryImport("wintrust.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CryptCATOpen(string path, uint flags, nint provider, uint version, uint encoding);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CryptCATClose(nint catalog);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CryptCATEnumerateMember(nint catalog, nint previous);

    [LibraryImport("wintrust.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CryptCATAdminAcquireContext2(out nint admin, nint subsystem, string algorithm, nint policy, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CryptCATAdminCalcHashFromFileHandle2(nint admin, nint file, ref uint size, byte* hash, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CryptCATAdminEnumCatalogFromHash(nint admin, byte* hash, uint size, uint flags, nint* previous);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CryptCATCatalogInfoFromContext(nint catalog, CatalogInfo* info, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CryptCATAdminReleaseCatalogContext(nint admin, nint catalog, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CryptCATAdminReleaseContext(nint admin, uint flags);
}
