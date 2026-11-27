namespace FileScope.App;

public static class ShortcutsWindow
{
    public static ShortcutsDialog Create(double? width = null, double? height = null) =>
        new(["Open", "Clear"], ["ContextMenu"], width, height);
}
