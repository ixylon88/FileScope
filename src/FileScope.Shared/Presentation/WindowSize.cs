namespace FileScope.Shared.Presentation;

public sealed record WindowPreferences(double Width, double Height, bool Maximized);

public sealed record WindowSize(double Width, double Height, double MinWidth, double MinHeight)
{
    // All dimensions are WPF device-independent units. A smaller/new monitor
    // must not restore a window outside the primary monitor's usable area.
    public static WindowSize Restore(WindowPreferences? saved, double defaultWidth, double defaultHeight,
        double minimumWidth, double minimumHeight, double availableWidth, double availableHeight) =>
        new WindowSize(defaultWidth, defaultHeight, minimumWidth, minimumHeight)
            .Fit(saved?.Width, saved?.Height, availableWidth, availableHeight);

    internal WindowSize Fit(double? width, double? height, double availableWidth, double availableHeight)
    {
        var maxWidth = Positive(availableWidth, Width);
        var maxHeight = Positive(availableHeight, Height);
        var minWidth = Math.Min(MinWidth, maxWidth);
        var minHeight = Math.Min(MinHeight, maxHeight);
        return new(Math.Clamp(Positive(width ?? Width, Width), minWidth, maxWidth),
            Math.Clamp(Positive(height ?? Height, Height), minHeight, maxHeight),
            minWidth, minHeight);
    }

    private static double Positive(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;
}
