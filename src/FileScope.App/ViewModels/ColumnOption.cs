using FileScope.App.Infrastructure;
using FileScope.App.Localization;

namespace FileScope.App.ViewModels;

public sealed class ColumnOption(string id, string label, LocalizationService strings,
    Func<FileRowViewModel, string> exportValue, bool localize = true)
    : ObservableObject
{
    public string Id { get; } = id;
    public string Label => localize ? strings[label] : label;
    public bool IsExportSelected { get; set => SetProperty(ref field, value); } = true;
    public string GetExportValue(FileRowViewModel row) => exportValue(row);
    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));

    // Stable identifiers are independent of translations and of column visibility.
    public static IReadOnlyList<ColumnOption> CreateDefaults(LocalizationService strings) =>
    [
        new("Name", "Column_Name", strings, static row => row.FileName), new("Status", "Column_Status", strings, static row => row.StateText),
        new("Size", "Column_Size", strings, static row => row.SizeText), new("Modified", "Column_Modified", strings, static row => row.ModifiedText),
        new("MD5", "MD5", strings, static row => row.MD5, false), new("SHA1", "SHA-1", strings, static row => row.SHA1, false),
        new("SHA256", "SHA-256", strings, static row => row.SHA256, false), new("SHA384", "SHA-384", strings, static row => row.SHA384, false),
        new("SHA512", "SHA-512", strings, static row => row.SHA512, false),
        new("SignatureChoice", "Column_SignatureChoice", strings, static row => row.SignatureChoiceText),
        new("SigningTime", "Column_SigningTime", strings, static row => row.SigningTimeText),
        new("Signature", "Column_Signature", strings, static row => row.SignatureText), new("Publisher", "Column_Publisher", strings, static row => row.Publisher),
        new("Subject", "Column_Subject", strings, static row => row.Subject), new("Issuer", "Column_Issuer", strings, static row => row.Issuer),
        new("Serial", "Column_Serial", strings, static row => row.SerialNumber), new("ValidFrom", "Column_ValidFrom", strings, static row => row.ValidFrom),
        new("ValidUntil", "Column_ValidUntil", strings, static row => row.ValidUntil), new("CertSha1", "Column_CertSha1", strings, static row => row.CertificateSha1),
        new("CertSha256", "Column_CertSha256", strings, static row => row.CertificateSha256), new("CertAlgorithm", "Column_CertAlgorithm", strings, static row => row.CertificateAlgorithm),
        new("NativeCode", "Column_NativeCode", strings, static row => row.NativeCode),
        new("FileDescription", "Column_FileDescription", strings, static row => row.FileDescription),
        new("FileLanguage", "Column_FileLanguage", strings, static row => row.FileLanguage),
        new("FileType", "Column_FileType", strings, static row => row.FileType),
        new("FileVersion", "Column_FileVersion", strings, static row => row.FileVersion),
        new("ProductName", "Column_ProductName", strings, static row => row.ProductName),
        new("ProductVersion", "Column_ProductVersion", strings, static row => row.ProductVersion),
        new("CompanyName", "Column_CompanyName", strings, static row => row.CompanyName),
        new("OriginalFilename", "Column_OriginalFilename", strings, static row => row.OriginalFilename),
        new("InternalName", "Column_InternalName", strings, static row => row.InternalName),
        new("Copyright", "Column_Copyright", strings, static row => row.FileCopyright),
        new("Trademarks", "Column_Trademarks", strings, static row => row.FileTrademarks),
        new("Comments", "Column_Comments", strings, static row => row.FileComments),
        new("PeMachine", "Column_PeMachine", strings, static row => row.PeMachine),
        new("PeFormat", "Column_PeFormat", strings, static row => row.PeFormat),
        new("ManagedPlatform", "Column_ManagedPlatform", strings, static row => row.ManagedPlatformText),
        new("Path", "Column_Path", strings, static row => row.FullPath),
        new("Diagnostic", "Column_Diagnostic", strings, static row => row.Diagnostic)
    ];
}
