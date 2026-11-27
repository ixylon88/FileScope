using FileScope.Core.Models;
using FileScope.Core.Signatures;
using FileScope.Shared.Localization;

namespace FileScope.Shared.Presentation;

/// <summary>Catalog membership and the catalog's trust result are independent of the embedded signature.</summary>
public static class CatalogPresentation
{
    public static string Summary(CatalogLookup? lookup, LocalizationService strings)
    {
        if (lookup is null) return strings["Signature_NotChecked"];
        var statuses = lookup.Matches.Select(match => Trust(match.Trust, strings)).Distinct().ToList();
        if (!lookup.IndexComplete || lookup.Issues.Count > 0) statuses.Add(strings["Incomplete"]);
        return statuses.Count > 0 ? string.Join(" · ", statuses) : strings["NoCatalog"];
    }

    public static IEnumerable<string> Details(CatalogLookup lookup, LocalizationService strings)
    {
        foreach (var match in lookup.Matches)
        {
            yield return $"{strings[match.Registered ? "Registered" : "Loose"]} · {match.Path}";
            var native = match.Trust.NativeStatusCode is int code ? $"0x{unchecked((uint)code):X8}" : null;
            var fields = new[] { match.Member, Trust(match.Trust, strings), match.Trust.Certificate?.Publisher, native, match.Trust.Diagnostic };
            yield return string.Join(" · ", fields.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        foreach (var issue in lookup.IndexIssues) yield return issue;
        foreach (var issue in lookup.Issues) yield return issue;
    }

    private static string Trust(SignatureResult trust, LocalizationService strings)
    {
        var group = SignatureGroup.From(trust.Status, trust.RevocationMode);
        return trust.Status == SignatureStatus.NoEmbeddedSignature
            ? $"{group?.Code} | {strings["CatalogUnsigned"]}"
            : group?.Format(strings) ?? strings["Signature_CheckFailed"];
    }
}
