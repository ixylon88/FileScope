using System.Collections.Specialized;
using System.IO;
using System.Windows.Controls;
using FileScope.App;
using FileScope.App.ViewModels;

namespace FileScope.Ui.Tests;

public sealed partial class UiTests
{
    [TestMethod]
    public Task SearchKeepsTypedSpacesWithoutRefilteringTheSameQuery() => WpfTestHost.RunAsync((_, strings) =>
    {
        using var options = CreateOptions(strings);
        using var vm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, options);
        var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            var match = Path.Combine(Path.GetTempPath(), "filter-needle.bin");
            vm.DropFilesCommand.Execute(new[] { match, Path.Combine(Path.GetTempPath(), "other.bin") });
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 2);

            var panel = (Grid)window.FindName("FiltersPanel");
            var editor = VisualTreeQueries.VisualChildren<TextBox>(panel).Single(box =>
                box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == nameof(vm.SearchText));
            editor.Text = "  filter-needle  ";
            PumpUntil(() => vm.SearchText == editor.Text && vm.RowsView.Count == 1);
            Assert.AreEqual("  filter-needle  ", vm.SearchText);
            Assert.AreEqual(match, ((FileRowViewModel)vm.RowsView.GetItemAt(0)).FullPath);

            var resets = 0;
            ((INotifyCollectionChanged)vm.RowsView).CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
            };
            editor.Text = "filter-needle";
            PumpUntil(() => vm.SearchText == "filter-needle");
            Assert.AreEqual(0, resets);

            editor.Text = "  ";
            PumpUntil(() => vm.SearchText == "  " && vm.RowsView.Count == 2);
            Assert.AreEqual(1, resets);
            editor.Text = "filter-needle";
            PumpUntil(() => vm.RowsView.Count == 1);
            vm.ResetFiltersCommand.Execute(null);
            PumpUntil(() => editor.Text.Length == 0 && vm.RowsView.Count == 2);
        }
        finally { window.Close(); }
    });
}
