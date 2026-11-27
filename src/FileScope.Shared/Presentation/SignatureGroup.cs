using FileScope.Core.Models;
using FileScope.Shared.Localization;

namespace FileScope.Shared.Presentation;

public enum StatusTone { Neutral, Success, Information, Warning, Error }

/// <summary>A stable display and filter code. Embedded groups also determine FileScope's move selection.</summary>
public readonly record struct SignatureGroup
{
    private const byte CatalogPrefix = 0x10;
    private const byte NoCatalogCode = 0x1C;
    private const byte IncompleteCode = 0x1D;
    private readonly SignatureStatus _trustStatus;

    public byte Code { get; }
    public RevocationMode? Mode { get; }
    public bool IsCatalog => (Code & CatalogPrefix) != 0;
    public SignatureStatus? Status => Code is NoCatalogCode or IncompleteCode ? null : _trustStatus;
    public StatusTone Tone => Code == IncompleteCode ? StatusTone.Warning : ToneFor(_trustStatus);

    public static SignatureGroup NoCatalogMatch => new(SignatureStatus.NotChecked, NoCatalogCode);
    public static SignatureGroup IncompleteCatalogSearch => new(SignatureStatus.NotChecked, IncompleteCode);

    private SignatureGroup(SignatureStatus status, byte code, RevocationMode? mode = null)
    {
        _trustStatus = status;
        Code = code;
        Mode = mode;
    }

    internal SignatureGroup AsCatalog() => new(_trustStatus, (byte)(Code | CatalogPrefix), Mode);

    /// <summary>
    /// Assigns stable FileScope codes, independent of enum ordinals and Windows error codes.
    /// Only valid results distinguish online/cache modes; other statuses normalize the mode.
    /// Unknown statuses or an unknown mode for a valid result have no group.
    /// </summary>
    public static SignatureGroup? From(SignatureStatus status, RevocationMode mode) => status switch
    {
        SignatureStatus.NotChecked => new(status, 0x0),
        SignatureStatus.Valid when mode == RevocationMode.Online => new(status, 0x1, mode),
        SignatureStatus.Valid when mode == RevocationMode.OfflineCache => new(status, 0x2, mode),
        SignatureStatus.NoEmbeddedSignature => new(status, 0x3),
        SignatureStatus.InvalidSignature => new(status, 0x4),
        SignatureStatus.Untrusted => new(status, 0x5),
        SignatureStatus.Expired => new(status, 0x6),
        SignatureStatus.Revoked => new(status, 0x7),
        SignatureStatus.RevocationUnknown => new(status, 0x8),
        SignatureStatus.UnsupportedFormat => new(status, 0x9),
        SignatureStatus.UnsupportedPlatform => new(status, 0xA),
        SignatureStatus.CheckFailed => new(status, 0xB),
        _ => null
    };

    public static StatusTone ToneFor(SignatureStatus status) => status switch
    {
        SignatureStatus.Valid => StatusTone.Success,
        SignatureStatus.Untrusted or SignatureStatus.RevocationUnknown or SignatureStatus.UnsupportedFormat
            or SignatureStatus.UnsupportedPlatform => StatusTone.Warning,
        SignatureStatus.InvalidSignature or SignatureStatus.Expired or SignatureStatus.Revoked
            or SignatureStatus.CheckFailed => StatusTone.Error,
        SignatureStatus.NotChecked or SignatureStatus.NoEmbeddedSignature => StatusTone.Neutral,
        _ => StatusTone.Neutral
    };

    /// <summary>Prefixes the localized status; valid groups include their revocation-mode detail.</summary>
    public string Format(LocalizationService strings)
    {
        var key = Code switch
        {
            NoCatalogCode => "NoCatalog",
            IncompleteCode => "Incomplete",
            _ when IsCatalog && Status == SignatureStatus.NoEmbeddedSignature => "CatalogUnsigned",
            _ when Status == SignatureStatus.Valid => Mode == RevocationMode.Online ? "Signature_ValidOnline" : "Signature_ValidOffline",
            _ => $"Signature_{Status}"
        };
        return $"{Code:X} | {strings[key]}";
    }
}
