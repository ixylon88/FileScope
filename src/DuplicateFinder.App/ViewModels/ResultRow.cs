using DuplicateFinder.Core.Models;
using FileScope.Core.Models;

namespace DuplicateFinder.App.ViewModels;

public sealed class ResultRow(FoundFile file, HashFormat hashFormat = HashFormat.HexUpper) : ObservableObject
{
    private static LocalizationService Strings => LocalizationService.Instance;
    private HashFormat _hashFormat = hashFormat;
    private IReadOnlyDictionary<ChecksumAlgorithm, string> _displayHashes = DigestFormatter.FormatAll(file.Hashes, hashFormat);
    private string? CachedDetails { get; set; }

    public FoundFile File { get; } = file;
    public bool Marked { get; set => SetProperty(ref field, value); }
    internal string? RecycleError { get; init; }
    public string? Diagnostic => RecycleError ?? File.Diagnostic;

    public int CurrentGroup
    {
        get;
        internal set
        {
            if (field == value) return;
            field = value;
            CachedDetails = null;

            OnPropertyChanged();
            OnPropertyChanged(nameof(GroupTitle));
            OnPropertyChanged(nameof(Streams));
            OnPropertyChanged(nameof(Details));
        }
    }

    public string GroupTitle => CurrentGroup == 0 ? Strings["Singles"] : Strings.Format("GroupNumber", File.Group);
    public string Streams => Strings["Streams_" + (CurrentGroup == 0 ? StreamComparison.Unmatched : File.Streams)];
    public StatusTone SignatureTone => SignatureGroup.ToneFor(File.Embedded.Status);
    public string Signature => SignatureGroup.From(File.Embedded.Status, File.Embedded.RevocationMode)?.Format(Strings)
        ?? Strings["Signature_CheckFailed"];
    public string Sha256 => _displayHashes.GetValueOrDefault(ChecksumAlgorithm.SHA256) ?? "";
    public string ModifiedText => File.ModifiedUtc is { } utc ? $"{utc.ToString("g", Strings.Culture)} UTC" : "";
    public CatalogPresentation CatalogResult { get; } = CatalogPresentation.From(file.Catalogs);
    public IEnumerable<SignatureBadgeItem> CatalogBadges => CatalogResult.Badges(Strings);
    public string Catalog => CatalogResult.Summary(Strings);

    public string Details => CachedDetails ??= BuildDetails();

    private string BuildDetails()
    {
        var lines = new List<string> { File.Path, $"{Strings["Folder"]} · {File.Root.Path} ({File.Root.Priority})" };
        if (File.ModifiedUtc.HasValue) lines.Add($"{Strings["Modified"]} · {ModifiedText}");
        lines.Add($"{Streams} · {Strings["Variant"]} {File.Variant}");
        lines.Add($"{Strings["SignatureFilterLabel"]} · {Signature}");

        if (File.IsHardLink) lines.Add(Strings["HardLink"]);
        if (File.Diagnostic != null) lines.Add(File.Diagnostic);
        if (RecycleError != null) lines.Add(RecycleError);
        if (File.Embedded.Diagnostic != null) lines.Add(File.Embedded.Diagnostic);
        lines.AddRange(File.Embedded.Signatures.Select(FormatSigner));

        lines.Add($"{Strings["Column_Catalog"]} · {Catalog}");
        if (File.Catalogs is { } lookup) lines.AddRange(CatalogPresentation.Details(lookup, Strings));

        lines.AddRange(_displayHashes.Select(pair => $"{pair.Key}  {pair.Value}"));
        return string.Join(Environment.NewLine, lines);
    }

    private string FormatSigner(EmbeddedSignature signer)
    {
        var status = SignatureGroup.From(signer.Status, File.Embedded.RevocationMode)?.Format(Strings);
        var thumbprint = DigestFormatter.Format(signer.Certificate?.ThumbprintSha256, _hashFormat);
        return $"{signer.Index} · {status} · {signer.Certificate?.Publisher} · {thumbprint}";
    }

    internal void SetHashFormat(HashFormat format)
    {
        if (_hashFormat == format) return;
        _displayHashes = DigestFormatter.FormatAll(File.Hashes, format);
        _hashFormat = format;
        CachedDetails = null;

        OnPropertyChanged(nameof(Sha256));
        OnPropertyChanged(nameof(Details));
    }

    internal bool MatchesHash(string text) =>
        DigestSearch.MatchesAny(File.Hashes.Values, _displayHashes.Values, _hashFormat, text);

    internal void Relocalize()
    {
        CachedDetails = null;
        OnPropertyChanged(string.Empty);
    }
}
