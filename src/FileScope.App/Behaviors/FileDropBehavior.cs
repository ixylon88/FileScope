using System.Windows;
using System.Windows.Input;

namespace FileScope.App.Behaviors;

public static class FileDropBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(FileDropBehavior), new PropertyMetadata(null, OnCommandChanged));
    public static void SetCommand(DependencyObject target, ICommand? value) => target.SetValue(CommandProperty, value);
    public static ICommand? GetCommand(DependencyObject target) => (ICommand?)target.GetValue(CommandProperty);

    private static void OnCommandChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not UIElement element) return;
        if (args.OldValue is not null)
        {
            element.RemoveHandler(UIElement.PreviewDragOverEvent, new DragEventHandler(OnDragOver));
            element.RemoveHandler(UIElement.PreviewDropEvent, new DragEventHandler(OnDrop));
        }
        if (args.NewValue is not null)
        {
            element.AllowDrop = true;
            // Preview + handledEventsToo also works above TextBoxes and the DataGrid.
            element.AddHandler(UIElement.PreviewDragOverEvent, new DragEventHandler(OnDragOver), true);
            element.AddHandler(UIElement.PreviewDropEvent, new DragEventHandler(OnDrop), true);
        }
    }

    private static string[]? GetFiles(DragEventArgs args) => args.Data.GetDataPresent(DataFormats.FileDrop)
        ? args.Data.GetData(DataFormats.FileDrop) as string[] : null;

    private static void OnDragOver(object sender, DragEventArgs args)
    {
        var files = GetFiles(args);
        args.Effects = files is { Length: > 0 } && GetCommand((DependencyObject)sender)?.CanExecute(files) == true
            && (args.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs args)
    {
        var files = GetFiles(args);
        var command = GetCommand((DependencyObject)sender);
        args.Handled = true;
        args.Effects = DragDropEffects.None;
        if (files is not { Length: > 0 } || command?.CanExecute(files) != true) return;
        args.Effects = DragDropEffects.Copy;
        command.Execute(files);
    }
}
