using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using FileScope.App.Presentation;
using FileScope.Core.Models;

namespace FileScope.App.ViewModels;

public sealed class FileRowViewModel(string path, LocalizationService strings, HashFormat format = HashFormat.HexUpper) : ObservableObject
{
    private HashFormat _hashFormat = format;
    private IReadOnlyDictionary<ChecksumAlgorithm, string> _hashText = ReadOnlyDictionary<ChecksumAlgorithm, string>.Empty;
    private SignatureOption? _selectedSignature;
    private RevocationMode _runningRevocationMode;
    private LocalizedTextCache _details;
    private LocalizedTextCache _catalogDetails;

    public IReadOnlyList<SignatureOption> Signatures { get; private set; } = [];
    public bool HasMultipleSignatures => Signatures.Count > 1;
    public event EventHandler? SignaturesChanged;
    public event EventHandler? SignatureSelectionChanged;
    public SignatureOption? SelectedSignature
    {
        get => _selectedSignature;
        set
        {
            if (value is null || !Signatures.Contains(value) || ReferenceEquals(value, _selectedSignature)) return;
            _selectedSignature = value;
            RebuildCertificateHashText();
            RefreshRow();
            SignatureSelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private CertificateInfo? Certificate => SelectedSignature?.Value.Certificate ??
        (Signatures.Count == 0 ? Result?.Signature.Certificate : null);
    public string SignatureChoiceText => SelectedSignature?.Label ?? "";
    public DateTimeOffset? SigningTime => SelectedSignature?.Value.Timestamp.Time;
    public string SigningTimeText
    {
        get
        {
            if (SelectedSignature is null) return "";
            var timestamp = SelectedSignature.Value.Timestamp;
            if (timestamp.Status == TimestampStatus.NotPresent) return strings["Timestamp_NotPresent"];
            if (timestamp.Status == TimestampStatus.Unreadable) return strings["Timestamp_Unreadable"];
            var date = timestamp.Time?.UtcDateTime.ToString("G", strings.Culture) ?? "";
            return $"{date} UTC ({strings["Timestamp_" + timestamp.Status]})";
        }
    }
    public string FullPath { get; private set; } = path;
    public string FileName => GetName(FullPath);

    /// <summary>Keep the analysis and selected signer when a successful move changes only the path.</summary>
    public void Relocate(string fullPath)
    {
        FullPath = Path.GetFullPath(fullPath);
        if (Result is not null) Result = Result with { FullPath = FullPath };
        RefreshRow();
    }
    public FileAnalysisResult? Result { get; private set; }
    public RowStatus State { get; private set; } = RowStatus.Queued;
    public AnalysisStage Stage { get; private set; }
    public double Progress { get; private set; }
    public long? Length => Result?.Length;
    public string SizeText => Length?.ToString("N0", strings.Culture) ?? "";
    public string ModifiedText => FormatDate(Result?.LastWriteTime);
    public string MD5 => Hash(ChecksumAlgorithm.MD5);
    public string SHA1 => Hash(ChecksumAlgorithm.SHA1);
    public string SHA256 => Hash(ChecksumAlgorithm.SHA256);
    public string SHA384 => Hash(ChecksumAlgorithm.SHA384);
    public string SHA512 => Hash(ChecksumAlgorithm.SHA512);
    public string FileDescription => Result?.Properties?.Info?.FileDescription ?? "";
    public string FileLanguage => Result?.Properties?.Info?.Language ?? "";
    public string FileType => Result?.Properties?.Info?.FileType ?? "";
    public string FileVersion => Result?.Properties?.Info?.FileVersion ?? "";
    public string ProductName => Result?.Properties?.Info?.ProductName ?? "";
    public string ProductVersion => Result?.Properties?.Info?.ProductVersion ?? "";
    public string CompanyName => Result?.Properties?.Info?.CompanyName ?? "";
    public string OriginalFilename => Result?.Properties?.Info?.OriginalFilename ?? "";
    public string InternalName => Result?.Properties?.Info?.InternalName ?? "";
    public string FileCopyright => Result?.Properties?.Info?.Copyright ?? "";
    public string FileTrademarks => Result?.Properties?.Info?.Trademarks ?? "";
    public string FileComments => Result?.Properties?.Info?.Comments ?? "";
    public string PeMachine => Result?.PeHeader?.Header is { } header
        ? header.MachineCode is null || header.MachineName.StartsWith("0x", StringComparison.Ordinal)
            ? header.MachineName : $"{header.MachineName} (0x{header.MachineCode:X4})"
        : "";
    public string PeFormat => Result?.PeHeader?.Header?.Format ?? "";
    public string ManagedPlatformText => Result?.PeHeader?.Header is { } header
        ? header.ManagedPlatform switch
        {
            ManagedPlatform.AnyCpu => "AnyCPU",
            ManagedPlatform.Prefer32Bit => strings["ManagedPlatform_Prefer32Bit"],
            ManagedPlatform.MachineSpecific => header.MachineName,
            ManagedPlatform.None => "",
            _ => ""
        } : "";
    public SignatureStatus SignatureState => SelectedSignature?.Value.Status ?? Result?.Signature.Status ?? SignatureStatus.NotChecked;
    public string Publisher => Certificate?.Publisher ?? "";
    public string Subject => Certificate?.Subject ?? "";
    public string Issuer => Certificate?.Issuer ?? "";
    public string SerialNumber => Certificate?.SerialNumber ?? "";
    public string CertificateSha1 { get; private set; } = "";
    public string CertificateSha256 { get; private set; } = "";
    public DateTimeOffset? ValidFromDate => Certificate?.NotBefore;
    public DateTimeOffset? ValidUntilDate => Certificate?.NotAfter;
    public string ValidFrom => FormatDate(Certificate?.NotBefore);
    public string ValidUntil => FormatDate(Certificate?.NotAfter);
    public string CertificateAlgorithm => Certificate?.SignatureAlgorithm ?? "";
    public string NativeCode => (SelectedSignature is not null ? SelectedSignature.Value.NativeStatusCode : Result?.Signature.NativeStatusCode) is int code ? $"0x{unchecked((uint)code):X8}" : "";
    public string StateText => State == RowStatus.Running
        ? AnalysisStageText.Format(Stage, _runningRevocationMode, strings)
        : strings[$"Row_{State}"];
    public RevocationMode SignatureMode => Result?.Signature.RevocationMode ?? RevocationMode.OfflineCache;
    public string SignatureText => SignatureGroup.From(SignatureState, SignatureMode)?.Format(strings) ?? "?";
    public CatalogPresentation CatalogResult { get; private set; } = CatalogPresentation.From(null);
    public IEnumerable<SignatureBadgeItem> CatalogBadges => CatalogResult.Badges(strings);
    public string CatalogText => CatalogResult.Summary(strings);
    public string CatalogDetails => _catalogDetails[strings.Culture] ??= BuildCatalogDetails();

    private string BuildCatalogDetails() => Result?.Catalogs is { } lookup
        ? string.Join(Environment.NewLine, CatalogPresentation.Details(lookup, strings).Prepend(CatalogText)) : CatalogText;

    public string Diagnostic => Result is null ? "" : string.Join(" — ", new[]
    {
        Result.Error == AnalysisError.None ? null : strings[$"Error_{Result.Error}"],
        Result.Diagnostic, SelectedSignature?.Value.Diagnostic ?? (Signatures.Count == 0 ? Result.Signature.Diagnostic : null),
        MetadataDiagnostic("PropertiesError", Result.Properties?.Diagnostic),
        MetadataDiagnostic("PeHeaderError", Result.PeHeader?.Diagnostic),
        CatalogPresentation.IncompleteDiagnostic(Result.Catalogs, strings)
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string Details => _details[strings.Culture] ??= BuildDetails();

    private string BuildDetails()
    {
        var text = new StringBuilder();
        AppendField("Column_Path", FullPath);
        AppendField("Column_Status", StateText);
        AppendField("Column_Size", SizeText);
        AppendField("Column_Modified", ModifiedText);

        foreach (var algorithm in Enum.GetValues<ChecksumAlgorithm>())
        {
            var hash = Hash(algorithm);
            if (hash.Length != 0) text.AppendLine(CultureInfo.CurrentCulture, $"{algorithm}: {hash}");
        }

        AppendField("Column_SignatureChoice", SignatureChoiceText);
        AppendField("Column_Signature", SignatureText);
        AppendField("Column_Catalog", CatalogDetails);
        AppendField("Column_SigningTime", SigningTimeText);

        AppendField("Column_Publisher", Publisher);
        AppendField("Column_Subject", Subject);
        AppendField("Column_Issuer", Issuer);
        AppendField("Column_Serial", SerialNumber);
        AppendField("Column_CertSha1", CertificateSha1);
        AppendField("Column_CertSha256", CertificateSha256);
        AppendField("Column_ValidFrom", ValidFrom);
        AppendField("Column_ValidUntil", ValidUntil);
        AppendField("Column_CertAlgorithm", CertificateAlgorithm);
        AppendField("Column_NativeCode", NativeCode);

        foreach (var field in FileMetadataField.All) AppendField(field.LabelKey, field.Read(this));
        AppendField("Column_Diagnostic", Diagnostic);
        return text.ToString().TrimEnd();

        void AppendField(string key, string value)
        {
            if (value.Length != 0) text.AppendLine(CultureInfo.CurrentCulture, $"{strings[key]}: {value}");
        }
    }

    public void Reset()
    {
        State = RowStatus.Queued;
        Progress = 0;
        ApplyResult(null);
    }

    public void Start(RevocationMode revocationMode = RevocationMode.OfflineCache)
    {
        State = RowStatus.Running;
        Stage = AnalysisStage.OpeningFile;
        _runningRevocationMode = revocationMode;
        RefreshRow();
    }

    public void SetProgress(AnalysisProgress progress, double percent)
    {
        var percentChanged = Progress != percent;
        Progress = percent;

        if (Stage != progress.Stage)
        {
            Stage = progress.Stage;
            _details.Clear();
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(Details));
        }

        if (percentChanged) OnPropertyChanged(nameof(Progress));
    }

    public void Complete(FileAnalysisResult result)
    {
        State = result.Succeeded ? RowStatus.Completed : RowStatus.Failed;
        Progress = 100;
        ApplyResult(result);
    }

    public void Cancel()
    {
        State = RowStatus.Cancelled;
        RefreshRow();
    }

    public void RefreshLanguage()
    {
        _details.Clear();
        _catalogDetails.Clear();
        foreach (var signature in Signatures) signature.RefreshLanguage();
        OnPropertyChanged(null);
    }

    private void RefreshRow()
    {
        _details.Clear();
        OnPropertyChanged(null);
    }

    private void ApplyResult(FileAnalysisResult? result)
    {
        Result = result;
        CatalogResult = CatalogPresentation.From(result?.Catalogs);
        RebuildSignatures();
        RebuildHashText();

        _details.Clear();
        _catalogDetails.Clear();
        SignaturesChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(null);
    }

    private void RebuildSignatures()
    {
        var entries = Result?.Signature.Signatures ?? [];
        // Also support inspectors using the original single-signature contract.
        if (entries.Count == 0 && Result?.Signature is { Certificate: not null } single)
        {
            entries = [new EmbeddedSignature(0, single.Status, single.Certificate,
                single.NativeStatusCode, single.Diagnostic, "", SignatureTimestamp.NotPresent)];
        }

        Signatures = Array.AsReadOnly(entries.Select(entry => new SignatureOption(entry, strings)).ToArray());
        _selectedSignature = Signatures.Count > 0 ? Signatures[0] : default;
    }

    public void SetHashFormat(HashFormat format)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        if (_hashFormat == format) return;
        _hashFormat = format;
        RebuildHashText();
        RefreshRow();
    }

    private void RebuildHashText()
    {
        _hashText = DigestFormatter.FormatAll(Result?.Checksums, _hashFormat);
        RebuildCertificateHashText();
    }

    private void RebuildCertificateHashText()
    {
        CertificateSha1 = DigestFormatter.Format(Certificate?.ThumbprintSha1, _hashFormat);
        CertificateSha256 = DigestFormatter.Format(Certificate?.ThumbprintSha256, _hashFormat);
    }

    private bool MatchesHash(string query) => Result is not null &&
        (DigestSearch.MatchesAny(Result.Checksums.Values, _hashText.Values, _hashFormat, query)
            || DigestSearch.Matches(Certificate?.ThumbprintSha1, CertificateSha1, _hashFormat, query)
            || DigestSearch.Matches(Certificate?.ThumbprintSha256, CertificateSha256, _hashFormat, query));

    public bool Matches(string query)
    {
        bool Contains(string value) => value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

        if (query.Length == 0 || Contains(FullPath) || MatchesHash(query)) return true;
        if (Contains(SizeText) || Contains(ModifiedText) || Contains(StateText)) return true;

        if (Contains(SignatureText) || Contains(SignatureChoiceText) || Contains(SigningTimeText)
            || Contains(Publisher) || Contains(Subject) || Contains(Issuer) || Contains(SerialNumber)
            || Contains(ValidFrom) || Contains(ValidUntil) || Contains(CertificateAlgorithm) || Contains(NativeCode))
        {
            return true;
        }

        if (Contains(Diagnostic)) return true;
        foreach (var field in FileMetadataField.All)
        {
            if (Contains(field.Read(this))) return true;
        }

        return Contains(CatalogDetails);
    }

    private string Hash(ChecksumAlgorithm algorithm) =>
        _hashText.GetValueOrDefault(algorithm, "");
    private string? MetadataDiagnostic(string key, string? diagnostic) =>
        string.IsNullOrWhiteSpace(diagnostic) ? null : strings.Format(key, diagnostic);
    private string FormatDate(DateTimeOffset? date) =>
        date?.UtcDateTime.ToString("g", strings.Culture) is string formatted ? $"{formatted} UTC" : "";
    private static string GetName(string filePath)
    {
        try { return Path.GetFileName(filePath); }
        catch (ArgumentException) { return filePath; }
    }
}
