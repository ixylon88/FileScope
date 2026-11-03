using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;

namespace FileScope.Core.Signatures;

/// <summary>
/// Inspects the primary embedded Authenticode signature using Windows trust policy.
/// No catalog lookup, private-key import, certificate installation or file execution.
/// </summary>
public sealed class WindowsAuthenticodeInspector : ISignatureInspector
{
    public SignatureResult Inspect(FileStream file, string fullPath, RevocationMode revocationMode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return new SignatureResult(SignatureStatus.UnsupportedPlatform, revocationMode);
        try
        {
            var result = InspectWindows(file, fullPath, revocationMode);
            // WinVerifyTrust is synchronous and cannot be interrupted mid-call.
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (CryptographicException ex)
        {
            return new SignatureResult(SignatureStatus.CheckFailed, revocationMode, Diagnostic: ex.Message);
        }
        catch (ExternalException ex)
        {
            return new SignatureResult(SignatureStatus.CheckFailed, revocationMode, Diagnostic: ex.Message);
        }
    }

    [SupportedOSPlatform("windows")]
    private static SignatureResult InspectWindows(FileStream stream, string path, RevocationMode mode)
    {
        var fileInfo = new Native.WinTrustFileInfo
        {
            Size = (uint)Marshal.SizeOf<Native.WinTrustFileInfo>(),
            FilePath = path,
            FileHandle = stream.SafeFileHandle.DangerousGetHandle()
        };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<Native.WinTrustFileInfo>());
        var settingsPointer = IntPtr.Zero;
        var marshaled = false;
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var data = new Native.WinTrustData
        {
            Size = (uint)Marshal.SizeOf<Native.WinTrustData>(),
            UiChoice = 2,                  // WTD_UI_NONE
            RevocationChecks = 1,          // WTD_REVOKE_WHOLECHAIN
            UnionChoice = 1,               // WTD_CHOICE_FILE
            FileInfo = filePointer,
            StateAction = 1,               // WTD_STATEACTION_VERIFY
            ProviderFlags = 0x80u | 0x2000u // CHAIN_EXCLUDE_ROOT | DISABLE_MD2_MD4
                | (mode == RevocationMode.OfflineCache ? 0x1000u : 0u),
            UiContext = 0
        };
        try
        {
            Marshal.StructureToPtr(fileInfo, filePointer, false);
            marshaled = true;
            var settings = new Native.WinTrustSignatureSettings
            {
                Size = (uint)Marshal.SizeOf<Native.WinTrustSignatureSettings>(),
                Index = 0,
                Flags = 1 // WSS_VERIFY_SPECIFIC: explicitly choose the primary signature.
            };
            settingsPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Native.WinTrustSignatureSettings>());
            Marshal.StructureToPtr(settings, settingsPointer, false);
            data.SignatureSettings = settingsPointer;
            var status = Native.WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            CertificateInfo? certificate = null;
            string? diagnostic = null;
            try
            {
                // Read the signer associated with this trust operation, including on
                // an untrusted/expired certificate. Metadata is not proof of trust.
                certificate = ReadSigner(data.StateData);
            }
            catch (CryptographicException ex) { diagnostic = ex.Message; }
            catch (ArgumentException ex) { diagnostic = ex.Message; }
            return new SignatureResult(MapStatus(status), mode, certificate, status, diagnostic);
        }
        finally
        {
            try
            {
                if (data.StateData != IntPtr.Zero)
                {
                    data.StateAction = 2; // WTD_STATEACTION_CLOSE, including failure paths
                    _ = Native.WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                }
            }
            finally
            {
                if (marshaled) Marshal.DestroyStructure<Native.WinTrustFileInfo>(filePointer);
                Marshal.FreeHGlobal(filePointer);
                if (settingsPointer != IntPtr.Zero) Marshal.FreeHGlobal(settingsPointer);
                GC.KeepAlive(stream);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static CertificateInfo? ReadSigner(IntPtr state)
    {
        if (state == IntPtr.Zero) return null;
        var provider = Native.WTHelperProvDataFromStateData(state);
        if (provider == IntPtr.Zero) return null;
        var signer = Native.WTHelperGetProvSignerFromChain(provider, 0, false, 0);
        if (signer == IntPtr.Zero) return null;
        var providerCertificate = Native.WTHelperGetProvCertFromChain(signer, 0);
        if (providerCertificate == IntPtr.Zero) return null;
        var prefix = Marshal.PtrToStructure<Native.ProviderCertificatePrefix>(providerCertificate);
        if (prefix.Certificate == IntPtr.Zero) return null;
        var context = Marshal.PtrToStructure<Native.CertificateContext>(prefix.Certificate);
        if (context.Encoded == IntPtr.Zero || context.EncodedLength == 0 || context.EncodedLength > 16 * 1024 * 1024)
            return null;
        var der = new byte[checked((int)context.EncodedLength)];
        Marshal.Copy(context.Encoded, der, 0, der.Length);
        using var certificate = X509CertificateLoader.LoadCertificate(der);
        return new CertificateInfo(
            certificate.GetNameInfo(X509NameType.SimpleName, false), certificate.Subject,
            certificate.Issuer, certificate.SerialNumber, certificate.Thumbprint,
            certificate.GetCertHashString(HashAlgorithmName.SHA256),
            new DateTimeOffset(certificate.NotBefore.ToUniversalTime()),
            new DateTimeOffset(certificate.NotAfter.ToUniversalTime()),
            certificate.SignatureAlgorithm.FriendlyName ?? certificate.SignatureAlgorithm.Value ?? "");
    }

    private static SignatureStatus MapStatus(int status) => unchecked((uint)status) switch
    {
        0 => SignatureStatus.Valid,
        0x800B0100 => SignatureStatus.NoEmbeddedSignature, // TRUST_E_NOSIGNATURE
        0x80096010 or 0x80096004 => SignatureStatus.InvalidSignature,
        0x800B0101 => SignatureStatus.Expired,
        0x800B010C or 0x80092010 => SignatureStatus.Revoked,
        0x80092012 or 0x80092013 or 0x800B010E => SignatureStatus.RevocationUnknown,
        0x800B0109 or 0x800B010A or 0x800B0111 or 0x800B0004 or 0x80092026 => SignatureStatus.Untrusted,
        0x800B0001 or 0x800B0003 => SignatureStatus.UnsupportedFormat,
        _ => SignatureStatus.CheckFailed
    };

    // Default native packing works for x64, x86 and ARM64. Native DWORD/BOOL
    // fields are 32-bit; pointers are pointer-sized. Never apply Pack=1 here.
    [SupportedOSPlatform("windows")]
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WinTrustFileInfo
        {
            public uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WinTrustData
        {
            public uint Size;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WinTrustSignatureSettings
        {
            public uint Size;
            public uint Index;
            public uint Flags;
            public uint SecondarySignatures;
            public uint VerifiedIndex;
            public IntPtr CryptoPolicy;
        }

        // Only the first two fields are needed; native code owns the full struct.
        [StructLayout(LayoutKind.Sequential)]
        internal struct ProviderCertificatePrefix
        {
            public uint Size;
            public IntPtr Certificate;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CertificateContext
        {
            public uint Encoding;
            public IntPtr Encoded;
            public uint EncodedLength;
            public IntPtr CertificateInfo;
            public IntPtr CertificateStore;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint index,
            [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint index);
    }
}
