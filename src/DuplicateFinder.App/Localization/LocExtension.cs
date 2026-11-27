using System.Resources;

namespace DuplicateFinder.App;

/// <summary>Identifies this product's text resources when the designer opens a view without starting the app.</summary>
public sealed class LocExtension(string key) : FileScope.Shared.LocExtension(key)
{
    protected override ResourceManager DesignStrings => ProductStrings;
    private static readonly ResourceManager ProductStrings = new("DuplicateFinder.App.Resources.Strings", typeof(App).Assembly);
}
