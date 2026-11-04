using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;

namespace FileScope.Core.Signatures;

/// <summary>
/// Inspects embedded Authenticode signatures independently using Windows trust policy.
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
            var result = InspectWindows(file, fullPath, revocationMode, cancellationToken);
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
    private static SignatureResult InspectWindows(FileStream stream, string path, RevocationMode mode,
        CancellationToken cancellationToken)
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
            settingsPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Native.WinTrustSignatureSettings>());
            data.SignatureSettings = settingsPointer;
            var signatures = new List<EmbeddedSignature>();
            uint secondaryCount = 0;
            var foundSigner = false;
            for (uint index = 0; ; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settings = new Native.WinTrustSignatureSettings
                {
                    Size = (uint)Marshal.SizeOf<Native.WinTrustSignatureSettings>(),
                    Index = index,
                    Flags = index == 0 ? 3u : 1u // VERIFY_SPECIFIC; first call also GET_SECONDARY_SIG_COUNT
                };
                Marshal.StructureToPtr(settings, settingsPointer, false);
                data.StateAction = 1;
                data.StateData = IntPtr.Zero;
                try
                {
                    var status = Native.WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                    settings = Marshal.PtrToStructure<Native.WinTrustSignatureSettings>(settingsPointer);
                    if (index == 0) secondaryCount = settings.SecondarySignatures;
                    CertificateInfo? certificate = null;
                    var timestamp = SignatureTimestamp.Unreadable;
                    var algorithm = "";
                    string? diagnostic = null;
                    var mappedStatus = MapStatus(status);
                    if (settings.VerifiedIndex != index)
                    {
                        mappedStatus = SignatureStatus.CheckFailed;
                        diagnostic = $"Windows did not return the requested signature index {index}.";
                    }
                    else
                    {
                        try
                        {
                            var metadata = ReadSigner(data.StateData, status == 0);
                            certificate = metadata.Certificate;
                            timestamp = metadata.Timestamp;
                            algorithm = metadata.DigestAlgorithm;
                            foundSigner |= metadata.FoundSigner;
                        }
                        catch (CryptographicException ex) { diagnostic = ex.Message; }
                        catch (ArgumentException ex) { diagnostic = ex.Message; }
                    }
                    signatures.Add(new(index, mappedStatus, certificate, status, diagnostic, algorithm, timestamp));
                }
                finally { CloseState(ref action, ref data); }
                if (index >= secondaryCount) break;
            }
            var primary = signatures[0];
            var hasSignature = foundSigner || secondaryCount > 0 || primary.Status is
                SignatureStatus.Valid or SignatureStatus.InvalidSignature or SignatureStatus.Untrusted
                or SignatureStatus.Expired or SignatureStatus.Revoked or SignatureStatus.RevocationUnknown;
            return new SignatureResult(primary.Status, mode, primary.Certificate,
                primary.NativeStatusCode, primary.Diagnostic)
            {
                Signatures = hasSignature
                    ? signatures.AsReadOnly() : Array.Empty<EmbeddedSignature>()
            };
        }
        finally
        {
            try
            {
                CloseState(ref action, ref data);
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
    private static void CloseState(ref Guid action, ref Native.WinTrustData data)
    {
        if (data.StateData == IntPtr.Zero) return;
        data.StateAction = 2; // CLOSE even for failed verification and cancellation
        try { _ = Native.WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
        finally { data.StateData = IntPtr.Zero; }
    }

    private sealed record SignerMetadata(bool FoundSigner, CertificateInfo? Certificate,
        string DigestAlgorithm, SignatureTimestamp Timestamp);

    [SupportedOSPlatform("windows")]
    private static SignerMetadata ReadSigner(IntPtr state, bool trusted)
    {
        var empty = new SignerMetadata(false, null, "", SignatureTimestamp.Unreadable);
        if (state == IntPtr.Zero) return empty;
        var provider = Native.WTHelperProvDataFromStateData(state);
        if (provider == IntPtr.Zero) return empty;
        var signer = Native.WTHelperGetProvSignerFromChain(provider, 0, false, 0);
        if (signer == IntPtr.Zero) return empty;
        var info = Marshal.PtrToStructure<Native.ProviderSigner>(signer);
        var certificate = ReadCertificate(signer);
        if (info.SignerInfo == IntPtr.Zero)
            return new(true, certificate, "", SignatureTimestamp.Unreadable);
        var message = Marshal.PtrToStructure<Native.MessageSignerInfo>(info.SignerInfo);
        var algorithm = Marshal.PtrToStringAnsi(message.HashAlgorithm.ObjectId) ?? "";
        algorithm = algorithm switch
        {
            "1.2.840.113549.2.5" => "MD5", "1.3.14.3.2.26" => "SHA-1",
            "2.16.840.1.101.3.4.2.1" => "SHA-256", "2.16.840.1.101.3.4.2.2" => "SHA-384",
            "2.16.840.1.101.3.4.2.3" => "SHA-512", _ => algorithm
        };
        var hasTimestamp = false;
        var times = new List<DateTimeOffset>();
        var attributes = message.UnauthenticatedAttributes;
        // Native arrays are owned by WinVerifyTrust and read only while its state is open.
        if (attributes.Count > 4096) throw new CryptographicException("Too many signature attributes.");
        for (var i = 0; attributes.Data != IntPtr.Zero && i < attributes.Count; i++)
        {
            var attribute = Marshal.PtrToStructure<Native.CryptAttribute>(
                IntPtr.Add(attributes.Data, checked(i * Marshal.SizeOf<Native.CryptAttribute>())));
            var oid = Marshal.PtrToStringAnsi(attribute.ObjectId) ?? "";
            if (!AuthenticodeTimestampReader.IsTimestamp(oid)) continue;
            hasTimestamp = true;
            if (attribute.Values.Count > 4096) throw new CryptographicException("Too many timestamp values.");
            for (var j = 0; attribute.Values.Data != IntPtr.Zero && j < attribute.Values.Count; j++)
            {
                var blob = Marshal.PtrToStructure<Native.Blob>(
                    IntPtr.Add(attribute.Values.Data, checked(j * Marshal.SizeOf<Native.Blob>())));
                if (blob.Data == IntPtr.Zero || blob.Count == 0 || blob.Count > 16 * 1024 * 1024) continue;
                var bytes = new byte[checked((int)blob.Count)];
                Marshal.Copy(blob.Data, bytes, 0, bytes.Length);
                if (AuthenticodeTimestampReader.Read(oid, bytes) is { } time) times.Add(time);
            }
        }
        var trustedCounterSigner = false;
        // The metadata parser does not establish trust. Confirmation also needs a
        // successful Windows policy result and a successfully checked countersigner.
        for (uint i = 0; trusted && i < info.CounterSignerCount; i++)
        {
            var pointer = Native.WTHelperGetProvSignerFromChain(provider, 0, true, i);
            if (pointer != IntPtr.Zero && Marshal.PtrToStructure<Native.ProviderSigner>(pointer).Error == 0)
            { trustedCounterSigner = true; break; }
        }
        DateTimeOffset? verifiedAsOf = null;
        try
        {
            var fileTime = ((long)info.VerifyAsOf.High << 32) | info.VerifyAsOf.Low;
            if (fileTime > 0) verifiedAsOf = new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime));
        }
        catch (ArgumentOutOfRangeException) { /* No usable verification time. */ }
        return new(true, certificate, algorithm,
            AuthenticodeTimestampReader.Select(times, hasTimestamp, verifiedAsOf, trustedCounterSigner));
    }

    [SupportedOSPlatform("windows")]
    private static CertificateInfo? ReadCertificate(IntPtr signer)
    {
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

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeFileTime { public uint Low; public uint High; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProviderSigner
        {
            public uint Size;
            public NativeFileTime VerifyAsOf; // FILETIME has DWORD alignment, not Int64 alignment.
            public uint CertificateCount;
            public IntPtr Certificates;
            public uint SignerType;
            public IntPtr SignerInfo;
            public uint Error;
            public uint CounterSignerCount;
            public IntPtr CounterSigners;
            public IntPtr ChainContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Blob { public uint Count; public IntPtr Data; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AlgorithmIdentifier { public IntPtr ObjectId; public Blob Parameters; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CryptAttribute { public IntPtr ObjectId; public Blob Values; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MessageSignerInfo
        {
            public uint Version;
            public Blob Issuer;
            public Blob SerialNumber;
            public AlgorithmIdentifier HashAlgorithm;
            public AlgorithmIdentifier HashEncryptionAlgorithm;
            public Blob EncryptedHash;
            public Blob AuthenticatedAttributes;
            public Blob UnauthenticatedAttributes;
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
