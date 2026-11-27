using FileScope.Core.Models;
using FileScope.Core.Signatures;

namespace DuplicateFinder.Core;

public sealed record SearchRoot(string Path, int Priority);
public sealed record ScanIssue(string Path, string Reason);
public sealed record ScanProgress(string Stage, string Path, int Completed, int Total);
public enum StreamComparison { Unmatched, Identical, Different, Unknown }
public sealed record SearchOptions(IReadOnlyList<ChecksumAlgorithm> Hashes, bool Embedded, bool Catalogs, RevocationMode Revocation)
{
    public FileSizeRange SizeRange { get; init; }
}

public sealed class FoundFile
{
    internal FoundFile(string path, SearchRoot root)
    {
        Path = path;
        Root = root;
    }

    public string Path { get; }
    public SearchRoot Root { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public long? Size => Stamp?.Length;
    public DateTime? ModifiedUtc => Stamp?.ModifiedUtc;
    public bool IsHardLink => Stamp?.Links > 1;
    public int Group { get; internal set; }
    public int Variant { get; internal set; }
    public StreamComparison Streams { get; internal set; }
    public string? Diagnostic { get; internal set; }
    public IReadOnlyDictionary<ChecksumAlgorithm, string> Hashes { get; internal set; } = new Dictionary<ChecksumAlgorithm, string>();
    public SignatureResult Embedded { get; internal set; } = SignatureResult.NotChecked;
    public CatalogLookup? Catalogs { get; internal set; }

    internal FileStamp? Stamp { get; set; }
    internal IReadOnlyList<NamedStream>? Manifest { get; set; }
}

public sealed record ScanResult(IReadOnlyList<FoundFile> Files, IReadOnlyList<ScanIssue> Issues);
internal readonly record struct FileKey(ulong Volume, Guid Id);
internal sealed record FileStamp(FileKey Key, long Length, DateTime ModifiedUtc, long ChangeTime, uint Links);
internal sealed record NamedStream(string Suffix, long Length);
