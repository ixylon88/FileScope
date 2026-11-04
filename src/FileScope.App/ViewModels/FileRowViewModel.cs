using System.Text;
using FileScope.App.Infrastructure;
using FileScope.App.Localization;
using FileScope.App.Presentation;
using FileScope.Core.Models;

namespace FileScope.App.ViewModels;

public sealed class FileRowViewModel(string path, LocalizationService strings, HashFormat format = HashFormat.HexUpper) : ObservableObject
{
    private HashFormat _hashFormat = format;
    private readonly Dictionary<ChecksumAlgorithm, string> _hashText = [];
    private string _certificateSha1 = "", _certificateSha256 = "";
    private SignatureOption? _selectedSignature;
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
            OnPropertyChanged(null);
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
    public string FullPath { get; } = path;
    public string FileName { get; } = GetName(path);
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
        ? header.MachineName.StartsWith("0x", StringComparison.Ordinal)
            ? header.MachineName : $"{header.MachineName} (0x{header.MachineCode:X4})"
        : "";
    public string PeFormat => Result?.PeHeader?.Header?.Format ?? "";
    public string ManagedPlatformText => Result?.PeHeader?.Header is { } header
        ? header.ManagedPlatform switch
        {
            ManagedPlatform.AnyCpu => "AnyCPU",
            ManagedPlatform.Prefer32Bit => strings["ManagedPlatform_Prefer32Bit"],
            ManagedPlatform.MachineSpecific => header.MachineName,
            _ => ""
        } : "";
    public SignatureStatus SignatureState => SelectedSignature?.Value.Status ?? Result?.Signature.Status ?? SignatureStatus.NotChecked;
    public string Publisher => Certificate?.Publisher ?? "";
    public string Subject => Certificate?.Subject ?? "";
    public string Issuer => Certificate?.Issuer ?? "";
    public string SerialNumber => Certificate?.SerialNumber ?? "";
    public string CertificateSha1 => _certificateSha1;
    public string CertificateSha256 => _certificateSha256;
    public DateTimeOffset? ValidFromDate => Certificate?.NotBefore;
    public DateTimeOffset? ValidUntilDate => Certificate?.NotAfter;
    public string ValidFrom => FormatDate(Certificate?.NotBefore);
    public string ValidUntil => FormatDate(Certificate?.NotAfter);
    public string CertificateAlgorithm => Certificate?.SignatureAlgorithm ?? "";
    public string NativeCode => (SelectedSignature is not null ? SelectedSignature.Value.NativeStatusCode : Result?.Signature.NativeStatusCode) is int code ? $"0x{unchecked((uint)code):X8}" : "";
    public string StateText => State == RowStatus.Running
        ? strings[Stage switch
        {
            AnalysisStage.CheckingSignature => "CheckingSignature",
            AnalysisStage.ReadingMetadata => "ReadingMetadata",
            _ => "Reading"
        }]
        : strings[$"Row_{State}"];
    public string SignatureText => SignatureState == SignatureStatus.Valid
        ? strings[Result?.Signature.RevocationMode == RevocationMode.Online ? "Signature_ValidOnline" : "Signature_ValidOffline"]
        : strings[$"Signature_{SignatureState}"];
    public string Diagnostic => Result is null ? "" : string.Join(" — ", new[]
    {
        Result.Error == AnalysisError.None ? null : strings[$"Error_{Result.Error}"],
        Result.Diagnostic, SelectedSignature?.Value.Diagnostic ?? (Signatures.Count == 0 ? Result.Signature.Diagnostic : null),
        MetadataDiagnostic("PropertiesError", Result.Properties?.Diagnostic),
        MetadataDiagnostic("PeHeaderError", Result.PeHeader?.Diagnostic)
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string Details
    {
        get
        {
            var text = new StringBuilder();
            void Line(string key, string value) { if (value.Length != 0) text.AppendLine($"{strings[key]}: {value}"); }
            Line("Column_Path", FullPath);
            Line("Column_Status", StateText);
            Line("Column_Size", SizeText);
            Line("Column_Modified", ModifiedText);
            foreach (var algorithm in Enum.GetValues<ChecksumAlgorithm>())
                if (Hash(algorithm).Length != 0) text.AppendLine($"{algorithm}: {Hash(algorithm)}");
            Line("Column_SignatureChoice", SignatureChoiceText);
            Line("Column_Signature", SignatureText);
            Line("Column_SigningTime", SigningTimeText);
            Line("Column_Publisher", Publisher);
            Line("Column_Subject", Subject);
            Line("Column_Issuer", Issuer);
            Line("Column_Serial", SerialNumber);
            Line("Column_CertSha1", CertificateSha1);
            Line("Column_CertSha256", CertificateSha256);
            Line("Column_ValidFrom", ValidFrom);
            Line("Column_ValidUntil", ValidUntil);
            Line("Column_CertAlgorithm", CertificateAlgorithm);
            Line("Column_NativeCode", NativeCode);
            Line("Column_FileDescription", FileDescription);
            Line("Column_FileLanguage", FileLanguage);
            Line("Column_FileType", FileType);
            Line("Column_FileVersion", FileVersion);
            Line("Column_ProductName", ProductName);
            Line("Column_ProductVersion", ProductVersion);
            Line("Column_CompanyName", CompanyName);
            Line("Column_OriginalFilename", OriginalFilename);
            Line("Column_InternalName", InternalName);
            Line("Column_Copyright", FileCopyright);
            Line("Column_Trademarks", FileTrademarks);
            Line("Column_Comments", FileComments);
            Line("Column_PeMachine", PeMachine);
            Line("Column_PeFormat", PeFormat);
            Line("Column_ManagedPlatform", ManagedPlatformText);
            Line("Column_Diagnostic", Diagnostic);
            return text.ToString().TrimEnd();
        }
    }

    public void Reset()
    {
        Result = null;
        RebuildSignatures();
        RebuildHashText();
        State = RowStatus.Queued;
        Progress = 0;
        RefreshLanguage();
    }

    public void Start()
    {
        State = RowStatus.Running;
        Stage = AnalysisStage.Reading;
        RefreshLanguage();
    }

    public void SetProgress(AnalysisProgress progress, double percent)
    {
        var changedStage = Stage != progress.Stage;
        Stage = progress.Stage;
        Progress = percent;
        OnPropertyChanged(nameof(Progress));
        if (changedStage) OnPropertyChanged(nameof(StateText));
    }

    public void Complete(FileAnalysisResult result)
    {
        Result = result;
        RebuildSignatures();
        RebuildHashText();
        State = result.Succeeded ? RowStatus.Completed : RowStatus.Failed;
        Progress = 100;
        RefreshLanguage();
    }

    public void Cancel()
    {
        State = RowStatus.Cancelled;
        RefreshLanguage();
    }

    public void RefreshLanguage()
    {
        foreach (var signature in Signatures) signature.RefreshLanguage();
        OnPropertyChanged(null);
    }

    private void RebuildSignatures()
    {
        var entries = Result?.Signature.Signatures ?? [];
        // Also support inspectors using the original single-signature contract.
        if (entries.Count == 0 && Result?.Signature is { Certificate: not null } single)
            entries = [new EmbeddedSignature(0, single.Status, single.Certificate,
                single.NativeStatusCode, single.Diagnostic, "", SignatureTimestamp.NotPresent)];
        Signatures = Array.AsReadOnly(entries.Select(entry => new SignatureOption(entry, strings)).ToArray());
        _selectedSignature = Signatures.FirstOrDefault();
        SignaturesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetHashFormat(HashFormat format)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        if (_hashFormat == format) return;
        _hashFormat = format;
        RebuildHashText();
        OnPropertyChanged(null); // Includes Details and the values used by clipboard/search.
    }

    private void RebuildHashText()
    {
        _hashText.Clear();
        if (Result is not null)
            foreach (var (algorithm, hex) in Result.Checksums)
                _hashText[algorithm] = DigestFormatter.Format(hex, _hashFormat);
        RebuildCertificateHashText();
    }

    private void RebuildCertificateHashText()
    {
        _certificateSha1 = DigestFormatter.Format(Certificate?.ThumbprintSha1, _hashFormat);
        _certificateSha256 = DigestFormatter.Format(Certificate?.ThumbprintSha256, _hashFormat);
    }

    public bool Matches(string query) => query.Length == 0 || new[]
    {
        FullPath, SizeText, ModifiedText, StateText, SignatureText, SignatureChoiceText, SigningTimeText, MD5, SHA1, SHA256, SHA384, SHA512,
        Publisher, Subject, Issuer, SerialNumber, CertificateSha1, CertificateSha256,
        ValidFrom, ValidUntil, CertificateAlgorithm, NativeCode, Diagnostic,
        FileDescription, FileLanguage, FileType, FileVersion, ProductName, ProductVersion,
        CompanyName, OriginalFilename, InternalName, FileCopyright, FileTrademarks, FileComments,
        PeMachine, PeFormat, ManagedPlatformText
    }.Any(value => value.Contains(query, StringComparison.CurrentCultureIgnoreCase));

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
