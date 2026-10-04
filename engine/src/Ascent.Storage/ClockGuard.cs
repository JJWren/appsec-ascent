using Ascent.Core.Time;
using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>A clock reading taken when a command opens the progress store.</summary>
/// <param name="Now">The system clock's time.</param>
/// <param name="EffectiveNow">The later of <paramref name="Now"/> and the latest time any earlier command saw.</param>
/// <param name="MovedBackwards">True when the system clock is more than <see cref="ClockGuard.Tolerance"/> behind that latest time.</param>
public sealed record ClockReading(DateTimeOffset Now, DateTimeOffset EffectiveNow, bool MovedBackwards);

/// <summary>
/// Keeps timed attempts monotonic (P17). Deadlines are checked against <see cref="ClockReading.EffectiveNow"/>, so
/// turning the clock back can't add time. This is a spoiler shield, not DRM (SEAL-06).
/// </summary>
public static class ClockGuard
{
    /// <summary>How far back the clock may drift before the Engine warns.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    /// <summary>Reads the clock and records the latest time seen.</summary>
    public static ClockReading Read(SqliteConnection connection, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        var now = time.GetUtcNow();
        var lastSeen = Utc.TryParse(MetaStore.Get(connection, MetaStore.LastSeenUtc));
        var effective = lastSeen is { } seen && seen > now ? seen : now;
        MetaStore.Set(connection, MetaStore.LastSeenUtc, Utc.ToText(effective));
        return new ClockReading(now, effective, lastSeen is { } previous && now < previous - Tolerance);
    }
}
