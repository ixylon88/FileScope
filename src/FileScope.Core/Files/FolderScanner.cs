namespace FileScope.Core.Files;

public sealed record FolderScanIssue(string Path, string Message);

public sealed record FolderScanResult(
    IReadOnlyList<string> Files, IReadOnlyList<FolderScanIssue> Issues, int SkippedDirectoryLinks);

public interface IFolderScanner
{
    FolderScanResult Scan(string folderPath, CancellationToken cancellationToken = default);
}

public sealed class FolderScanner(Func<string, bool>? skipDirectory) : IFolderScanner
{
    public FolderScanner() : this(null) { }

    // An explicitly selected root may itself be a directory link. Within that root,
    // directory reparse points are never traversed. File links remain individual paths:
    // hard links cannot create directory cycles, and file symlinks are resolved on opening.
    public FolderScanResult Scan(string folderPath, CancellationToken cancellationToken = default) =>
        Scan(folderPath, includeFile: null, enteringDirectory: null, cancellationToken);

    /// <summary>Filters files using the metadata returned by directory enumeration, before collecting their paths.</summary>
    public FolderScanResult Scan(string folderPath, Func<FileInfo, bool>? includeFile,
        Action<string>? enteringDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(folderPath);
        var pending = new Stack<string>();
        var files = new List<string>();
        var issues = new List<FolderScanIssue>();
        var skippedLinks = 0;
        pending.Push(root);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        };

        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                enteringDirectory?.Invoke(directory);

                // Also recheck queued directories: a link must not be followed
                // if it appeared after the parent was enumerated.
                if (directory != root && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    skippedLinks++;
                    continue;
                }
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry is FileInfo file)
                    {
                        if (includeFile?.Invoke(file) != false) files.Add(file.FullName);
                    }
                    else if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        // Junctions/symbolic links can escape the selected tree
                        // or point back to an ancestor. Never recurse into them.
                        skippedLinks++;
                    }
                    else if (skipDirectory?.Invoke(entry.FullName) != true)
                    {
                        pending.Push(entry.FullName);
                    }
                }
            }
            catch (Exception ex) when (directory != root && ex is IOException or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                // Preserve files already found and continue with sibling folders.
                issues.Add(new FolderScanIssue(directory, ex.Message));
            }
        }
        return new FolderScanResult(files.AsReadOnly(), issues.AsReadOnly(), skippedLinks);
    }
}
