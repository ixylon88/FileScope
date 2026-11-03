using System.Collections.ObjectModel;
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
                if (args.Length == 2 && args[0] == "--signed-file")
                {
                    var signed = await nativeAnalyzer.AnalyzeAsync(args[1], new AnalysisOptions([], true));
                    Check(signed.Succeeded, "signed fixture can be read");
                    Check(signed.Signature.Certificate is not null, "embedded signer metadata extracted");
                    Check(signed.Signature.NativeStatusCode.HasValue, "native trust result retained");
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
