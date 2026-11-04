using System.Collections.ObjectModel;
using System.Formats.Asn1;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using FileScope.Core.Abstractions;
using FileScope.Core.Checksums;
using FileScope.Core.Models;
using FileScope.Core.Services;
using FileScope.Core.Signatures;

internal static class CoreChecks
{
    private static int _passed;

    private static async Task<int> Main(string[] args)
    {
        var temp = Path.Combine(Path.GetTempPath(), "FileScope-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            CheckTimestamps();
            var algorithms = Enum.GetValues<ChecksumAlgorithm>();
            var checksumService = new StreamChecksumService(new ChecksumStrategyFactory());
            var abcExpected = new Dictionary<ChecksumAlgorithm, string>
            {
                [ChecksumAlgorithm.MD5] = "900150983CD24FB0D6963F7D28E17F72",
                [ChecksumAlgorithm.SHA1] = "A9993E364706816ABA3E25717850C26C9CD0D89D",
                [ChecksumAlgorithm.SHA256] = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
                [ChecksumAlgorithm.SHA384] = "CB00753F45A35E8BB5A03D699AC65007272C32AB0EDED1631A8B605A43FF5BED8086072BA1E7CC2358BAECA134C825A7",
                [ChecksumAlgorithm.SHA512] = "DDAF35A193617ABACC417349AE20413112E6FA4E89A97EA20A9EEEE64B55D39A2192992A274FC1A836BA3C23A3FEEBBD454D4423643CE80E2A9AC94FA54CA49F"
            };
            using (var stream = new CountingStream(Encoding.ASCII.GetBytes("abc")))
            {
                var result = await checksumService.ComputeAsync(stream, algorithms);
                foreach (var algorithm in algorithms) Equal(abcExpected[algorithm], result[algorithm], $"abc / {algorithm}");
                Equal(3L, stream.BytesRead, "all five hashes read data only once");
                Check(stream.CanRead, "checksum service leaves caller-owned stream open");
            }
            using (var empty = new MemoryStream())
            {
                var result = await checksumService.ComputeAsync(empty, [ChecksumAlgorithm.SHA256]);
                Equal("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", result[ChecksumAlgorithm.SHA256], "empty file hash");
            }
            using (var stream = new CountingStream(Encoding.ASCII.GetBytes("abc")))
            {
                var result = await checksumService.ComputeAsync(stream, []);
                Equal(0, result.Count, "no algorithms / no hashes");
                Equal(0L, stream.BytesRead, "no algorithms / no data read");
            }
            using (var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc")))
            {
                var result = await checksumService.ComputeAsync(stream, [ChecksumAlgorithm.SHA256, ChecksumAlgorithm.SHA256]);
                Equal(1, result.Count, "repeated algorithm is deduplicated");
            }
            var sourceSelection = new List<ChecksumAlgorithm> { ChecksumAlgorithm.SHA256 };
            var snapshot = new AnalysisOptions(sourceSelection, false);
            sourceSelection.Clear();
            Equal(ChecksumAlgorithm.SHA256, snapshot.Algorithms.Single(), "options are an immutable snapshot");

            var largeData = Enumerable.Range(0, 2 * 1024 * 1024 + 13).Select(i => (byte)(i % 251)).ToArray();
            using (var stream = new CountingStream(largeData))
            {
                var result = await checksumService.ComputeAsync(stream, algorithms);
                // Independent whole-buffer API checks chunk boundaries and streaming composition.
                Equal(Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(largeData)),
                    result[ChecksumAlgorithm.SHA512], "multi-buffer streaming hash");
                Equal((long)largeData.Length, stream.BytesRead, "large file / one pass");
                Check(stream.ReadCalls > 2, "large file uses bounded chunks");
            }
            using (var cancelled = new CancellationTokenSource())
            using (var stream = new CountingStream(largeData))
            {
                cancelled.Cancel();
                await ExpectCancellation(() => checksumService.ComputeAsync(stream, algorithms, cancellationToken: cancelled.Token));
                Equal(0L, stream.BytesRead, "pre-cancelled operation performs no reads");
            }
            var factory = new TrackingFactory();
            using (var cancelled = new CancellationTokenSource())
            using (var stream = new CountingStream(largeData, () => cancelled.Cancel()))
            {
                await ExpectCancellation(() => new StreamChecksumService(factory).ComputeAsync(stream, algorithms, cancellationToken: cancelled.Token));
                Equal(5, factory.Disposed, "all strategies disposed after cancellation");
            }
            var partialFactory = new TrackingFactory();
            using (var stream = new MemoryStream())
            {
                try
                {
                    await new StreamChecksumService(partialFactory).ComputeAsync(stream,
                        [ChecksumAlgorithm.SHA256, (ChecksumAlgorithm)999]);
                    throw new Exception("Expected invalid-algorithm failure.");
                }
                catch (ArgumentOutOfRangeException) { }
                Equal(1, partialFactory.Disposed, "partial construction cleans up strategies");
            }

            var path = Path.Combine(temp, "Prüfung 中文 ' sample.bin");
            await File.WriteAllTextAsync(path, "abc", new UTF8Encoding(false));
            var signatures = new StubSignatures();
            var analyzer = new FileAnalysisService(checksumService, signatures);
            var analyzed = await analyzer.AnalyzeAsync(path, new AnalysisOptions(algorithms, true));
            Check(analyzed.Succeeded, "Unicode and apostrophe path");
            Equal(3L, analyzed.Length!.Value, "file size");
            Equal(abcExpected[ChecksumAlgorithm.SHA256], analyzed.Checksums[ChecksumAlgorithm.SHA256], "file pipeline hash");
            Check(analyzed.LastWriteTime.HasValue, "modification timestamp");
            Equal(1, signatures.Calls, "signature strategy is invoked");
            Check(signatures.SawReadableHandle, "signature receives the open readable stream");
            var metadataOnly = await analyzer.AnalyzeAsync(path, new AnalysisOptions([], false));
            Check(metadataOnly.Succeeded && metadataOnly.Checksums.Count == 0, "metadata-only analysis");
            Equal(1, signatures.Calls, "disabled signatures do not invoke native strategy");
            Equal(SignatureStatus.NotChecked, metadataOnly.Signature.Status, "not checked is explicit");
            Equal(AnalysisError.NotFound, (await analyzer.AnalyzeAsync(Path.Combine(temp, "missing.bin"), snapshot)).Error,
                "missing file becomes a per-file failure");
            Equal(AnalysisError.DirectoryNotSupported, (await analyzer.AnalyzeAsync(temp, snapshot)).Error,
                "directory drop is reported, never traversed");

            var native = typeof(WindowsAuthenticodeInspector).GetNestedType("Native", BindingFlags.NonPublic)!;
            var nativeData = native.GetNestedType("WinTrustData", BindingFlags.NonPublic)!;
            var nativeFile = native.GetNestedType("WinTrustFileInfo", BindingFlags.NonPublic)!;
            var nativeSettings = native.GetNestedType("WinTrustSignatureSettings", BindingFlags.NonPublic)!;
            Equal(IntPtr.Size == 8 ? 88 : 52, Marshal.SizeOf(nativeData), "WINTRUST_DATA ABI size");
            Equal(IntPtr.Size == 8 ? 32 : 16, Marshal.SizeOf(nativeFile), "WINTRUST_FILE_INFO ABI size");
            Equal(IntPtr.Size == 8 ? 32 : 24, Marshal.SizeOf(nativeSettings), "WINTRUST_SIGNATURE_SETTINGS ABI size");

            var nativeSigner = native.GetNestedType("ProviderSigner", BindingFlags.NonPublic)!;
            var nativeMessage = native.GetNestedType("MessageSignerInfo", BindingFlags.NonPublic)!;
            var nativeAttribute = native.GetNestedType("CryptAttribute", BindingFlags.NonPublic)!;
            Equal(IntPtr.Size == 8 ? 64 : 44, Marshal.SizeOf(nativeSigner), "CRYPT_PROVIDER_SGNR ABI size");
            Equal(4L, Marshal.OffsetOf(nativeSigner, "VerifyAsOf").ToInt64(), "FILETIME keeps DWORD alignment");
            Equal(IntPtr.Size == 8 ? 136 : 68, Marshal.SizeOf(nativeMessage), "CMSG_SIGNER_INFO ABI size");
            Equal(IntPtr.Size == 8 ? 120L : 60L, Marshal.OffsetOf(nativeMessage, "UnauthenticatedAttributes").ToInt64(),
                "CMSG_SIGNER_INFO unsigned attributes offset");
            Equal(IntPtr.Size == 8 ? 24 : 12, Marshal.SizeOf(nativeAttribute), "CRYPT_ATTRIBUTE ABI size");

            if (OperatingSystem.IsWindows())
            {
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Equal(AnalysisError.ReadFailure, (await analyzer.AnalyzeAsync(path, snapshot)).Error,
                        "locked file is reported as a read failure");
                }
                var nativeAnalyzer = new FileAnalysisService(checksumService, new WindowsAuthenticodeInspector());
                var unsigned = await nativeAnalyzer.AnalyzeAsync(typeof(FileAnalysisService).Assembly.Location,
                    new AnalysisOptions([], true));
                Equal(SignatureStatus.NoEmbeddedSignature, unsigned.Signature.Status, "unsigned PE / native WinVerifyTrust");
                Check(unsigned.Signature.Certificate is null, "unsigned PE has no signer metadata");
                Equal(0, unsigned.Signature.Signatures.Count, "unsigned PE has no selectable signature");
                if (args.Length == 2 && args[0] == "--signed-file")
                {
                    var signed = await nativeAnalyzer.AnalyzeAsync(args[1], new AnalysisOptions([], true));
                    Check(signed.Succeeded, "signed fixture can be read");
                    Check(signed.Signature.Certificate is not null, "embedded signer metadata extracted");
                    Check(signed.Signature.NativeStatusCode.HasValue, "native trust result retained");
                    Check(signed.Signature.Signatures.Count >= 1, "signed fixture exposes signature entries");
                    Check(signed.Signature.Signatures.Select(s => s.Index).SequenceEqual(
                        Enumerable.Range(0, signed.Signature.Signatures.Count).Select(i => (uint)i)),
                        "signature indices are complete and unique");
                    foreach (var entry in signed.Signature.Signatures)
                        Console.WriteLine($"INFO signature {entry.Index + 1}: {entry.Status}, {entry.DigestAlgorithm}, {entry.Timestamp}");
                    Console.WriteLine($"INFO signed fixture: {signed.Signature.Status}, 0x{signed.Signature.NativeStatusCode:X8}");
                    // Trust depends on this machine's roots, clock and cached revocation data.
                    // This does not assert that an arbitrary supplied file is trusted.
                }
                else Console.WriteLine("SKIP signed-file check: pass -SignedFile to Verify.ps1.");
            }
            else
            {
                await using var file = File.OpenRead(path);
                Equal(SignatureStatus.UnsupportedPlatform,
                    new WindowsAuthenticodeInspector().Inspect(file, path, RevocationMode.OfflineCache).Status,
                    "non-Windows signature inspection is explicit");
                Console.WriteLine("SKIP native Windows checks on this operating system.");
            }
            Console.WriteLine($"PASS: {_passed} core assertions.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temp, true); }
    }

    private static void CheckTimestamps()
    {
        var time = new DateTimeOffset(2024, 2, 3, 12, 34, 56, TimeSpan.Zero);
        var legacy = LegacyTimestamp(time);
        var rfc = RfcTimestamp(time.AddMilliseconds(123));
        Equal<DateTimeOffset?>(time, AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.CounterSignatureOid, legacy),
            "legacy countersignature signing-time decoded");
        Equal<DateTimeOffset?>(time.AddMilliseconds(123), AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.Rfc3161Oid, rfc),
            "RFC 3161 genTime decoded with fractional seconds");
        Check(AuthenticodeTimestampReader.Read("1.2.840.113549.1.9.5", legacy) is null,
            "self-declared signing time is not accepted as a countersignature");
        foreach (var oid in new[] { AuthenticodeTimestampReader.CounterSignatureOid, AuthenticodeTimestampReader.Rfc3161Oid })
        {
            Check(AuthenticodeTimestampReader.Read(oid, new byte[] { 0x30, 0x82, 0xff }) is null,
                "truncated timestamp is handled / " + oid);
            Check(AuthenticodeTimestampReader.Read(oid, Array.Empty<byte>()) is null,
                "empty timestamp is handled / " + oid);
        }
        Check(AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.CounterSignatureOid,
            LegacyTimestamp(time, duplicate: true)) is null, "ambiguous duplicate signing times are rejected");
        Check(AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.CounterSignatureOid,
            LegacyTimestamp(time, includeTime: false)) is null, "countersignature without signing-time has no invented date");
        Check(AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.Rfc3161Oid, rfc[..^10]) is null,
            "truncated RFC 3161 container is rejected");
        var confirmed = AuthenticodeTimestampReader.Select([time.AddDays(-1), time], true, time, true);
        Check(confirmed.Time == time && confirmed.Status == TimestampStatus.Verified,
            "only the timestamp used by a successful Windows check is confirmed");
        Equal(TimestampStatus.Unverified, AuthenticodeTimestampReader.Select([time], true, time, false).Status,
            "decoded time alone is not proof of timestamp trust");
        Equal(TimestampStatus.Unverified, AuthenticodeTimestampReader.Select([time], true, time.AddDays(1), true).Status,
            "Windows verification time cannot replace a different embedded timestamp");
        Equal(TimestampStatus.NotPresent, AuthenticodeTimestampReader.Select([], false, DateTimeOffset.UtcNow, true).Status,
            "current verification time never becomes a missing signing date");
        Equal(TimestampStatus.Unreadable, AuthenticodeTimestampReader.Select([], true, time, true).Status,
            "unreadable and absent timestamps are distinguished");
    }

    private static byte[] LegacyTimestamp(DateTimeOffset time, bool duplicate = false, bool includeTime = true)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        var tag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
        writer.PushSequence();
        writer.WriteInteger(1);
        writer.PushSequence(); writer.PushSequence(); writer.PopSequence(); writer.WriteInteger(1); writer.PopSequence();
        WriteAlgorithm(writer);
        writer.PushSetOf(tag);
        if (includeTime)
            for (var i = 0; i < (duplicate ? 2 : 1); i++)
            {
                writer.PushSequence(); writer.WriteObjectIdentifier("1.2.840.113549.1.9.5");
                writer.PushSetOf(); writer.WriteUtcTime(time); writer.PopSetOf(); writer.PopSequence();
            }
        writer.PopSetOf(tag);
        WriteAlgorithm(writer);
        writer.WriteOctetString([1, 2, 3]);
        writer.PopSequence();
        return writer.Encode();
    }

    private static byte[] RfcTimestamp(DateTimeOffset time)
    {
        var tst = new AsnWriter(AsnEncodingRules.DER);
        tst.PushSequence(); tst.WriteInteger(1); tst.WriteObjectIdentifier("1.2.3");
        tst.PushSequence(); WriteAlgorithm(tst); tst.WriteOctetString(new byte[32]); tst.PopSequence();
        tst.WriteInteger(123); tst.WriteGeneralizedTime(time, omitFractionalSeconds: false); tst.PopSequence();
        var writer = new AsnWriter(AsnEncodingRules.DER);
        var tag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
        writer.PushSequence(); writer.WriteObjectIdentifier("1.2.840.113549.1.7.2");
        writer.PushSequence(tag); writer.PushSequence(); writer.WriteInteger(3);
        writer.PushSetOf(); WriteAlgorithm(writer); writer.PopSetOf();
        writer.PushSequence(); writer.WriteObjectIdentifier("1.2.840.113549.1.9.16.1.4");
        writer.PushSequence(tag); writer.WriteOctetString(tst.Encode()); writer.PopSequence(tag); writer.PopSequence();
        writer.PushSetOf(); writer.PopSetOf();
        writer.PopSequence(); writer.PopSequence(tag); writer.PopSequence();
        return writer.Encode();
    }

    private static void WriteAlgorithm(AsnWriter writer)
    {
        writer.PushSequence(); writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1"); writer.PopSequence();
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        _passed++;
        Console.WriteLine("PASS: " + label);
    }
    private static void Equal<T>(T expected, T actual, string label) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{label} (expected {expected}; actual {actual})");
    private static async Task ExpectCancellation(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { Check(true, "operation cancels"); return; }
        throw new Exception("Expected OperationCanceledException.");
    }

    private sealed class StubSignatures : ISignatureInspector
    {
        public int Calls { get; private set; }
        public bool SawReadableHandle { get; private set; }
        public SignatureResult Inspect(FileStream file, string fullPath, RevocationMode mode, CancellationToken cancellationToken = default)
        {
            Calls++;
            SawReadableHandle = file.CanRead && !file.SafeFileHandle.IsInvalid && file.Position == 0;
            return new SignatureResult(SignatureStatus.NoEmbeddedSignature, mode);
        }
    }

    private sealed class TrackingFactory : IChecksumStrategyFactory
    {
        public int Disposed;
        public IChecksumStrategy Create(ChecksumAlgorithm algorithm) =>
            new TrackedStrategy(new ChecksumStrategyFactory().Create(algorithm), this);
        private sealed class TrackedStrategy(IChecksumStrategy inner, TrackingFactory owner) : IChecksumStrategy
        {
            public ChecksumAlgorithm Algorithm => inner.Algorithm;
            public void Append(ReadOnlySpan<byte> data) => inner.Append(data);
            public string Complete() => inner.Complete();
            public void Dispose() { inner.Dispose(); owner.Disposed++; }
        }
    }

    private sealed class CountingStream(byte[] bytes, Action? afterRead = null) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, false);
        public long BytesRead { get; private set; }
        public int ReadCalls { get; private set; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await _inner.ReadAsync(buffer, cancellationToken);
            ReadCalls++;
            BytesRead += count;
            if (count > 0) afterRead?.Invoke();
            return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
