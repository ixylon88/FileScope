using System.Globalization;

namespace FileScope.App.Presentation;

/// <summary>Keeps one formatted string. A read in another UI language needs a fresh value.</summary>
internal struct LocalizedTextCache
{
    private CultureInfo? _culture;
    private string? _text;

    public string? this[CultureInfo culture]
    {
        readonly get => _culture == culture ? _text : null;
        set
        {
            _culture = culture;
            _text = value;
        }
    }

    public void Clear() => _text = null;
}
