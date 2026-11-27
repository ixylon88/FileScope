namespace DuplicateFinder.App;

public static class ShortcutsWindow
{
    public static ShortcutsDialog Create() => new(["Scan", "Filter"], ["Mark"]);
}
