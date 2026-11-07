using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Navigation;
using FileScope.App.Localization;
using FileScope.App.Presentation;
using FileScope.App.Services;

namespace FileScope.App;

/// <summary>Displays build information and the bundled license without changing user preferences.</summary>
public partial class AboutWindow : Window
{
    private readonly IExternalLinkService _links;

    public AboutWindow() : this(new ExternalLinkService()) { }

    public AboutWindow(IExternalLinkService links)
    {
        ArgumentNullException.ThrowIfNull(links);
        _links = links;
        InitializeComponent();

        // Use the application assembly even when opened by the separate UI test host.
        // InformationalVersion preserves release suffixes and SDK-provided build metadata.
        var assembly = typeof(App).Assembly;
        VersionValue.Text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "—";
        using var stream = assembly.GetManifestResourceStream("FileScope.App.LICENSE")
            ?? throw new InvalidOperationException("The bundled FileScope license is missing.");
        using var reader = new StreamReader(stream);
        LicenseText.Text = reader.ReadToEnd();

        var workArea = SystemParameters.WorkArea;
        var size = WindowSize.Restore(null, Width, Height, MinWidth, MinHeight, workArea.Width, workArea.Height);
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

    private void OpenProjectLink(object sender, RequestNavigateEventArgs args)
    {
        args.Handled = true;
        LinkErrorText.Visibility = _links.TryOpen(args.Uri) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CloseAbout(object sender, RoutedEventArgs args) => Close();
}
