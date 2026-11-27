using System.Buffers.Binary;
using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FileScope.App.Localization;
using FileScope.App.Presentation;
using FileScope.App.ViewModels;
using FileScope.Core.Models;

namespace FileScope.Presentation.Tests;

/// <summary>
/// Checks relationships between shipped resources without loading WPF.
/// Compilation and the UI scenarios cover C# syntax and runtime bindings.
/// </summary>
[TestClass]
public sealed partial class ResourceConsistencyTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Behaviors = "clr-namespace:FileScope.App.Behaviors";

    [TestMethod]
    [DataRow("en")]
    [DataRow("de")]
    [DataRow("zh-Hans")]
    public void TranslationsHaveMatchingKeysAndPlaceholders(string culture)
    {
        var baseline = ReadStrings("en");
        var translated = ReadStrings(culture);

        Assert.IsTrue(baseline.Count > 0, "The neutral resource set must not be empty.");
        AssertSameKeys(baseline.Keys, translated.Keys, culture);

        foreach (var (key, value) in translated)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"{culture}/{key}: empty translation.");

            var expected = Placeholders(baseline[key]);
            var actual = Placeholders(value);
            CollectionAssert.AreEqual(expected, actual, $"{culture}/{key}: format placeholders differ.");
        }
    }

    [TestMethod]
    public void XamlLocalizationKeysExist()
    {
        var strings = ReadStrings("en");

        foreach (var (name, document) in ReadXaml())
        {
            foreach (var attribute in document.Descendants().Attributes())
            {
                foreach (Match match in LocalizationReference().Matches(attribute.Value))
                {
                    string key = match.Groups[1].Value;
                    Assert.IsTrue(strings.ContainsKey(key), $"{name}: unknown localization key {key}.");
                }
            }
        }
    }

    [TestMethod]
    public void ModelLabelsExist()
    {
        var strings = ReadStrings("en");
        var hashIds = Enum.GetNames<ChecksumAlgorithm>().ToHashSet(StringComparer.Ordinal);
        var keys = Enum.GetNames<RowStatus>().Select(value => "Row_" + value)
            .Concat(Enum.GetNames<HashFormat>().Select(value => "HashFormat_" + value))
            .Concat(Enum.GetValues<AnalysisError>().Where(value => value != AnalysisError.None)
                .Select(value => "Error_" + value))
            .Concat(Enum.GetValues<SignatureStatus>().Where(value => value != SignatureStatus.Valid)
                .Select(value => "Signature_" + value))
            .Concat(["Signature_ValidOnline", "Signature_ValidOffline"])
            .Concat(ColumnOption.CreateDefaults(LocalizationService.Instance)
                .Where(column => !hashIds.Contains(column.Id)).Select(column => "Column_" + column.Id));

        foreach (string key in keys)
            Assert.IsTrue(strings.ContainsKey(key), $"Missing model label: {key}.");
    }

    [TestMethod]
    public void ThemePalettesHaveMatchingKeys()
    {
        var light = PaletteKeys(ReadXamlFile("LightThemePalette.xaml"));
        var dark = PaletteKeys(ReadXamlFile("DarkThemePalette.xaml"));

        AssertSameKeys(light, dark, "Light / Dark palettes");
    }

    [TestMethod]
    public void DynamicResourcesExistInBothPalettes()
    {
        // These brushes are supplied by WPF's Fluent theme, not by our palettes.
        string[] fluentKeys =
        [
            "SystemFillColorAttentionBrush", "SystemFillColorCriticalBrush",
            "TextOnAccentFillColorSelectedTextBrush"
        ];
        var light = PaletteKeys(ReadXamlFile("LightThemePalette.xaml"));
        var dark = PaletteKeys(ReadXamlFile("DarkThemePalette.xaml"));

        foreach (var (name, document) in ReadXaml())
        {
            foreach (var attribute in document.Descendants().Attributes())
            {
                foreach (Match match in DynamicReference().Matches(attribute.Value))
                {
                    string key = match.Groups[1].Value;
                    Assert.IsTrue(fluentKeys.Contains(key, StringComparer.Ordinal)
                        || (light.Contains(key) && dark.Contains(key)),
                        $"{name}: dynamic resource {key} must exist in both palettes.");
                }
            }
        }
    }

    [TestMethod]
    public void TableColumnsMatchTheOptionsCatalog()
    {
        var columns = TableColumns();
        string[] ids = columns.Select(column => (string?)column.Attribute(Behaviors + "ColumnLayoutBehavior.Id") ?? "").ToArray();
        string[] expected = ColumnOption.CreateDefaults(LocalizationService.Instance).Select(column => column.Id).ToArray();

        Assert.IsTrue(ids.All(id => !string.IsNullOrWhiteSpace(id)), "Every table column needs an ID.");
        CollectionAssert.AllItemsAreUnique(ids, "Table column IDs must be unique.");
        CollectionAssert.AllItemsAreUnique(expected, "Options column IDs must be unique.");
        AssertSameKeys(expected, ids, "Table / options columns");
    }

    [TestMethod]
    public void TableDefaultsKeepAutomaticWidthsAndPlainRows()
    {
        var grid = ReadXamlFile("MainWindow.xaml").Descendants(Wpf + "DataGrid").Single();

        Assert.AreEqual("0", (string?)grid.Attribute("AlternationCount"), "Alternating rows remain disabled.");
        Assert.IsNull(grid.Attribute("AlternatingRowBackground"));

        foreach (var column in TableColumns())
        {
            string id = (string?)column.Attribute(Behaviors + "ColumnLayoutBehavior.Id") ?? "";
            double minimum = double.Parse(column.Attribute("MinWidth")!.Value, CultureInfo.InvariantCulture);

            Assert.AreEqual("Auto", (string?)column.Attribute("Width"), $"Automatic width / {id}.");
            Assert.IsTrue(minimum > 0, $"Positive minimum width / {id}.");

            if (id == "Path")
            {
                Assert.IsNull(column.Attribute("MaxWidth"), "The saved path option controls its maximum width.");
            }
            else
            {
                double maximum = double.Parse(column.Attribute("MaxWidth")!.Value, CultureInfo.InvariantCulture);
                Assert.IsTrue(minimum <= maximum && maximum < 1000, $"Width bounds / {id}.");
            }
        }
    }

    [TestMethod]
    public void SharedStylesKeepNativeControlTemplates()
    {
        var controls = ReadXamlFile("Controls.xaml");

        Assert.IsFalse(controls.Descendants(Wpf + "ControlTemplate").Any(),
            "Shared styles must preserve WPF's native control templates.");
    }

    [TestMethod]
    public void SplashIsEmbeddedAndUsesTheSmallPng()
    {
        using var projectStream = OpenAsset("Source.AppProject.xml");
        var project = XDocument.Load(projectStream);
        var splashes = project.Descendants("SplashScreen").ToArray();

        Assert.HasCount(1, splashes);
        Assert.AreEqual("Resources/FileScopeSplash.png",
            splashes[0].Attribute("Include")?.Value.Replace('\\', '/'));

        using var image = OpenAsset("Source.Splash.png");
        Span<byte> header = stackalloc byte[24];
        image.ReadExactly(header);

        Assert.IsTrue(header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "PNG signature.");
        Assert.IsTrue(header.Slice(12, 4).SequenceEqual("IHDR"u8), "PNG dimension header.");
        Assert.AreEqual(256u, BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4)), "Splash width.");
        Assert.AreEqual(256u, BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4)), "Splash height.");
    }

    private static Dictionary<string, string> ReadStrings(string culture)
    {
        var manager = new ResourceManager("FileScope.App.Resources.Strings", typeof(LocalizationService).Assembly);
        var resourceCulture = culture == "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(culture);

        // No parent fallback: a missing translation must not pass as English.
        using var resources = manager.GetResourceSet(resourceCulture, createIfNotExists: true, tryParents: false);
        Assert.IsNotNull(resources, $"Missing resource set: {culture}.");

        return resources.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key,
            entry => (string)entry.Value!, StringComparer.Ordinal);
    }

    private static string[] Placeholders(string value) =>
        FormatPlaceholder().Matches(value).Select(match => match.Value).Order(StringComparer.Ordinal).ToArray();

    private static void AssertSameKeys(IEnumerable<string> expected, IEnumerable<string> actual, string context)
    {
        string[] missing = expected.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] extra = actual.Except(expected, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.IsTrue(missing.Length == 0 && extra.Length == 0,
            $"{context}: missing [{string.Join(", ", missing)}]; unexpected [{string.Join(", ", extra)}].");
    }

    private static HashSet<string> PaletteKeys(XDocument document)
    {
        string[] keys = document.Root!.Elements().Select(element => element.Attribute(Xaml + "Key")!.Value).ToArray();

        Assert.IsTrue(keys.Length > 0, "A palette must contain resources.");
        CollectionAssert.AllItemsAreUnique(keys, "Palette resource keys must be unique.");
        return keys.ToHashSet(StringComparer.Ordinal);
    }

    private static XElement[] TableColumns() => ReadXamlFile("MainWindow.xaml").Descendants()
        .Where(element => element.Name.LocalName is "DataGridTextColumn" or "DataGridTemplateColumn" or "CellBoundTemplateColumn")
        .ToArray();

    private static IEnumerable<(string Name, XDocument Document)> ReadXaml()
    {
        string[] names = typeof(ResourceConsistencyTests).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Source.Xaml.", StringComparison.Ordinal)).ToArray();

        Assert.IsTrue(names.Length > 0, "XAML sources must be embedded in the test assembly.");

        foreach (string name in names)
        {
            using var stream = OpenAsset(name);
            yield return (name, XDocument.Load(stream));
        }
    }

    private static XDocument ReadXamlFile(string name) =>
        ReadXaml().Single(asset => asset.Name.EndsWith(name, StringComparison.Ordinal)).Document;

    private static Stream OpenAsset(string name)
    {
        var stream = typeof(ResourceConsistencyTests).Assembly.GetManifestResourceStream(name);
        Assert.IsNotNull(stream, $"Missing embedded test asset: {name}.");
        return stream;
    }

    [GeneratedRegex(@"\{\d+[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex FormatPlaceholder();

    [GeneratedRegex(@"\{loc:Loc\s+([A-Za-z_]\w*)", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizationReference();

    [GeneratedRegex(@"\{DynamicResource\s+([A-Za-z_]\w*)\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex DynamicReference();
}
