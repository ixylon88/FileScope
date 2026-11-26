using FileScope.Core.Models;

namespace FileScope.App.ViewModels;

public sealed class ChecksumOption(ChecksumAlgorithm algorithm, string label, bool selected) : ObservableObject
{
    public ChecksumAlgorithm Algorithm { get; } = algorithm;
    public string Label { get; } = label;
    public bool IsSelected { get; set => SetProperty(ref field, value); } = selected;
}

public enum RowStatus { Queued, Running, Completed, Failed, Cancelled }
