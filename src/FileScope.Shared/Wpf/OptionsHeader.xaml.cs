using System.Windows.Controls;

namespace FileScope.Shared;

/// <summary>Arranges the three display settings while each application supplies its own bound selectors.</summary>
public partial class OptionsHeader : Grid
{
    public OptionsHeader() => InitializeComponent();

    public ComboBox ThemeSelector { get => (ComboBox)ThemeHost.Child; set => ThemeHost.Child = value; }

    public ComboBox HashFormatSelector { get => (ComboBox)HashFormatHost.Child; set => HashFormatHost.Child = value; }

    public ComboBox LanguageSelector { get => (ComboBox)LanguageHost.Child; set => LanguageHost.Child = value; }
}
