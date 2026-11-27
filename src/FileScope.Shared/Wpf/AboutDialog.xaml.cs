using System.Reflection;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Navigation;

namespace FileScope.Shared;

/// <summary>Displays build information and the bundled license without changing user preferences.</summary>
public partial class AboutDialog : Window
{
    private readonly IExternalLinkService _links;
    private readonly IReadOnlyList<string> _features;

    public RelayCommand CloseCommand { get; }

    public AboutDialog(Assembly assembly, string product, IReadOnlyList<string> features, IExternalLinkService links)
    {
        _links = links;
        _features = features;
        CloseCommand = new(Close);
        InitializeComponent();

        ProductName.Text = product;
        var icon = BitmapDecoder.Create(new Uri($"pack://application:,,,/{assembly.GetName().Name};component/Resources/{product}.ico"),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        ProductIcon.Source = icon.Frames.OrderBy(frame => frame.PixelWidth).First(frame => frame.PixelWidth >= 48);
        Icon = ProductIcon.Source;

        VersionValue.Text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "—";
        using var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + ".LICENSE")
            ?? throw new InvalidOperationException($"The bundled {product} license is missing.");
        using var reader = new StreamReader(stream);
        LicenseText.Text = reader.ReadToEnd();

        WindowPlacement.FitToWorkArea(this);
        UpdateLanguage(this, EventArgs.Empty);
        LocalizationService.Instance.CultureChanged += UpdateLanguage;
        Closed += (_, _) => LocalizationService.Instance.CultureChanged -= UpdateLanguage;
    }

    private void UpdateLanguage(object? sender, EventArgs args)
    {
        var strings = LocalizationService.Instance;
        Language = XmlLanguage.GetLanguage(strings.Culture.IetfLanguageTag);
        FeatureList.ItemsSource = _features.Select(key => strings[key]).ToArray();
    }

    private void OpenProjectLink(object sender, RequestNavigateEventArgs args)
    {
        args.Handled = true;
        LinkErrorText.Visibility = _links.TryOpen(args.Uri) ? Visibility.Collapsed : Visibility.Visible;
    }
}
