namespace FileScope.Shared;

public sealed class FilterOption<T>(T value, Func<string> label) : ObservableObject
{
    public FilterOption(T value, string resourceKey, LocalizationService strings)
        : this(value, () => strings[resourceKey]) { }

    public T Value { get; } = value;
    public string Label => label();
    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));
}
