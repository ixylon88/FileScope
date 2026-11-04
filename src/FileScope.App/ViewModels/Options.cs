using FileScope.App.Infrastructure;
using FileScope.App.Localization;
using FileScope.Core.Models;

namespace FileScope.App.ViewModels;

public sealed class ChecksumOption(ChecksumAlgorithm algorithm, string label, bool selected) : ObservableObject
{
    public ChecksumAlgorithm Algorithm { get; } = algorithm;
    public string Label { get; } = label;
    public bool IsSelected { get; set => SetProperty(ref field, value); } = selected;
}

public sealed class FilterOption<T>(T value, string resourceKey, LocalizationService strings) : ObservableObject
{
    public T Value { get; } = value;
    public string Label => strings[resourceKey];
    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));
}

public enum RowStatus { Queued, Running, Completed, Failed, Cancelled }
public enum SignatureFilter { All, Valid, NoSignature, Attention, NotChecked }
