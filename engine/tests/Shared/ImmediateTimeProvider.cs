namespace Ascent.Tests.Shared;

/// <summary>A clock whose timers fire at once, so retry delays don't slow tests down. The time itself stands still.</summary>
internal sealed class ImmediateTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>How many timers were started: one per delay.</summary>
    public int Delays { get; private set; }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Delays++;
        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new Fired();
    }

    private sealed class Fired : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
