using Ascent.Core.Errors;
using Ascent.Core.Time;
using Microsoft.Extensions.Time.Testing;

namespace Ascent.Engine.Tests.Time;

public sealed class LocalCalendarTests
{
    [Fact]
    public void Local_dates_follow_the_learners_time_zone()
    {
        var zone = LocalCalendar.ResolveZone("America/Phoenix"); // UTC-7, no daylight saving
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 5, 30, 0, TimeSpan.Zero));
        var calendar = new LocalCalendar(clock, zone);

        calendar.Today.ShouldBe(new DateOnly(2026, 10, 3));
        calendar.CurrentWeek.ShouldBe(new IsoWeek(2026, 40));
        calendar.Zone.ShouldBe(zone);
    }

    [Theory]
    [InlineData("America/New_York")]
    [InlineData("Eastern Standard Time")]
    [InlineData("Europe/London")]
    [InlineData("UTC")]
    public void Iana_and_windows_ids_both_resolve(string id) => LocalCalendar.ResolveZone(id).ShouldNotBeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_configured_zone_means_the_os_zone(string? id) => LocalCalendar.ResolveZone(id).ShouldBe(TimeZoneInfo.Local);

    [Fact]
    public void An_unknown_zone_is_a_usage_error()
    {
        var error = Should.Throw<AscentException>(() => LocalCalendar.ResolveZone("Mars/Olympus_Mons"));
        error.ExitCode.ShouldBe(ExitCodes.Usage);
        error.NextStep!.ShouldContain("IANA");
    }

    [Fact]
    public void Daylight_saving_transitions_keep_local_days_right()
    {
        var zone = LocalCalendar.ResolveZone("America/New_York");
        var calendar = new LocalCalendar(TimeProvider.System, zone);

        // 2026-03-08 02:00 local, clocks spring forward; 2026-11-01 02:00 local, clocks fall back.
        calendar.ToLocalDate(new DateTimeOffset(2026, 3, 8, 4, 59, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 3, 7));
        calendar.ToLocalDate(new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 3, 8));
        calendar.ToLocalDate(new DateTimeOffset(2026, 11, 2, 3, 59, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 11, 1));
        calendar.ToLocalDate(new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 11, 2));
    }

    [Fact]
    public void Iso_weeks_handle_week_53_and_year_boundaries()
    {
        IsoWeek.Of(new DateOnly(2026, 12, 31)).ShouldBe(new IsoWeek(2026, 53));
        IsoWeek.Of(new DateOnly(2027, 1, 1)).ShouldBe(new IsoWeek(2026, 53));
        IsoWeek.Of(new DateOnly(2027, 1, 4)).ShouldBe(new IsoWeek(2027, 1));
        IsoWeek.Of(new DateOnly(2025, 12, 29)).ShouldBe(new IsoWeek(2026, 1));
        new IsoWeek(2027, 1).Previous().ShouldBe(new IsoWeek(2026, 53));
        new IsoWeek(2026, 53).Monday.ShouldBe(new DateOnly(2026, 12, 28));
        new IsoWeek(2026, 40).Contains(new DateOnly(2026, 10, 4)).ShouldBeTrue();
        new IsoWeek(2026, 40).Contains(new DateOnly(2026, 10, 5)).ShouldBeFalse();
    }

    [Fact]
    public void Iso_weeks_compare_and_format()
    {
        var a = new IsoWeek(2026, 52);
        var b = new IsoWeek(2027, 1);
        (a < b).ShouldBeTrue();
        (b > a).ShouldBeTrue();
        (a <= new IsoWeek(2026, 52)).ShouldBeTrue();
        (b >= a).ShouldBeTrue();
        a.CompareTo(b).ShouldBeLessThan(0);
        new IsoWeek(2026, 3).ToString().ShouldBe("2026-W03");
    }

    [Fact]
    public void Utc_text_round_trips()
    {
        var instant = new DateTimeOffset(2026, 10, 3, 14, 52, 59, 123, TimeSpan.FromHours(-7));
        var text = Utc.ToText(instant);

        text.ShouldBe("2026-10-03T21:52:59.123Z");
        Utc.Parse(text).ShouldBe(instant);
        Utc.TryParse(text).ShouldBe(instant);
        Utc.TryParse("not a time").ShouldBeNull();
        Utc.TryParse(null).ShouldBeNull();
    }
}
