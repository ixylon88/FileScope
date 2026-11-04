using Microsoft.Win32;

namespace FileScope.App.Services;

public interface IFilePickerService
{
    string[] PickFiles(string title, string allFilesLabel);
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
}
