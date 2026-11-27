using System.Windows;

namespace FileScope.Shared;

/// <summary>Restores the normal size and remembers the last non-minimized state.</summary>
public sealed class WindowPlacement
{
    private readonly Window _window;
    private WindowState _lastVisibleState;

    public WindowPlacement(Window window, WindowPreferences? saved, bool centerOnWorkArea = false)
    {
        _window = window;
        FitToWorkArea(window, saved?.Width, saved?.Height);

        if (centerOnWorkArea)
        {
            var work = SystemParameters.WorkArea;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = work.Left + (work.Width - window.Width) / 2;
            window.Top = work.Top + (work.Height - window.Height) / 2;
        }

        if (saved?.Maximized == true) window.WindowState = WindowState.Maximized;
        _lastVisibleState = window.WindowState;
        window.StateChanged += OnStateChanged;
    }

    /// <summary>Uses the factory size as the fallback and saves the user's changes after the dialog closes.</summary>
    public static T RememberDialogSize<T>(T dialog, WindowPreferences? saved, Action<WindowPreferences> save)
        where T : Window
    {
        var placement = new WindowPlacement(dialog, saved);
        WindowPreferences? initial = null;
        WindowPreferences? final = null;

        // Compare actual window bounds after first layout, including DPI rounding.
        // Closing still has RestoreBounds. Closed confirms that closing wasn't cancelled.
        dialog.Loaded += (_, _) => initial ??= placement.Capture();
        dialog.Closing += (_, _) => final = placement.Capture();
        dialog.Closed += (_, _) =>
        {
            if (initial != null && final != null && initial != final) save(final);
        };
        return dialog;
    }

    internal static void FitToWorkArea(Window window, double? width = null, double? height = null)
    {
        var work = SystemParameters.WorkArea;
        var defaults = new WindowSize(window.Width, window.Height, window.MinWidth, window.MinHeight);
        var size = defaults.Fit(width, height, work.Width, work.Height);

        window.MinWidth = size.MinWidth;
        window.MinHeight = size.MinHeight;
        window.Width = size.Width;
        window.Height = size.Height;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState != WindowState.Minimized) _lastVisibleState = _window.WindowState;
    }

    public WindowPreferences Capture()
    {
        // RestoreBounds holds the normal size while maximized and is empty before the first Show.
        var bounds = _window.RestoreBounds;
        return new(bounds.IsEmpty ? _window.Width : bounds.Width, bounds.IsEmpty ? _window.Height : bounds.Height,
            _lastVisibleState == WindowState.Maximized);
    }
}
