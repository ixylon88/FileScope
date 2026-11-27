namespace FileScope.Shared;

/// <summary>Resolves a single file from the live selection whenever a Shell action is invoked.</summary>
public sealed class FileShellCommands<TRow> where TRow : class
{
    private readonly Action _errorChanged;
    private (string Key, string Path)? _error;

    public AsyncRelayCommand Properties { get; }
    public AsyncRelayCommand Explorer { get; }
    public bool HasError => _error.HasValue;
    public string GetErrorText(LocalizationService strings) => _error is { } error ? strings.Format(error.Key, error.Path) : "";

    public FileShellCommands(IFileShellService shell, Func<TRow?> selectedRow, Func<TRow, string> pathOf,
        Predicate<TRow> stillListed, Action errorChanged, Action<Exception> onError)
    {
        _errorChanged = errorChanged;
        Properties = new(_ =>
        {
            if (selectedRow() is { } row)
            {
                var path = pathOf(row);
                SetError(shell.TryShowProperties(path) ? null : ("FilePropertiesError", path));
            }
            return Task.CompletedTask;
        }, onError, _ => selectedRow() != null);

        Explorer = new(async _ =>
        {
            if (selectedRow() is not { } row) return;

            var path = pathOf(row);
            var success = await shell.TryShowInExplorerAsync(path).ConfigureAwait(true);

            // A scan or cleanup may have replaced the result while Explorer was opening.
            if (stillListed(row)) SetError(success ? null : ("FileExplorerError", path));
        }, onError, _ => selectedRow() != null);
    }

    public void ClearError() => SetError(null);

    private void SetError((string Key, string Path)? error)
    {
        if (_error == error) return;

        _error = error;
        _errorChanged();
    }

    public void NotifyCanExecuteChanged()
    {
        Properties.NotifyCanExecuteChanged();
        Explorer.NotifyCanExecuteChanged();
    }
}
