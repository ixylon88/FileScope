using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace FileScope.App.Infrastructure;

/// <summary>
/// A sealed class that serves as a build date attribute.
/// </summary>
[ExcludeFromCodeCoverage]
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class BuildDateAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BuildDateAttribute"/> class.
    /// </summary>
    /// <param name="rawValue">The raw input value (date time string of the form yyyyMMddHHmmss).</param>
    public BuildDateAttribute(string rawValue)
    {
        RawValue = rawValue;
        BuildTimestamp = DateTimeOffset.ParseExact(rawValue, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    /// <summary>
    /// Gets the raw value (date time string).
    /// </summary>
    public string RawValue { get; }

    /// <summary>
    /// Gets the build date time.
    /// </summary>
    public DateTimeOffset BuildTimestamp { get; }
}