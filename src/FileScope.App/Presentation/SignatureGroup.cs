using FileScope.App.Localization;
using FileScope.Core.Models;

namespace FileScope.App.Presentation;

/// <summary>A signature group shared by the displayed code and the move-selection rule.</summary>
public readonly record struct SignatureGroup
{
    public SignatureStatus Status { get; }
    public char Code { get; }
    public RevocationMode? Mode { get; }

    private SignatureGroup(SignatureStatus status, char code, RevocationMode? mode = null)
    {
        Status = status;
        Code = code;
        Mode = mode;
    }

    /// <summary>
    /// Assigns stable FileScope codes, independent of enum ordinals and Windows error codes.
    /// Only valid results distinguish online/cache modes; other statuses normalize the mode.
    /// Unknown statuses or an unknown mode for a valid result have no group.
    /// </summary>
    public static SignatureGroup? From(SignatureStatus status, RevocationMode mode) => status switch
    {
        SignatureStatus.NotChecked => new(status, '0'),
        SignatureStatus.Valid when mode == RevocationMode.Online => new(status, '1', mode),
        SignatureStatus.Valid when mode == RevocationMode.OfflineCache => new(status, '2', mode),
        SignatureStatus.NoEmbeddedSignature => new(status, '3'),
        SignatureStatus.InvalidSignature => new(status, '4'),
        SignatureStatus.Untrusted => new(status, '5'),
        SignatureStatus.Expired => new(status, '6'),
        SignatureStatus.Revoked => new(status, '7'),
        SignatureStatus.RevocationUnknown => new(status, '8'),
        SignatureStatus.UnsupportedFormat => new(status, '9'),
        SignatureStatus.UnsupportedPlatform => new(status, 'A'),
        SignatureStatus.CheckFailed => new(status, 'B'),
        _ => null
    };

    /// <summary>Prefixes the localized status; valid groups include their revocation-mode detail.</summary>
    public string Format(LocalizationService strings)
    {
        var key = Status == SignatureStatus.Valid
            ? Mode == RevocationMode.Online ? "Signature_ValidOnline" : "Signature_ValidOffline"
            : $"Signature_{Status}";
        return $"{Code} | {strings[key]}";
    }
}
