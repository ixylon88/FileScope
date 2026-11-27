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
        FitToWorkArea(window, saved);

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

    internal static void FitToWorkArea(Window window, WindowPreferences? saved = null)
    {
        var work = SystemParameters.WorkArea;
        var size = WindowSize.Restore(saved, window.Width, window.Height,
            window.MinWidth, window.MinHeight, work.Width, work.Height);
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
