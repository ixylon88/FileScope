using System.IO;
using System.Windows.Controls;
using FileScope.App;
using FileScope.App.ViewModels;
using FileScope.Core.Models;

namespace FileScope.Ui.Tests;

public sealed partial class UiTests
{
    [TestMethod]
    public Task SelectedDetailsFollowTheRunningAnalysis() => WpfTestHost.RunAsync((_, strings) =>
    {
        var analysis = new ControlledProgressAnalysis();
        using var options = CreateOptions(strings);
        using var vm = CreateMainViewModel(analysis, new EmptyPicker(), strings, options);
        vm.OnlineRevocation = true;
        var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        window.Show();

        try
        {
            var grid = (DataGrid)window.FindName("ResultsGrid");
            var details = new SelectedFileDetailsChecks(window);
            details.Expander.IsExpanded = true;
            vm.DropFilesCommand.Execute(new[] { Path.Combine(Path.GetTempPath(), "stage.bin") });
            PumpUntil(() => analysis.Started.Task.IsCompleted && vm.Rows.Single().State == RowStatus.Running);
            grid.SelectedItem = vm.Rows.Single();
            PumpUntil(() => details.Editor.Text.Contains(strings["OpeningFile"], StringComparison.Ordinal));

            analysis.Progress!.Report(new(AnalysisStage.Reading, 1, 10));
            PumpUntil(() => details.Editor.Text.Contains(strings["Reading"], StringComparison.Ordinal));
            analysis.Progress.Report(new(AnalysisStage.CheckingSignature, 10, 10));
            PumpUntil(() => details.Editor.Text.Contains(strings["CheckingSignatureOnline"], StringComparison.Ordinal));

            analysis.Release.SetResult();
            PumpUntil(() => !vm.IsBusy);
            Assert.Contains(strings["Row_Completed"], details.Editor.Text);
        }
        finally { analysis.Release.TrySetResult(); window.Close(); }
    });
}
