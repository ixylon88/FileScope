using System.Windows;
using FileScope.Core.Files;

namespace FileScope.App.Services;

public interface IFileMoveReportService
{
    void Show(IReadOnlyList<FileMoveResult> results);
}

public sealed class FileMoveReportService : IFileMoveReportService
{
    public void Show(IReadOnlyList<FileMoveResult> results)
    {
        var window = new FileMoveReportWindow(results);
        if (Application.Current.MainWindow is { IsVisible: true } owner) window.Owner = owner;
        window.ShowDialog();
    }
}
