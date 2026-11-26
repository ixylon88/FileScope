using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using FileScope.Core.Models;

namespace FileScope.Core.Signatures;

public sealed partial class WindowsAuthenticodeInspector
{
    // WINTRUST_CATALOG_INFO names both the signed catalog and the actual open member file.
    // All input strings, the handle and SIP hash outlive VERIFY and CLOSE. No catalog is installed.
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CatalogTrustInfo
    {
        public uint Size, Version;
        public char* CatalogPath, MemberTag, MemberPath;
        public nint MemberHandle;
        public byte* CalculatedHash;
        public uint HashLength;
        public nint CatalogContext, Admin;
    }

    [SupportedOSPlatform("windows")]
    internal static unsafe SignatureResult VerifyMember(FileStream file, nint handle, string catalog, string tag,
        RevocationMode mode, nint admin, byte* hash, uint size)
    {
        var policy = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        fixed (char* path = catalog, member = tag, name = file.Name)
        {
            var info = new CatalogTrustInfo
            {
                Size = (uint)sizeof(CatalogTrustInfo),
                CatalogPath = path,
                MemberTag = member,
                MemberPath = name,
                MemberHandle = handle,
                CalculatedHash = hash,
                HashLength = size,
                Admin = admin
            };
            var data = new Native.WinTrustData
            {
                Size = (uint)sizeof(Native.WinTrustData),
                UiChoice = 2,
                RevocationChecks = 1,
                UnionChoice = 2,
                FileInfo = (nint)(&info),
                StateAction = 1,
                ProviderFlags = 0x80u | 0x2000u | (mode == RevocationMode.OfflineCache ? 0x1000u : 0u)
            };
            try
            {
                var status = Native.WinVerifyTrust(-1, ref policy, ref data);
                try
                {
                    var signer = ReadSigner(data.StateData, status == 0);
                    return new(MapStatus(status), mode, signer.Certificate, status);
                }
                catch (Exception ex) when (ex is CryptographicException or ArgumentException)
                { return new(MapStatus(status), mode, NativeStatusCode: status, Diagnostic: ex.Message); }
            }
            finally
            {
                CloseState(ref policy, ref data);
                GC.KeepAlive(file);
            }
        }
    }
}
