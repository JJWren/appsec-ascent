using System.Globalization;

namespace Ascent.Core.Time;

/// <summary>The one text format for stored UTC instants: <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>.</summary>
public static class Utc
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>Formats an instant as UTC text.</summary>
    public static string ToText(DateTimeOffset instant) => instant.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>Parses UTC text written by <see cref="ToText"/>.</summary>
    public static DateTimeOffset Parse(string text) =>
        DateTimeOffset.ParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>Parses UTC text, or returns null for null or malformed text.</summary>
    public static DateTimeOffset? TryParse(string? text) =>
        text is not null && DateTimeOffset.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;
}
