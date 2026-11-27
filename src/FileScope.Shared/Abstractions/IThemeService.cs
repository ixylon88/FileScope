using FileScope.Shared.Presentation;

namespace FileScope.Shared.Abstractions;

/// <summary>Applies the selected appearance without exposing WPF to the view models.</summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    void Apply(AppTheme theme);
}
