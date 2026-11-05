namespace FileScope.Core.Models;

public sealed record FilePropertiesInfo
{
    public string? FileDescription { get; init; }
    public string? Language { get; init; }
    public string? FileType { get; init; }
    public string? FileVersion { get; init; }
    public string? ProductName { get; init; }
    public string? ProductVersion { get; init; }
    public string? CompanyName { get; init; }
    public string? OriginalFilename { get; init; }
    public string? InternalName { get; init; }
    public string? Copyright { get; init; }
    public string? Trademarks { get; init; }
    public string? Comments { get; init; }
}

// Null result: option disabled. Null Header: inspected, but no supported image found.
// Optional metadata failures carry diagnostics without discarding file hashes.
public sealed record FilePropertiesResult(FilePropertiesInfo? Info, string? Diagnostic = null);
public enum ManagedPlatform { None, AnyCpu, Prefer32Bit, MachineSpecific }
// Historical PE names remain compatible with settings and export column IDs.
// NE images have no COFF Machine field; do not invent a PE code for them.
public sealed record PeHeaderInfo(ushort? MachineCode, string MachineName, string Format, ManagedPlatform ManagedPlatform);
public sealed record PeHeaderResult(PeHeaderInfo? Header, string? Diagnostic = null);
