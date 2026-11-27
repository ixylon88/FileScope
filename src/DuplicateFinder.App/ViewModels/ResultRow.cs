using DuplicateFinder.Core.Models;
using FileScope.Core.Models;

namespace DuplicateFinder.App.ViewModels;

public sealed class ResultRow(FoundFile file, HashFormat hashFormat = HashFormat.HexUpper) : ObservableObject
{
    private static LocalizationService Strings => LocalizationService.Instance;
    private HashFormat _hashFormat = hashFormat;
    private IReadOnlyDictionary<ChecksumAlgorithm, string> _displayHashes = FormatHashes(file, hashFormat);
    public FoundFile File { get; } = file;
    public bool Marked { get; set => SetProperty(ref field, value); }
    internal string? RecycleError { get; set; }
    public string? Diagnostic => RecycleError ?? File.Diagnostic;
    public int CurrentGroup { get; internal set; }
    public string GroupTitle => CurrentGroup == 0 ? Strings["Singles"] : Strings.Format("GroupNumber", File.Group);
    public string Streams => Strings["Streams_" + (CurrentGroup == 0 ? StreamComparison.Unmatched : File.Streams)];
    private static string FormatTrust(SignatureResult trust) =>
        SignatureGroup.From(trust.Status, trust.RevocationMode)?.Format(Strings) ?? Strings["Signature_CheckFailed"];
    public StatusTone SignatureTone => SignatureGroup.ToneFor(File.Embedded.Status);
    public string Signature => FormatTrust(File.Embedded);
    public string Sha256 => _displayHashes.GetValueOrDefault(ChecksumAlgorithm.SHA256) ?? "";
    public string ModifiedText => File.ModifiedUtc is { } utc ? $"{utc.ToString("g", Strings.Culture)} UTC" : "";
    public string Catalog => CatalogPresentation.Summary(File.Catalogs, Strings);

    public string Details
    {
        get
        {
            var lines = new List<string> { File.Path, $"{Strings["Folder"]} · {File.Root.Path} ({File.Root.Priority})" };
            if (File.ModifiedUtc.HasValue) lines.Add($"{Strings["Modified"]} · {ModifiedText}");
            lines.Add($"{Streams} · {Strings["Variant"]} {File.Variant}");
            lines.Add($"{Strings["SignatureFilterLabel"]} · {Signature}");
            if (File.IsHardLink) lines.Add(Strings["HardLink"]);
            if (File.Diagnostic != null) lines.Add(File.Diagnostic);
            if (RecycleError != null) lines.Add(RecycleError);
            if (File.Embedded.Diagnostic != null) lines.Add(File.Embedded.Diagnostic);
            foreach (var sig in File.Embedded.Signatures)
            {
                lines.Add($"{sig.Index} · {SignatureGroup.From(sig.Status, File.Embedded.RevocationMode)?.Format(Strings)} · {sig.Certificate?.Publisher} · {DigestFormatter.Format(sig.Certificate?.ThumbprintSha256, _hashFormat)}");
            }

            lines.Add($"{Strings["Column_Catalog"]} · {Catalog}");
            if (File.Catalogs is { } lookup)
            {
                lines.AddRange(CatalogPresentation.Details(lookup, Strings));
            }

            lines.AddRange(_displayHashes.Select(pair => $"{pair.Key}  {pair.Value}"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    private static IReadOnlyDictionary<ChecksumAlgorithm, string> FormatHashes(FoundFile source, HashFormat format) =>
        format == HashFormat.HexUpper || source.Hashes.Count == 0 ? source.Hashes :
            source.Hashes.ToDictionary(pair => pair.Key, pair => DigestFormatter.Format(pair.Value, format));

    internal void SetHashFormat(HashFormat format)
    {
        _hashFormat = format;
        _displayHashes = FormatHashes(File, format);
        OnPropertyChanged(nameof(Sha256));
        OnPropertyChanged(nameof(Details));
    }

    internal bool MatchesHash(string text) =>
        File.Hashes.Values.Any(hash => hash.Contains(text, StringComparison.OrdinalIgnoreCase)) ||
        (_hashFormat != HashFormat.HexUpper && _displayHashes.Values.Any(hash => hash.Contains(text,
            _hashFormat is HashFormat.Base64 or HashFormat.Base64Url ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)));

    internal void Relocalize() => OnPropertyChanged(string.Empty);
}
