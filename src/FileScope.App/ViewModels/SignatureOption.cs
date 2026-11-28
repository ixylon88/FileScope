using FileScope.App.Presentation;
using FileScope.Core.Models;

namespace FileScope.App.ViewModels;

public sealed class SignatureOption(EmbeddedSignature value, LocalizationService strings) : ObservableObject
{
    private LocalizedTextCache _label;

    public EmbeddedSignature Value { get; } = value;
    public string Label => _label[strings.Culture] ??= BuildLabel();

    public void RefreshLanguage()
    {
        _label.Clear();
        OnPropertyChanged(nameof(Label));
    }

    private string BuildLabel() => string.Join(" · ", new[]
    {
        strings.Format("SignatureChoice", (long)Value.Index + 1),
        Value.Certificate?.Publisher, Value.DigestAlgorithm
    }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
