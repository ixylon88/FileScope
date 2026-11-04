using System.Text.Json;
using FileScope.App.Localization;
using FileScope.App.Presentation;
using FileScope.App.Services;
using FileScope.App.ViewModels;
using FileScope.Core.Models;

internal static class PresentationChecks
{
    private static int _passed;
    private static int Main()
    {
        try
        {
            CheckFormats();
            CheckRows();
            CheckOptions();
            Console.WriteLine($"PASS: {_passed} presentation / settings assertions.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private static void CheckFormats()
    {
        (string Hex, string Lower, string Colon, string Base64, string Url)[] vectors =
        [
            ("", "", "", "", ""), ("00FF", "00ff", "00:FF", "AP8=", "AP8"),
            ("FF", "ff", "FF", "/w==", "_w"), ("FBFFFF", "fbffff", "FB:FF:FF", "+///", "-___"),
            ("000102", "000102", "00:01:02", "AAEC", "AAEC")
        ];
        foreach (var vector in vectors)
        {
            Check(DigestFormatter.Format(vector.Hex.ToLowerInvariant(), HashFormat.HexUpper) == vector.Hex, "uppercase hex vector");
            Check(DigestFormatter.Format(vector.Hex, HashFormat.HexLower) == vector.Lower, "lowercase hex vector");
            Check(DigestFormatter.Format(vector.Hex, HashFormat.HexColon) == vector.Colon, "colon hex vector");
            Check(DigestFormatter.Format(vector.Hex, HashFormat.Base64) == vector.Base64, "Base64 byte vector / padding");
            Check(DigestFormatter.Format(vector.Hex, HashFormat.Base64Url) == vector.Url, "Base64url byte vector / no padding");
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
        Check(row.SHA256 == sha256.ToLowerInvariant(), "hex case switches without analysis");
        row.SetHashFormat(HashFormat.HexUpper);
        Check(row.SHA256 == sha256, "hex round-trip preserves original digest");
        row.Reset();
        Check(row.SHA256 == "" && row.CertificateSha1 == "", "reset clears formatted caches");
        row.SetHashFormat(HashFormat.Base64);
        row.Complete(result);
        Check(row.SHA256 == base64, "in-flight/future results use the most recent format");
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
            using var options = new OptionsViewModel(strings, store);
            Check(options.Columns.Count == 22 && options.Columns.Select(column => column.Id).Distinct().Count() == 22,
                "all columns have unique persistent IDs");
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
            Check(options.Columns[0].Id == "Issuer" && options.Columns[1].Id == "SHA512" && options.Columns.Count == 22,
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
                    && options.Columns.All(column => !column.Label.StartsWith('[')), "translated option labels / " + culture.CultureName);
            }
            options.SelectedLanguage = options.Languages.Single(language => language.CultureName == "de");
            var savedOrder = options.Columns.Select(column => column.Id).ToArray();
            using (var reopened = new OptionsViewModel(strings, store))
            {
                Check(reopened.Columns.Select(column => column.Id).SequenceEqual(savedOrder), "column order survives a restart");
                Check(reopened.SelectedLanguage.CultureName == "de" && reopened.SelectedHashFormat.Value == HashFormat.Base64Url,
                    "language and hash format survive a restart");
                reopened.ResetOrderCommand.Execute(null);
                Check(reopened.Columns.Select(column => column.Id).SequenceEqual(ColumnOption.CreateDefaults(strings).Select(column => column.Id)),
                    "reset restores the entire default order");
            }
            Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "settings save leaves no temporary files");
            File.WriteAllText(path, "{ invalid JSON");
            using (var fallback = new OptionsViewModel(strings, store))
                Check(fallback.HasSettingsError && fallback.Columns.Count == 22, "corrupt settings fall back without blocking the app");
            store.Save(new UserPreferences { Language = "invalid", HashFormat = "999", ColumnOrder = ["SHA256", "SHA256"] });
            using (var normalized = new OptionsViewModel(strings, store))
                Check(normalized.SelectedHashFormat.Value == HashFormat.HexUpper && normalized.Columns.Count == 22,
                    "invalid saved enum and duplicate columns are normalized");
            using (var failing = new OptionsViewModel(strings, new FailingStore()))
            {
                failing.SelectedColumn = failing.Columns[^1];
                failing.MoveFirstCommand.Execute(null);
                Check(failing.HasSettingsError && failing.Columns[0].Id == "Diagnostic", "save failures keep options usable in memory");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); strings.SetCulture("en"); }
    }
    private sealed class FailingStore : IPreferencesStore
    {
        public UserPreferences Load() => new();
        public void Save(UserPreferences preferences) => throw new UnauthorizedAccessException("Test write failure");
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        _passed++;
        Console.WriteLine("PASS: " + label);
    }
}
