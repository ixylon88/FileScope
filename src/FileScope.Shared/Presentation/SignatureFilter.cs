using FileScope.Core.Models;
using FileScope.Shared.Localization;

namespace FileScope.Shared.Presentation;

public enum SignatureSource { Embedded, Catalog, Both }
public enum SignatureFilterKind { All, Valid, ExceptValid, Attention }

/// <summary>Filters the selected embedded signature, the set of catalog outcomes, or both sources.</summary>
public readonly record struct SignatureFilter
{
    public SignatureGroup? Group { get; }
    public SignatureSource Source { get; }
    private SignatureFilterKind Kind { get; }

    private SignatureFilter(SignatureFilterKind kind, SignatureSource source) => (Kind, Source) = (kind, source);

    private SignatureFilter(SignatureGroup group)
    {
        Group = group;
        Source = group.IsCatalog ? SignatureSource.Catalog : SignatureSource.Embedded;
    }

    public static SignatureFilter All => default;
    public static SignatureFilter Valid => For(SignatureFilterKind.Valid, SignatureSource.Embedded);
    public static SignatureFilter ExceptValid => For(SignatureFilterKind.ExceptValid, SignatureSource.Embedded);
    public static SignatureFilter Attention => For(SignatureFilterKind.Attention, SignatureSource.Embedded);

    public static SignatureFilter For(SignatureFilterKind kind, SignatureSource source) =>
        kind == SignatureFilterKind.All ? All : new(kind, source);

    public static SignatureFilter Exact(SignatureGroup group) => new(group);

    public bool Matches(SignatureStatus status, RevocationMode mode) => Matches(status, mode, CatalogPresentation.From(null));

    public bool Matches(SignatureStatus status, RevocationMode mode, CatalogPresentation catalog)
    {
        if (Group is { } exact)
            return exact.IsCatalog ? catalog.Groups.Contains(exact) : SignatureGroup.From(status, mode) == exact;

        if (Kind == SignatureFilterKind.All) return true;

        var embedded = Source != SignatureSource.Catalog;
        var catalogs = Source != SignatureSource.Embedded;
        if (Kind == SignatureFilterKind.Attention)
        {
            return (embedded && SignatureGroup.ToneFor(status) is StatusTone.Warning or StatusTone.Error)
                || (catalogs && catalog.Groups.Any(group => group.Tone is StatusTone.Warning or StatusTone.Error));
        }

        // Negate the combined result once: one valid catalog is enough even if another catalog failed.
        var valid = (embedded && status == SignatureStatus.Valid)
            || (catalogs && catalog.Groups.Any(group => group.Status == SignatureStatus.Valid));
        return Kind == SignatureFilterKind.Valid ? valid : !valid;
    }

    public string Format(LocalizationService strings)
    {
        if (Group is { } exact) return exact.Format(strings);
        if (Kind == SignatureFilterKind.All) return strings["AllSignatures"];

        return strings.Format("ScopedSignatureFilter", strings[$"SignatureSource_{Source}"], strings[$"Filter_{Kind}"]);
    }

    public static IReadOnlyList<FilterOption<SignatureFilter>> Options(LocalizationService strings)
    {
        var filters = new List<SignatureFilter> { All };
        foreach (var kind in new[] { SignatureFilterKind.Valid, SignatureFilterKind.ExceptValid, SignatureFilterKind.Attention })
        {
            foreach (var source in Enum.GetValues<SignatureSource>()) filters.Add(For(kind, source));
        }

        var embedded = (from status in Enum.GetValues<SignatureStatus>()
                        from mode in Enum.GetValues<RevocationMode>()
                        select SignatureGroup.From(status, mode))
            .OfType<SignatureGroup>().Distinct().OrderBy(group => group.Code).ToArray();

        filters.AddRange(embedded.Select(Exact));
        filters.AddRange(embedded.Select(group => Exact(group.AsCatalog())));
        filters.Add(Exact(SignatureGroup.NoCatalogMatch));
        filters.Add(Exact(SignatureGroup.IncompleteCatalogSearch));

        return filters.Select(filter => new FilterOption<SignatureFilter>(filter, () => filter.Format(strings))).ToArray();
    }
}
