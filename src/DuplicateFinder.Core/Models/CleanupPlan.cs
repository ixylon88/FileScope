namespace DuplicateFinder.Core;

public sealed record CleanupItem(FoundFile File, FoundFile? Keeper);
public sealed record CleanupPlan(IReadOnlyList<CleanupItem> Items)
{
    public int Groups => Items.Select(i => i.File.Group).Distinct().Count();

    public static CleanupPlan Create(IEnumerable<FoundFile> scan, Func<FoundFile, bool>? inScope = null)
    {
        var items = new List<CleanupItem>();
        foreach (var variant in scan.Where(f => f.Variant > 0).GroupBy(f => f.Variant))
        {
            var priority = variant.Max(f => f.Root.Priority);
            var keeper = variant.FirstOrDefault(f => f.Root.Priority == priority && f.Diagnostic == null
                && f.Streams != StreamComparison.Unknown);
            if (keeper == null) continue;

            foreach (var file in variant)
            {
                if (file.Root.Priority < priority && file.Diagnostic == null && !file.IsHardLink
                    && file.Streams != StreamComparison.Unknown && (inScope == null || inScope(file)))
                {
                    items.Add(new(file, keeper));
                }
            }
        }

        return new(items.OrderBy(i => i.File.Root.Priority).ThenBy(i => i.File.Path, StringComparer.Ordinal).ToArray());
    }
}

public sealed record RecycleResult(FoundFile File, bool Moved, string? Reason);
