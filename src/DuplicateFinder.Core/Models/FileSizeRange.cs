using System.Text.Json.Serialization;

namespace DuplicateFinder.Core.Models;

/// <summary>Inclusive limits on the unnamed data stream, in bytes. A missing bound is open.</summary>
public readonly record struct FileSizeRange
{
    [JsonConstructor]
    public FileSizeRange(long? minimumBytes, long? maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumBytes.GetValueOrDefault(), nameof(minimumBytes));
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes.GetValueOrDefault(), nameof(maximumBytes));
        if (minimumBytes > maximumBytes)
        {
            throw new ArgumentException("The minimum file size exceeds the maximum.", nameof(minimumBytes));
        }

        MinimumBytes = minimumBytes;
        MaximumBytes = maximumBytes;
    }

    public long? MinimumBytes { get; }
    public long? MaximumBytes { get; }

    public bool Includes(long bytes) =>
        bytes >= MinimumBytes.GetValueOrDefault() && bytes <= MaximumBytes.GetValueOrDefault(long.MaxValue);
}
