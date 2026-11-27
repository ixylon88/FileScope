using System.Windows;

namespace FileScope.App.Services;

public interface IConfirmationService
{
    bool Confirm(string message, string title);
}

/// <summary>Keeps modal WPF dialogs outside the view model and replaceable in checks.</summary>
public sealed class ConfirmationService : IConfirmationService
{
    public bool Confirm(string message, string title)
    {
        var owner = Application.Current?.MainWindow;
        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}
