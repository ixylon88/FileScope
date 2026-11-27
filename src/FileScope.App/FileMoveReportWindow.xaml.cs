using System.Windows;
using System.Windows.Markup;
using FileScope.App.Localization;
using FileScope.App.Presentation;
using FileScope.Core.Files;

namespace FileScope.App;

public partial class FileMoveReportWindow : Window
{
    private readonly IReadOnlyList<FileMoveResult> _results;

    public FileMoveReportWindow(IReadOnlyList<FileMoveResult> results)
    {
        _results = results.ToArray();
        InitializeComponent();
        var work = SystemParameters.WorkArea;
        var size = WindowSize.Restore(null, Width, Height, MinWidth, MinHeight, work.Width, work.Height);
        MinWidth = size.MinWidth;
        MinHeight = size.MinHeight;
        Width = size.Width;
        Height = size.Height;
        UpdateLanguage(this, EventArgs.Empty);
        LocalizationService.Instance.CultureChanged += UpdateLanguage;
        Closed += (_, _) => LocalizationService.Instance.CultureChanged -= UpdateLanguage;
    }

    private void UpdateLanguage(object? sender, EventArgs args)
    {
        var strings = LocalizationService.Instance;
        Language = XmlLanguage.GetLanguage(strings.Culture.IetfLanguageTag);
        MoveSummaryText.Text = FileMoveReport.Summary(_results, strings);
        MoveReportText.Text = FileMoveReport.Format(_results, strings);
    }

    private void CloseReport(object sender, RoutedEventArgs args) => Close();
}
