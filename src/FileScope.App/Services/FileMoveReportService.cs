using System.Windows;
using FileScope.App.Presentation;
using FileScope.Core.Files;

namespace FileScope.App.Services;

public interface IFileMoveReportService
{
    void Show(IReadOnlyList<FileMoveResult> results);
}

public sealed class FileMoveReportService : IFileMoveReportService
{
    public static ReportDialog CreateDialog(IReadOnlyList<FileMoveResult> results)
    {
        var snapshot = results.ToArray();
        return ReportDialog.ForText("MoveReportTitle",
            strings => FileMoveReport.Summary(snapshot, strings),
            strings => FileMoveReport.Format(snapshot, strings),
            new WindowSize(960, 640, 480, 320));
    }

    public void Show(IReadOnlyList<FileMoveResult> results)
    {
        var window = CreateDialog(results);
        if (Application.Current.MainWindow is { IsVisible: true } owner) window.Owner = owner;
        window.ShowDialog();
    }
}
