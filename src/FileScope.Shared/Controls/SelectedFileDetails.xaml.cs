using System.Windows;
using System.Windows.Controls;

namespace FileScope.Shared.Controls;

/// <summary>Displays the path and selectable detail text supplied by the host's current selection.</summary>
public partial class SelectedFileDetails : UserControl
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(SelectedFileDetails), new PropertyMetadata(""));

    public static readonly DependencyProperty DetailsTextProperty = DependencyProperty.Register(
        nameof(DetailsText), typeof(string), typeof(SelectedFileDetails), new PropertyMetadata(""));

    public string FilePath { get => (string)GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
    public string DetailsText { get => (string)GetValue(DetailsTextProperty); set => SetValue(DetailsTextProperty, value); }

    public SelectedFileDetails() => InitializeComponent();
}
