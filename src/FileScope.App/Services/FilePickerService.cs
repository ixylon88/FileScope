using System.Globalization;
using Microsoft.Win32;

namespace FileScope.App.Services;

public interface IFilePickerService
{
    string[] PickFiles(string title, string allFilesLabel);
    string? PickFolder(string title);
    string? PickExportDestination(string title, string csvFilesLabel, string jsonFilesLabel);
}

public sealed class FilePickerService : IFilePickerService
{
    public string[] PickFiles(string title, string allFilesLabel)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = $"{allFilesLabel} (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickExportDestination(string title, string csvFilesLabel, string jsonFilesLabel)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = $"{csvFilesLabel} (*.csv)|*.csv|{jsonFilesLabel} (*.json)|*.json",
            // UTC, independent of UI culture; colons are not valid in Windows file names.
            FileName = "FileScope_" + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH-mm-ss'Z'", CultureInfo.InvariantCulture),
            FilterIndex = 1,
            DefaultExt = ".csv",
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
