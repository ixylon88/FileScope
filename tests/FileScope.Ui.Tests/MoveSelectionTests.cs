using System.IO;
using FileScope.App.ViewModels;
using FileScope.Core.Models;

namespace FileScope.Ui.Tests;

public sealed partial class UiTests
{
    [TestMethod]
    public Task LargeMoveSelectionUsesOnlyCurrentRowsAndPreservesSelectionOrder() => WpfTestHost.RunAsync((_, strings) =>
    {
        var mover = new RecordingFileMover { OnMove = (_, _, _) => [] };
        using var vm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, CreateOptions(strings), fileMover: mover);
        var analysis = new AnalysisOptions([], inspectSignature: true);

        for (var index = 0; index < 1500; index++)
        {
            var row = new FileRowViewModel(Path.Combine(Path.GetTempPath(), $"selection-{index}.exe"), strings);
            row.Complete(Result(row.FullPath, analysis));
            vm.Rows.Add(row);
        }

        var selection = vm.Rows.Reverse().ToArray();
        var original = selection[0];
        var replacement = new FileRowViewModel(original.FullPath, strings);
        replacement.Complete(original.Result!);
        vm.SetSelectedRows([.. selection, original, replacement]);
        Assert.IsTrue(vm.CanMoveSelected);
        vm.MoveSelectedCommand.Execute(null);
        PumpUntil(() => !vm.IsBusy);
        CollectionAssert.AreEqual(selection.Select(row => row.FullPath).ToArray(), mover.Sources);

        vm.Rows[^1] = replacement;
        Assert.IsFalse(vm.MoveSelectedCommand.CanExecute(null));
        Assert.AreEqual("", vm.MoveSelectedMenu);

        vm.SetSelectedRows([original, replacement, replacement]);
        Assert.AreSame(replacement, vm.SingleSelectedRow);
        Assert.IsTrue(vm.CanMoveSelected);

        vm.Rows.Clear();
        Assert.IsNull(vm.SingleSelectedRow);
        Assert.IsFalse(vm.CanMoveSelected);
    });
}
