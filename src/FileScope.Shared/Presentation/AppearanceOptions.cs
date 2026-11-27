using FileScope.Shared.Localization;

namespace FileScope.Shared.Presentation;

public static class AppearanceOptions
{
    public static IReadOnlyList<FilterOption<AppTheme>> Themes(LocalizationService strings) =>
        Enum.GetValues<AppTheme>()
            .Select(theme => new FilterOption<AppTheme>(theme, $"Theme_{theme}", strings))
            .ToList().AsReadOnly();

    public static IReadOnlyList<FilterOption<HashFormat>> HashFormats(LocalizationService strings) =>
        Enum.GetValues<HashFormat>()
            .Select(format => new FilterOption<HashFormat>(format, $"HashFormat_{format}", strings))
            .ToList().AsReadOnly();
}
