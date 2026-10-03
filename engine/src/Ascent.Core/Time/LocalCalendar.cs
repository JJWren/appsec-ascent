using System.Globalization;
using Ascent.Core.Errors;

namespace Ascent.Core.Time;

/// <summary>An ISO 8601 week (weeks start on Monday; week 1 contains the year's first Thursday).</summary>
/// <param name="Year">The ISO week-numbering year.</param>
/// <param name="Week">The week number, 1–53.</param>
public readonly record struct IsoWeek(int Year, int Week) : IComparable<IsoWeek>
{
    /// <summary>The week containing <paramref name="date"/>.</summary>
    public static IsoWeek Of(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        return new IsoWeek(ISOWeek.GetYear(dateTime), ISOWeek.GetWeekOfYear(dateTime));
    }

    /// <summary>Monday of this week.</summary>
    public DateOnly Monday => DateOnly.FromDateTime(ISOWeek.ToDateTime(Year, Week, DayOfWeek.Monday));

    /// <summary>The week before this one.</summary>
    public IsoWeek Previous() => Of(Monday.AddDays(-7));

    /// <summary>True when <paramref name="date"/> falls in this week.</summary>
    public bool Contains(DateOnly date) => Of(date) == this;

    /// <inheritdoc />
    public int CompareTo(IsoWeek other) => Year != other.Year ? Year.CompareTo(other.Year) : Week.CompareTo(other.Week);

    /// <summary>Formats as <c>2026-W40</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year}-W{Week:00}");

    /// <summary>Earlier than.</summary>
    public static bool operator <(IsoWeek left, IsoWeek right) => left.CompareTo(right) < 0;

    /// <summary>Later than.</summary>
    public static bool operator >(IsoWeek left, IsoWeek right) => left.CompareTo(right) > 0;

    /// <summary>Earlier than or equal.</summary>
    public static bool operator <=(IsoWeek left, IsoWeek right) => left.CompareTo(right) <= 0;

    /// <summary>Later than or equal.</summary>
    public static bool operator >=(IsoWeek left, IsoWeek right) => left.CompareTo(right) >= 0;
}

/// <summary>Local days and ISO weeks in the Learner's time zone (PORT-U2-02, P21).</summary>
public sealed class LocalCalendar
{
    private readonly TimeProvider time;

    /// <summary>Creates a calendar for a time zone.</summary>
    public LocalCalendar(TimeProvider time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(zone);
        this.time = time;
        Zone = zone;
    }

    /// <summary>The time zone that defines local days.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>Today's local date.</summary>
    public DateOnly Today => ToLocalDate(time.GetUtcNow());

    /// <summary>This local week.</summary>
    public IsoWeek CurrentWeek => IsoWeek.Of(Today);

    /// <summary>
    /// Resolves a configured time zone: an IANA ID (<c>America/Phoenix</c>) or a Windows ID (<c>US Mountain Standard Time</c>).
    /// Null or empty means the operating system's zone.
    /// </summary>
    public static TimeZoneInfo ResolveZone(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return TimeZoneInfo.Local;
        }

        const string hint = "Use an IANA ID such as 'America/Phoenix', or a Windows ID such as 'US Mountain Standard Time'.";
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(configured.Trim());
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new AscentException("Unknown time zone '" + configured + "'.", hint, ExitCodes.Usage, ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new AscentException("The time zone '" + configured + "' could not be read.", hint, ExitCodes.Usage, ex);
        }
    }

    /// <summary>The local date of a UTC instant.</summary>
    public DateOnly ToLocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);
}
