using FileScope.App.Infrastructure;
using FileScope.App.Localization;
using FileScope.Core.Models;

namespace FileScope.App.ViewModels;

public sealed class SignatureOption(EmbeddedSignature value, LocalizationService strings) : ObservableObject
{
    public EmbeddedSignature Value { get; } = value;
    public string Label => string.Join(" · ", new[]
    {
        strings.Format("SignatureChoice", (long)Value.Index + 1),
        Value.Certificate?.Publisher, Value.DigestAlgorithm
    }.Where(part => !string.IsNullOrWhiteSpace(part)));
    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));
}
