using System.Text.Json;

namespace FileScope.Shared.Infrastructure;

public static class JsonSettingsFile
{
    /// <summary>Replaces the settings file only after the complete JSON has been written and closed.</summary>
    public static void Save<T>(string path, T settings, JsonSerializerOptions? options = null)
    {
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);

        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (output)
            {
                JsonSerializer.Serialize(output, settings, options);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        catch
        {
            // A cleanup failure must not hide the original save error.
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            throw;
        }
    }
}
