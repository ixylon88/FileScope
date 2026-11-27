using FileScope.Shared.Abstractions;
using FileScope.Shared.Localization;

namespace FileScope.Shared.Infrastructure;

/// <summary>Resolves a single file from the live selection whenever a Shell action is invoked.</summary>
public sealed class FileShellCommands<TRow>(IFileShellService shell, Func<TRow?> selectedRow, Func<TRow, string> pathOf,
    Predicate<TRow> stillListed, Action errorChanged, Action<Exception> onError) where TRow : class
{
    private (string Key, string Path)? _error;

    public AsyncRelayCommand Properties => field ??= new(ShowPropertiesAsync, onError, HasSelection);
    public AsyncRelayCommand Explorer => field ??= new(ShowInExplorerAsync, onError, HasSelection);
    public bool HasError => _error.HasValue;
    public string GetErrorText(LocalizationService strings) => _error is { } error ? strings.Format(error.Key, error.Path) : "";

    private bool HasSelection(object? parameter) => selectedRow() is not null;

    private Task ShowPropertiesAsync(object? parameter)
    {
        if (selectedRow() is { } row)
        {
            var path = pathOf(row);
            SetError(shell.TryShowProperties(path) ? null : ("FilePropertiesError", path));
        }
        return Task.CompletedTask;
    }

    private async Task ShowInExplorerAsync(object? parameter)
    {
        if (selectedRow() is not { } row) return;

        var path = pathOf(row);
        var success = await shell.TryShowInExplorerAsync(path).ConfigureAwait(true);

        // A scan or cleanup may have replaced the result while Explorer was opening.
        if (stillListed(row)) SetError(success ? null : ("FileExplorerError", path));
    }

    public void ClearError() => SetError(null);

    private void SetError((string Key, string Path)? error)
    {
        if (_error == error) return;

        _error = error;
        errorChanged();
    }

    public void NotifyCanExecuteChanged()
    {
        Properties.NotifyCanExecuteChanged();
        Explorer.NotifyCanExecuteChanged();
    }
}
