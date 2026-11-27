namespace FileScope.App;

public static class AboutWindow
{
    public static AboutDialog Create(double width, double? height = null) => Create(null, width, height);

    public static AboutDialog Create(IExternalLinkService? links = null, double? width = null, double? height = null) =>
        new(typeof(App).Assembly, "FileScope",
            ["AboutFeatureHashes", "AboutFeatureSignatures", "AboutFeatureMetadata", "AboutFeatureFiles", "AboutFeatureExport"],
            links ?? new ExternalLinkService(), width, height);
}
