using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileScope.Shared.Infrastructure;
using FileScope.Shared.Localization;
using FileScope.Shared.Presentation;

namespace FileScope.Shared.Dialogs;

/// <summary>Shows the common table shortcuts together with the commands available in the host application.</summary>
public partial class ShortcutsDialog : Window
{
    public RelayCommand CloseCommand { get; }

    public ShortcutsDialog(IReadOnlyList<string> actions, IReadOnlyList<string> selection,
        double? width = null, double? height = null)
    {
        CloseCommand = new(Close);
        InitializeComponent();

        InsertShortcuts(ActionsList, 0, actions);
        InsertShortcuts(SelectionList, SelectionList.Children.Count, selection);
        WindowPlacement.FitToWorkArea(this, width, height);
    }

    public static void BindHelp(Window owner, Func<ShortcutsDialog> createDialog)
    {
        owner.InputBindings.Add(new KeyBinding(ApplicationCommands.Help, Key.F1, ModifierKeys.None));
        owner.CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, (_, args) =>
        {
            args.Handled = true;
            var dialog = createDialog();
            dialog.Owner = owner;
            dialog.ShowDialog();
        }));
    }

    private static void InsertShortcuts(Panel group, int index, IReadOnlyList<string> shortcuts)
    {
        foreach (var shortcut in shortcuts)
        {
            var row = new HeaderedContentControl();
            row.SetBinding(HeaderedContentControl.HeaderProperty, LocExtension.CreateBinding($"Shortcut{shortcut}Keys"));
            row.SetBinding(ContentControl.ContentProperty, LocExtension.CreateBinding($"Shortcut{shortcut}Action"));
            group.Children.Insert(index++, row);
        }
    }
}
