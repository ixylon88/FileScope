namespace FileScope.App;

public static class ShortcutsWindow
{
    public static ShortcutsDialog Create() => new(["Open", "Clear"], ["ContextMenu"]);
}
