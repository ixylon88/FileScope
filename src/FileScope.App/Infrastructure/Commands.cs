using System.Windows.Input;

namespace FileScope.App.Infrastructure;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(
    Func<object?, Task> execute, Action<Exception> onError, Func<object?, bool>? canExecute = null) : ICommand
{
    private bool _executing;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_executing && (canExecute?.Invoke(parameter) ?? true);

#pragma warning disable VSTHRD100 // Avoid async void methods: a problem of ICommand
    public async void Execute(object? parameter)
#pragma warning restore VSTHRD100 // Avoid async void methods
    {
        if (!CanExecute(parameter)) return;
        _executing = true;
        NotifyCanExecuteChanged();
        try { await execute(parameter); }
        catch (Exception ex) { onError(ex); }
        finally { _executing = false; NotifyCanExecuteChanged(); }
    }
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
