using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace FileScope.Ui.Tests;

internal static class FileMenuChecks
{
    public static MenuItem MenuFor(ContextMenu menu, System.Windows.Input.ICommand command) =>
        menu.Items.OfType<MenuItem>().Single(item => ReferenceEquals(item.Command, command));

    public static void InvokeMenuItem(MenuItem item) =>
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();

    internal sealed class RecordingFileShell : IFileShellService
    {
        public List<(string Action, string Path)> Requests { get; } = [];
        public bool Succeed { get; set; } = true;
        public TaskCompletionSource<bool>? ExplorerCompletion { get; set; }
        public bool TryShowProperties(string fullPath) { Requests.Add(("properties", fullPath)); return Succeed; }
        public Task<bool> TryShowInExplorerAsync(string fullPath)
        {
            Requests.Add(("explorer", fullPath));
            return ExplorerCompletion?.Task ?? Task.FromResult(Succeed);
        }
    }
}
