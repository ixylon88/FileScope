using System.Text;
using FileScope.App.Localization;
using FileScope.Core.Files;

namespace FileScope.App.Presentation;

public static class FileMoveReport
{
    public static string Summary(IReadOnlyList<FileMoveResult> results, LocalizationService strings) =>
        strings.Format("MoveReportSummary", results.Count, results.Count(result => result.Outcome == FileMoveOutcome.Moved),
            results.Count(result => result.Outcome is FileMoveOutcome.Failed or FileMoveOutcome.SourceMissing),
            results.Count(result => result.Outcome is not (FileMoveOutcome.Moved or FileMoveOutcome.Failed or FileMoveOutcome.SourceMissing)));

    public static string Format(IReadOnlyList<FileMoveResult> results, LocalizationService strings)
    {
        var text = new StringBuilder();
        foreach (var result in results)
        {
            text.AppendLine(strings["MoveOutcome_" + result.Outcome]);
            text.AppendLine(strings.Format("MoveReportSource", result.SourcePath));
            text.AppendLine(strings.Format("MoveReportDestination", result.DestinationPath ?? "—"));
            if (!string.IsNullOrWhiteSpace(result.Detail)) text.AppendLine(result.Detail);
            text.AppendLine();
        }
        return text.ToString();
    }
}
