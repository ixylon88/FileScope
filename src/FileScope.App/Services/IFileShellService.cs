namespace FileScope.App.Services;

/// <summary>Explicit file actions in Windows Shell, separate from file analysis.</summary>
public interface IFileShellService
{
    bool TryShowProperties(string fullPath);
    Task<bool> TryShowInExplorerAsync(string fullPath);
}
