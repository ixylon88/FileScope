namespace DuplicateFinder.App;

public static class ShortcutsWindow
{
    public static ShortcutsDialog Create(double? width = null, double? height = null) =>
        new(["Scan", "Filter"], ["Mark", "ContextMenu"], width, height);
}
