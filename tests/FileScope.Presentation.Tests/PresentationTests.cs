using FileScope.App.Presentation;
using FileScope.App.Services;
using FileScope.App.ViewModels;
using FileScope.Core.Models;
using FileScope.Core.Export;
using FileScope.Core.Signatures;
using FileScope.Core.Files;

namespace FileScope.Presentation.Tests;

[TestClass]
public sealed class PresentationTests
{
    [AssemblyInitialize]
    public static void InitializeResources(TestContext _) =>
        LocalizationService.Instance.UseApplicationResources(typeof(PresentationTests).Assembly, "FileScope.App.Resources.Strings");

    [TestInitialize]
    public void Initialize()
    {
        var strings = LocalizationService.Instance;
        strings.SetCulture("en");

        // MSTest can use a fresh execution context even when the singleton's
        // selected language has not changed since the previous test.
        System.Globalization.CultureInfo.CurrentCulture = strings.Culture;
        System.Globalization.CultureInfo.CurrentUICulture = strings.Culture;
    }

    [TestCleanup]
    public void Cleanup() => LocalizationService.Instance.SetCulture("en");

    [TestMethod]
    [TestCategory("Shared")]
    public void BothAppsOfferOneExactFilterPerDisplayedCode()
    {
        var strings = LocalizationService.Instance;
        var options = SignatureFilter.Options(strings);
        var exact = options.Where(option => option.Value.Group.HasValue).ToArray();
        Assert.HasCount(16, options);
        Assert.AreEqual("0123456789AB", string.Concat(exact.Select(option => option.Value.Group!.Value.Code)));

        foreach (var language in strings.Languages)
        {
            strings.SetCulture(language.CultureName);
            foreach (var status in Enum.GetValues<SignatureStatus>())
            foreach (var mode in Enum.GetValues<RevocationMode>())
            {
                var group = SignatureGroup.From(status, mode)!.Value;
                var option = exact.Single(option => option.Value.Matches(status, mode));
                Assert.AreEqual(group.Format(strings), option.Label);
                Assert.IsTrue(SignatureFilter.All.Matches(status, mode));
                Assert.AreEqual(status == SignatureStatus.Valid, SignatureFilter.Valid.Matches(status, mode));
                Assert.AreEqual(status != SignatureStatus.Valid, SignatureFilter.ExceptValid.Matches(status, mode));
                Assert.AreEqual(group.Code is not ('0' or '1' or '2' or '3'), SignatureFilter.Attention.Matches(status, mode));
            }
        }
    }

    [TestMethod]
    public void CatalogDetailsDoNotChangeTheEmbeddedFilterOrMoveGroup()
    {
        var strings = LocalizationService.Instance;
        var row = new FileRowViewModel("member.bin", strings);
        var embedded = new SignatureResult(SignatureStatus.NoEmbeddedSignature, RevocationMode.OfflineCache);
        var valid = new SignatureResult(SignatureStatus.Valid, RevocationMode.Online, NativeStatusCode: 0);
        var catalog = new CatalogMatch("pack.cat", "MEMBER", false, valid);
        var lookup = new CatalogLookup([catalog], [], false) { IndexIssues = ["missing.cat"] };
        row.Complete(new(row.FullPath, 42, null, new Dictionary<ChecksumAlgorithm, string>(), embedded) { Catalogs = lookup });
        var field = ColumnOption.CreateDefaults(strings).Single(option => option.Id == "Catalog");

        foreach (var language in strings.Languages)
        {
            strings.SetCulture(language.CultureName);
            Assert.AreEqual(SignatureStatus.NoEmbeddedSignature, SignatureMoveSelection.GetStatus([row]));
            Assert.IsFalse(SignatureFilter.Valid.Matches(row.SignatureState, row.SignatureMode));
            StringAssert.StartsWith(row.SignatureText, "3 | ");
            StringAssert.StartsWith(row.CatalogText, "1 | ");
            StringAssert.Contains(row.CatalogText, strings["Incomplete"]);
            StringAssert.Contains(row.Diagnostic, strings["Incomplete"]);
            foreach (var value in new[] { "pack.cat", "MEMBER", "0x00000000", "missing.cat", strings["Loose"] })
            {
                Assert.IsTrue(row.Matches(value));
                StringAssert.Contains(row.Details, value);
                StringAssert.Contains(field.GetExportValue(row), value);
            }
            Assert.AreEqual(strings["NoCatalog"], CatalogPresentation.Summary(new([], [], true), strings));
            Assert.AreEqual(strings["Signature_NotChecked"], CatalogPresentation.Summary(null, strings));
            Assert.AreEqual("3 | " + strings["CatalogUnsigned"], CatalogPresentation.Summary(new([catalog with { Trust = embedded }], [], true), strings));
        }
    }

    [TestMethod]
    public void MovePresentation()
    {
        var strings = LocalizationService.Instance;
        strings.SetCulture("en");
        FileRowViewModel Row(string name, SignatureStatus status, RevocationMode mode = RevocationMode.OfflineCache)
        {
            var row = new FileRowViewModel(Path.GetFullPath(name), strings);
            row.Complete(new FileAnalysisResult(row.FullPath, 3, null,
                new Dictionary<ChecksumAlgorithm, string> { [ChecksumAlgorithm.SHA256] = "00FF" }, new SignatureResult(status, mode)));
            return row;
        }
        var first = Row("first.exe", SignatureStatus.Revoked);
        var second = Row("second.exe", SignatureStatus.Revoked);
        Assert.IsTrue(SignatureMoveSelection.GetStatus([]) is null
            && SignatureMoveSelection.GetStatus([new FileRowViewModel("queued", strings)]) is null,
            "empty and unanalysed selections have no move category");
        Assert.IsTrue(SignatureMoveSelection.GetStatus([Row("unchecked", SignatureStatus.NotChecked)]) is null,
            "unchecked signatures do not offer a move category");
        Assert.IsTrue(SignatureMoveSelection.GetStatus([first, second]) == SignatureStatus.Revoked,
            "identical completed signature results offer one move category");
        Assert.IsTrue(SignatureMoveSelection.GetStatus([first, Row("other", SignatureStatus.NoEmbeddedSignature)]) is null,
            "mixed signature results cannot be moved as one group");
        Assert.IsTrue(SignatureMoveSelection.GetStatus([Row("offline", SignatureStatus.Valid), Row("online", SignatureStatus.Valid, RevocationMode.Online)]) is null,
            "valid online and offline labels are distinct move groups");
        var failed = Row("failed", SignatureStatus.Revoked);
        failed.Complete(failed.Result! with { Error = AnalysisError.ReadFailure });
        Assert.IsTrue(SignatureMoveSelection.GetStatus([first, failed]) is null,
            "a failed analysis cannot contribute a stale signature result to the move group");
        var entries = new EmbeddedSignature[]
        {
            new(0, SignatureStatus.Valid, null, 0, null, "SHA-256", SignatureTimestamp.NotPresent),
            new(1, SignatureStatus.Revoked, null, 1, "selected diagnostic", "SHA-1", SignatureTimestamp.NotPresent)
        };
        var multi = Row("multi.exe", SignatureStatus.Valid);
        multi.Complete(multi.Result! with { Signature = multi.Result.Signature with { Signatures = entries } });
        Assert.IsTrue(SignatureMoveSelection.GetStatus([first, multi]) is null, "the initially displayed signer controls move grouping");
        multi.SelectedSignature = multi.Signatures[1];
        Assert.IsTrue(SignatureMoveSelection.GetStatus([first, multi]) == SignatureStatus.Revoked,
            "choosing another signer immediately changes move grouping");
        var selected = multi.SelectedSignature;
        var hashes = multi.Result!.Checksums;
        var newPath = Path.GetFullPath(Path.Combine("revoked", "multi.exe"));
        multi.Relocate(newPath);
        Assert.IsTrue(multi.FullPath == newPath && multi.Result.FullPath == newPath && multi.FileName == "multi.exe"
            && ReferenceEquals(multi.SelectedSignature, selected) && ReferenceEquals(multi.Result.Checksums, hashes)
            && multi.SHA256 == "00FF" && multi.State == RowStatus.Completed,
            "relocation updates both paths while preserving checksums, completion and signer selection");
        var columns = ColumnOption.CreateDefaults(strings);
        var exported = ResultExportProjection.Capture([multi], columns);
        Assert.IsTrue(exported.Rows[0][columns.Select(column => column.Id).ToList().IndexOf("Path")] == newPath
            && multi.Details.Contains(newPath) && multi.Matches("revoked"),
            "exports, details and filtering use a relocated file's new path");
        var report = Enum.GetValues<FileMoveOutcome>().Select((outcome, index) =>
            new FileMoveResult("source-" + index, "destination-" + index, outcome, outcome == FileMoveOutcome.Failed ? "custom error" : null)).ToArray();
        var statuses = Enum.GetValues<SignatureStatus>().Where(status => status != SignatureStatus.NotChecked).ToArray();
        foreach (var culture in new[] { "de", "en", "zh-Hans" })
        {
            strings.SetCulture(culture);
            var folders = statuses.Select(status => SignatureMoveSelection.FolderName(status, strings)).ToArray();
            Assert.IsTrue(folders.All(FileMoveService.IsValidFolderName) && folders.Distinct(StringComparer.OrdinalIgnoreCase).Count() == folders.Length,
                "all signature categories have distinct legal subfolders of at most eight characters / " + culture);
            var text = FileMoveReport.Format(report, strings);
            Assert.IsTrue(report.All(result => text.Contains(result.SourcePath) && text.Contains(result.DestinationPath!)
                    && text.Contains(strings["MoveOutcome_" + result.Outcome])) && text.Contains("custom error"),
                "the move report includes every localized outcome, both paths and failure details / " + culture);
            Assert.IsTrue(FileMoveReport.Summary(report, strings) == strings.Format("MoveReportSummary", 9, 1, 2, 6),
                "the move summary distinguishes success, errors and skipped or cancelled files / " + culture);
        }
        strings.SetCulture("en");
    }

    [TestMethod]
    public void ProgressBuffer()
    {
        var previousContext = SynchronizationContext.Current;
        var context = new CountingContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var buffer = new AnalysisProgressBuffer();
            var first = buffer.Start(0);
            Assert.IsTrue(buffer.Drain().Current?.Progress.Stage == AnalysisStage.OpeningFile,
                "a new file is visible as opening before its first progress report");
            for (var i = 1; i <= 20000; i++) first.Report(new AnalysisProgress(AnalysisStage.Reading, i, 20000));
            var update = buffer.Drain();
            Assert.IsTrue(context.Posts == 0 && update.Current?.Progress.BytesRead == 20000 && update.Completed.Count == 0,
                "twenty thousand reports retain only the latest progress and enqueue no synchronization-context callbacks");
            var result = FileAnalysisResult.Failure("first.bin", AnalysisError.NotFound, null);
            buffer.Complete(0, result);
            var second = buffer.Start(1);
            second.Report(new AnalysisProgress(AnalysisStage.CheckingSignature, 3, 3));
            first.Report(new AnalysisProgress(AnalysisStage.Reading, 1, 20000));
            update = buffer.Drain();
            Assert.IsTrue(update.Current?.Index == 1 && update.Current.Progress.Stage == AnalysisStage.CheckingSignature,
                "late reports from a previous file cannot replace the current phase");
            Assert.IsTrue(update.Completed.Count == 1 && ReferenceEquals(update.Completed[0].Result, result),
                "completed results survive progress coalescing");
            buffer.Complete(1, result with { FullPath = "second.bin" });
            update = buffer.Drain();
            Assert.IsTrue(update.Current == null && update.Completed.Single().Index == 1 && buffer.Drain().Completed.Count == 0,
                "completion removes transient progress and every result is delivered exactly once");
            var third = buffer.Start(2);
            Parallel.For(0, 10000, i => third.Report(new AnalysisProgress(AnalysisStage.Reading, i, 10000)));
            Assert.IsTrue(buffer.Drain().Current is { Index: 2, Progress.TotalBytes: 10000 },
                "concurrent producers publish an intact progress snapshot");
            buffer.Close();
            third.Report(new AnalysisProgress(AnalysisStage.CheckingSignature, 1, 1));
            buffer.Start(3).Report(new AnalysisProgress(AnalysisStage.Reading, 1, 1));
            buffer.Complete(3, result);
            update = buffer.Drain();
            Assert.IsTrue(update.Current == null && update.Completed.Count == 0, "closing a batch rejects all late progress and results");
            var strings = LocalizationService.Instance;
            foreach (var culture in new[] { "de", "en", "zh-Hans" })
            {
                strings.SetCulture(culture);
                var phases = Enum.GetValues<AnalysisStage>().Select(stage => AnalysisStageText.Format(stage, RevocationMode.OfflineCache, strings)).ToArray();
                Assert.IsTrue(phases.Distinct().Count() == Enum.GetValues<AnalysisStage>().Length && phases.All(value => value.Length > 0)
                    && AnalysisStageText.Format(AnalysisStage.CheckingSignature, RevocationMode.Online, strings) != phases[(int)AnalysisStage.CheckingSignature]
                    && AnalysisStageText.Format(AnalysisStage.CheckingCatalogs, RevocationMode.Online, strings) != phases[(int)AnalysisStage.CheckingCatalogs],
                    "all phases are distinct and online revocation has an explicit label / " + culture);
            }
        }
        finally
        {
            LocalizationService.Instance.SetCulture("en");
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed class CountingContext : SynchronizationContext
    {
        public int Posts { get; private set; }
        public override void Post(SendOrPostCallback callback, object? state) => Posts++;
    }

    [TestMethod]
    public void RunSummaries()
    {
        var strings = LocalizationService.Instance;
        var known = new FileAnalysisResult("known.bin", 1024, null,
            new Dictionary<ChecksumAlgorithm, string>(), SignatureResult.NotChecked);
        var failed = FileAnalysisResult.Failure("missing.bin", AnalysisError.NotFound, "missing fixture");
        var results = new List<FileAnalysisResult> { known, known with { Length = 0 }, failed };
        var summary = AnalysisRunSummary.Capture(TimeSpan.FromMilliseconds(1234), results);
        Assert.IsTrue(summary is { ProcessedFiles: 3, TotalBytes: 1024, UnknownSizeFiles: 1 },
            "run totals count zero-length files and distinguish unknown sizes from zero");
        results.Clear();
        Assert.IsTrue(summary is { ProcessedFiles: 3, TotalBytes: 1024, UnknownSizeFiles: 1 }
            && summary.Elapsed == TimeSpan.FromMilliseconds(1234), "run summary is a frozen value after results change");
        var empty = AnalysisRunSummary.Capture(TimeSpan.Zero, []);
        Assert.IsTrue(empty is { ProcessedFiles: 0, TotalBytes: 0, UnknownSizeFiles: 0 }, "an early cancellation can have an empty summary");
        var huge = AnalysisRunSummary.Capture(TimeSpan.FromDays(1) + TimeSpan.FromHours(2),
            [known with { Length = long.MaxValue }, known with { Length = long.MaxValue }]);
        Assert.IsTrue(huge.TotalBytes == 2m * long.MaxValue, "file-size totals remain exact beyond the Int64 limit");
        try
        {
            foreach (var language in strings.Languages)
            {
                strings.SetCulture(language.CultureName);
                var expected = language.CultureName switch
                {
                    "de" => "Dauer: 00:00:01.234 · Dateisumme: 1.024 logische Bytes Größe bei 1 Datei(en) unbekannt.",
                    "zh-Hans" => "耗时：00:00:01.234 · 文件总大小：1,024 逻辑字节 1 个文件的大小未知。",
                    _ => "Elapsed: 00:00:01.234 · Total file size: 1,024 logical bytes Unknown size for 1 file(s)."
                };
                Assert.IsTrue(summary.Format(strings) == expected,
                    "duration, grouped bytes and unknown sizes are localized / " + language.CultureName);
            }
            strings.SetCulture("en");
            Assert.IsTrue(huge.Format(strings) == "Elapsed: 1.02:00:00.000 · Total file size: 18,446,744,073,709,551,614 logical bytes",
                "durations retain days and known byte totals have no unknown-size warning");
        }
        finally { strings.SetCulture("en"); }
    }

    [TestMethod]
    [TestCategory("Shared")]
    [DataRow("", "", "", "", "")]
    [DataRow("00FF", "00ff", "00:FF", "AP8=", "AP8")]
    [DataRow("FF", "ff", "FF", "/w==", "_w")]
    [DataRow("FBFFFF", "fbffff", "FB:FF:FF", "+///", "-___")]
    [DataRow("000102", "000102", "00:01:02", "AAEC", "AAEC")]
    public void KnownDigestFormats(string hex, string lower, string colon, string base64, string url)
    {
        Assert.AreEqual(hex, DigestFormatter.Format(hex.ToLowerInvariant(), HashFormat.HexUpper));
        Assert.AreEqual(lower, DigestFormatter.Format(hex, HashFormat.HexLower));
        Assert.AreEqual(colon, DigestFormatter.Format(hex, HashFormat.HexColon));
        Assert.AreEqual(base64, DigestFormatter.Format(hex, HashFormat.Base64));
        Assert.AreEqual(url, DigestFormatter.Format(hex, HashFormat.Base64Url));
    }

    [TestMethod]
    [TestCategory("Shared")]
    [DataRow(16)]
    [DataRow(20)]
    [DataRow(32)]
    [DataRow(48)]
    [DataRow(64)]
    public void DigestBytesSurviveEncoding(int size)
    {
        var hex = Convert.ToHexString(Enumerable.Range(0, size).Select(index => (byte)index).ToArray());
        var base64 = DigestFormatter.Format(hex, HashFormat.Base64);

        Assert.AreEqual(hex, Convert.ToHexString(Convert.FromBase64String(base64)));
    }

    [TestMethod]
    public void Rows()
    {
        const string sha256 = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        const string base64 = "ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=";
        var strings = LocalizationService.Instance;
        var row = new FileRowViewModel("sample.bin", strings);
        var certificate = new CertificateInfo("Publisher", "Subject", "Issuer", "00AB", "00FF", sha256,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "sha256RSA");
        var result = new FileAnalysisResult("sample.bin", 3, DateTimeOffset.UtcNow,
            new Dictionary<ChecksumAlgorithm, string> { [ChecksumAlgorithm.SHA256] = sha256 },
            new SignatureResult(SignatureStatus.Valid, RevocationMode.OfflineCache, certificate, 0));
        row.Complete(result);
        row.SetHashFormat(HashFormat.Base64);
        Assert.IsTrue(row.SHA256 == base64 && row.CertificateSha256 == base64 && row.CertificateSha1 == "AP8=", "file and certificate digests share the format");
        Assert.IsTrue(ReferenceEquals(row.Result, result) && result.Checksums[ChecksumAlgorithm.SHA256] == sha256,
            "formatting retains immutable analysis data");
        Assert.IsTrue(row.Details.Contains(base64) && row.Matches(base64), "details and search use displayed digests");
        Assert.IsTrue(row.MD5 == "" && row.SerialNumber == "00AB" && row.NativeCode == "0x00000000", "uncomputed hashes and non-hash fields remain unchanged");
        row.SetHashFormat(HashFormat.HexLower);
        Assert.IsTrue(row.SHA256 == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "hex case switches without analysis");
        row.SetHashFormat(HashFormat.HexUpper);
        Assert.IsTrue(row.SHA256 == sha256, "hex round-trip preserves original digest");
        row.Reset();
        Assert.IsTrue(row.SHA256 == "" && row.CertificateSha1 == "", "reset clears formatted caches");
        row.SetHashFormat(HashFormat.Base64);
        row.Complete(result);
        Assert.IsTrue(row.SHA256 == base64, "in-flight/future results use the most recent format");
    }
    [TestMethod]
    public void Metadata()
    {
        var strings = LocalizationService.Instance;
        strings.SetCulture("en");
        var row = new FileRowViewModel("sample.exe", strings);
        var result = new FileAnalysisResult("sample.exe", 1, null,
            new Dictionary<ChecksumAlgorithm, string> { [ChecksumAlgorithm.SHA256] = "00FF" }, SignatureResult.NotChecked)
        {
            Properties = new(new FilePropertiesInfo
            {
                FileDescription = "Example description", Language = "Deutsch", FileType = "Application",
                FileVersion = "1.2.3.4", ProductName = "Example product", ProductVersion = "2.3.4-beta",
                CompanyName = "Example company", OriginalFilename = "original.exe", InternalName = "internal",
                Copyright = "Copyright sample", Trademarks = "Trademark sample", Comments = "Comment sample"
            }),
            PeHeader = new(new PeHeaderInfo(0x014c, "i386", "PE32", ManagedPlatform.Prefer32Bit))
        };
        row.Complete(result);
        foreach (var value in new[] { "Example description", "Deutsch", "Application", "1.2.3.4", "Example product",
            "2.3.4-beta", "Example company", "original.exe", "internal", "Copyright sample", "Trademark sample", "Comment sample",
            "i386 (0x014C)", "PE32", "AnyCPU" })
            Assert.IsTrue(row.Matches(value) && row.Details.Contains(value), "metadata is included in search and details / " + value);
        foreach (var language in strings.Languages)
        {
            strings.SetCulture(language.CultureName); row.RefreshLanguage();
            Assert.IsTrue(row.ManagedPlatformText == strings["ManagedPlatform_Prefer32Bit"] && !row.ManagedPlatformText.StartsWith('['),
                "platform annotation translates / " + language.CultureName);
        }
        row.SetHashFormat(HashFormat.Base64);
        Assert.IsTrue(row.SHA256 == "AP8=" && ReferenceEquals(row.Result, result) && row.FileVersion == "1.2.3.4"
            && row.PeMachine == "i386 (0x014C)", "hash format does not modify version or machine values");
        row.Complete(result with { Properties = new(null, "Version read failed"), PeHeader = new(null, "Header read failed") });
        Assert.IsTrue(row.State == RowStatus.Completed && row.FileVersion == "" && row.PeMachine == ""
            && row.Diagnostic.Contains("Version read failed") && row.Diagnostic.Contains("Header read failed")
            && row.SHA256 == "AP8=", "metadata errors leave the row usable and expose both diagnostics");
        row.Complete(result with { Properties = null, PeHeader = new(null) });
        Assert.IsTrue(row.FileDescription == "" && row.FileType == "" && row.PeMachine == "" && row.PeFormat == ""
            && row.ManagedPlatformText == "" && row.Diagnostic == "", "disabled or non-PE metadata remains empty");
        row.Complete(result with { PeHeader = new(new PeHeaderInfo(0xffff, "0xFFFF", "PE32", ManagedPlatform.None)) });
        Assert.IsTrue(row.PeMachine == "0xFFFF" && row.ManagedPlatformText == "", "unknown native machine code remains visible without duplication");
        row.Complete(result with { PeHeader = new(new PeHeaderInfo(null, "x86 (16-bit)", "NE (Win16)", ManagedPlatform.None)) });
        Assert.IsTrue(row.PeMachine == "x86 (16-bit)" && row.PeFormat == "NE (Win16)" && row.ManagedPlatformText == "",
            "Win16 display has neither a fictitious COFF code nor a .NET platform");
        Assert.IsTrue(row.Matches("Win16") && row.Details.Contains("NE (Win16)"), "Win16 header appears in search and details");
        var legacyColumns = ColumnOption.CreateDefaults(strings);
        foreach (var column in legacyColumns) column.IsExportSelected = column.Id is "PeMachine" or "PeFormat";
        var legacyExport = ResultExportProjection.Capture([row], legacyColumns);
        Assert.IsTrue(legacyExport.Columns.Select(column => column.Id).SequenceEqual(["PeMachine", "PeFormat"])
            && legacyExport.Rows[0].SequenceEqual(["x86 (16-bit)", "NE (Win16)"]),
            "CSV/JSON projection includes NE metadata under the existing stable column IDs");
        row.Reset();
        Assert.IsTrue(row.FileVersion == "" && row.ManagedPlatformText == "" && !row.Matches("Example product"), "reset clears all metadata");
        row.Start();
        row.SetProgress(new(AnalysisStage.ReadingMetadata, 1, 1), 95);
        Assert.IsTrue(row.StateText == strings["ReadingMetadata"] && !row.StateText.StartsWith('['), "metadata reading has a localized progress label");
        strings.SetCulture("en");
    }

    [TestMethod]
    public void SignatureStatusCodes()
    {
        var strings = LocalizationService.Instance;
        strings.SetCulture("en");
        (SignatureStatus Status, string Code)[] cases =
        [
            (SignatureStatus.NotChecked, "0"), (SignatureStatus.Valid, "2"),
            (SignatureStatus.NoEmbeddedSignature, "3"), (SignatureStatus.InvalidSignature, "4"),
            (SignatureStatus.Untrusted, "5"), (SignatureStatus.Expired, "6"),
            (SignatureStatus.Revoked, "7"), (SignatureStatus.RevocationUnknown, "8"),
            (SignatureStatus.UnsupportedFormat, "9"), (SignatureStatus.UnsupportedPlatform, "A"),
            (SignatureStatus.CheckFailed, "B")
        ];
        Assert.IsTrue(cases.Select(item => item.Status).ToHashSet().SetEquals(Enum.GetValues<SignatureStatus>()),
            "the documented code contract covers every signature status");
        var rows = cases.Select(item =>
        {
            var row = new FileRowViewModel("signature-" + item.Code + ".exe", strings);
            row.Complete(new FileAnalysisResult(row.FullPath, 1, null, new Dictionary<ChecksumAlgorithm, string>(),
                new SignatureResult(item.Status, RevocationMode.OfflineCache)));
            return row;
        }).ToArray();
        var originals = rows.Select(row => row.Result).ToArray();
        var online = new FileRowViewModel("online.exe", strings);
        online.Complete(originals[1]! with
        {
            FullPath = online.FullPath,
            Signature = new SignatureResult(SignatureStatus.Valid, RevocationMode.Online, NativeStatusCode: 0)
        });
        foreach (var language in strings.Languages)
        {
            strings.SetCulture(language.CultureName);
            foreach (var row in rows) row.RefreshLanguage();
            online.RefreshLanguage();
            Assert.IsTrue(rows.Select((row, index) => row.SignatureText == cases[index].Code + " | " +
                    strings[cases[index].Status == SignatureStatus.Valid ? "Signature_ValidOffline" : "Signature_" + cases[index].Status]
                    && ReferenceEquals(row.Result, originals[index])).All(matches => matches),
                "existing rows retain fixed hex codes while their text changes language / " + language.CultureName);
            Assert.IsTrue(online.SignatureText == "1 | " + strings["Signature_ValidOnline"] && online.SignatureText != rows[1].SignatureText
                && online.NativeCode == "0x00000000" && SignatureMoveSelection.GetStatus([rows[1], online]) is null,
                "valid online/cache results have distinct codes 1/2 and move groups, separate from the native code / " + language.CultureName);
            var columns = ColumnOption.CreateDefaults(strings);
            foreach (var column in columns) column.IsExportSelected = column.Id == "Signature";
            var exported = ResultExportProjection.Capture(rows, columns);
            Assert.IsTrue(rows.Select((row, index) => row.Details.Contains(row.SignatureText, StringComparison.Ordinal)
                    && row.Matches(cases[index].Code + " |") && exported.Rows[index][0] == row.SignatureText).All(matches => matches),
                "details, search and the shared CSV/JSON projection retain the displayed status code / " + language.CultureName);
        }
        var allModes = Enum.GetValues<SignatureStatus>().SelectMany(status => Enum.GetValues<RevocationMode>().Select(mode =>
        {
            var row = new FileRowViewModel("group.exe", strings);
            row.Complete(new FileAnalysisResult(row.FullPath, 1, null, new Dictionary<ChecksumAlgorithm, string>(), new SignatureResult(status, mode)));
            return row;
        })).ToArray();
        Assert.IsTrue(allModes.All(first => allModes.All(second =>
                (SignatureMoveSelection.GetStatus([first, second]) is not null) ==
                (first.SignatureText[0] != '0' && first.SignatureText[0] == second.SignatureText[0]))),
            "all 484 status/mode pairs agree: matching nonzero display codes exactly identify movable groups");
        strings.SetCulture("en");
    }

    [TestMethod]
    public void Signatures()
    {
        var strings = LocalizationService.Instance;
        strings.SetCulture("en");
        var firstDate = new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var certificate = new CertificateInfo("First publisher", "First subject", "First issuer", "01", "00FF", "00FF",
            firstDate.AddYears(-1), firstDate.AddYears(1), "sha256RSA");
        var second = certificate with { Publisher = "Second publisher", SerialNumber = "02", ThumbprintSha1 = "FF",
            NotBefore = firstDate.AddYears(-2), NotAfter = firstDate.AddYears(2) };
        var entries = new EmbeddedSignature[]
        {
            new(0, SignatureStatus.Valid, certificate, 0, null, "SHA-256", new(firstDate, TimestampStatus.Verified)),
            new(1, SignatureStatus.Untrusted, second, unchecked((int)0x800B0109), "Second diagnostic", "SHA-1", SignatureTimestamp.NotPresent),
            new(2, SignatureStatus.Valid, certificate, 0, null, "SHA-384", new(firstDate.AddDays(1), TimestampStatus.Unverified)),
            new(3, SignatureStatus.CheckFailed, null, null, "No metadata", "", SignatureTimestamp.Unreadable)
        };
        var result = new FileAnalysisResult("signed.exe", 1, firstDate,
            new Dictionary<ChecksumAlgorithm, string> { [ChecksumAlgorithm.SHA256] = "00FF" },
            new SignatureResult(SignatureStatus.Valid, RevocationMode.OfflineCache, certificate, 0)
            { Signatures = Array.AsReadOnly(entries) });
        var row = new FileRowViewModel(result.FullPath, strings);
        row.Complete(result);
        Assert.IsTrue(row.HasMultipleSignatures && row.Signatures.Count == 4 && row.SelectedSignature == row.Signatures[0],
            "all signatures are selectable, including repeated certificates and failed entries");
        Assert.IsTrue(row.SigningTime == firstDate && row.SigningTimeText.Contains("verified") && row.Publisher == "First publisher",
            "primary timestamp and certificate belong to the same signature");
        var changes = 0;
        row.SignatureSelectionChanged += (_, _) => changes++;
        row.SetHashFormat(HashFormat.Base64);
        row.SelectedSignature = row.Signatures[1];
        Assert.IsTrue(changes == 1 && row.SignatureState == SignatureStatus.Untrusted && row.NativeCode == "0x800B0109"
            && row.SignatureText == "5 | " + strings["Signature_Untrusted"]
            && row.Publisher == "Second publisher" && row.SerialNumber == "02" && row.CertificateSha1 == "/w==",
            "selection switches verification status, certificate fields and formatted fingerprints together");
        Assert.IsTrue(row.ValidFromDate == second.NotBefore && row.ValidUntilDate == second.NotAfter,
            "date sorting follows the selected certificate");
        Assert.IsTrue(row.SigningTime is null && row.SigningTimeText == strings["Timestamp_NotPresent"],
            "missing timestamp does not reuse the previous signature's date");
        Assert.IsTrue(row.SHA256 == "AP8=" && ReferenceEquals(result, row.Result), "signature selection preserves file hashes and analysis result");
        Assert.IsTrue(row.Details.Contains("Second publisher") && row.Details.Contains("Second diagnostic") && row.Matches("Second publisher")
            && !row.Matches("First publisher"), "details and search follow the selected signature");
        var exportColumns = ColumnOption.CreateDefaults(strings);
        var signatureExport = ResultExportProjection.Capture([row], exportColumns);
        var columnIds = exportColumns.Select(column => column.Id).ToList();
        Assert.IsTrue(signatureExport.Rows[0][columnIds.IndexOf("Publisher")] == "Second publisher"
            && signatureExport.Rows[0][columnIds.IndexOf("Serial")] == "02"
            && signatureExport.Rows[0][columnIds.IndexOf("CertSha1")] == "/w=="
            && signatureExport.Rows[0][columnIds.IndexOf("SigningTime")] == strings["Timestamp_NotPresent"],
            "CSV snapshot uses certificate, timestamp and fingerprint from the selected signature");
        row.SelectedSignature = row.Signatures[2];
        Assert.IsTrue(row.SigningTimeText.Contains(strings["Timestamp_Unverified"]) && row.SigningTime == firstDate.AddDays(1),
            "unconfirmed timestamp remains explicitly labelled");
        foreach (var language in strings.Languages)
        {
            strings.SetCulture(language.CultureName);
            row.RefreshLanguage();
            Assert.IsTrue(row.SelectedSignature.Label.StartsWith(strings.Format("SignatureChoice", 3), StringComparison.CurrentCulture)
                && !row.SigningTimeText.Contains('['), "signature labels and timestamp status translate / " + language.CultureName);
        }
        row.SelectedSignature = row.Signatures[3];
        Assert.IsTrue(row.Publisher == "" && row.CertificateSha1 == "" && row.NativeCode == ""
            && row.SigningTimeText == strings["Timestamp_Unreadable"], "failed entry never borrows primary metadata");
        row.Reset();
        Assert.IsTrue(!row.HasMultipleSignatures && row.Signatures.Count == 0 && row.SelectedSignature is null
            && row.SigningTime is null && row.SigningTimeText == "" && row.SignatureText == "0 | " + strings["Signature_NotChecked"],
            "reset removes all signature state and restores the unchecked display code");
        strings.SetCulture("en");
    }

    [TestMethod]
    public void OptionsWindowPreferences()
    {
        var strings = LocalizationService.Instance;
        var directory = Path.Combine(Path.GetTempPath(), "FileScope-options-window-" + Guid.NewGuid().ToString("N"));
        var store = new PreferencesStore(Path.Combine(directory, "settings.json"));
        var main = new WindowPreferences(1200, 800, false);
        var settings = new WindowPreferences(900, 850, true);
        try
        {
            store.Save(new UserPreferences { Window = main });
            using var options = new OptionsViewModel(strings, store, new RecordingThemeService());
            Assert.IsTrue(options.OptionsWindowPreferences is null && options.WindowPreferences == main,
                "old settings retain independent options window defaults");
            options.SaveOptionsWindowPreferences(settings);
            options.SelectedCsvDelimiter = options.CsvDelimiters.Single(option => option.Value == CsvDelimiter.Comma);
            options.SaveWindowPreferences(main);
            Assert.IsTrue(store.Load().OptionsWindow == settings && store.Load().Window == main,
                "options size/state survives other saves without changing main window geometry");
            foreach (var invalid in new[] { new WindowPreferences(double.NaN, 800, false), new(-1, 800, false), new(800, double.PositiveInfinity, false), new(800, 0, false) })
                options.SaveOptionsWindowPreferences(invalid);
            using var reopened = new OptionsViewModel(strings, store, new RecordingThemeService());
            Assert.IsTrue(reopened.OptionsWindowPreferences == settings && reopened.WindowPreferences == main,
                "options window geometry survives JSON save/reload and invalid dimensions are ignored");
            var restored = WindowSize.Restore(settings, 730, 740, 640, 600, 800, 700);
            Assert.IsTrue(restored is { Width: 800, Height: 700 }, "options window fits a smaller work area on restore");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [TestCategory("Shared")]
    public void WindowSizes()
    {
        static WindowSize Restore(WindowPreferences? saved, double width = 1920, double height = 1040) =>
            WindowSize.Restore(saved, 1440, 900, 1080, 680, width, height);
        Assert.IsTrue(Restore(null) is { Width: 1440, Height: 900 }, "old settings without window data use the default size");
        Assert.IsTrue(Restore(new(1234, 789, true)) is { Width: 1234, Height: 789 }, "saved normal dimensions are preserved");
        Assert.IsTrue(Restore(new(9000, 9000, false)) is { Width: 1920, Height: 1040 }, "oversized window is clamped to the new work area");
        Assert.IsTrue(Restore(new(200, 100, false)) is { Width: 1080, Height: 680 }, "tiny saved size respects the minimum");
        Assert.IsTrue(Restore(new(double.NaN, double.PositiveInfinity, false)) is { Width: 1440, Height: 900 }, "non-finite dimensions are ignored");
        Assert.IsTrue(Restore(new(-10, 0, false)) is { Width: 1440, Height: 900 }, "non-positive dimensions are ignored");
        Assert.IsTrue(Restore(null, 800, 600) is { Width: 800, Height: 600, MinWidth: 800, MinHeight: 600 },
            "small primary monitor also reduces minimum bounds so the window fits");
    }

    [TestMethod]
    public void Options()
    {
        var strings = LocalizationService.Instance;
        strings.SetCulture("en");
        var directory = Path.Combine(Path.GetTempPath(), "FileScope-options-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        var store = new PreferencesStore(path);
        try
        {
            using var options = new OptionsViewModel(strings, store, new RecordingThemeService());
            Assert.IsTrue(options.AnalysisPreferences == new AnalysisPreferences(), "missing checkbox settings retain first-run defaults");
            Assert.IsTrue(options.PathAutoWidth && options.Columns[^1].Id == "Path", "old settings default to automatic width and a final path column");
            options.PathAutoWidth = false;
            Assert.IsTrue(!store.Load().PathAutoWidth, "turning off automatic path width persists an explicit false");
            Assert.IsTrue(options.SelectedCsvDelimiter.Value == CsvDelimiter.Semicolon, "old settings default to semicolon CSV");
            options.SelectedCsvDelimiter = options.CsvDelimiters.Single(option => option.Value == CsvDelimiter.Comma);
            Assert.IsTrue(options.Columns.All(column => column.IsExportSelected), "older preferences select all export columns initially");
            options.ClearExportColumnsCommand.Execute(null);
            Assert.IsTrue(!options.HasExportColumns && store.Load().ExportColumns is { Length: 0 }, "explicit empty export selection is saved");
            using (var emptyExport = new OptionsViewModel(strings, store, new RecordingThemeService()))
                Assert.IsTrue(!emptyExport.HasExportColumns, "an empty export selection survives a restart");
            foreach (var column in options.Columns) column.IsExportSelected = column.Id is "Name" or "SHA256" or "PeMachine";
            var selection = new AnalysisPreferences
            {
                MD5 = true, SHA1 = true, SHA256 = false, SHA384 = true, SHA512 = false,
                InspectCertificates = false, OnlineRevocation = true, InspectFileProperties = true, InspectPeHeader = true
            };
            options.SaveAnalysisPreferences(selection);
            Assert.IsTrue(options.Columns.Count == 40 && options.Columns.Select(column => column.Id).Distinct().Count() == 40,
                "all columns have unique persistent IDs");
            Assert.IsTrue(options.Columns.Take(3).Select(column => column.Id).SequenceEqual(["Name", "Status", "Diagnostic"]),
                "default column order puts diagnostics immediately after analysis status");
            Assert.IsTrue(!options.MoveUpCommand.CanExecute(null), "cannot move the first column above the start");
            var selected = options.Columns.Single(column => column.Id == "SHA512");
            options.SelectedColumn = selected;
            options.MoveFirstCommand.Execute(null);
            Assert.IsTrue(options.Columns[0] == selected && options.SelectedColumn == selected, "move to top keeps selection");
            options.MoveLastCommand.Execute(null);
            Assert.IsTrue(options.Columns[^1] == selected && !options.MoveDownCommand.CanExecute(null), "move to bottom respects boundary");
            options.MoveUpCommand.Execute(null);
            options.MoveDownCommand.Execute(null);
            Assert.IsTrue(options.Columns[^1] == selected, "up/down preserve a valid permutation");
            options.SetColumnOrder(["Issuer", "SHA512", "Issuer", "missing-column"]);
            Assert.IsTrue(options.Columns[0].Id == "Issuer" && options.Columns[1].Id == "SHA512" && options.Columns.Count == 40,
                "restore ignores duplicates/unknown IDs and appends missing columns");
            var widthRevision = options.WidthRevision;
            var formatChanges = 0;
            options.HashFormatChanged += (_, _) => formatChanges++;
            options.SelectedHashFormat = options.HashFormats.Single(format => format.Value == HashFormat.Base64Url);
            Assert.IsTrue(formatChanges == 1 && options.WidthRevision != widthRevision, "format change publishes a sizing request");
            foreach (var culture in options.Languages)
            {
                options.SelectedLanguage = culture;
                Assert.IsTrue(options.HashFormats.All(format => !format.Label.StartsWith('['))
                    && options.CsvDelimiters.All(delimiter => !delimiter.Label.StartsWith('['))
                    && options.Columns.All(column => !column.Label.StartsWith('[')), "translated option labels / " + culture.CultureName);
                Assert.IsTrue(options.SelectedCsvDelimiter.Value == CsvDelimiter.Comma, "language changes retain the CSV delimiter");
            }
            options.SelectedLanguage = options.Languages.Single(language => language.CultureName == "de");
            var window = new WindowPreferences(1270, 810, true);
            options.SaveWindowPreferences(window);
            options.SelectedHashFormat = options.HashFormats.Single(format => format.Value == HashFormat.Base64Url);
            options.SelectedLanguage = options.Languages.Single(language => language.CultureName == "en");
            options.SelectedLanguage = options.Languages.Single(language => language.CultureName == "de");
            Assert.IsTrue(store.Load().Window == window, "changing UI options preserves saved window size/state");
            Assert.IsTrue(store.Load().Language == "de" && store.Load().HashFormat == nameof(HashFormat.Base64Url),
                "saving window state preserves language and hash format");
            options.SaveWindowPreferences(new(double.NaN, 0, false));
            Assert.IsTrue(store.Load().Window == window, "invalid geometry never replaces usable saved settings");
            var savedOrder = options.Columns.Select(column => column.Id).ToArray();
            using (var reopened = new OptionsViewModel(strings, store, new RecordingThemeService()))
            {
                Assert.IsTrue(reopened.SelectedCsvDelimiter.Value == CsvDelimiter.Comma,
                    "CSV delimiter survives JSON save/load and all other preference changes");
                Assert.IsTrue(reopened.AnalysisPreferences == selection,
                    "all checkbox states survive JSON save/load and other option changes");
                Assert.IsTrue(reopened.Columns.Where(column => column.IsExportSelected).Select(column => column.Id)
                    .SequenceEqual(["Name", "SHA256", "PeMachine"]),
                    "export field selection survives language, format, order, analysis and window saves");
                Assert.IsTrue(reopened.WindowPreferences == window, "normal size and maximized state survive a restart");
                Assert.IsTrue(!reopened.PathAutoWidth, "bounded path width survives restart and unrelated preference saves");
                reopened.PathAutoWidth = true;
                Assert.IsTrue(store.Load().PathAutoWidth, "automatic path width can be re-enabled and saved");
                Assert.IsTrue(reopened.Columns.Select(column => column.Id).SequenceEqual(savedOrder), "column order survives a restart");
                Assert.IsTrue(reopened.SelectedLanguage.CultureName == "de" && reopened.SelectedHashFormat.Value == HashFormat.Base64Url,
                    "language and hash format survive a restart");
                reopened.ResetOrderCommand.Execute(null);
                Assert.IsTrue(reopened.Columns.Count(column => column.IsExportSelected) == 3, "resetting column order retains export selection");
                Assert.IsTrue(reopened.Columns.Select(column => column.Id).SequenceEqual(ColumnOption.CreateDefaults(strings).Select(column => column.Id)),
                    "reset restores the entire default order");
            }
            Assert.IsTrue(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "settings save leaves no temporary files");
            var none = new AnalysisPreferences { SHA256 = false, InspectCertificates = false };
            options.SaveAnalysisPreferences(none);
            using (var reopened = new OptionsViewModel(strings, store, new RecordingThemeService()))
                Assert.IsTrue(reopened.AnalysisPreferences == none && reopened.WindowPreferences == window
                    && reopened.SelectedLanguage.CultureName == "de" && reopened.SelectedHashFormat.Value == HashFormat.Base64Url,
                    "all checkboxes off is retained without overwriting window/language/format settings");
            File.WriteAllText(path, "{\"Analysis\":{\"MD5\":true,\"SHA256\":false}}");
            using (var partial = new OptionsViewModel(strings, store, new RecordingThemeService()))
                Assert.IsTrue(partial.AnalysisPreferences is { MD5: true, SHA256: false, InspectCertificates: true, OnlineRevocation: false, InspectFileProperties: false, InspectPeHeader: false },
                    "missing individual fields get defaults while explicit false is respected");
            using (var legacy = new OptionsViewModel(strings, store, new RecordingThemeService()))
                Assert.IsTrue(legacy.PathAutoWidth, "JSON without PathAutoWidth explicitly defaults to automatic sizing");
            File.WriteAllText(path, "{ invalid JSON");
            using (var fallback = new OptionsViewModel(strings, store, new RecordingThemeService()))
                Assert.IsTrue(fallback.HasSettingsError && fallback.Columns.Count == 40, "corrupt settings fall back without blocking the app");
            store.Save(new UserPreferences { Language = "invalid", HashFormat = "999", CsvDelimiter = "999", ColumnOrder = ["SHA256", "SHA256"] });
            using (var normalized = new OptionsViewModel(strings, store, new RecordingThemeService()))
                Assert.IsTrue(normalized.SelectedHashFormat.Value == HashFormat.HexUpper && normalized.Columns.Count == 40
                    && normalized.SelectedCsvDelimiter.Value == CsvDelimiter.Semicolon
                    && normalized.AnalysisPreferences == new AnalysisPreferences(),
                    "invalid saved enum and duplicate columns are normalized");
            store.Save(new UserPreferences { ExportColumns = ["Name", "Name", "future-column"] });
            using (var normalized = new OptionsViewModel(strings, store, new RecordingThemeService()))
            {
                Assert.IsTrue(normalized.Columns.Single(column => column.IsExportSelected).Id == "Name",
                    "unknown/duplicate export IDs do not enable unintended fields");
                normalized.SelectAllExportColumnsCommand.Execute(null);
                Assert.IsTrue(store.Load().ExportColumns?.Length == 40, "select all saves every supported export field");
            }
            var previousOrder = ColumnOption.CreateDefaults(strings).Select(column => column.Id)
                .Where(id => id != "Diagnostic").Append("Diagnostic").ToArray();
            store.Save(new UserPreferences { ColumnOrder = previousOrder });
            using (var previous = new OptionsViewModel(strings, store, new RecordingThemeService()))
            {
                Assert.IsTrue(previous.Columns.Select(column => column.Id).SequenceEqual(previousOrder),
                    "a saved column order is retained when the defaults change");
                previous.ResetOrderCommand.Execute(null);
                Assert.IsTrue(previous.Columns.Take(3).Select(column => column.Id).SequenceEqual(["Name", "Status", "Diagnostic"])
                    && store.Load().ColumnOrder?.Take(3).SequenceEqual(["Name", "Status", "Diagnostic"]) == true,
                    "resetting an older column order applies and saves the new diagnostic position");
            }
            using var failing = new OptionsViewModel(strings, new FailingStore(), new RecordingThemeService());
            var moved = failing.Columns[^1];
            failing.SelectedColumn = moved;
            failing.MoveFirstCommand.Execute(null);
            Assert.IsTrue(failing.HasSettingsError && failing.Columns[0] == moved, "save failures keep options usable in memory");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); strings.SetCulture("en"); }
    }
    [TestMethod]
    public void ExportProjection()
    {
        var strings = LocalizationService.Instance;
        strings.SetCulture("en");
        var columns = ColumnOption.CreateDefaults(strings);
        var row = new FileRowViewModel("Prüfung 中文.exe", strings, HashFormat.Base64);
        row.Complete(new FileAnalysisResult(row.FullPath, 1024, null,
            new Dictionary<ChecksumAlgorithm, string> { [ChecksumAlgorithm.SHA256] = "00FF" }, SignatureResult.NotChecked)
        {
            Properties = new(new FilePropertiesInfo { FileVersion = "01.02.003", Comments = "with, comma\nand \"quotes\"" }),
            PeHeader = new(new PeHeaderInfo(0x8664, "AMD64 (x64)", "PE32+", ManagedPlatform.MachineSpecific))
        });
        var failed = new FileRowViewModel("missing.exe", strings);
        failed.Complete(FileAnalysisResult.Failure(failed.FullPath, AnalysisError.NotFound, "missing fixture"));
        var all = ResultExportProjection.Capture([row, failed], columns);
        var ids = columns.Select(column => column.Id).ToList();
        Assert.IsTrue(all.Columns.Count == 40 && all.Rows.Count == 2 && all.Rows.All(values => values.Count == 40),
            "every registered column can export completed and failed rows");
        Assert.IsTrue(all.Rows[0][ids.IndexOf("SHA256")] == "AP8=" && all.Rows[0][ids.IndexOf("FileVersion")] == "01.02.003"
            && all.Rows[0][ids.IndexOf("PeMachine")] == "AMD64 (x64) (0x8664)"
            && all.Rows[0][ids.IndexOf("Comments")] == "with, comma\nand \"quotes\"",
            "projection preserves current hash format, architecture, version zeros and multiline metadata");
        Assert.IsTrue(all.Rows[1][ids.IndexOf("SHA256")] == "" && all.Rows[1][ids.IndexOf("Diagnostic")].Contains("missing fixture"),
            "failed rows export blank missing data and their diagnostic");
        foreach (var column in columns) column.IsExportSelected = column.Id is "Name" or "SHA256" or "PeMachine";
        var selected = ResultExportProjection.Capture([row, failed], columns.Reverse());
        Assert.IsTrue(selected.Columns.Select(column => column.Header).SequenceEqual(new[] { strings["Column_PeMachine"], "SHA-256", strings["Column_Name"] })
            && selected.Rows[0].SequenceEqual(["AMD64 (x64) (0x8664)", "AP8=", "Prüfung 中文.exe"]),
            "export selection and ordering are independent of grid visibility");
        row.Reset();
        strings.SetCulture("zh-Hans");
        Assert.IsTrue(selected.Rows[0][1] == "AP8=" && selected.Columns[2].Header == "File name",
            "captured data and headers do not change after reset or a language switch");
        Assert.IsTrue(selected.Columns.Select(column => column.Id).SequenceEqual(["PeMachine", "SHA256", "Name"]),
            "snapshot retains stable JSON IDs in the selected field order");
        var translated = ResultExportProjection.Capture([row], columns);
        Assert.IsTrue(translated.Columns.Select(column => column.Id).SequenceEqual(["Name", "SHA256", "PeMachine"]),
            "JSON field IDs remain stable across language changes");
        Assert.IsTrue(translated.Columns[0].Header == strings["Column_Name"] && translated.Rows[0][1] == "",
            "a fresh export uses the current language and current results");
        foreach (var column in columns) column.IsExportSelected = false;
        try { ResultExportProjection.Capture([row], columns); Assert.Fail("Expected empty selection failure."); }
        catch (InvalidOperationException) { Console.WriteLine("projection rejects exporting zero fields"); }
        strings.SetCulture("en");
    }

    [TestMethod]
    public void ThemePreferences()
    {
        var strings = LocalizationService.Instance;
        strings.SetCulture("en");
        var selection = new AnalysisPreferences { MD5 = true, OnlineRevocation = true, InspectPeHeader = true };
        var original = new UserPreferences
        {
            Language = "en", HashFormat = nameof(HashFormat.Base64Url), CsvDelimiter = nameof(CsvDelimiter.Comma),
            ColumnOrder = ColumnOption.CreateDefaults(strings).Select(column => column.Id).Reverse().ToArray(),
            ExportColumns = ["Name", "SHA256"], Window = new(1100, 720, true),
            OptionsWindow = new(800, 750, false), Analysis = selection
        };
        var store = new ThemePreferencesStore(original);
        var service = new RecordingThemeService(AppTheme.Dark);
        using var options = new OptionsViewModel(strings, store, service);
        Assert.IsTrue(options.SelectedTheme.Value == AppTheme.Dark && service.ApplyCount == 1 && store.SaveCount == 0
            && options.Themes.Select(theme => theme.Value).SequenceEqual([AppTheme.Light, AppTheme.Dark]),
            "older settings retain the current application theme without writing preferences");
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            var restoredStore = new ThemePreferencesStore(original with { Theme = theme.ToString() });
            var restoredService = new RecordingThemeService(theme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
            using var restored = new OptionsViewModel(strings, restoredStore, restoredService);
            Assert.IsTrue(restored.SelectedTheme.Value == theme && restoredService.CurrentTheme == theme
                && restoredService.ApplyCount == 1 && restoredStore.SaveCount == 0,
                "saved theme is applied during construction without a settings write / " + theme);
        }
        foreach (var invalid in new string?[] { null, "", "unknown-theme", "999" })
        {
            var fallbackStore = new ThemePreferencesStore(original with { Theme = invalid });
            var fallbackService = new RecordingThemeService(AppTheme.Dark);
            using var fallback = new OptionsViewModel(strings, fallbackStore, fallbackService);
            Assert.IsTrue(fallback.SelectedTheme.Value == AppTheme.Dark && fallbackService.CurrentTheme == AppTheme.Dark
                && fallbackStore.SaveCount == 0, "missing or invalid saved themes use the current appearance / " + invalid);
        }
        options.SelectedTheme = options.Themes.Single(theme => theme.Value == AppTheme.Light);
        Assert.IsTrue(service.CurrentTheme == AppTheme.Light && store.Saved.Theme == "Light" && store.SaveCount == 1,
            "changing only the theme applies and saves it immediately");
        Assert.IsTrue(store.Saved.Language == original.Language && store.Saved.HashFormat == original.HashFormat
            && store.Saved.CsvDelimiter == original.CsvDelimiter
            && store.Saved.ColumnOrder!.SequenceEqual(original.ColumnOrder!)
            && store.Saved.ExportColumns!.ToHashSet().SetEquals(original.ExportColumns!)
            && store.Saved.Window == original.Window && store.Saved.OptionsWindow == original.OptionsWindow
            && store.Saved.Analysis == selection, "theme saves preserve all unrelated preference sections");
        var applyCount = service.ApplyCount;
        var saveCount = store.SaveCount;
        var notifications = 0;
        options.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(options.SelectedTheme)) notifications++; };
        options.SelectedTheme = options.SelectedTheme;
        Assert.IsTrue(service.ApplyCount == applyCount && store.SaveCount == saveCount && notifications == 0,
            "selecting the current theme does not apply, save or notify again");
        options.SelectedTheme = null!;
        options.SelectedTheme = new FilterOption<AppTheme>(AppTheme.Dark, "Theme_Dark", strings);
        options.SelectedTheme = new FilterOption<AppTheme>((AppTheme)999, "Theme_Dark", strings);
        Assert.IsTrue(service.ApplyCount == applyCount && store.SaveCount == saveCount && options.SelectedTheme.Value == AppTheme.Light,
            "null and foreign theme choices cannot bypass the registered options");
        service.FailApply = true;
        var rejected = false;
        try { options.SelectedTheme = options.Themes.Single(theme => theme.Value == AppTheme.Dark); }
        catch (InvalidOperationException) { rejected = true; }
        finally { service.FailApply = false; }
        Assert.IsTrue(rejected && options.SelectedTheme.Value == AppTheme.Light && store.Saved.Theme == "Light"
            && service.ApplyCount == applyCount && store.SaveCount == saveCount && notifications == 0,
            "a failed application does not publish or persist a theme that was not applied");
        store.FailWrites = true;
        options.SelectedTheme = options.Themes.Single(theme => theme.Value == AppTheme.Dark);
        Assert.IsTrue(service.CurrentTheme == AppTheme.Dark && options.SelectedTheme.Value == AppTheme.Dark
            && options.HasSettingsError && store.Saved.Theme == "Light",
            "a settings write failure retains the live theme and reports the unsaved preference");
        store.FailWrites = false;
        options.SelectedTheme = options.Themes.Single(theme => theme.Value == AppTheme.Light);
        Assert.IsTrue(!options.HasSettingsError && store.Saved.Theme == "Light" && service.CurrentTheme == AppTheme.Light,
            "a later successful theme save clears the settings error");
        applyCount = service.ApplyCount;
        try
        {
            foreach (var language in strings.Languages)
            {
                options.SelectedLanguage = language;
                var expected = language.CultureName switch
                {
                    "de" => new[] { "Hell", "Dunkel" },
                    "zh-Hans" => new[] { "浅色", "深色" },
                    _ => new[] { "Light", "Dark" }
                };
                Assert.IsTrue(options.Themes.Select(theme => theme.Label).SequenceEqual(expected)
                    && service.ApplyCount == applyCount && options.SelectedTheme.Value == AppTheme.Light,
                    "theme labels follow the language without changing appearance / " + language.CultureName);
            }
            var directory = Path.Combine(Path.GetTempPath(), "FileScope-theme-" + Guid.NewGuid().ToString("N"));
            try
            {
                var disk = new PreferencesStore(Path.Combine(directory, "settings.json"));
                disk.Save(store.Saved);
                using (var first = new OptionsViewModel(strings, disk, new RecordingThemeService()))
                    first.SelectedTheme = first.Themes.Single(theme => theme.Value == AppTheme.Dark);
                var restartService = new RecordingThemeService();
                using var restarted = new OptionsViewModel(strings, disk, restartService);
                Assert.IsTrue(restarted.SelectedTheme.Value == AppTheme.Dark && restartService.CurrentTheme == AppTheme.Dark
                    && restarted.AnalysisPreferences == selection && restarted.WindowPreferences == original.Window
                    && restarted.OptionsWindowPreferences == original.OptionsWindow,
                    "a theme-only change survives a real JSON round trip together with other settings");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        finally { strings.SetCulture("en"); }
    }

    [TestMethod]
    public void OptionsPreviewRollsBackEveryFieldAndKeepsIndependentWrites()
    {
        var strings = LocalizationService.Instance;
        var store = new ThemePreferencesStore(new UserPreferences { Language = "en", Theme = "Light" });
        var themes = new RecordingThemeService();
        using var options = new OptionsViewModel(strings, store, themes);
        var originalOrder = options.Columns.Select(column => column.Id).ToArray();
        var originalSelection = options.SelectedColumn;

        options.BeginEdit();
        options.SelectedLanguage = options.Languages.Single(value => value.CultureName == "de");
        options.SelectedTheme = options.Themes.Single(value => value.Value == AppTheme.Dark);
        options.SelectedHashFormat = options.HashFormats.Single(value => value.Value == HashFormat.Base64);
        options.SelectedCsvDelimiter = options.CsvDelimiters.Single(value => value.Value == CsvDelimiter.Comma);
        options.PathAutoWidth = false;
        options.SelectedColumn = options.Columns[^1];
        options.MoveFirstCommand.Execute(null);
        options.ClearExportColumnsCommand.Execute(null);
        Assert.AreEqual(0, store.SaveCount);
        Assert.AreEqual(("de", AppTheme.Dark), (strings.Culture.Name, themes.CurrentTheme));
        Assert.IsFalse(options.HasExportColumns);

        var analysis = options.AnalysisPreferences with { MD5 = true, SHA256 = false };
        var main = new WindowPreferences(920, 680, true);
        options.SaveAnalysisPreferences(analysis);
        options.SaveWindowPreferences(main);
        Assert.AreEqual(("en", "Light", "HexUpper", "Semicolon", true),
            (store.Saved.Language, store.Saved.Theme, store.Saved.HashFormat, store.Saved.CsvDelimiter, store.Saved.PathAutoWidth));
        CollectionAssert.AreEqual(originalOrder, store.Saved.ColumnOrder);
        CollectionAssert.AreEqual(originalOrder, store.Saved.ExportColumns);

        var placement = new WindowPreferences(740, 640, false);
        options.CancelEdit(placement);
        Assert.AreEqual(("en", AppTheme.Light), (strings.Culture.Name, themes.CurrentTheme));
        Assert.AreEqual((HashFormat.HexUpper, CsvDelimiter.Semicolon, true),
            (options.SelectedHashFormat.Value, options.SelectedCsvDelimiter.Value, options.PathAutoWidth));
        Assert.AreSame(originalSelection, options.SelectedColumn);
        CollectionAssert.AreEqual(originalOrder, options.Columns.Select(column => column.Id).ToArray());
        Assert.IsTrue(options.Columns.All(column => column.IsExportSelected));
        Assert.AreEqual((main, placement, analysis), (store.Saved.Window, store.Saved.OptionsWindow, store.Saved.Analysis));

        var saves = store.SaveCount;
        options.BeginEdit();
        options.CancelEdit(placement);
        Assert.AreEqual(saves, store.SaveCount);
    }

    [TestMethod]
    public void FailedOptionsSaveCanBeRetriedOrDiscarded()
    {
        var strings = LocalizationService.Instance;
        var store = new ThemePreferencesStore(new UserPreferences { Language = "en", Theme = "Light" });
        var themes = new RecordingThemeService();
        using var options = new OptionsViewModel(strings, store, themes);
        var placement = new WindowPreferences(760, 660, false);

        options.BeginEdit();
        options.SelectedTheme = options.Themes.Single(value => value.Value == AppTheme.Dark);
        options.ClearExportColumnsCommand.Execute(null);
        store.FailWrites = true;
        Assert.IsFalse(options.TryCommitEdit(placement));
        Assert.IsTrue(options.HasSettingsError);
        Assert.AreEqual((AppTheme.Dark, "Light", 0), (themes.CurrentTheme, store.Saved.Theme, store.SaveCount));

        store.FailWrites = false;
        Assert.IsTrue(options.TryCommitEdit(placement));
        Assert.IsFalse(options.HasSettingsError);
        Assert.AreEqual(("Dark", 1, placement), (store.Saved.Theme, store.SaveCount, store.Saved.OptionsWindow));
        Assert.IsNotNull(store.Saved.ExportColumns);
        Assert.IsEmpty(store.Saved.ExportColumns);

        options.BeginEdit();
        options.SelectedTheme = options.Themes.Single(value => value.Value == AppTheme.Light);
        options.SelectAllExportColumnsCommand.Execute(null);
        store.FailWrites = true;
        Assert.IsFalse(options.TryCommitEdit(placement));
        options.CancelEdit(placement);
        Assert.AreEqual(AppTheme.Dark, themes.CurrentTheme);
        Assert.IsFalse(options.HasExportColumns);
        Assert.AreEqual("Dark", store.Saved.Theme);
    }

    private sealed class ThemePreferencesStore(UserPreferences initial) : IPreferencesStore
    {
        public UserPreferences Saved { get; private set; } = initial;
        public int SaveCount { get; private set; }
        public bool FailWrites { get; set; }
        public UserPreferences Load() => Saved;
        public void Save(UserPreferences preferences)
        {
            if (FailWrites) throw new UnauthorizedAccessException("Test theme settings failure");
            Saved = preferences;
            SaveCount++;
        }
    }

    private sealed class FailingStore : IPreferencesStore
    {
        public UserPreferences Load() => new();
        public void Save(UserPreferences preferences) => throw new UnauthorizedAccessException("Test write failure");
    }
    private sealed class RecordingThemeService(AppTheme initialTheme = AppTheme.Light) : IThemeService
    {
        public AppTheme CurrentTheme { get; private set; } = initialTheme;
        public int ApplyCount { get; private set; }
        public bool FailApply { get; set; }
        public void Apply(AppTheme theme)
        {
            if (FailApply) throw new InvalidOperationException("Test theme failure");
            CurrentTheme = theme;
            ApplyCount++;
        }
    }

}
