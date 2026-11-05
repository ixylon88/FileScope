namespace FileScope.Core.Export;

public static class ResultExportFormats
{
    public static bool TryFromPath(string path, out ResultExportFormat format)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            format = ResultExportFormat.Csv;
            return true;
        }
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            format = ResultExportFormat.Json;
            return true;
        }
        format = default;
        return false;
    }
}
