using FileScope.App.Localization;
using FileScope.App.Presentation;
using FileScope.App.Services;
using FileScope.App.ViewModels;
using FileScope.Core.Models;
using FileScope.Core.Export;

internal static class PresentationChecks
{
    private static int _passed;
    private static int Main()
    {
        try
        {
            CheckFormats();
            CheckRows();
            CheckMetadata();
            CheckSignatures();
            CheckWindowSizes();
            CheckOptionsWindowPreferences();
            CheckOptions();
            CheckExportProjection();
            CheckRunSummaries();
            CheckThemePreferences();
            CheckProgressBuffer();
            Console.WriteLine();
            ConsoleColor backgroundBackup = Console.BackgroundColor;
            ConsoleColor foregroundBackup = Console.ForegroundColor;
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"PASS: {_passed} presentation / settings assertions.");
            Console.BackgroundColor = backgroundBackup;
            Console.ForegroundColor = foregroundBackup;
            Console.WriteLine("=======================");
            Console.WriteLine();
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private static void CheckProgressBuffer()
    {
        var previousContext = SynchronizationContext.Current;
        var context = new CountingContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var buffer = new AnalysisProgressBuffer();
            var first = buffer.Start(0);
            Check(buffer.Drain().Current?.Progress.Stage == AnalysisStage.OpeningFile,
                "a new file is visible as opening before its first progress report");
            for (var i = 1; i <= 20000; i++) first.Report(new AnalysisProgress(AnalysisStage.Reading, i, 20000));
            var update = buffer.Drain();
            Check(context.Posts == 0 && update.Current?.Progress.BytesRead == 20000 && update.Completed.Count == 0,
                "twenty thousand reports retain only the latest progress and enqueue no synchronization-context callbacks");
            var result = FileAnalysisResult.Failure("first.bin", AnalysisError.NotFound, null);
            buffer.Complete(0, result);
            var second = buffer.Start(1);
            second.Report(new AnalysisProgress(AnalysisStage.CheckingSignature, 3, 3));
            first.Report(new AnalysisProgress(AnalysisStage.Reading, 1, 20000));
            update = buffer.Drain();
            Check(update.Current?.Index == 1 && update.Current.Progress.Stage == AnalysisStage.CheckingSignature,
                "late reports from a previous file cannot replace the current phase");
            Check(update.Completed.Count == 1 && ReferenceEquals(update.Completed[0].Result, result),
                "completed results survive progress coalescing");
            buffer.Complete(1, result with { FullPath = "second.bin" });
            update = buffer.Drain();
            Check(update.Current == null && update.Completed.Single().Index == 1 && buffer.Drain().Completed.Count == 0,
                "completion removes transient progress and every result is delivered exactly once");
            var third = buffer.Start(2);
            Parallel.For(0, 10000, i => third.Report(new AnalysisProgress(AnalysisStage.Reading, i, 10000)));
            Check(buffer.Drain().Current is { Index: 2, Progress.TotalBytes: 10000 },
                "concurrent producers publish an intact progress snapshot");
            buffer.Close();
            third.Report(new AnalysisProgress(AnalysisStage.CheckingSignature, 1, 1));
            buffer.Start(3).Report(new AnalysisProgress(AnalysisStage.Reading, 1, 1));
            buffer.Complete(3, result);
            update = buffer.Drain();
            Check(update.Current == null && update.Completed.Count == 0, "closing a batch rejects all late progress and results");
            var strings = LocalizationService.Instance;
            foreach (var culture in new[] { "de", "en", "zh-Hans" })
            {
                strings.SetCulture(culture);
                var phases = Enum.GetValues<AnalysisStage>().Select(stage => AnalysisStageText.Format(stage, RevocationMode.OfflineCache, strings)).ToArray();
                Check(phases.Distinct().Count() == 5 && phases.All(value => value.Length > 0)
                    && AnalysisStageText.Format(AnalysisStage.CheckingSignature, RevocationMode.Online, strings) != phases[(int)AnalysisStage.CheckingSignature],
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

    private static void CheckRunSummaries()
    {
        var strings = LocalizationService.Instance;
        var known = new FileAnalysisResult("known.bin", 1024, null,
            new Dictionary<ChecksumAlgorithm, string>(), SignatureResult.NotChecked);
        var failed = FileAnalysisResult.Failure("missing.bin", AnalysisError.NotFound, "missing fixture");
        var results = new List<FileAnalysisResult> { known, known with { Length = 0 }, failed };
        var summary = AnalysisRunSummary.Capture(TimeSpan.FromMilliseconds(1234), results);
        Check(summary is { ProcessedFiles: 3, TotalBytes: 1024, UnknownSizeFiles: 1 },
            "run totals count zero-length files and distinguish unknown sizes from zero");
        results.Clear();
        Check(summary is { ProcessedFiles: 3, TotalBytes: 1024, UnknownSizeFiles: 1 }
            && summary.Elapsed == TimeSpan.FromMilliseconds(1234), "run summary is a frozen value after results change");
        var empty = AnalysisRunSummary.Capture(TimeSpan.Zero, []);
        Check(empty is { ProcessedFiles: 0, TotalBytes: 0, UnknownSizeFiles: 0 }, "an early cancellation can have an empty summary");
        var huge = AnalysisRunSummary.Capture(TimeSpan.FromDays(1) + TimeSpan.FromHours(2),
            [known with { Length = long.MaxValue }, known with { Length = long.MaxValue }]);
        Check(huge.TotalBytes == 2m * long.MaxValue, "file-size totals remain exact beyond the Int64 limit");
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
                Check(summary.Format(strings) == expected,
                    "duration, grouped bytes and unknown sizes are localized / " + language.CultureName);
            }
            strings.SetCulture("en");
            Check(huge.Format(strings) == "Elapsed: 1.02:00:00.000 · Total file size: 18,446,744,073,709,551,614 logical bytes",
                "durations retain days and known byte totals have no unknown-size warning");
        }
        finally { strings.SetCulture("en"); }
    }

    private static void CheckFormats()
    {
        (string Hex, string Lower, string Colon, string Base64, string Url)[] vectors =
        [
            ("", "", "", "", ""), ("00FF", "00ff", "00:FF", "AP8=", "AP8"),
            ("FF", "ff", "FF", "/w==", "_w"), ("FBFFFF", "fbffff", "FB:FF:FF", "+///", "-___"),
            ("000102", "000102", "00:01:02", "AAEC", "AAEC")
        ];
        foreach (var (Hex, Lower, Colon, Base64, Url) in vectors)
        {
            Check(DigestFormatter.Format(Hex.ToLowerInvariant(), HashFormat.HexUpper) == Hex, "uppercase hex vector");
            Check(DigestFormatter.Format(Hex, HashFormat.HexLower) == Lower, "lowercase hex vector");
            Check(DigestFormatter.Format(Hex, HashFormat.HexColon) == Colon, "colon hex vector");
            Check(DigestFormatter.Format(Hex, HashFormat.Base64) == Base64, "Base64 byte vector / padding");
            Check(DigestFormatter.Format(Hex, HashFormat.Base64Url) == Url, "Base64url byte vector / no padding");
        }
        foreach (var size in new[] { 16, 20, 32, 48, 64 })
        {
            var hex = Convert.ToHexString(Enumerable.Range(0, size).Select(index => (byte)index).ToArray());
            var base64 = DigestFormatter.Format(hex, HashFormat.Base64);
            Check(Convert.ToHexString(Convert.FromBase64String(base64)) == hex, $"digest bytes survive encoding / {size}");
        }
    }
    private static void CheckRows()
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
        Check(row.SHA256 == base64 && row.CertificateSha256 == base64 && row.CertificateSha1 == "AP8=", "file and certificate digests share the format");
        Check(ReferenceEquals(row.Result, result) && result.Checksums[ChecksumAlgorithm.SHA256] == sha256,
            "formatting retains immutable analysis data");
        Check(row.Details.Contains(base64) && row.Matches(base64), "details and search use displayed digests");
        Check(row.MD5 == "" && row.SerialNumber == "00AB" && row.NativeCode == "0x00000000", "uncomputed hashes and non-hash fields remain unchanged");
        row.SetHashFormat(HashFormat.HexLower);
        Check(row.SHA256 == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "hex case switches without analysis");
        row.SetHashFormat(HashFormat.HexUpper);
        Check(row.SHA256 == sha256, "hex round-trip preserves original digest");
        row.Reset();
        Check(row.SHA256 == "" && row.CertificateSha1 == "", "reset clears formatted caches");
        row.SetHashFormat(HashFormat.Base64);
        row.Complete(result);
        Check(row.SHA256 == base64, "in-flight/future results use the most recent format");
    }
    private static void CheckMetadata()
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
            Check(row.Matches(value) && row.Details.Contains(value), "metadata is included in search and details / " + value);
        foreach (var language in strings.Languages)
        {
            strings.SetCulture(language.CultureName); row.RefreshLanguage();
            Check(row.ManagedPlatformText == strings["ManagedPlatform_Prefer32Bit"] && !row.ManagedPlatformText.StartsWith('['),
                "platform annotation translates / " + language.CultureName);
        }
        row.SetHashFormat(HashFormat.Base64);
        Check(row.SHA256 == "AP8=" && ReferenceEquals(row.Result, result) && row.FileVersion == "1.2.3.4"
            && row.PeMachine == "i386 (0x014C)", "hash format does not modify version or machine values");
        row.Complete(result with { Properties = new(null, "Version read failed"), PeHeader = new(null, "Header read failed") });
        Check(row.State == RowStatus.Completed && row.FileVersion == "" && row.PeMachine == ""
            && row.Diagnostic.Contains("Version read failed") && row.Diagnostic.Contains("Header read failed")
            && row.SHA256 == "AP8=", "metadata errors leave the row usable and expose both diagnostics");
        row.Complete(result with { Properties = null, PeHeader = new(null) });
        Check(row.FileDescription == "" && row.FileType == "" && row.PeMachine == "" && row.PeFormat == ""
            && row.ManagedPlatformText == "" && row.Diagnostic == "", "disabled or non-PE metadata remains empty");
        row.Complete(result with { PeHeader = new(new PeHeaderInfo(0xffff, "0xFFFF", "PE32", ManagedPlatform.None)) });
        Check(row.PeMachine == "0xFFFF" && row.ManagedPlatformText == "", "unknown native machine code remains visible without duplication");
        row.Complete(result with { PeHeader = new(new PeHeaderInfo(null, "x86 (16-bit)", "NE (Win16)", ManagedPlatform.None)) });
        Check(row.PeMachine == "x86 (16-bit)" && row.PeFormat == "NE (Win16)" && row.ManagedPlatformText == "",
            "Win16 display has neither a fictitious COFF code nor a .NET platform");
        Check(row.Matches("Win16") && row.Details.Contains("NE (Win16)"), "Win16 header appears in search and details");
        var legacyColumns = ColumnOption.CreateDefaults(strings);
        foreach (var column in legacyColumns) column.IsExportSelected = column.Id is "PeMachine" or "PeFormat";
        var legacyExport = ResultExportProjection.Capture([row], legacyColumns);
        Check(legacyExport.Columns.Select(column => column.Id).SequenceEqual(["PeMachine", "PeFormat"])
            && legacyExport.Rows[0].SequenceEqual(["x86 (16-bit)", "NE (Win16)"]),
            "CSV/JSON projection includes NE metadata under the existing stable column IDs");
        row.Reset();
        Check(row.FileVersion == "" && row.ManagedPlatformText == "" && !row.Matches("Example product"), "reset clears all metadata");
        row.Start();
        row.SetProgress(new(AnalysisStage.ReadingMetadata, 1, 1), 95);
        Check(row.StateText == strings["ReadingMetadata"] && !row.StateText.StartsWith('['), "metadata reading has a localized progress label");
        strings.SetCulture("en");
    }

    private static void CheckSignatures()
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
        Check(row.HasMultipleSignatures && row.Signatures.Count == 4 && row.SelectedSignature == row.Signatures[0],
            "all signatures are selectable, including repeated certificates and failed entries");
        Check(row.SigningTime == firstDate && row.SigningTimeText.Contains("verified") && row.Publisher == "First publisher",
            "primary timestamp and certificate belong to the same signature");
        var changes = 0;
        row.SignatureSelectionChanged += (_, _) => changes++;
        row.SetHashFormat(HashFormat.Base64);
        row.SelectedSignature = row.Signatures[1];
        Check(changes == 1 && row.SignatureState == SignatureStatus.Untrusted && row.NativeCode == "0x800B0109"
            && row.Publisher == "Second publisher" && row.SerialNumber == "02" && row.CertificateSha1 == "/w==",
            "selection switches verification status, certificate fields and formatted fingerprints together");
        Check(row.ValidFromDate == second.NotBefore && row.ValidUntilDate == second.NotAfter,
            "date sorting follows the selected certificate");
        Check(row.SigningTime is null && row.SigningTimeText == strings["Timestamp_NotPresent"],
            "missing timestamp does not reuse the previous signature's date");
        Check(row.SHA256 == "AP8=" && ReferenceEquals(result, row.Result), "signature selection preserves file hashes and analysis result");
        Check(row.Details.Contains("Second publisher") && row.Details.Contains("Second diagnostic") && row.Matches("Second publisher")
            && !row.Matches("First publisher"), "details and search follow the selected signature");
        var exportColumns = ColumnOption.CreateDefaults(strings);
        var signatureExport = ResultExportProjection.Capture([row], exportColumns);
        var columnIds = exportColumns.Select(column => column.Id).ToList();
        Check(signatureExport.Rows[0][columnIds.IndexOf("Publisher")] == "Second publisher"
            && signatureExport.Rows[0][columnIds.IndexOf("Serial")] == "02"
            && signatureExport.Rows[0][columnIds.IndexOf("CertSha1")] == "/w=="
            && signatureExport.Rows[0][columnIds.IndexOf("SigningTime")] == strings["Timestamp_NotPresent"],
            "CSV snapshot uses certificate, timestamp and fingerprint from the selected signature");
        row.SelectedSignature = row.Signatures[2];
        Check(row.SigningTimeText.Contains(strings["Timestamp_Unverified"]) && row.SigningTime == firstDate.AddDays(1),
            "unconfirmed timestamp remains explicitly labelled");
        foreach (var language in strings.Languages)
        {
            strings.SetCulture(language.CultureName);
            row.RefreshLanguage();
            Check(row.SelectedSignature.Label.StartsWith(strings.Format("SignatureChoice", 3), StringComparison.CurrentCulture)
                && !row.SigningTimeText.Contains('['), "signature labels and timestamp status translate / " + language.CultureName);
        }
        row.SelectedSignature = row.Signatures[3];
        Check(row.Publisher == "" && row.CertificateSha1 == "" && row.NativeCode == ""
            && row.SigningTimeText == strings["Timestamp_Unreadable"], "failed entry never borrows primary metadata");
        row.Reset();
        Check(!row.HasMultipleSignatures && row.Signatures.Count == 0 && row.SelectedSignature is null
            && row.SigningTime is null && row.SigningTimeText == "", "reset removes all signature state");
        strings.SetCulture("en");
    }

    private static void CheckOptionsWindowPreferences()
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
            Check(options.OptionsWindowPreferences is null && options.WindowPreferences == main,
                "old settings retain independent options window defaults");
            options.SaveOptionsWindowPreferences(settings);
            options.SelectedCsvDelimiter = options.CsvDelimiters.Single(option => option.Value == CsvDelimiter.Comma);
            options.SaveWindowPreferences(main);
            Check(store.Load().OptionsWindow == settings && store.Load().Window == main,
                "options size/state survives other saves without changing main window geometry");
            foreach (var invalid in new[] { new WindowPreferences(double.NaN, 800, false), new(-1, 800, false), new(800, double.PositiveInfinity, false), new(800, 0, false) })
                options.SaveOptionsWindowPreferences(invalid);
            using var reopened = new OptionsViewModel(strings, store, new RecordingThemeService());
            Check(reopened.OptionsWindowPreferences == settings && reopened.WindowPreferences == main,
                "options window geometry survives JSON save/reload and invalid dimensions are ignored");
            var restored = WindowSize.Restore(settings, 730, 740, 640, 600, 800, 700);
            Check(restored is { Width: 800, Height: 700 }, "options window fits a smaller work area on restore");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static void CheckWindowSizes()
    {
        static WindowSize Restore(WindowPreferences? saved, double width = 1920, double height = 1040) =>
            WindowSize.Restore(saved, 1440, 900, 1080, 680, width, height);
        Check(Restore(null) is { Width: 1440, Height: 900 }, "old settings without window data use the default size");
        Check(Restore(new(1234, 789, true)) is { Width: 1234, Height: 789 }, "saved normal dimensions are preserved");
        Check(Restore(new(9000, 9000, false)) is { Width: 1920, Height: 1040 }, "oversized window is clamped to the new work area");
        Check(Restore(new(200, 100, false)) is { Width: 1080, Height: 680 }, "tiny saved size respects the minimum");
        Check(Restore(new(double.NaN, double.PositiveInfinity, false)) is { Width: 1440, Height: 900 }, "non-finite dimensions are ignored");
        Check(Restore(new(-10, 0, false)) is { Width: 1440, Height: 900 }, "non-positive dimensions are ignored");
        Check(Restore(null, 800, 600) is { Width: 800, Height: 600, MinWidth: 800, MinHeight: 600 },
            "small primary monitor also reduces minimum bounds so the window fits");
    }

    private static void CheckOptions()
    {
        var strings = LocalizationService.Instance;
        strings.SetCulture("en");
        var directory = Path.Combine(Path.GetTempPath(), "FileScope-options-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        var store = new PreferencesStore(path);
        try
        {
            using var options = new OptionsViewModel(strings, store, new RecordingThemeService());
            Check(options.AnalysisPreferences == new AnalysisPreferences(), "missing checkbox settings retain first-run defaults");
            Check(options.SelectedCsvDelimiter.Value == CsvDelimiter.Semicolon, "old settings default to semicolon CSV");
            options.SelectedCsvDelimiter = options.CsvDelimiters.Single(option => option.Value == CsvDelimiter.Comma);
            Check(options.Columns.All(column => column.IsExportSelected), "older preferences select all export columns initially");
            options.ClearExportColumnsCommand.Execute(null);
            Check(!options.HasExportColumns && store.Load().ExportColumns is { Length: 0 }, "explicit empty export selection is saved");
            using (var emptyExport = new OptionsViewModel(strings, store, new RecordingThemeService()))
                Check(!emptyExport.HasExportColumns, "an empty export selection survives a restart");
            foreach (var column in options.Columns) column.IsExportSelected = column.Id is "Name" or "SHA256" or "PeMachine";
            var selection = new AnalysisPreferences
            {
                MD5 = true, SHA1 = true, SHA256 = false, SHA384 = true, SHA512 = false,
                InspectCertificates = false, OnlineRevocation = true, InspectFileProperties = true, InspectPeHeader = true
            };
            options.SaveAnalysisPreferences(selection);
            Check(options.Columns.Count == 39 && options.Columns.Select(column => column.Id).Distinct().Count() == 39,
                "all columns have unique persistent IDs");
            Check(options.Columns.Take(3).Select(column => column.Id).SequenceEqual(["Name", "Status", "Diagnostic"]),
                "default column order puts diagnostics immediately after analysis status");
            Check(!options.MoveUpCommand.CanExecute(null), "cannot move the first column above the start");
            var selected = options.Columns.Single(column => column.Id == "SHA512");
            options.SelectedColumn = selected;
            options.MoveFirstCommand.Execute(null);
            Check(options.Columns[0] == selected && options.SelectedColumn == selected, "move to top keeps selection");
            options.MoveLastCommand.Execute(null);
            Check(options.Columns[^1] == selected && !options.MoveDownCommand.CanExecute(null), "move to bottom respects boundary");
            options.MoveUpCommand.Execute(null);
            options.MoveDownCommand.Execute(null);
            Check(options.Columns[^1] == selected, "up/down preserve a valid permutation");
            options.SetColumnOrder(["Issuer", "SHA512", "Issuer", "missing-column"]);
            Check(options.Columns[0].Id == "Issuer" && options.Columns[1].Id == "SHA512" && options.Columns.Count == 39,
                "restore ignores duplicates/unknown IDs and appends missing columns");
            var widthRevision = options.WidthRevision;
            var formatChanges = 0;
            options.HashFormatChanged += (_, _) => formatChanges++;
            options.SelectedHashFormat = options.HashFormats.Single(format => format.Value == HashFormat.Base64Url);
            Check(formatChanges == 1 && options.WidthRevision != widthRevision, "format change publishes a sizing request");
            foreach (var culture in options.Languages)
            {
                options.SelectedLanguage = culture;
                Check(options.HashFormats.All(format => !format.Label.StartsWith('['))
                    && options.CsvDelimiters.All(delimiter => !delimiter.Label.StartsWith('['))
                    && options.Columns.All(column => !column.Label.StartsWith('[')), "translated option labels / " + culture.CultureName);
                Check(options.SelectedCsvDelimiter.Value == CsvDelimiter.Comma, "language changes retain the CSV delimiter");
            }
            options.SelectedLanguage = options.Languages.Single(language => language.CultureName == "de");
            var window = new WindowPreferences(1270, 810, true);
            options.SaveWindowPreferences(window);
            options.SelectedHashFormat = options.HashFormats.Single(format => format.Value == HashFormat.Base64Url);
            options.SelectedLanguage = options.Languages.Single(language => language.CultureName == "en");
            options.SelectedLanguage = options.Languages.Single(language => language.CultureName == "de");
            Check(store.Load().Window == window, "changing UI options preserves saved window size/state");
            Check(store.Load().Language == "de" && store.Load().HashFormat == nameof(HashFormat.Base64Url),
                "saving window state preserves language and hash format");
            options.SaveWindowPreferences(new(double.NaN, 0, false));
            Check(store.Load().Window == window, "invalid geometry never replaces usable saved settings");
            var savedOrder = options.Columns.Select(column => column.Id).ToArray();
            using (var reopened = new OptionsViewModel(strings, store, new RecordingThemeService()))
            {
                Check(reopened.SelectedCsvDelimiter.Value == CsvDelimiter.Comma,
                    "CSV delimiter survives JSON save/load and all other preference changes");
                Check(reopened.AnalysisPreferences == selection,
                    "all checkbox states survive JSON save/load and other option changes");
                Check(reopened.Columns.Where(column => column.IsExportSelected).Select(column => column.Id)
                    .SequenceEqual(["Name", "SHA256", "PeMachine"]),
                    "export field selection survives language, format, order, analysis and window saves");
                Check(reopened.WindowPreferences == window, "normal size and maximized state survive a restart");
                Check(reopened.Columns.Select(column => column.Id).SequenceEqual(savedOrder), "column order survives a restart");
                Check(reopened.SelectedLanguage.CultureName == "de" && reopened.SelectedHashFormat.Value == HashFormat.Base64Url,
                    "language and hash format survive a restart");
                reopened.ResetOrderCommand.Execute(null);
                Check(reopened.Columns.Count(column => column.IsExportSelected) == 3, "resetting column order retains export selection");
                Check(reopened.Columns.Select(column => column.Id).SequenceEqual(ColumnOption.CreateDefaults(strings).Select(column => column.Id)),
                    "reset restores the entire default order");
            }
            Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "settings save leaves no temporary files");
            var none = new AnalysisPreferences { SHA256 = false, InspectCertificates = false };
            options.SaveAnalysisPreferences(none);
            using (var reopened = new OptionsViewModel(strings, store, new RecordingThemeService()))
                Check(reopened.AnalysisPreferences == none && reopened.WindowPreferences == window
                    && reopened.SelectedLanguage.CultureName == "de" && reopened.SelectedHashFormat.Value == HashFormat.Base64Url,
                    "all checkboxes off is retained without overwriting window/language/format settings");
            File.WriteAllText(path, "{\"Analysis\":{\"MD5\":true,\"SHA256\":false}}");
            using (var partial = new OptionsViewModel(strings, store, new RecordingThemeService()))
                Check(partial.AnalysisPreferences is { MD5: true, SHA256: false, InspectCertificates: true, OnlineRevocation: false, InspectFileProperties: false, InspectPeHeader: false },
                    "missing individual fields get defaults while explicit false is respected");
            File.WriteAllText(path, "{ invalid JSON");
            using (var fallback = new OptionsViewModel(strings, store, new RecordingThemeService()))
                Check(fallback.HasSettingsError && fallback.Columns.Count == 39, "corrupt settings fall back without blocking the app");
            store.Save(new UserPreferences { Language = "invalid", HashFormat = "999", CsvDelimiter = "999", ColumnOrder = ["SHA256", "SHA256"] });
            using (var normalized = new OptionsViewModel(strings, store, new RecordingThemeService()))
                Check(normalized.SelectedHashFormat.Value == HashFormat.HexUpper && normalized.Columns.Count == 39
                    && normalized.SelectedCsvDelimiter.Value == CsvDelimiter.Semicolon
                    && normalized.AnalysisPreferences == new AnalysisPreferences(),
                    "invalid saved enum and duplicate columns are normalized");
            store.Save(new UserPreferences { ExportColumns = ["Name", "Name", "future-column"] });
            using (var normalized = new OptionsViewModel(strings, store, new RecordingThemeService()))
            {
                Check(normalized.Columns.Single(column => column.IsExportSelected).Id == "Name",
                    "unknown/duplicate export IDs do not enable unintended fields");
                normalized.SelectAllExportColumnsCommand.Execute(null);
                Check(store.Load().ExportColumns?.Length == 39, "select all saves every supported export field");
            }
            var previousOrder = ColumnOption.CreateDefaults(strings).Select(column => column.Id)
                .Where(id => id != "Diagnostic").Append("Diagnostic").ToArray();
            store.Save(new UserPreferences { ColumnOrder = previousOrder });
            using (var previous = new OptionsViewModel(strings, store, new RecordingThemeService()))
            {
                Check(previous.Columns.Select(column => column.Id).SequenceEqual(previousOrder),
                    "a saved column order is retained when the defaults change");
                previous.ResetOrderCommand.Execute(null);
                Check(previous.Columns.Take(3).Select(column => column.Id).SequenceEqual(["Name", "Status", "Diagnostic"])
                    && store.Load().ColumnOrder?.Take(3).SequenceEqual(["Name", "Status", "Diagnostic"]) == true,
                    "resetting an older column order applies and saves the new diagnostic position");
            }
            using var failing = new OptionsViewModel(strings, new FailingStore(), new RecordingThemeService());
            var moved = failing.Columns[^1];
            failing.SelectedColumn = moved;
            failing.MoveFirstCommand.Execute(null);
            Check(failing.HasSettingsError && failing.Columns[0] == moved, "save failures keep options usable in memory");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); strings.SetCulture("en"); }
    }
    private static void CheckExportProjection()
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
        Check(all.Columns.Count == 39 && all.Rows.Count == 2 && all.Rows.All(values => values.Count == 39),
            "every registered column can export completed and failed rows");
        Check(all.Rows[0][ids.IndexOf("SHA256")] == "AP8=" && all.Rows[0][ids.IndexOf("FileVersion")] == "01.02.003"
            && all.Rows[0][ids.IndexOf("PeMachine")] == "AMD64 (x64) (0x8664)"
            && all.Rows[0][ids.IndexOf("Comments")] == "with, comma\nand \"quotes\"",
            "projection preserves current hash format, architecture, version zeros and multiline metadata");
        Check(all.Rows[1][ids.IndexOf("SHA256")] == "" && all.Rows[1][ids.IndexOf("Diagnostic")].Contains("missing fixture"),
            "failed rows export blank missing data and their diagnostic");
        foreach (var column in columns) column.IsExportSelected = column.Id is "Name" or "SHA256" or "PeMachine";
        var selected = ResultExportProjection.Capture([row, failed], columns.Reverse());
        Check(selected.Columns.Select(column => column.Header).SequenceEqual(new[] { strings["Column_PeMachine"], "SHA-256", strings["Column_Name"] })
            && selected.Rows[0].SequenceEqual(["AMD64 (x64) (0x8664)", "AP8=", "Prüfung 中文.exe"]),
            "export selection and ordering are independent of grid visibility");
        row.Reset();
        strings.SetCulture("zh-Hans");
        Check(selected.Rows[0][1] == "AP8=" && selected.Columns[2].Header == "File name",
            "captured data and headers do not change after reset or a language switch");
        Check(selected.Columns.Select(column => column.Id).SequenceEqual(["PeMachine", "SHA256", "Name"]),
            "snapshot retains stable JSON IDs in the selected field order");
        var translated = ResultExportProjection.Capture([row], columns);
        Check(translated.Columns.Select(column => column.Id).SequenceEqual(["Name", "SHA256", "PeMachine"]),
            "JSON field IDs remain stable across language changes");
        Check(translated.Columns[0].Header == strings["Column_Name"] && translated.Rows[0][1] == "",
            "a fresh export uses the current language and current results");
        foreach (var column in columns) column.IsExportSelected = false;
        try { ResultExportProjection.Capture([row], columns); throw new Exception("Expected empty selection failure."); }
        catch (InvalidOperationException) { Check(true, "projection rejects exporting zero fields"); }
        strings.SetCulture("en");
    }

    private static void CheckThemePreferences()
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
        Check(options.SelectedTheme.Value == AppTheme.Dark && service.ApplyCount == 1 && store.SaveCount == 0
            && options.Themes.Select(theme => theme.Value).SequenceEqual([AppTheme.Light, AppTheme.Dark]),
            "older settings retain the current application theme without writing preferences");
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            var restoredStore = new ThemePreferencesStore(original with { Theme = theme.ToString() });
            var restoredService = new RecordingThemeService(theme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
            using var restored = new OptionsViewModel(strings, restoredStore, restoredService);
            Check(restored.SelectedTheme.Value == theme && restoredService.CurrentTheme == theme
                && restoredService.ApplyCount == 1 && restoredStore.SaveCount == 0,
                "saved theme is applied during construction without a settings write / " + theme);
        }
        foreach (var invalid in new string?[] { null, "", "unknown-theme", "999" })
        {
            var fallbackStore = new ThemePreferencesStore(original with { Theme = invalid });
            var fallbackService = new RecordingThemeService(AppTheme.Dark);
            using var fallback = new OptionsViewModel(strings, fallbackStore, fallbackService);
            Check(fallback.SelectedTheme.Value == AppTheme.Dark && fallbackService.CurrentTheme == AppTheme.Dark
                && fallbackStore.SaveCount == 0, "missing or invalid saved themes use the current appearance / " + invalid);
        }
        options.SelectedTheme = options.Themes.Single(theme => theme.Value == AppTheme.Light);
        Check(service.CurrentTheme == AppTheme.Light && store.Saved.Theme == "Light" && store.SaveCount == 1,
            "changing only the theme applies and saves it immediately");
        Check(store.Saved.Language == original.Language && store.Saved.HashFormat == original.HashFormat
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
        Check(service.ApplyCount == applyCount && store.SaveCount == saveCount && notifications == 0,
            "selecting the current theme does not apply, save or notify again");
        options.SelectedTheme = null!;
        options.SelectedTheme = new FilterOption<AppTheme>(AppTheme.Dark, "Theme_Dark", strings);
        options.SelectedTheme = new FilterOption<AppTheme>((AppTheme)999, "Theme_Dark", strings);
        Check(service.ApplyCount == applyCount && store.SaveCount == saveCount && options.SelectedTheme.Value == AppTheme.Light,
            "null and foreign theme choices cannot bypass the registered options");
        service.FailApply = true;
        var rejected = false;
        try { options.SelectedTheme = options.Themes.Single(theme => theme.Value == AppTheme.Dark); }
        catch (InvalidOperationException) { rejected = true; }
        finally { service.FailApply = false; }
        Check(rejected && options.SelectedTheme.Value == AppTheme.Light && store.Saved.Theme == "Light"
            && service.ApplyCount == applyCount && store.SaveCount == saveCount && notifications == 0,
            "a failed application does not publish or persist a theme that was not applied");
        store.FailWrites = true;
        options.SelectedTheme = options.Themes.Single(theme => theme.Value == AppTheme.Dark);
        Check(service.CurrentTheme == AppTheme.Dark && options.SelectedTheme.Value == AppTheme.Dark
            && options.HasSettingsError && store.Saved.Theme == "Light",
            "a settings write failure retains the live theme and reports the unsaved preference");
        store.FailWrites = false;
        options.SelectedTheme = options.Themes.Single(theme => theme.Value == AppTheme.Light);
        Check(!options.HasSettingsError && store.Saved.Theme == "Light" && service.CurrentTheme == AppTheme.Light,
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
                Check(options.Themes.Select(theme => theme.Label).SequenceEqual(expected)
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
                Check(restarted.SelectedTheme.Value == AppTheme.Dark && restartService.CurrentTheme == AppTheme.Dark
                    && restarted.AnalysisPreferences == selection && restarted.WindowPreferences == original.Window
                    && restarted.OptionsWindowPreferences == original.OptionsWindow,
                    "a theme-only change survives a real JSON round trip together with other settings");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        finally { strings.SetCulture("en"); }
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

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        _passed++;
        Console.WriteLine("PASS: " + label);
    }
}
