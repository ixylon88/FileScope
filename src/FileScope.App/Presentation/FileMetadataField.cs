using FileScope.App.ViewModels;

namespace FileScope.App.Presentation;

/// <summary>Stable export ID, localized label and displayed value for each version or executable-header field.</summary>
internal sealed record FileMetadataField(string Id, Func<FileRowViewModel, string> Read)
{
    public string LabelKey { get; } = "Column_" + Id;

    public static IReadOnlyList<FileMetadataField> All { get; } =
    [
        new("FileDescription", static row => row.FileDescription),
        new("FileLanguage", static row => row.FileLanguage),
        new("FileType", static row => row.FileType),
        new("FileVersion", static row => row.FileVersion),
        new("ProductName", static row => row.ProductName),
        new("ProductVersion", static row => row.ProductVersion),
        new("CompanyName", static row => row.CompanyName),
        new("OriginalFilename", static row => row.OriginalFilename),
        new("InternalName", static row => row.InternalName),
        new("Copyright", static row => row.FileCopyright),
        new("Trademarks", static row => row.FileTrademarks),
        new("Comments", static row => row.FileComments),
        new("PeMachine", static row => row.PeMachine),
        new("PeFormat", static row => row.PeFormat),
        new("ManagedPlatform", static row => row.ManagedPlatformText)
    ];
}
