namespace FileScope.App;

public static class AboutWindow
{
    public static AboutDialog Create(IExternalLinkService? links = null) => new(typeof(App).Assembly, "FileScope",
        ["AboutFeatureHashes", "AboutFeatureSignatures", "AboutFeatureMetadata", "AboutFeatureFiles", "AboutFeatureExport"],
        links ?? new ExternalLinkService());
}
