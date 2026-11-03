using System.Text;
using FileScope.App.Infrastructure;
using FileScope.App.Localization;
using FileScope.Core.Models;

namespace FileScope.App.ViewModels;

public sealed class FileRowViewModel(string path, LocalizationService strings) : ObservableObject
{
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
    public SignatureStatus SignatureState => Result?.Signature.Status ?? SignatureStatus.NotChecked;
    public string Publisher => Result?.Signature.Certificate?.Publisher ?? "";
    public string Subject => Result?.Signature.Certificate?.Subject ?? "";
    public string Issuer => Result?.Signature.Certificate?.Issuer ?? "";
    public string SerialNumber => Result?.Signature.Certificate?.SerialNumber ?? "";
    public string CertificateSha1 => Result?.Signature.Certificate?.ThumbprintSha1 ?? "";
    public string CertificateSha256 => Result?.Signature.Certificate?.ThumbprintSha256 ?? "";
    public string ValidFrom => FormatDate(Result?.Signature.Certificate?.NotBefore);
    public string ValidUntil => FormatDate(Result?.Signature.Certificate?.NotAfter);
    public string CertificateAlgorithm => Result?.Signature.Certificate?.SignatureAlgorithm ?? "";
    public string NativeCode => Result?.Signature.NativeStatusCode is int code ? $"0x{unchecked((uint)code):X8}" : "";
    public string StateText => State == RowStatus.Running
        ? strings[Stage == AnalysisStage.CheckingSignature ? "CheckingSignature" : "Reading"]
        : strings[$"Row_{State}"];
    public string SignatureText => SignatureState == SignatureStatus.Valid
        ? strings[Result?.Signature.RevocationMode == RevocationMode.Online ? "Signature_ValidOnline" : "Signature_ValidOffline"]
        : strings[$"Signature_{SignatureState}"];
    public string Diagnostic => Result is null ? "" : string.Join(" — ", new[]
    {
        Result.Error == AnalysisError.None ? null : strings[$"Error_{Result.Error}"],
        Result.Diagnostic, Result.Signature.Diagnostic
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
            Line("Column_Signature", SignatureText);
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
            Line("Column_Diagnostic", Diagnostic);
            return text.ToString().TrimEnd();
        }
    }

    public void Reset()
    {
        Result = null;
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
        State = result.Succeeded ? RowStatus.Completed : RowStatus.Failed;
        Progress = 100;
        RefreshLanguage();
    }

    public void Cancel()
    {
        State = RowStatus.Cancelled;
        RefreshLanguage();
    }

    public void RefreshLanguage() => OnPropertyChanged(null);

    public bool Matches(string query) => query.Length == 0 || new[]
    {
        FullPath, SizeText, ModifiedText, StateText, SignatureText, MD5, SHA1, SHA256, SHA384, SHA512,
        Publisher, Subject, Issuer, SerialNumber, CertificateSha1, CertificateSha256,
        ValidFrom, ValidUntil, CertificateAlgorithm, NativeCode, Diagnostic
    }.Any(value => value.Contains(query, StringComparison.CurrentCultureIgnoreCase));

    private string Hash(ChecksumAlgorithm algorithm) =>
        Result?.Checksums.TryGetValue(algorithm, out var hash) == true ? hash : "";
    private string FormatDate(DateTimeOffset? date) =>
        date?.UtcDateTime.ToString("g", strings.Culture) is string formatted ? $"{formatted} UTC" : "";
    private static string GetName(string filePath)
    {
        try { return Path.GetFileName(filePath); }
        catch (ArgumentException) { return filePath; }
    }
}
