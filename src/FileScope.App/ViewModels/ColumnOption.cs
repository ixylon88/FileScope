using FileScope.App.Infrastructure;
using FileScope.App.Localization;

namespace FileScope.App.ViewModels;

public sealed class ColumnOption(string id, string label, LocalizationService strings, bool localize = true)
    : ObservableObject
{
    public string Id { get; } = id;
    public string Label => localize ? strings[label] : label;
    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));

    // Stable identifiers are independent of translations and of column visibility.
    public static IReadOnlyList<ColumnOption> CreateDefaults(LocalizationService strings) =>
    [
        new("Name", "Column_Name", strings), new("Status", "Column_Status", strings),
        new("Size", "Column_Size", strings), new("Modified", "Column_Modified", strings),
        new("MD5", "MD5", strings, false), new("SHA1", "SHA-1", strings, false),
        new("SHA256", "SHA-256", strings, false), new("SHA384", "SHA-384", strings, false),
        new("SHA512", "SHA-512", strings, false),
        new("Signature", "Column_Signature", strings), new("Publisher", "Column_Publisher", strings),
        new("Subject", "Column_Subject", strings), new("Issuer", "Column_Issuer", strings),
        new("Serial", "Column_Serial", strings), new("ValidFrom", "Column_ValidFrom", strings),
        new("ValidUntil", "Column_ValidUntil", strings), new("CertSha1", "Column_CertSha1", strings),
        new("CertSha256", "Column_CertSha256", strings), new("CertAlgorithm", "Column_CertAlgorithm", strings),
        new("NativeCode", "Column_NativeCode", strings), new("Path", "Column_Path", strings),
        new("Diagnostic", "Column_Diagnostic", strings)
    ];
}
