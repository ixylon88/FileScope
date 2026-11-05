using Microsoft.Win32;

namespace FileScope.App.Services;

public interface IFilePickerService
{
    string[] PickFiles(string title, string allFilesLabel);
    string? PickFolder(string title);
    string? PickCsvDestination(string title, string csvFilesLabel);
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

    public string? PickCsvDestination(string title, string csvFilesLabel)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = $"{csvFilesLabel} (*.csv)|*.csv",
            FileName = "FileScope.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
