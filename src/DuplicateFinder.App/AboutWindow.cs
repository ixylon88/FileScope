namespace DuplicateFinder.App;

public static class AboutWindow
{
    public static AboutDialog Create() => new(typeof(App).Assembly, "DuplicateFinder",
        ["AboutFolders", "AboutComparison", "AboutStreams", "AboutSignatures", "AboutRecycle"], new ExternalLinkService());
}
