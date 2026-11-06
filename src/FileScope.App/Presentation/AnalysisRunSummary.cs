using System.Globalization;
using FileScope.App.Localization;
using FileScope.Core.Models;

namespace FileScope.App.Presentation;

/// <summary>A frozen summary of the results returned by one analysis batch.</summary>
public sealed record AnalysisRunSummary(TimeSpan Elapsed, int ProcessedFiles, decimal TotalBytes, int UnknownSizeFiles)
{
    public static AnalysisRunSummary Capture(TimeSpan elapsed, IEnumerable<FileAnalysisResult> results)
    {
        var processed = 0;
        var unknown = 0;
        decimal bytes = 0;
        foreach (var result in results)
        {
            processed++;
            if (result.Length is >= 0) bytes += result.Length.Value;
            else unknown++;
        }
        // Decimal preserves exact integral totals even when their sum exceeds Int64.
        // These are logical file sizes per result, not measured physical I/O bytes.
        return new(elapsed, processed, bytes, unknown);
    }

    public string Format(LocalizationService strings)
    {
        var duration = Elapsed.ToString(Elapsed.Days > 0 ? @"d\.hh\:mm\:ss\.fff" : @"hh\:mm\:ss\.fff",
            CultureInfo.InvariantCulture);
        var text = strings.Format("RunSummary", duration, TotalBytes);
        return UnknownSizeFiles > 0 ? $"{text} {strings.Format("RunSummaryUnknownSizes", UnknownSizeFiles)}" : text;
    }
}
