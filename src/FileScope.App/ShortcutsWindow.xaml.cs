using System.Windows;
using System.Windows.Markup;
using FileScope.App.Localization;
using FileScope.App.Presentation;

namespace FileScope.App;

/// <summary>A read-only reference for existing commands and native WPF table navigation.</summary>
public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow()
    {
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

    private void UpdateLanguage(object? sender, EventArgs args) =>
        Language = XmlLanguage.GetLanguage(LocalizationService.Instance.Culture.IetfLanguageTag);

    private void CloseShortcuts(object sender, RoutedEventArgs args) => Close();
}
