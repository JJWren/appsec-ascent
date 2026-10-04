namespace Ascent.Cli.Tests.TestSupport;

/// <summary>A clock tests can move in either direction (FakeTimeProvider refuses to go backwards).</summary>
internal sealed class MutableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
