using FileScope.Core.Models;
using FileScope.Core.Signatures;
using FileScope.Shared.Localization;

namespace FileScope.Shared.Presentation;

public readonly record struct SignatureBadgeItem(string Text, StatusTone Tone);

/// <summary>Distinct catalog outcomes for a row. Several members and an incomplete lookup can coexist.</summary>
public sealed class CatalogPresentation
{
    private static readonly CatalogPresentation Unchecked = new([default(SignatureGroup).AsCatalog()]);
    private static readonly CatalogPresentation NoMatch = new([SignatureGroup.NoCatalogMatch]);
    private static readonly CatalogPresentation Incomplete = new([SignatureGroup.IncompleteCatalogSearch]);

    public IReadOnlyList<SignatureGroup> Groups { get; }

    private CatalogPresentation(IReadOnlyList<SignatureGroup> groups) => Groups = groups;

    public static CatalogPresentation From(CatalogLookup? lookup)
    {
        if (lookup is null) return Unchecked;

        var incomplete = IsIncomplete(lookup);
        if (lookup.Matches.Count == 0) return incomplete ? Incomplete : NoMatch;

        var groups = lookup.Matches.Select(match => MemberGroup(match.Trust))
            .Distinct().OrderBy(group => group.Code).ToList();
        if (incomplete) groups.Add(SignatureGroup.IncompleteCatalogSearch);

        return new(groups.AsReadOnly());
    }

    public static bool IsIncomplete(CatalogLookup lookup) =>
        !lookup.IndexComplete || lookup.IndexIssues.Count > 0 || lookup.Issues.Count > 0;

    public IEnumerable<SignatureBadgeItem> Badges(LocalizationService strings) =>
        Groups.Select(group => new SignatureBadgeItem(group.Format(strings), group.Tone));

    public string Summary(LocalizationService strings) => string.Join(" · ", Groups.Select(group => group.Format(strings)));

    public static string Summary(CatalogLookup? lookup, LocalizationService strings) => From(lookup).Summary(strings);

    public static IEnumerable<string> Details(CatalogLookup lookup, LocalizationService strings)
    {
        foreach (var match in lookup.Matches)
        {
            yield return $"{strings[match.Registered ? "Registered" : "Loose"]} · {match.Path}";
            var native = match.Trust.NativeStatusCode is int code ? $"0x{unchecked((uint)code):X8}" : null;
            var fields = new[] { match.Member, MemberGroup(match.Trust).Format(strings), match.Trust.Certificate?.Publisher, native, match.Trust.Diagnostic };
            yield return string.Join(" · ", fields.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        foreach (var issue in lookup.IndexIssues) yield return issue;
        foreach (var issue in lookup.Issues) yield return issue;
    }

    private static SignatureGroup MemberGroup(SignatureResult trust)
    {
        var group = SignatureGroup.From(trust.Status, trust.RevocationMode)
            ?? SignatureGroup.From(SignatureStatus.CheckFailed, trust.RevocationMode)!.Value;
        return group.AsCatalog();
    }
}
