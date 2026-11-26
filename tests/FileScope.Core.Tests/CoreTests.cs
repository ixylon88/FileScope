using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FileScope.Core.Abstractions;
using FileScope.Core.Checksums;
using FileScope.Core.Models;
using FileScope.Core.Metadata;
using FileScope.Core.Services;
using FileScope.Core.Signatures;
using FileScope.Core.Files;
using FileScope.Core.Export;

namespace FileScope.Core.Tests;

[TestClass]
public sealed class CoreTests
{
    private static readonly Dictionary<ChecksumAlgorithm, string> KnownDigests = new()
    {
        [ChecksumAlgorithm.MD5] = "900150983CD24FB0D6963F7D28E17F72",
        [ChecksumAlgorithm.SHA1] = "A9993E364706816ABA3E25717850C26C9CD0D89D",
        [ChecksumAlgorithm.SHA256] = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
        [ChecksumAlgorithm.SHA384] = "CB00753F45A35E8BB5A03D699AC65007272C32AB0EDED1631A8B605A43FF5BED8086072BA1E7CC2358BAECA134C825A7",
        [ChecksumAlgorithm.SHA512] = "DDAF35A193617ABACC417349AE20413112E6FA4E89A97EA20A9EEEE64B55D39A2192992A274FC1A836BA3C23A3FEEBBD454D4423643CE80E2A9AC94FA54CA49F"
    };

    private string _directory = null!;
    private string _plainFile = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "FileScope-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _plainFile = Path.Combine(_directory, "Prüfung 中文 ' sample.bin");
        File.WriteAllText(_plainFile, "abc", new UTF8Encoding(false));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task OptionalCatalogFailuresPreserveCompletedHashes()
    {
        var analyzer = new FileAnalysisService(new StreamChecksumService(new ChecksumStrategyFactory()), new StubSignatures());
        var uncheckedResult = await analyzer.AnalyzeAsync(_plainFile, new AnalysisOptions([ChecksumAlgorithm.SHA256], false));
        Assert.IsNull(uncheckedResult.Catalogs);

        var progress = new RecordedAnalysisProgress();
        var missing = Path.Combine(_directory, "missing.cat");
        var options = new AnalysisOptions([ChecksumAlgorithm.SHA256], false, catalogs: new CatalogSearch([missing]));
        var result = await analyzer.AnalyzeAsync(_plainFile, options, progress);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(KnownDigests[ChecksumAlgorithm.SHA256], result.Checksums[ChecksumAlgorithm.SHA256]);
        Assert.AreEqual(SignatureStatus.NotChecked, result.Signature.Status);
        Assert.AreEqual(AnalysisStage.CheckingCatalogs, progress.Values.Last().Stage);
        Assert.IsNotNull(result.Catalogs);
        Assert.IsFalse(result.Catalogs.IndexComplete);
        if (OperatingSystem.IsWindows()) Assert.IsTrue(result.Catalogs.IndexIssues.Single().Contains(missing));
        else Assert.IsNotEmpty(result.Catalogs.Issues);
    }

    [TestMethod]
    public async Task FileScopeFindsOnlyTheSuppliedCatalogMember()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows catalog provider.");
        var member = Path.Combine(_directory, "member.txt");
        var catalog = Path.Combine(_directory, "loose.cat");
        using (var source = typeof(CoreTests).Assembly.GetManifestResourceStream("FileScope.Core.Tests.loose.cat"))
        using (var target = File.Create(catalog))
        {
            Assert.IsNotNull(source);
            source.CopyTo(target);
        }

        var bytes = "DuplicateFinder catalog membership regression fixture."u8.ToArray();
        await File.WriteAllBytesAsync(member, bytes);
        var analyzer = new FileAnalysisService(new StreamChecksumService(new ChecksumStrategyFactory()), new StubSignatures());
        var options = new AnalysisOptions([ChecksumAlgorithm.SHA256], false, catalogs: new CatalogSearch([catalog]));
        var original = await analyzer.AnalyzeAsync(member, options);
        Assert.IsNotNull(original.Catalogs);
        Assert.IsTrue(original.Catalogs.IndexComplete);
        Assert.IsEmpty(original.Catalogs.Issues);

        // Windows can return multiple member tags from the same V2 catalog.
        var sha256 = original.Checksums[ChecksumAlgorithm.SHA256];
        var match = original.Catalogs.Matches.SingleOrDefault(m =>
            string.Equals(m.Path, catalog, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Member, sha256, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(match, $"Missing SHA-256 catalog member {sha256}. " +
            $"Returned tags: {string.Join(", ", original.Catalogs.Matches.Select(m => m.Member))}");
        Assert.IsFalse(match.Registered);
        Assert.AreNotEqual(SignatureStatus.Valid, match.Trust.Status);
        Assert.AreEqual(SignatureStatus.NotChecked, original.Signature.Status);

        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(member, bytes);
        var changed = await analyzer.AnalyzeAsync(member, options);
        Assert.IsNotNull(changed.Catalogs);
        Assert.IsTrue(changed.Catalogs.IndexComplete);
        Assert.IsEmpty(changed.Catalogs.Matches.Where(m =>
            string.Equals(m.Path, catalog, StringComparison.OrdinalIgnoreCase)));
        Assert.IsEmpty(changed.Catalogs.Issues);
    }

    [TestMethod]
    public async Task AllHashesReadTheStreamOnce()
    {
        var algorithms = Enum.GetValues<ChecksumAlgorithm>();
        var checksumService = new StreamChecksumService(new ChecksumStrategyFactory());

        using var stream = new CountingStream(Encoding.ASCII.GetBytes("abc"));

        var result = await checksumService.ComputeAsync(stream, algorithms);
        foreach (var algorithm in algorithms) Assert.AreEqual(KnownDigests[algorithm], result[algorithm], $"abc / {algorithm}");
        Assert.AreEqual(3L, stream.BytesRead, "all five hashes read data only once");
        Assert.IsTrue(stream.CanRead, "checksum service leaves caller-owned stream open");
    }

    [TestMethod]
    public async Task EmptyStreamHasKnownSha256()
    {
        var checksumService = new StreamChecksumService(new ChecksumStrategyFactory());

        using var empty = new MemoryStream();

        var result = await checksumService.ComputeAsync(empty, [ChecksumAlgorithm.SHA256]);
        Assert.AreEqual("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", result[ChecksumAlgorithm.SHA256], "empty file hash");
    }

    [TestMethod]
    public async Task NoAlgorithmsPerformNoReads()
    {
        var checksumService = new StreamChecksumService(new ChecksumStrategyFactory());

        using var stream = new CountingStream(Encoding.ASCII.GetBytes("abc"));

        var result = await checksumService.ComputeAsync(stream, []);
        Assert.AreEqual(0, result.Count, "no algorithms / no hashes");
        Assert.AreEqual(0L, stream.BytesRead, "no algorithms / no data read");
    }

    [TestMethod]
    public async Task RepeatedAlgorithmsAreDeduplicated()
    {
        var checksumService = new StreamChecksumService(new ChecksumStrategyFactory());

        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc"));

        var result = await checksumService.ComputeAsync(stream, [ChecksumAlgorithm.SHA256, ChecksumAlgorithm.SHA256]);
        Assert.AreEqual(1, result.Count, "repeated algorithm is deduplicated");
    }

    [TestMethod]
    public void AnalysisOptionsSnapshotTheSelection()
    {
        var sourceSelection = new List<ChecksumAlgorithm> { ChecksumAlgorithm.SHA256 };
        var snapshot = new AnalysisOptions(sourceSelection, false);
        sourceSelection.Clear();
        Assert.AreEqual(ChecksumAlgorithm.SHA256, snapshot.Algorithms.Single(), "options are an immutable snapshot");
    }

    [TestMethod]
    public async Task LargeStreamUsesBoundedBuffersAndOnePass()
    {
        var algorithms = Enum.GetValues<ChecksumAlgorithm>();
        var checksumService = new StreamChecksumService(new ChecksumStrategyFactory());
        var largeData = Enumerable.Range(0, 2 * 1024 * 1024 + 13).Select(i => (byte)(i % 251)).ToArray();

        using var stream = new CountingStream(largeData);

        var result = await checksumService.ComputeAsync(stream, algorithms);
        // Independent whole-buffer API checks chunk boundaries and streaming composition.
        Assert.AreEqual(Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(largeData)),
            result[ChecksumAlgorithm.SHA512], "multi-buffer streaming hash");
        Assert.AreEqual(largeData.Length, stream.BytesRead, "large file / one pass");
        Assert.IsTrue(stream.ReadCalls > 2, "large file uses bounded chunks");
    }

    [TestMethod]
    public async Task PreCancelledHashingPerformsNoReads()
    {
        var algorithms = Enum.GetValues<ChecksumAlgorithm>();
        var checksumService = new StreamChecksumService(new ChecksumStrategyFactory());
        var largeData = Enumerable.Range(0, 2 * 1024 * 1024 + 13).Select(i => (byte)(i % 251)).ToArray();

        using var cancelled = new CancellationTokenSource();
        using var stream = new CountingStream(largeData);

        cancelled.Cancel();
        await ExpectCancellationAsync(() => checksumService.ComputeAsync(stream, algorithms, cancellationToken: cancelled.Token));
        Assert.AreEqual(0L, stream.BytesRead, "pre-cancelled operation performs no reads");
    }

    [TestMethod]
    public async Task CancellationDisposesAllHashStrategies()
    {
        var algorithms = Enum.GetValues<ChecksumAlgorithm>();
        var largeData = Enumerable.Range(0, 2 * 1024 * 1024 + 13).Select(i => (byte)(i % 251)).ToArray();

        var factory = new TrackingFactory();
        using var cancelled = new CancellationTokenSource();
        using var stream = new CountingStream(largeData, cancelled.Cancel);

        await ExpectCancellationAsync(() => new StreamChecksumService(factory).ComputeAsync(stream, algorithms, cancellationToken: cancelled.Token));
        Assert.AreEqual(5, factory.Disposed, "all strategies disposed after cancellation");
    }

    [TestMethod]
    public async Task FailedStrategyCreationDisposesEarlierStrategies()
    {
        var partialFactory = new TrackingFactory();
        using var stream = new MemoryStream();

        try
        {
            await new StreamChecksumService(partialFactory).ComputeAsync(stream,
                [ChecksumAlgorithm.SHA256, (ChecksumAlgorithm)999]);
            Assert.Fail("Expected invalid-algorithm failure.");
        }
        catch (ArgumentOutOfRangeException) { }
        Assert.AreEqual(1, partialFactory.Disposed, "partial construction cleans up strategies");
    }

    [TestMethod]
    public async Task AnalysisPipelineKeepsResultsAndReportsFileErrors()
    {
        var path = _plainFile;
        var algorithms = Enum.GetValues<ChecksumAlgorithm>();
        var checksumService = new StreamChecksumService(new ChecksumStrategyFactory());
        var snapshot = new AnalysisOptions([ChecksumAlgorithm.SHA256], false);

        var signatures = new StubSignatures();
        var analyzer = new FileAnalysisService(checksumService, signatures);
        var analyzed = await analyzer.AnalyzeAsync(path, new AnalysisOptions(algorithms, true));
        Assert.IsTrue(analyzed.Succeeded, "Unicode and apostrophe path");
        Assert.AreEqual(3L, analyzed.Length!.Value, "file size");
        Assert.AreEqual(KnownDigests[ChecksumAlgorithm.SHA256], analyzed.Checksums[ChecksumAlgorithm.SHA256], "file pipeline hash");
        Assert.IsTrue(analyzed.LastWriteTime.HasValue, "modification timestamp");
        Assert.AreEqual(1, signatures.Calls, "signature strategy is invoked");
        Assert.IsTrue(signatures.SawReadableHandle, "signature receives the open readable stream");
        var metadataProgress = new RecordedAnalysisProgress();
        var metadataOnly = await analyzer.AnalyzeAsync(path, new AnalysisOptions([], false), metadataProgress);
        Assert.IsTrue(metadataProgress.Values.Select(value => value.Stage).SequenceEqual([AnalysisStage.OpeningFile]),
            "disabled inspections do not advertise checksum, metadata or signature work");
        Assert.IsTrue(metadataOnly.Succeeded && metadataOnly.Checksums.Count == 0, "metadata-only analysis");
        Assert.AreEqual(1, signatures.Calls, "disabled signatures do not invoke native strategy");
        Assert.AreEqual(SignatureStatus.NotChecked, metadataOnly.Signature.Status, "not checked is explicit");
        Assert.AreEqual(AnalysisError.NotFound, (await analyzer.AnalyzeAsync(Path.Combine(_directory, "missing.bin"), snapshot)).Error,
            "missing file becomes a per-file failure");
        Assert.AreEqual(AnalysisError.DirectoryNotSupported, (await analyzer.AnalyzeAsync(_directory, snapshot)).Error,
            "file analysis rejects directories; traversal belongs to the folder scanner");
    }

    [TestMethod]
    public void NativeSignatureStructuresHaveCorrectLayout()
    {
        var native = typeof(WindowsAuthenticodeInspector).GetNestedType("Native", BindingFlags.NonPublic)!;
        var nativeData = native.GetNestedType("WinTrustData", BindingFlags.NonPublic)!;
        var nativeFile = native.GetNestedType("WinTrustFileInfo", BindingFlags.NonPublic)!;
        var nativeSettings = native.GetNestedType("WinTrustSignatureSettings", BindingFlags.NonPublic)!;
        Assert.AreEqual(IntPtr.Size == 8 ? 88 : 52, Marshal.SizeOf(nativeData), "WINTRUST_DATA ABI size");
        Assert.AreEqual(IntPtr.Size == 8 ? 32 : 16, Marshal.SizeOf(nativeFile), "WINTRUST_FILE_INFO ABI size");
        Assert.AreEqual(IntPtr.Size == 8 ? 32 : 24, Marshal.SizeOf(nativeSettings), "WINTRUST_SIGNATURE_SETTINGS ABI size");

        var nativeSigner = native.GetNestedType("ProviderSigner", BindingFlags.NonPublic)!;
        var nativeMessage = native.GetNestedType("MessageSignerInfo", BindingFlags.NonPublic)!;
        var nativeAttribute = native.GetNestedType("CryptAttribute", BindingFlags.NonPublic)!;
        Assert.AreEqual(IntPtr.Size == 8 ? 64 : 44, Marshal.SizeOf(nativeSigner), "CRYPT_PROVIDER_SGNR ABI size");
        Assert.AreEqual(4L, Marshal.OffsetOf(nativeSigner, "VerifyAsOf").ToInt64(), "FILETIME keeps DWORD alignment");
        Assert.AreEqual(IntPtr.Size == 8 ? 136 : 68, Marshal.SizeOf(nativeMessage), "CMSG_SIGNER_INFO ABI size");
        Assert.AreEqual(IntPtr.Size == 8 ? 120L : 60L, Marshal.OffsetOf(nativeMessage, "UnauthenticatedAttributes").ToInt64(),
            "CMSG_SIGNER_INFO unsigned attributes offset");
        Assert.AreEqual(IntPtr.Size == 8 ? 24 : 12, Marshal.SizeOf(nativeAttribute), "CRYPT_ATTRIBUTE ABI size");
    }

    [TestMethod]
    public async Task LockedFileReportsReadFailure()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file sharing is required.");

        var analyzer = new FileAnalysisService(new StreamChecksumService(new ChecksumStrategyFactory()), new StubSignatures());
        using var locked = new FileStream(_plainFile, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.AreEqual(AnalysisError.ReadFailure,
            (await analyzer.AnalyzeAsync(_plainFile, new AnalysisOptions([ChecksumAlgorithm.SHA256], false))).Error,
            "locked file is reported as a read failure");
    }

    [TestMethod]
    public async Task UnsignedPeHasNoEmbeddedSignature()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Native Authenticode requires Windows.");

        var nativeAnalyzer = new FileAnalysisService(new StreamChecksumService(new ChecksumStrategyFactory()), new WindowsAuthenticodeInspector());
        var unsigned = await nativeAnalyzer.AnalyzeAsync(typeof(FileAnalysisService).Assembly.Location,
            new AnalysisOptions([], true));
        Assert.AreEqual(SignatureStatus.NoEmbeddedSignature, unsigned.Signature.Status, "unsigned PE / native WinVerifyTrust");
        Assert.IsTrue(unsigned.Signature.Certificate is null, "unsigned PE has no signer metadata");
        Assert.AreEqual(0, unsigned.Signature.Signatures.Count, "unsigned PE has no selectable signature");
    }

    [TestMethod]
    public async Task OptionalSignedFileExposesSignerMetadata()
    {
        // Test Explorer also runs without a selected .runsettings file.
        // Check optional inputs before the OS guard so this path is tested on every host.
        TestContext.Properties.TryGetValue("SignedFile", out var parameter);
        var path = parameter as string;
        if (string.IsNullOrWhiteSpace(path)) Assert.Inconclusive("Set SignedFile in the selected .runsettings file.");
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Native Authenticode requires Windows.");

        Assert.IsTrue(Path.IsPathFullyQualified(path) && File.Exists(path), "SignedFile must name an existing absolute path.");

        var nativeAnalyzer = new FileAnalysisService(new StreamChecksumService(new ChecksumStrategyFactory()), new WindowsAuthenticodeInspector());
        var signed = await nativeAnalyzer.AnalyzeAsync(path, new AnalysisOptions([], true));
        Assert.IsTrue(signed.Succeeded, "signed fixture can be read");
        Assert.IsTrue(signed.Signature.Certificate is not null, "embedded signer metadata extracted");
        Assert.IsTrue(signed.Signature.NativeStatusCode.HasValue, "native trust result retained");
        Assert.IsTrue(signed.Signature.Signatures.Count >= 1, "signed fixture exposes signature entries");
        Assert.IsTrue(signed.Signature.Signatures.Select(s => s.Index).SequenceEqual(
            Enumerable.Range(0, signed.Signature.Signatures.Count).Select(i => (uint)i)),
            "signature indices are complete and unique");
        foreach (var entry in signed.Signature.Signatures)
            Console.WriteLine($"INFO signature {entry.Index + 1}: {entry.Status}, {entry.DigestAlgorithm}, {entry.Timestamp}");
        Console.WriteLine($"INFO signed fixture: {signed.Signature.Status}, 0x{signed.Signature.NativeStatusCode:X8}");
        // Trust depends on this machine's roots, clock and cached revocation data.
        // This does not assert that an arbitrary supplied file is trusted.
    }

    [TestMethod]
    public void NonWindowsSignatureInspectionIsExplicit()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("This test covers the non-Windows result.");

        using var file = File.OpenRead(_plainFile);
        Assert.AreEqual(SignatureStatus.UnsupportedPlatform,
            new WindowsAuthenticodeInspector().Inspect(file, _plainFile, RevocationMode.OfflineCache).Status);
    }

    [TestMethod]
    public void WindowsVersionHandlesPreservePositions()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Native version handles require Windows.");
            return;
        }
        RequireVersionHandleApi();

        var nePath = Path.Combine(_directory, "version-fixture.exe");
        File.WriteAllBytes(nePath, NeVersionFixture());
        var baseline = System.Diagnostics.FileVersionInfo.GetVersionInfo(nePath);
        Assert.AreEqual("1.2.3.4", baseline.FileVersion, "generated NE fixture contains readable VERSIONINFO");

        CheckVersionHandles([_plainFile, nePath, typeof(FileAnalysisService).Assembly.Location,
            typeof(object).Assembly.Location, Path.Combine(Environment.SystemDirectory, "kernel32.dll")]);
    }

    [TestMethod]
    public void OptionalVersionFilesPreservePositions()
    {
        TestContext.Properties.TryGetValue("VersionFiles", out var parameter);
        var json = parameter as string;
        var paths = string.IsNullOrWhiteSpace(json) ? [] : System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
        if (paths.Length == 0) Assert.Inconclusive("Set VersionFiles in the selected .runsettings file.");

        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Native version handles require Windows.");
            return;
        }
        RequireVersionHandleApi();

        Assert.IsTrue(paths.All(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path)),
            "VersionFiles must contain existing absolute paths.");

        CheckVersionHandles(paths);
    }

    private static void RequireVersionHandleApi()
    {
        if (!OperatingSystem.IsWindows() || !WindowsFileVersionReader.IsAvailable)
            Assert.Inconclusive("The native Windows version-handle API is unavailable.");
    }

    [TestMethod]
    public void FileNameFilter()
    {
        // No fixture files are created: matching needs names, never file contents.
        var names = new[] { "Setup.EXE", "tool.dll", "tool.exe.bak", "debug.dll", "test.dll", "notes.txt", "copy.tmp" };
        var paths = names.Select(name => Path.Combine("not-created", "parent.exe", name)).ToArray();

        var all = new FileNameFilter("");
        var (allFiles, allExcluded) = all.Apply(paths);
        Assert.IsTrue(!all.IsActive && allFiles.SequenceEqual(paths) && allExcluded == 0,
            "empty intake pattern admits every path in its original order without file access");

        void Matches(string pattern, params string[] expected)
        {
            var filter = new FileNameFilter(pattern);
            var (files, excluded) = filter.Apply(paths);
            Assert.IsTrue(filter.IsActive && files.Select(Path.GetFileName).SequenceEqual(expected)
                && excluded == paths.Length - expected.Length,
                "filename intake pattern and excluded count / " + pattern);
        }

        Matches(@"\.exe$", "Setup.EXE");
        Matches(@"\.(exe|dll)$", "Setup.EXE", "tool.dll", "debug.dll", "test.dll");
        Matches("setup", "Setup.EXE");
        Matches("^(setup|install)", "Setup.EXE");
        Matches("^(?!.*setup).*$", names.Skip(1).ToArray());
        Matches(@"^(?!.*\.(tmp|bak)$).*$", "Setup.EXE", "tool.dll", "debug.dll", "test.dll", "notes.txt");
        Matches(@"^(?!.*(debug|test)).*\.dll$", "tool.dll");
        Matches("parent");
        Matches("^(?!.*parent).*$", names);

        var (dotFiles, dotExcluded) = new FileNameFilter(@"\.exe$").Apply(["app.exe", "appexe", "appXexe"]);
        Assert.IsTrue(dotFiles.SequenceEqual(["app.exe"]) && dotExcluded == 2,
            "escaped period matches a literal extension separator");

        var (spaceFiles, spaceExcluded) = new FileNameFilter(" ").Apply(["has space.exe", "plain.exe"]);
        Assert.IsTrue(spaceFiles.SequenceEqual(["has space.exe"]) && spaceExcluded == 1,
            "spaces remain meaningful regex characters");

        var (duplicateFiles, duplicateExcluded) = new FileNameFilter(@"\.dll$").Apply(["a.dll", "", " ", "a.dll", "a.txt"]);
        Assert.IsTrue(duplicateFiles.SequenceEqual(["a.dll", "a.dll"]) && duplicateExcluded == 1,
            "intake matching leaves deduplication to the caller and ignores empty paths");

        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("tr-TR");
            Assert.IsTrue(new FileNameFilter("^install").Apply(["INSTALL.EXE"]).Files.Count == 1,
                "filename matching is case-insensitive independently of application culture");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = originalCulture; }

        try
        {
            _ = new FileNameFilter("[");
            Assert.Fail("Expected invalid regex rejection.");
        }
        catch (ArgumentException) { Console.WriteLine("invalid intake patterns are rejected instead of admitting every file"); }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var input in new[] { paths, Array.Empty<string>() })
        {
            try
            {
                all.Apply(input, cancellation.Token);
                Assert.Fail("Expected intake cancellation.");
            }
            catch (OperationCanceledException) { Console.WriteLine("cancelled filename filtering returns no selection, even for empty input"); }
        }

        using var midCancellation = new CancellationTokenSource();
        try
        {
            all.Apply(new CancelAfterFirstList(paths, midCancellation), midCancellation.Token);
            Assert.Fail("Expected mid-intake cancellation.");
        }
        catch (OperationCanceledException) { Console.WriteLine("cancellation between filenames does not return a partial selection"); }

        try
        {
            new FileNameFilter("^(a+)+$").Apply(["a", new string('a', 512) + "!"]);
            Assert.Fail("Expected regex timeout.");
        }
        catch (RegexMatchTimeoutException) { Console.WriteLine("excessive backtracking times out without returning a partial selection"); }
    }

    [TestMethod]
    public void FolderScan()
    {
        var temp = _directory;

        var root = Path.Combine(temp, "folder-input");
        var nested = Path.Combine(root, "sub", "中文");
        Directory.CreateDirectory(nested);
        var first = Path.Combine(root, "first.bin");
        var second = Path.Combine(nested, "Prüfung.txt");
        var hidden = Path.Combine(root, ".hidden");
        File.WriteAllText(first, "one");
        File.WriteAllText(second, "two");
        File.WriteAllText(hidden, "three");
        if (OperatingSystem.IsWindows()) File.SetAttributes(hidden, FileAttributes.Hidden);
        var scanner = new FolderScanner();
        var found = scanner.Scan(root);
        Assert.IsTrue(found.Files.ToHashSet(StringComparer.Ordinal).SetEquals([first, second, hidden]),
            "folder scan includes nested, Unicode and hidden files exactly once");
        Assert.IsTrue(found.Issues.Count == 0 && found.SkippedDirectoryLinks == 0, "normal scan has no omissions");
        var empty = Path.Combine(temp, "empty-folder");
        Directory.CreateDirectory(empty);
        Assert.IsTrue(scanner.Scan(empty).Files.Count == 0, "empty folder is a valid empty result");
        try { scanner.Scan(Path.Combine(root, "missing")); Assert.Fail("Expected missing root failure."); }
        catch (DirectoryNotFoundException) { Console.WriteLine("missing selected folder is reported"); }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { scanner.Scan(root, cancelled.Token); Assert.Fail("Expected scan cancellation."); }
            catch (OperationCanceledException) { Console.WriteLine("folder enumeration observes cancellation"); }
        }

        var link = Path.Combine(nested, "back-to-root");
        var created = false;
        try { Directory.CreateSymbolicLink(link, root); created = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        { Console.WriteLine("SKIP symbolic-link fixture: " + ex.Message); }
        if (created)
        {
            try
            {
                found = scanner.Scan(root);
                Assert.IsTrue(found.SkippedDirectoryLinks == 1 && found.Files.Count == 3,
                    "directory link to an ancestor is counted and never traversed");
                found = scanner.Scan(link);
                Assert.IsTrue(found.SkippedDirectoryLinks == 1 && found.Files.Count == 3
                    && found.Files.All(path => path.StartsWith(link + Path.DirectorySeparatorChar, StringComparison.Ordinal)),
                    "an explicitly selected directory link is a root, but links below it still cannot form a cycle");
            }
            finally { Directory.Delete(link); }
        }

        var fileLink = Path.Combine(root, "file-link.bin");
        var missingLink = Path.Combine(root, "missing-link.bin");
        try
        {
            var fileLinksCreated = false;
            try
            {
                File.CreateSymbolicLink(fileLink, first);
                File.CreateSymbolicLink(missingLink, Path.Combine(temp, "missing-target.bin"));
                fileLinksCreated = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            { Console.WriteLine("SKIP file-symlink fixture: " + ex.Message); }
            if (fileLinksCreated)
            {
                found = scanner.Scan(root);
                Assert.IsTrue(found.Files.ToHashSet().SetEquals([first, second, hidden, fileLink, missingLink])
                    && found.SkippedDirectoryLinks == 0,
                    "file symlinks, including broken ones, remain file paths for per-row analysis");
            }
        }
        finally { File.Delete(fileLink); File.Delete(missingLink); }

        var hardLink = Path.Combine(root, "hard-link.bin");
        var hardLinkCreated = OperatingSystem.IsWindows()
            ? LinkTestNative.CreateHardLinkW(hardLink, first, IntPtr.Zero)
            : OperatingSystem.IsLinux() && LinkTestNative.Link(Encoding.UTF8.GetBytes(first + '\0'),
                Encoding.UTF8.GetBytes(hardLink + '\0')) == 0;
        if (hardLinkCreated)
        {
            try
            {
                found = scanner.Scan(root);
                Assert.IsTrue(found.Files.ToHashSet().SetEquals([first, second, hidden, hardLink])
                    && found.SkippedDirectoryLinks == 0,
                    "hard links remain separate file paths and do not cause directory traversal");
            }
            finally { File.Delete(hardLink); }
        }
        else Console.WriteLine("SKIP hard-link fixture: " + Marshal.GetLastWin32Error());
    }

    [TestMethod]
    public void FileMoves()
    {
        var temp = _directory;

        var root = Path.Combine(temp, "moves");
        var service = new FileMoveService();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Source(string folder, string name = "same.bin")
        {
            var directory = Path.Combine(root, folder);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, folder);
            return path;
        }
        FileMoveResult Move(string source) => service.MoveToSubfolder([source], "unsigned", listed).Single();
        var first = Source("first");
        var second = Source("second");
        var results = service.MoveToSubfolder([first, second], "unsigned", listed);
        Assert.IsTrue(results.All(result => result.Outcome == FileMoveOutcome.Moved)
            && results.Select(result => result.DestinationPath).Distinct().Count() == 2,
            "equal filenames from different parents move to separate child folders");
        Assert.IsTrue(!File.Exists(first) && !File.Exists(second)
            && File.ReadAllText(results[0].DestinationPath!) == "first" && File.ReadAllText(results[1].DestinationPath!) == "second",
            "moving preserves contents and removes only the original names");
        var already = Move(results[0].DestinationPath!);
        Assert.IsTrue(already.Outcome == FileMoveOutcome.AlreadyInFolder && !Directory.Exists(Path.Combine(Path.GetDirectoryName(already.SourcePath)!, "unsigned")),
            "repeating a move does not create nested status folders");
        File.WriteAllText(first, "replacement");
        Assert.IsTrue(Move(first).Outcome == FileMoveOutcome.TargetExists
            && File.ReadAllText(first) == "replacement" && File.ReadAllText(results[0].DestinationPath!) == "first",
            "an existing destination is never overwritten and its source is retained");
        var blocked = Source("blocked");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(blocked)!, "unsigned"), "not a folder");
        var missing = Path.Combine(root, "missing.bin");
        var next = Source("after-error");
        results = service.MoveToSubfolder([blocked, missing, next], "unsigned", listed);
        Assert.IsTrue(results.Select(result => result.Outcome).SequenceEqual([FileMoveOutcome.Failed, FileMoveOutcome.SourceMissing, FileMoveOutcome.Moved])
            && File.Exists(blocked) && results[0].Detail?.Length > 0,
            "a blocked target and missing source are reported without stopping subsequent files");
        var reserved = Source("listed");
        listed.Add(Path.Combine(Path.GetDirectoryName(reserved)!, "unsigned", Path.GetFileName(reserved)));
        Assert.IsTrue(Move(reserved).Outcome == FileMoveOutcome.TargetListed && File.Exists(reserved),
            "a destination already represented by another result row is reserved even if missing on disk");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        results = service.MoveToSubfolder([first, blocked], "unsigned", listed, cancelled.Token);
        Assert.IsTrue(results.All(result => result.Outcome == FileMoveOutcome.Cancelled) && File.Exists(first) && File.Exists(blocked),
            "cancellation before a batch reports every unattempted file without moving it");
        using var midBatch = new CancellationTokenSource();
        var partialFirst = Source("partial-first");
        var partialSecond = Source("partial-second");
        results = service.MoveToSubfolder(new CancelAfterFirstList([partialFirst, partialSecond], midBatch), "unsigned", listed, midBatch.Token);
        Assert.IsTrue(results.Select(result => result.Outcome).SequenceEqual([FileMoveOutcome.Moved, FileMoveOutcome.Cancelled])
            && !File.Exists(partialFirst) && File.Exists(partialSecond),
            "cancellation between files retains the completed move and reports the remaining file");
        var invalid = new[] { "", " ", ".", "..", "../bad", "a/b", "a\\b", "123456789", "bad:", "CON", "nul.txt", "LPT1", "trail.", "trail " };
        Assert.IsTrue(invalid.All(name => !FileMoveService.IsValidFolderName(name)) && FileMoveService.IsValidFolderName("未签名"),
            "subfolders are short literal Windows names and support localized Unicode");
        try { service.MoveToSubfolder([first], "../bad", listed); Assert.Fail("Expected invalid folder rejection."); }
        catch (ArgumentException) { Assert.IsTrue(File.Exists(first), "invalid folder names are rejected before touching source files"); }

        var link = Path.Combine(root, "source-link.bin");
        var targetSource = Source("target-link");
        var linkedFolder = Path.Combine(Path.GetDirectoryName(targetSource)!, "unsigned");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        try
        {
            var created = false;
            try
            {
                File.CreateSymbolicLink(link, first);
                Directory.CreateSymbolicLink(linkedFolder, outside);
                created = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            { Console.WriteLine("SKIP move symlink fixtures: " + ex.Message); }
            if (created)
            {
                Assert.IsTrue(Move(link).Outcome == FileMoveOutcome.SourceLink && File.Exists(link) && File.Exists(first),
                    "moving a symbolic file link is skipped without touching its target");
                Assert.IsTrue(Move(targetSource).Outcome == FileMoveOutcome.TargetLink && File.Exists(targetSource) && !Directory.EnumerateFileSystemEntries(outside).Any(),
                    "a linked destination folder is not followed");
            }
        }
        finally
        {
            File.Delete(link);
            if (Directory.Exists(linkedFolder)) Directory.Delete(linkedFolder);
        }
        var hardLink = Path.Combine(root, "hard-link.bin");
        var hardLinkCreated = OperatingSystem.IsWindows()
            ? LinkTestNative.CreateHardLinkW(hardLink, first, IntPtr.Zero)
            : OperatingSystem.IsLinux() && LinkTestNative.Link(Encoding.UTF8.GetBytes(first + '\0'), Encoding.UTF8.GetBytes(hardLink + '\0')) == 0;
        if (hardLinkCreated)
        {
            var moved = Move(hardLink);
            Assert.IsTrue(moved.Outcome == FileMoveOutcome.Moved && File.Exists(first) && !File.Exists(hardLink)
                && File.ReadAllText(moved.DestinationPath!) == File.ReadAllText(first),
                "moving a hardlink renames that path while its other name remains available");
        }
        else Console.WriteLine("SKIP move hardlink fixture: " + Marshal.GetLastWin32Error());
    }

    private sealed class CancelAfterFirstList(string[] values, CancellationTokenSource cancellation) : IReadOnlyList<string>
    {
        public int Count => values.Length;
        public string this[int index] => values[index];
        public IEnumerator<string> GetEnumerator()
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (i == 1) cancellation.Cancel();
                yield return values[i];
            }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static class LinkTestNative
    {
#pragma warning disable SYSLIB1054 // Test harness has no unsafe source-generated interop configuration.
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateHardLinkW(string name, string existing, IntPtr securityAttributes);

        [DllImport("libc", EntryPoint = "link", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Link(byte[] existing, byte[] name); // NUL-terminated UTF-8 paths for libc.
#pragma warning restore SYSLIB1054
    }

    [TestMethod]
    public async Task CsvExportAsync()
    {
        var temp = _directory;

        var exporter = new ResultExporter([new CsvExportStrategy(), new JsonExportStrategy()]);
        var path = Path.Combine(temp, "results.csv");
        var table = new ResultExportTable([new("Name", "Name"), new("Notes", "Notes"), new("Hash", "Hash")],
            [["Prüfung 中文, sample.exe", "Line 1\r\n\"quoted\"; line 2", "+///"], ["second", "", "00FF"]]);
        await exporter.ExportAsync(path, table);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.IsTrue(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "CSV has a UTF-8 BOM for Unicode readers");
        Assert.AreEqual("\"Name\";\"Notes\";\"Hash\"\r\n\"Prüfung 中文, sample.exe\";\"Line 1\r\n\"\"quoted\"\"; line 2\";\"+///\"\r\n\"second\";\"\";\"00FF\"\r\n",
            await File.ReadAllTextAsync(path), "CSV uses semicolon by default and preserves embedded semicolons");
        await exporter.ExportAsync(path, table, new ResultExportOptions(CsvDelimiter: CsvDelimiter.Comma));
        Assert.AreEqual("\"Name\",\"Notes\",\"Hash\"\r\n\"Prüfung 中文, sample.exe\",\"Line 1\r\n\"\"quoted\"\"; line 2\",\"+///\"\r\n\"second\",\"\",\"00FF\"\r\n",
            await File.ReadAllTextAsync(path), "CSV quotes delimiters, embedded quotes/newlines and empty fields without altering hashes");

        var sentinel = "existing export must survive";
        await File.WriteAllTextAsync(path, sentinel);
        try { await exporter.ExportAsync(path, table, new ResultExportOptions(CsvDelimiter: (CsvDelimiter)999)); Assert.Fail("Expected invalid delimiter failure."); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("unsupported delimiters are rejected before writing"); }
        Assert.AreEqual(sentinel, await File.ReadAllTextAsync(path), "an invalid delimiter preserves an existing destination");
        try
        {
            await exporter.ExportAsync(path, new ResultExportTable([new("A", "A"), new("B", "B")], [["1", "2"], ["bad width"]]));
            Assert.Fail("Expected invalid row failure.");
        }
        catch (ArgumentException) { Console.WriteLine("CSV rejects inconsistent row lengths"); }
        Assert.AreEqual(sentinel, await File.ReadAllTextAsync(path), "a failed export preserves an existing destination");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var absent = Path.Combine(temp, "cancelled.csv");
            await ExpectCancellationAsync(() => exporter.ExportAsync(absent, table, cancellationToken: cancelled.Token));
            Assert.IsTrue(!File.Exists(absent), "pre-cancelled export creates no destination");
        }
        using (var cancelled = new CancellationTokenSource())
        {
            await ExpectCancellationAsync(() => exporter.ExportAsync(path,
                new ResultExportTable([new("Name", "Name")], new CancellingRows(cancelled)), cancellationToken: cancelled.Token));
            Assert.AreEqual(sentinel, await File.ReadAllTextAsync(path), "cancellation after writing begins preserves the existing destination");
        }
        try { await exporter.ExportAsync(path, new ResultExportTable([], [])); Assert.Fail("Expected empty columns failure."); }
        catch (ArgumentException) { Console.WriteLine("CSV requires at least one column"); }
        await exporter.ExportAsync(path, new ResultExportTable([new("Name", "Name")], [["replacement"]]));
        Assert.AreEqual("\"Name\"\r\n\"replacement\"\r\n", await File.ReadAllTextAsync(path), "a successful export replaces the destination completely");
        Assert.IsTrue(!Directory.EnumerateFiles(temp, ".filescope-export-*.tmp").Any(), "successful, failed and cancelled exports clean up temporary files");

        // Parse with a separate BCL CSV reader, not with the writer's escaping logic.
        var many = new ResultExportTable([new("Name", "Name"), new("Comment", "Comment"), new("Hash", "Hash")], Enumerable.Range(0, 1500)
            .Select(index => (IReadOnlyList<string>)new[]
            {
                "Prüfung 中文; file," + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                " Line 1\r\n\"quoted\"; line 2, ", "0000ABCDEF+/=="
            }).ToArray());
        foreach (var delimiter in Enum.GetValues<CsvDelimiter>())
        {
            await exporter.ExportAsync(path, many, new ResultExportOptions(CsvDelimiter: delimiter));
            using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(path, Encoding.UTF8)
            { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
            parser.SetDelimiters(delimiter == CsvDelimiter.Semicolon ? ";" : ",");
            Assert.IsTrue(parser.ReadFields()!.SequenceEqual(many.Columns.Select(column => column.Header)), "CSV header imports correctly / " + delimiter);
            var imported = 0;
            var matches = true;
            while (!parser.EndOfData)
            {
                var row = parser.ReadFields();
                matches &= imported < many.Rows.Count && row is not null && row.SequenceEqual(many.Rows[imported]);
                imported++;
            }
            Assert.IsTrue(matches && imported == 1500,
                "all 1500 rows round-trip with delimiters, quotes, CRLF, Unicode, spaces and leading zeros / " + delimiter);
        }
    }

    [TestMethod]
    public async Task JsonExportAsync()
    {
        var temp = _directory;

        var exporter = new ResultExporter([new CsvExportStrategy(), new JsonExportStrategy()]);
        var options = new ResultExportOptions(ResultExportFormat.Json);
        var path = Path.Combine(temp, "results.JSON");
        var table = new ResultExportTable([new("Name", "Dateiname"), new("SHA256", "Wert"), new("Notes", "Wert")],
            Enumerable.Range(0, 1500).Select(index => (IReadOnlyList<string>)new[]
            {
                "Prüfung 中文 😀; file," + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "0000ABCDEF+/==", index == 0 ? "" : "Line 1\r\n\"quoted\"\t\\path\u0001"
            }).ToArray());
        await exporter.ExportAsync(path, table, options);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.IsTrue(bytes[0] == (byte)'[', "JSON is UTF-8 without BOM");
        using (var parsed = System.Text.Json.JsonDocument.Parse(bytes))
        {
            var rows = parsed.RootElement;
            Assert.IsTrue(rows.GetArrayLength() == 1500, "JSON contains all 1500 exported rows");
            Assert.IsTrue(rows.EnumerateArray().Select((row, index) =>
                row.EnumerateObject().Select(property => property.Name).SequenceEqual(table.Columns.Select(column => column.Id))
                && row.EnumerateObject().Select(property => property.Value.GetString()).SequenceEqual(table.Rows[index])).All(match => match),
                "JSON round-trips selected IDs/order, Unicode, quotes, controls, leading zeros and empty strings");
            Assert.IsTrue(rows[0].GetProperty("SHA256").ValueKind == System.Text.Json.JsonValueKind.String,
                "JSON retains display values as strings instead of coercing hashes and versions");
        }
        var sentinel = "existing export must survive";
        await File.WriteAllTextAsync(path, sentinel);
        try
        {
            await exporter.ExportAsync(path, new ResultExportTable([new("Name", "Name")], [["good"], []]), options);
            Assert.Fail("Expected invalid row failure.");
        }
        catch (ArgumentException) { Console.WriteLine("JSON rejects inconsistent row widths"); }
        Assert.AreEqual(sentinel, await File.ReadAllTextAsync(path), "failed JSON export preserves the destination");
        foreach (var precancel in new[] { false, true })
        {
            using var cancelled = new CancellationTokenSource();
            if (precancel) cancelled.Cancel();
            await ExpectCancellationAsync(() => exporter.ExportAsync(path,
                new ResultExportTable([new("Name", "Name")], new CancellingRows(cancelled)), options, cancelled.Token));
            Assert.AreEqual(sentinel, await File.ReadAllTextAsync(path), "cancelled JSON preserves the destination / " + precancel);
        }
        foreach (var columns in new ResultExportColumn[][]
        {
            [], [new("duplicate", "A"), new("duplicate", "B")], [new("", "A")]
        })
        {
            try
            {
                await exporter.ExportAsync(path, new ResultExportTable(columns, []), options);
                Assert.Fail("Expected invalid columns failure.");
            }
            catch (ArgumentException) { Console.WriteLine("JSON rejects missing or duplicate column IDs"); }
        }
        try
        {
            await exporter.ExportAsync(path, table, new ResultExportOptions((ResultExportFormat)999));
            Assert.Fail("Expected unsupported format failure.");
        }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("unsupported export strategies are rejected"); }
        Assert.AreEqual(sentinel, await File.ReadAllTextAsync(path), "invalid export options preserve an existing destination");
        await exporter.ExportAsync(path, new ResultExportTable([new("Name", "Name")], []), options);
        using (var parsed = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path)))
            Assert.IsTrue(parsed.RootElement.GetArrayLength() == 0, "empty JSON data produces a valid empty array");
        Assert.IsTrue(!Directory.EnumerateFiles(temp, ".filescope-export-*.tmp").Any(), "JSON failure/cancellation/success leaves no temporary files");
        Assert.IsTrue(ResultExportFormats.TryFromPath("folder.csv/result.JsOn", out var json) && json == ResultExportFormat.Json
            && ResultExportFormats.TryFromPath("result.json.CSV", out var csv) && csv == ResultExportFormat.Csv,
            "format follows the final extension, case-insensitively");
        Assert.IsTrue(!ResultExportFormats.TryFromPath("result", out _) && !ResultExportFormats.TryFromPath("result.txt", out _),
            "unsupported or missing extensions cannot silently select a format");
    }

    private sealed class CancellingRows(CancellationTokenSource cancellation) : IReadOnlyList<IReadOnlyList<string>>
    {
        public int Count => 2;
        public IReadOnlyList<string> this[int index] => new[] { index.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        public IEnumerator<IReadOnlyList<string>> GetEnumerator()
        {
            yield return this[0];
            cancellation.Cancel();
            yield return this[1];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [TestMethod]
    public void PeHeaders()
    {
        var inspector = new PeHeaderInspector();
        foreach (var (code, name, plus) in new (ushort, string, bool)[]
        {
            (0x014c, "i386", false), (0x0184, "Alpha", false), (0x0284, "Alpha64", true),
            (0x01c0, "ARM", false), (0x01c4, "ARMNT (Thumb-2)", false),
            (0x8664, "AMD64 (x64)", true), (0xaa64, "ARM64", true),
            (0xa641, "ARM64EC", true), (0xa64e, "ARM64X", true), (0xffff, "0xFFFF", false)
        })
        {
            using var stream = new MemoryStream(PeFixture(code, plus));
            stream.Position = 17;
            var result = inspector.Inspect(stream);
            Assert.IsTrue(result is { Diagnostic: null, Header: not null } && result.Header.MachineCode == code
                && result.Header.MachineName == name && result.Header.Format == (plus ? "PE32+" : "PE32")
                && result.Header.ManagedPlatform == ManagedPlatform.None, "native PE machine and format / " + name);
            Assert.IsTrue(stream.CanRead && stream.Position == 17, "PE inspection preserves stream ownership and position / " + name);
        }
        foreach (var (flags, dll, nativeHeader, expected) in new (uint, bool, bool, ManagedPlatform)[]
        {
            (1, false, false, ManagedPlatform.AnyCpu), (1, true, false, ManagedPlatform.AnyCpu),
            (3, false, false, ManagedPlatform.MachineSpecific),
            (0x20001, false, false, ManagedPlatform.Prefer32Bit),
            (0x20003, false, false, ManagedPlatform.Prefer32Bit),
            (0x20001, true, false, ManagedPlatform.MachineSpecific),
            (0, false, false, ManagedPlatform.MachineSpecific),
            (17, false, false, ManagedPlatform.MachineSpecific),
            (1, false, true, ManagedPlatform.MachineSpecific)
        })
        {
            using var stream = new MemoryStream(PeFixture(0x014c, false, flags, dll, nativeHeader));
            var result = inspector.Inspect(stream);
            Assert.IsTrue(result.Diagnostic is null && result.Header?.ManagedPlatform == expected,
                $"CLR flags / {flags:X} / DLL={dll} / native header={nativeHeader}: {result.Diagnostic}");
        }
        using (var stream = new MemoryStream(PeFixture(0x8664, true, 1)))
            Assert.AreEqual(ManagedPlatform.MachineSpecific, inspector.Inspect(stream).Header!.ManagedPlatform, "IL-only x64 stays machine-specific");

        // Verify forwards its target platform to project references, so this DLL
        // is not always AnyCPU. Ask the runtime for the built module's metadata;
        // the process architecture alone cannot distinguish AnyCPU from x64.
        var coreAssembly = typeof(FileAnalysisService).Assembly;
        coreAssembly.ManifestModule.GetPEKind(out var peKind, out var machine);
        var expectedPlatform = machine == ImageFileMachine.I386 && peKind == PortableExecutableKinds.ILOnly
            ? ManagedPlatform.AnyCpu : ManagedPlatform.MachineSpecific;

        using (var stream = File.OpenRead(coreAssembly.Location))
        {
            var result = inspector.Inspect(stream);
            Assert.IsTrue(result is { Diagnostic: null, Header: { } header }
                && header.MachineCode == (ushort)machine && header.ManagedPlatform == expectedPlatform,
                $"real SDK-built library agrees with runtime PE metadata / {machine} / {peKind} / {expectedPlatform}");
        }

        var dosOnly = new byte[128];
        dosOnly[0] = (byte)'M'; dosOnly[1] = (byte)'Z';
        var le = PeFixture(0x014c, false); le[128] = (byte)'L';
        var hugeOffset = (byte[])dosOnly.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(hugeOffset.AsSpan(60), uint.MaxValue);
        foreach (var bytes in new byte[][] { [], "ordinary text"u8.ToArray(), new byte[512], dosOnly, le, hugeOffset })
        {
            using var stream = new MemoryStream(bytes);
            Assert.IsTrue(inspector.Inspect(stream) is { Header: null, Diagnostic: null }, "unsupported header input stays empty without error");
        }
        foreach (var bytes in new[] { PeFixture(0x014c, false)[..140], PeFixture(0x014c, false)[..200] })
        {
            using var stream = new MemoryStream(bytes);
            stream.Position = 7;
            Assert.IsTrue(inspector.Inspect(stream) is { Header: null, Diagnostic: not null } && stream.Position == 7,
                "truncated PE reports a diagnostic and restores the stream");
        }
        using (var stream = new PeCountingStream(new byte[2 * 1024 * 1024]))
        {
            stream.Write(PeFixture(0x014c, false));
            stream.Position = 35;
            Assert.IsTrue(inspector.Inspect(stream).Header?.MachineCode == 0x014c && stream.BytesRead < 64 * 1024,
                "PE inspection reads headers without scanning the file payload");
        }
        using (var cancelled = new CancellationTokenSource())
        using (var stream = new PeCountingStream(PeFixture(0x014c, false)))
        {
            cancelled.Cancel();
            try { inspector.Inspect(stream, cancelled.Token); Assert.Fail("Expected PE cancellation."); }
            catch (OperationCanceledException) { }
            Assert.AreEqual(0L, stream.BytesRead, "cancelled PE inspection performs no reads");
        }
    }

    [TestMethod]
    public void NeHeaders()
    {
        var inspector = new PeHeaderInspector();
        foreach (var dll in new[] { false, true })
        {
            using var stream = new PeCountingStream(NeFixture(dll));
            stream.Position = 17;
            var result = inspector.Inspect(stream);
            Assert.IsTrue(result is { Diagnostic: null, Header: { MachineCode: null, MachineName: "x86 (16-bit)", Format: "NE (Win16)", ManagedPlatform: ManagedPlatform.None } },
                "Win16 NE header is recognized without a fabricated PE machine code / DLL=" + dll);
            Assert.IsTrue(stream.CanRead && stream.Position == 17 && stream.BytesRead <= 128,
                "NE detection reuses the stream, restores its position and reads only fixed headers / DLL=" + dll);
        }
        foreach (var (target, format, machine) in new (byte, string, string)[]
        {
            (1, "NE (OS/2)", "x86 (16-bit)"), (3, "NE (DOS 4.x)", "x86 (16-bit)"),
            (4, "NE (Windows/386)", "x86"), (255, "NE (target 0xFF)", "x86")
        })
        {
            using var stream = new MemoryStream(NeFixture(false, target));
            var result = inspector.Inspect(stream);
            Assert.IsTrue(result.Header is { } header && header.Format == format && header.MachineName == machine,
                "NE target is preserved without assuming every NE image is Win16 / " + target);
        }
        var shortOs2 = NeFixture(true, 1)[..188];
        BinaryPrimitives.WriteUInt16LittleEndian(shortOs2.AsSpan(128 + 6), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(shortOs2.AsSpan(128 + 0x1c), 0);
        using (var stream = new MemoryStream(shortOs2))
            Assert.IsTrue(inspector.Inspect(stream).Header?.Format == "NE (OS/2)", "original 60-byte OS/2 NE header is accepted");
        var hugeSegments = NeFixture(false);
        BinaryPrimitives.WriteUInt16LittleEndian(hugeSegments.AsSpan(128 + 0x1c), ushort.MaxValue);
        var overlap = NeFixture(false);
        BinaryPrimitives.WriteUInt16LittleEndian(overlap.AsSpan(128 + 0x22), 32);
        var outsideEntry = NeFixture(false);
        BinaryPrimitives.WriteUInt16LittleEndian(outsideEntry.AsSpan(128 + 4), ushort.MaxValue);
        foreach (var bytes in new[] { NeFixture(false)[..130], NeFixture(false)[..188], hugeSegments, overlap, outsideEntry })
        {
            using var stream = new MemoryStream(bytes);
            stream.Position = 7;
            Assert.IsTrue(inspector.Inspect(stream) is { Header: null, Diagnostic: not null } && stream.Position == 7,
                "truncated NE header or invalid table bounds report a diagnostic and restore the stream");
        }
        using var cancellation = new CancellationTokenSource();
        using var cancelled = new PeCountingStream(NeFixture(false));
        cancellation.Cancel();
        try { inspector.Inspect(cancelled, cancellation.Token); Assert.Fail("Expected NE cancellation."); }
        catch (OperationCanceledException) { Assert.IsTrue(cancelled.BytesRead == 0, "cancelled NE inspection reads nothing"); }
    }

    // A small Win16 NE image: one code segment, entry table and DOS stub.
    // Offsets follow the NE specification, independently of the production reader.
    private static byte[] NeFixture(bool dll, byte target = 2)
    {
        var bytes = new byte[1024];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        U16(2, 128); U16(4, 1); U16(8, 4); U16(24, 64); U32(60, 128);
        bytes[128] = (byte)'N'; bytes[129] = (byte)'E'; bytes[130] = 5;
        U16(128 + 4, 72); U16(128 + 6, 2);
        U16(128 + 0x0c, (ushort)(dll ? 0x8000 : 0));
        U16(128 + 0x16, 1); U16(128 + 0x1c, 1);
        U16(128 + 0x22, 64); U16(128 + 0x32, 4); bytes[128 + 0x36] = target;
        U16(128 + 0x3e, 0x030a); // Windows 3.10
        U16(192, 32); U16(194, 16); U16(196, 0); U16(198, 16);
        bytes[512] = 0xcb; // RETF; inspected as bytes, never executed.
        return bytes;
    }

    // A minimal PE image with one .text section and an optional CLR header.
    // Fields are encoded independently of the framework parser used by production code.
    private static byte[] PeFixture(ushort machine, bool plus, uint? corFlags = null, bool dll = false, bool nativeHeader = false)
    {
        var bytes = new byte[1024];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; U32(60, 128);
        bytes[128] = (byte)'P'; bytes[129] = (byte)'E';
        U16(132, machine); U16(134, 1);
        var optionalSize = plus ? 240 : 224;
        U16(148, (ushort)optionalSize); U16(150, (ushort)(2 | (dll ? 0x2000 : 0)));
        const int optional = 152;
        U16(optional, (ushort)(plus ? 0x20b : 0x10b));
        U32(optional + 32, 0x2000); U32(optional + 36, 0x200);
        U32(optional + 56, 0x4000); U32(optional + 60, 0x200); U16(optional + 68, 2);
        U32(optional + (plus ? 108 : 92), 16);
        var section = optional + optionalSize;
        Encoding.ASCII.GetBytes(".text").CopyTo(bytes, section);
        U32(section + 8, 512); U32(section + 12, 0x2000);
        U32(section + 16, 512); U32(section + 20, 512); U32(section + 36, 0x60000020);
        if (corFlags.HasValue)
        {
            var corDirectory = optional + (plus ? 112 : 96) + 14 * 8;
            U32(corDirectory, 0x2000); U32(corDirectory + 4, 72);
            U32(512, 72); U16(516, 2); U16(518, 5); U32(528, corFlags.Value);
            U32(520, 0x2100); U32(524, 24); // Bounded metadata directory.
            U32(768, 0x424A5342); U16(772, 1); U16(774, 1); U32(780, 4);
            Encoding.ASCII.GetBytes("v4\0\0").CopyTo(bytes, 784);
            if (nativeHeader) { U32(576, 0x2180); U32(580, 4); }
        }
        return bytes;
    }

    [TestMethod]
    public void FileProperties()
    {
        var plainFile = _plainFile;

        var native = typeof(FilePropertiesInspector).GetNestedType("Native", BindingFlags.NonPublic)!;
        var shellInfo = native.GetNestedType("ShellFileInfo", BindingFlags.NonPublic)!;
        Assert.AreEqual(IntPtr.Size == 8 ? 696 : 692, Marshal.SizeOf(shellInfo), "SHFILEINFOW ABI size");
        var inspector = new FilePropertiesInspector();
        using var plainStream = File.OpenRead(plainFile);
        var plain = inspector.Inspect(plainStream, plainFile);
        Assert.IsTrue(plain is { Diagnostic: null, Info: not null } && string.IsNullOrEmpty(plain.Info.FileVersion)
            && string.IsNullOrEmpty(plain.Info.ProductName), "ordinary file has no fabricated version information");
        var assemblyPath = typeof(FileAnalysisService).Assembly.Location;
        using var assemblyStream = File.OpenRead(assemblyPath);
        assemblyStream.Position = 7;
        var assembly = inspector.Inspect(assemblyStream, assemblyPath);
        Assert.IsTrue(assembly is { Diagnostic: null, Info: not null } && !string.IsNullOrWhiteSpace(assembly.Info.FileVersion)
            && assembly.Info.OriginalFilename == "FileScope.Core.dll", "read VERSIONINFO from the built core assembly");
        Assert.IsTrue(!string.IsNullOrWhiteSpace(assembly.Info?.FileType), "file type is available independently of version fields");
        Assert.IsTrue(assemblyStream.CanRead && assemblyStream.Position == 7, "properties leave the supplied stream open at its original position");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { inspector.Inspect(plainStream, plainFile, cancelled.Token); Assert.Fail("Expected properties cancellation."); }
        catch (OperationCanceledException) { Console.WriteLine("cancelled properties inspection propagates cancellation"); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void CheckVersionHandles(IEnumerable<string> paths)
    {
        var directory = _directory;

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var reference = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            var bytes = File.ReadAllBytes(path);
            foreach (var position in new long[] { 0, 7, bytes.Length })
            foreach (var buffered in new[] { false, true })
            {
                if (buffered && position >= bytes.Length) continue;
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    128 * 1024, FileOptions.SequentialScan);
                var handle = file.SafeFileHandle;
                file.Position = position;
                var context = $"{Path.GetFileName(path)}, position {position}, buffered {buffered}";
                if (buffered)
                {
                    Assert.AreEqual(bytes[(int)position], file.ReadByte(), "prime version-test read buffer / " + context);
                    file.Position = position;
                }
                // Deliberately diverge the raw pointer. The inspector's SafeFileHandle getter
                // synchronizes it to FileStream.Position before the version API is called.
                var nativeBeforeBorrow = position == 7 ? Math.Min(19, bytes.Length) : position;
                Assert.IsTrue(VersionTestNative.SetFilePointerEx(handle, nativeBeforeBorrow, out _, 0),
                    "set native version-test position / " + context);
                var missingPath = Path.Combine(directory, "does-not-exist" + Path.GetExtension(path));
                var actual = new FilePropertiesInspector().Inspect(file, missingPath);
                Assert.IsTrue(actual.Diagnostic is null && actual.Info is not null,
                    "version metadata uses the supplied handle without reopening the supplied path");
                var expected = new FilePropertiesInfo
                {
                    FileType = actual.Info?.FileType, FileDescription = reference.FileDescription, Language = reference.Language,
                    FileVersion = reference.FileVersion, ProductName = reference.ProductName, ProductVersion = reference.ProductVersion,
                    CompanyName = reference.CompanyName, OriginalFilename = reference.OriginalFilename, InternalName = reference.InternalName,
                    Copyright = reference.LegalCopyright, Trademarks = reference.LegalTrademarks, Comments = reference.Comments
                };
                Assert.IsTrue(actual.Info == expected, "handle version fields match FileVersionInfo / " + context);
                // Query the cached handle: accessing file.SafeFileHandle here would normalize
                // the pointer again and could hide a broken restoration in the inspector.
                Assert.IsTrue(VersionTestNative.SetFilePointerEx(handle, 0, out var after, 1),
                    "query native version-test position / " + context);
                Assert.AreEqual(position, after, "version inspection restores the synchronized native file pointer / " + context);
                Assert.IsTrue(file.CanRead && file.Position == position,
                    "version inspection preserves the managed stream position and ownership / " + context);
                Assert.AreEqual(position < bytes.Length ? bytes[(int)position] : -1, file.ReadByte(),
                    "the next managed read still reads from the original position / " + context);
            }
        }
        }

    private static byte[] NeVersionFixture()
    {
        // Synthetic, self-contained ANSI Win16 resources; no third-party executable bytes.
        static byte[] Block(string key, byte[] value, params byte[][] children)
        {
            using var buffer = new MemoryStream();
            using var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true);
            writer.Write((ushort)0); writer.Write(checked((ushort)value.Length));
            writer.Write(Encoding.ASCII.GetBytes(key)); writer.Write((byte)0);
            void Align() { while (buffer.Length % 4 != 0) writer.Write((byte)0); }
            Align(); writer.Write(value); Align();
            foreach (var child in children) { writer.Write(child); Align(); }
            var bytes = buffer.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)bytes.Length));
            return bytes;
        }
        var fixedInfo = new byte[52];
        uint[] words = [0xfeef04bd, 0x00010000, 0x00010002, 0x00030004, 0x00010002, 0x00030004,
            0x3f, 0, 0x00010001, 1, 0, 0, 0];
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(fixedInfo.AsSpan(i * 4), words[i]);
        var table = Block("040904E4", [], Block("FileVersion", "1.2.3.4\0"u8.ToArray()),
            Block("ProductVersion", "1.2.3.4\0"u8.ToArray()), Block("ProductName", "FileScope test\0"u8.ToArray()));
        var resource = Block("VS_VERSION_INFO", fixedInfo, Block("StringFileInfo", [], table),
            Block("VarFileInfo", [], Block("Translation", [0x09, 0x04, 0xe4, 0x04])));
        var image = NeFixture(false);
        Array.Resize(ref image, 2048);
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(offset), value);
        U16(128 + 0x24, 74); U16(128 + 0x26, 98); U16(128 + 0x34, 1);
        const int resources = 128 + 74;
        U16(resources, 0); U16(resources + 2, 0x8010); U16(resources + 4, 1);
        U16(resources + 10, 1024); U16(resources + 12, checked((ushort)resource.Length));
        U16(resources + 14, 0x0030); U16(resources + 16, 0x8001);
        resource.CopyTo(image, 1024);
        return image;
    }

    private static class VersionTestNative
    {
#pragma warning disable SYSLIB1054 // Test harness has no unsafe source-generated interop configuration.
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetFilePointerEx(Microsoft.Win32.SafeHandles.SafeFileHandle file,
            long distance, out long position, uint method);
#pragma warning restore SYSLIB1054
    }

    [TestMethod]
    public async Task MetadataPipelineAsync()
    {
        var path = _plainFile;
        var checksums = new StreamChecksumService(new ChecksumStrategyFactory());

        foreach (var flags in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var metadata = new StubMetadata();
            var signatures = new StubSignatures();
            var analyzer = new FileAnalysisService(checksums, signatures, metadata, metadata);
            var progress = new RecordedAnalysisProgress();
            var result = await analyzer.AnalyzeAsync(path, new AnalysisOptions([ChecksumAlgorithm.SHA256], true,
                inspectFileProperties: flags.Item1, inspectPeHeader: flags.Item2), progress);
            var expectedStages = new List<AnalysisStage> { AnalysisStage.OpeningFile, AnalysisStage.Reading };
            if (flags.Item1) expectedStages.Add(AnalysisStage.ReadingMetadata);
            if (flags.Item2) expectedStages.Add(AnalysisStage.ReadingPeHeader);
            expectedStages.Add(AnalysisStage.CheckingSignature);
            Assert.IsTrue(progress.Values.Select(value => value.Stage).Distinct().SequenceEqual(expectedStages),
                "progress reports each enabled inspection separately and in execution order / " + flags);
            Assert.IsTrue(result.Succeeded && metadata.PropertyCalls == (flags.Item1 ? 1 : 0)
                && metadata.PeCalls == (flags.Item2 ? 1 : 0) && (result.Properties is not null) == flags.Item1
                && (result.PeHeader is not null) == flags.Item2, "metadata options independently control inspection and results / " + flags);
            Assert.IsTrue(signatures.SawReadableHandle && result.Checksums.Count == 1,
                "metadata inspection retains hashes and resets the stream for signature verification / " + flags);
            Assert.IsTrue(!flags.Item1 || ReferenceEquals(metadata.File, signatures.File),
                "properties and signature strategies receive exactly the same FileStream / " + flags);
        }
        var throwing = new StubMetadata { ThrowProperties = true, ThrowPe = true };
        var service = new FileAnalysisService(checksums, new StubSignatures(), throwing, throwing);
        var partial = await service.AnalyzeAsync(path, new AnalysisOptions([ChecksumAlgorithm.SHA256], false,
            inspectFileProperties: true, inspectPeHeader: true));
        Assert.IsTrue(partial.Succeeded && partial.Properties?.Diagnostic == "Properties unavailable"
            && partial.PeHeader?.Diagnostic == "PE unreadable" && throwing.PeCalls == 1 && partial.Checksums.Count == 1,
            "optional metadata failures preserve hashes and allow the other inspector to run");
        using (var cancelled = new CancellationTokenSource())
        {
            var metadata = new StubMetadata { Cancel = cancelled };
            service = new FileAnalysisService(checksums, new StubSignatures(), metadata, metadata);
            await ExpectCancellationAsync(() => service.AnalyzeAsync(path, new AnalysisOptions([], false,
                inspectFileProperties: true, inspectPeHeader: true), cancellationToken: cancelled.Token));
            Assert.AreEqual(0, metadata.PeCalls, "cancellation during properties does not become a diagnostic or run the next inspector");
        }
        // Content, not an extension, identifies the PE header.
        await File.WriteAllBytesAsync(path, PeFixture(0x0184, false));
        var real = await new FileAnalysisService(checksums, new StubSignatures()).AnalyzeAsync(path,
            new AnalysisOptions([], false, inspectPeHeader: true));
        Assert.IsTrue(real.PeHeader?.Header?.MachineName == "Alpha" && real.Properties is null,
            "real pipeline reads PE from a .bin file with properties disabled");
        foreach (var dll in new[] { false, true })
        {
            var image = NeFixture(dll);
            var legacyPath = path + (dll ? ".dll" : ".exe");
            await File.WriteAllBytesAsync(legacyPath, image);
            var legacy = await new FileAnalysisService(checksums, new StubSignatures()).AnalyzeAsync(legacyPath,
                new AnalysisOptions([ChecksumAlgorithm.SHA256], false, inspectPeHeader: true));
            Assert.IsTrue(legacy.PeHeader is { Diagnostic: null, Header.Format: "NE (Win16)" }
                && legacy.Checksums[ChecksumAlgorithm.SHA256] == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image)),
                "real pipeline hashes and recognizes Win16 EXE/DLL without execution / DLL=" + dll);
        }
        await File.WriteAllTextAsync(path, "abc", new UTF8Encoding(false));
    }

    private sealed class RecordedAnalysisProgress : IProgress<AnalysisProgress>
    {
        public List<AnalysisProgress> Values { get; } = [];
        public void Report(AnalysisProgress value) => Values.Add(value);
    }

    private sealed class StubMetadata : IFilePropertiesInspector, IPeHeaderInspector
    {
        public int PropertyCalls, PeCalls;
        public bool ThrowProperties, ThrowPe;
        public CancellationTokenSource? Cancel;
        public FileStream? File;
        public FilePropertiesResult Inspect(FileStream file, string path, CancellationToken cancellationToken = default)
        {
            File = file;
            PropertyCalls++;
            Cancel?.Cancel(); cancellationToken.ThrowIfCancellationRequested();
            if (ThrowProperties) throw new IOException("Properties unavailable");
            return new(new FilePropertiesInfo { ProductName = "Example product" });
        }
        public PeHeaderResult Inspect(Stream stream, CancellationToken cancellationToken = default)
        {
            PeCalls++;
            if (ThrowPe) throw new BadImageFormatException("PE unreadable");
            Assert.IsTrue(stream.Position == 0 && stream.CanRead, "PE strategy receives the original readable stream at offset zero");
            stream.Position = stream.Length;
            return new(new PeHeaderInfo(0x014c, "i386", "PE32", ManagedPlatform.AnyCpu));
        }
    }

    private sealed class PeCountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long BytesRead;
        public override int Read(byte[] buffer, int offset, int count)
        { var read = base.Read(buffer, offset, count); BytesRead += read; return read; }
        public override int Read(Span<byte> buffer)
        {
            // MemoryStream subclasses may route this overload through Read(byte[],...).
            // Count that read once, including when the base implementation delegates.
            var before = BytesRead;
            var read = base.Read(buffer);
            BytesRead = before + read;
            return read;
        }
    }

    [TestMethod]
    public void Timestamps()
    {
        var time = new DateTimeOffset(2024, 2, 3, 12, 34, 56, TimeSpan.Zero);
        var legacy = LegacyTimestamp(time);
        var rfc = RfcTimestamp(time.AddMilliseconds(123));
        Assert.AreEqual<DateTimeOffset?>(time, AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.CounterSignatureOid, legacy),
            "legacy countersignature signing-time decoded");
        Assert.AreEqual<DateTimeOffset?>(time.AddMilliseconds(123), AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.Rfc3161Oid, rfc),
            "RFC 3161 genTime decoded with fractional seconds");
        Assert.IsTrue(AuthenticodeTimestampReader.Read("1.2.840.113549.1.9.5", legacy) is null,
            "self-declared signing time is not accepted as a countersignature");
        foreach (var oid in new[] { AuthenticodeTimestampReader.CounterSignatureOid, AuthenticodeTimestampReader.Rfc3161Oid })
        {
            Assert.IsTrue(AuthenticodeTimestampReader.Read(oid, new byte[] { 0x30, 0x82, 0xff }) is null,
                "truncated timestamp is handled / " + oid);
            Assert.IsTrue(AuthenticodeTimestampReader.Read(oid, Array.Empty<byte>()) is null,
                "empty timestamp is handled / " + oid);
        }
        Assert.IsTrue(AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.CounterSignatureOid,
            LegacyTimestamp(time, duplicate: true)) is null, "ambiguous duplicate signing times are rejected");
        Assert.IsTrue(AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.CounterSignatureOid,
            LegacyTimestamp(time, includeTime: false)) is null, "countersignature without signing-time has no invented date");
        Assert.IsTrue(AuthenticodeTimestampReader.Read(AuthenticodeTimestampReader.Rfc3161Oid, rfc.AsMemory(0, rfc.Length - 10)) is null,
            "truncated RFC 3161 container is rejected");
        var confirmed = AuthenticodeTimestampReader.Select([time.AddDays(-1), time], true, time, true);
        Assert.IsTrue(confirmed.Time == time && confirmed.Status == TimestampStatus.Verified,
            "only the timestamp used by a successful Windows check is confirmed");
        Assert.AreEqual(TimestampStatus.Unverified, AuthenticodeTimestampReader.Select([time], true, time, false).Status,
            "decoded time alone is not proof of timestamp trust");
        Assert.AreEqual(TimestampStatus.Unverified, AuthenticodeTimestampReader.Select([time], true, time.AddDays(1), true).Status,
            "Windows verification time cannot replace a different embedded timestamp");
        Assert.AreEqual(TimestampStatus.NotPresent, AuthenticodeTimestampReader.Select([], false, DateTimeOffset.UtcNow, true).Status,
            "current verification time never becomes a missing signing date");
        Assert.AreEqual(TimestampStatus.Unreadable, AuthenticodeTimestampReader.Select([], true, time, true).Status,
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

    private static async Task ExpectCancellationAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { Console.WriteLine("operation cancels"); return; }
        Assert.Fail("Expected OperationCanceledException.");
    }

    private sealed class StubSignatures : ISignatureInspector
    {
        public int Calls { get; private set; }
        public bool SawReadableHandle { get; private set; }
        public FileStream? File { get; private set; }
        public SignatureResult Inspect(FileStream file, string fullPath, RevocationMode mode, CancellationToken cancellationToken = default)
        {
            File = file;
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
