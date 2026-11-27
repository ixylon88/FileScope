using FileScope.App.Services;

namespace FileScope.App.Presentation;

public sealed record WindowSize(double Width, double Height, double MinWidth, double MinHeight)
{
    // All dimensions are WPF device-independent units. A smaller/new monitor
    // must not restore a window outside the primary monitor's usable area.
    public static WindowSize Restore(WindowPreferences? saved, double defaultWidth, double defaultHeight,
        double minimumWidth, double minimumHeight, double availableWidth, double availableHeight)
    {
        var maxWidth = Positive(availableWidth, defaultWidth);
        var maxHeight = Positive(availableHeight, defaultHeight);
        var minWidth = Math.Min(minimumWidth, maxWidth);
        var minHeight = Math.Min(minimumHeight, maxHeight);
        return new(Math.Clamp(Positive(saved?.Width ?? defaultWidth, defaultWidth), minWidth, maxWidth),
            Math.Clamp(Positive(saved?.Height ?? defaultHeight, defaultHeight), minHeight, maxHeight),
            minWidth, minHeight);
    }

    private static double Positive(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;
}
