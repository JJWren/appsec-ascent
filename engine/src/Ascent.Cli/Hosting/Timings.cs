using System.Diagnostics;

namespace Ascent.Cli.Hosting;

/// <summary>Phase timings printed by <c>--timings</c> (P19).</summary>
public sealed class Timings
{
    private readonly List<(string Phase, TimeSpan Elapsed)> marks = [];
    private long last = Stopwatch.GetTimestamp();

    /// <summary>The phases recorded so far.</summary>
    public IReadOnlyList<(string Phase, TimeSpan Elapsed)> Marks => marks;

    /// <summary>Records the time since the previous mark under <paramref name="phase"/>.</summary>
    public void Mark(string phase)
    {
        var now = Stopwatch.GetTimestamp();
        marks.Add((phase, Stopwatch.GetElapsedTime(last, now)));
        last = now;
    }

    /// <summary>Starts timing afresh.</summary>
    public void Restart()
    {
        marks.Clear();
        last = Stopwatch.GetTimestamp();
    }
}
