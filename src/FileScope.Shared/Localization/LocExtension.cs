using System.ComponentModel;
using System.Resources;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace FileScope.Shared.Localization;

[MarkupExtensionReturnType(typeof(object))]
public class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    protected virtual ResourceManager? DesignStrings => null;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var target = (IProvideValueTarget?)serviceProvider.GetService(typeof(IProvideValueTarget));
        if ((target?.TargetObject is DependencyObject element && DesignerProperties.GetIsInDesignMode(element)) ||
            (bool)DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue)
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            return DesignStrings?.GetString(Key, culture) ?? LocalizationService.SharedText(Key, culture) ?? Key;
        }

        return CreateBinding(Key).ProvideValue(serviceProvider);
    }

    internal static Binding CreateBinding(string key) =>
        new($"[{key}]") { Source = LocalizationService.Instance, Mode = BindingMode.OneWay };
}
