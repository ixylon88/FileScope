using FileScope.Core.Models;
using FileScope.Shared.Localization;

namespace FileScope.Shared.Presentation;

/// <summary>The same embedded-signature groups drive both filters and the displayed codes.</summary>
public readonly record struct SignatureFilter
{
    private readonly string? _category;
    public SignatureGroup? Group { get; }

    private SignatureFilter(string category) => _category = category;
    private SignatureFilter(SignatureGroup group) => Group = group;

    public static SignatureFilter All => default;
    public static SignatureFilter Valid => new("Filter_Valid");
    public static SignatureFilter ExceptValid => new("Filter_ExceptValid");
    public static SignatureFilter Attention => new("Filter_Attention");

    public static SignatureFilter Exact(SignatureGroup group) => new(group);

    public bool Matches(SignatureStatus status, RevocationMode mode)
    {
        if (Group is { } group) return SignatureGroup.From(status, mode) == group;
        if (this == Valid) return status == SignatureStatus.Valid;
        if (this == ExceptValid) return status != SignatureStatus.Valid;
        if (this == Attention) return status is not (SignatureStatus.Valid or SignatureStatus.NotChecked or SignatureStatus.NoEmbeddedSignature);
        return true;
    }

    public string Format(LocalizationService strings) => Group?.Format(strings) ?? strings[_category ?? "AllSignatures"];

    public static IReadOnlyList<FilterOption<SignatureFilter>> Options(LocalizationService strings)
    {
        var exact = from status in Enum.GetValues<SignatureStatus>()
                    from mode in Enum.GetValues<RevocationMode>()
                    select SignatureGroup.From(status, mode);
        var filters = new[] { All, Valid, ExceptValid, Attention }.Concat(
            exact.OfType<SignatureGroup>().Distinct().OrderBy(group => group.Code).Select(Exact));

        return filters.Select(filter => new FilterOption<SignatureFilter>(filter, () => filter.Format(strings))).ToArray();
    }
}
