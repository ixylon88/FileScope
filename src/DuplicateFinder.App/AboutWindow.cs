namespace DuplicateFinder.App;

public static class AboutWindow
{
    public static AboutDialog Create(double? width = null, double? height = null) =>
        new(typeof(App).Assembly, "DuplicateFinder",
            ["AboutFolders", "AboutComparison", "AboutStreams", "AboutSignatures", "AboutRecycle"],
            new ExternalLinkService(), width, height);
}
