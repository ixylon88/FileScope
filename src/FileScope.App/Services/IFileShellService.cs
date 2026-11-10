namespace FileScope.App.Services;

/// <summary>Explicit file actions in Windows Shell, separate from file analysis.</summary>
public interface IFileShellService
{
    /// <summary>Invokes the Windows Properties command for one existing file.</summary>
    /// <param name="fullPath">Fully qualified file-system path, without command-line quoting.</param>
    /// <returns>Whether the Shell command was invoked successfully; false for an unavailable file or a rejected request.</returns>
    /// <remarks>The Windows implementation must be called on the application's UI thread.</remarks>
    bool TryShowProperties(string fullPath);

    /// <summary>Asks Explorer to open the parent folder and select one existing file.</summary>
    /// <param name="fullPath">Fully qualified file-system path, without command-line quoting.</param>
    /// <returns>A task reporting whether path validation and the native Shell calls succeeded.</returns>
    /// <remarks>Completion reports the native call's result, not an observed Explorer selection or a completed file analysis.</remarks>
    Task<bool> TryShowInExplorerAsync(string fullPath);
}
