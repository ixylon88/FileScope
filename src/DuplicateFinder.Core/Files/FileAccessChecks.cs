namespace DuplicateFinder.Core;

internal static class FileAccessChecks
{
    internal static bool Expected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
            or System.Security.SecurityException or NotSupportedException or ArgumentException;

    internal static bool IsRecycleDirectory(string path) =>
        string.Equals(System.IO.Path.GetFileName(path), "$Recycle.Bin", StringComparison.OrdinalIgnoreCase);

    internal static void CheckPath(string path)
    {
        for (string? current = path; current != null; current = System.IO.Path.GetDirectoryName(current))
        {
            if (IsRecycleDirectory(current)) throw new IOException("The recycle bin is excluded from the scan.");
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Reparse point: {current}");
            }
        }
    }

    internal static FileStream Open(string path, bool movable = false) =>
        new(path, FileMode.Open, FileAccess.Read, movable ? FileShare.Read | FileShare.Delete : FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);

    internal static FileStream OpenVerified(FoundFile entry, IFileFacts facts, bool movable = false)
    {
        CheckPath(entry.Path);
        var file = Open(entry.Path, movable);
        try
        {
            if (entry.Stamp == null || facts.Read(file) != entry.Stamp)
            {
                throw new IOException($"File changed since the scan: {entry.Path}");
            }

            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }
}
