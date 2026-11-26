using System.Diagnostics;
using System.IO;
using System.Windows;
using FileScope.App.Localization;

namespace DuplicateFinder.App;

public partial class ReportWindow : Window
{
    protected static LocalizationService Strings => LocalizationService.Instance;
    public ReportWindow(string title, string intro, IEnumerable<string> lines, bool confirm = false)
    {
        InitializeComponent();
        Title = title;
        Intro.Text = intro;
        Lines.ItemsSource = lines;
        if (confirm)
        {
            CloseButton.Content = Strings["Cancel"];
            Proceed.Visibility = Visibility.Visible;
        }
    }

    private void Accept(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OpenRepository(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/ixylon88/FileScope") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex) { MessageBox.Show(this, ex.Message, Title); }
    }

    internal static ReportWindow About()
    {
        using var reader = new StreamReader(typeof(App).Assembly.GetManifestResourceStream("DuplicateFinder.LICENSE")!);
        var lines = new[] { Strings["About1"], Strings["About2"], Strings["About3"], "", Strings["License"] }
            .Concat(reader.ReadToEnd().Split('\n').Select(s => s.TrimEnd('\r'))).ToArray();
        var window = new ReportWindow(Strings["About"], $"DuplicateFinder {typeof(App).Assembly.GetName().Version?.ToString(3)}", lines);
        window.Repository.Visibility = Visibility.Visible;
        return window;
    }
}

public sealed class ShortcutsWindow() : ReportWindow(Strings["Shortcuts"], "DuplicateFinder",
    new[] { Strings["KeyScan"], Strings["KeyFilter"], Strings["KeyMark"], Strings["KeyCancel"], Strings["KeyHelp"] });
