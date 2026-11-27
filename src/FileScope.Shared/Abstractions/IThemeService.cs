namespace FileScope.Shared;

public enum AppTheme { Light, Dark }

/// <summary>Applies the selected appearance without exposing WPF to the view models.</summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    void Apply(AppTheme theme);
}
