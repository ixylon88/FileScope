using System.Security;

namespace FileScope.Core.Files;

public enum FileMoveOutcome
{
    Moved, TargetExists, AlreadyInFolder, SourceMissing, SourceLink, TargetLink, TargetListed, Cancelled, Failed
}

public sealed record FileMoveResult(string SourcePath, string? DestinationPath, FileMoveOutcome Outcome, string? Detail = null);

public interface IFileMoveService
{
    IReadOnlyList<FileMoveResult> MoveToSubfolder(IReadOnlyList<string> sources, string folderName,
        IReadOnlySet<string> listedPaths, CancellationToken cancellationToken = default);
}

/// <summary>Moves each file into its own parent's subfolder, never replacing an existing path.</summary>
public sealed class FileMoveService : IFileMoveService
{
    public IReadOnlyList<FileMoveResult> MoveToSubfolder(IReadOnlyList<string> sources, string folderName,
        IReadOnlySet<string> listedPaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(listedPaths);
        if (!IsValidFolderName(folderName)) throw new ArgumentException("Expected a simple folder name with at most eight characters.", nameof(folderName));
        var results = new List<FileMoveResult>(sources.Count);
        foreach (var source in sources)
        {
            // Return partial results on cancellation; never lose track of completed moves.
            results.Add(cancellationToken.IsCancellationRequested
                ? new FileMoveResult(source, null, FileMoveOutcome.Cancelled)
                : MoveOne(source, folderName, listedPaths));
        }
        return results.AsReadOnly();
    }

    public static bool IsValidFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 8 || name is "." or ".."
            || name.EndsWith('.') || name.EndsWith(' ') || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)))
        {
            return false;
        }
        var stem = name.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL")
            && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && stem[3] is >= '1' and <= '9');
    }

    private static FileMoveResult MoveOne(string source, string folderName, IReadOnlySet<string> listedPaths)
    {
        string? destination = null;
        try
        {
            source = Path.GetFullPath(source);
            var parent = Path.GetDirectoryName(source) ?? throw new ArgumentException("The source has no parent directory.", nameof(source));
            var directory = Path.Combine(parent, folderName);
            destination = Path.Combine(directory, Path.GetFileName(source));
            var attributes = File.GetAttributes(source);
            // Moving a relative symbolic link into a child folder can break its target.
            // Ordinary hardlink paths are regular files and can be renamed safely.
            if ((attributes & FileAttributes.ReparsePoint) != 0) return Result(FileMoveOutcome.SourceLink);
            if ((attributes & FileAttributes.Directory) != 0) return Result(FileMoveOutcome.Failed, "The source is a directory.");
            if (string.Equals(Path.GetFileName(parent), folderName, StringComparison.OrdinalIgnoreCase))
                return new FileMoveResult(source, source, FileMoveOutcome.AlreadyInFolder);
            if (listedPaths.Contains(destination)) return Result(FileMoveOutcome.TargetListed);
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return Result(FileMoveOutcome.TargetLink);
            if (Path.Exists(destination)) return Result(FileMoveOutcome.TargetExists);
            // The native no-overwrite operation remains decisive if a target appears
            // after the preceding existence check. No copy/delete fallback is added here.
            File.Move(source, destination, overwrite: false);
            return Result(FileMoveOutcome.Moved);
        }
        catch (FileNotFoundException ex) { return Result(FileMoveOutcome.SourceMissing, ex.Message); }
        catch (DirectoryNotFoundException ex) { return Result(FileMoveOutcome.SourceMissing, ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            return Result(FileMoveOutcome.Failed, ex.Message);
        }

        FileMoveResult Result(FileMoveOutcome outcome, string? detail = null) => new(source, destination, outcome, detail);
    }
}
