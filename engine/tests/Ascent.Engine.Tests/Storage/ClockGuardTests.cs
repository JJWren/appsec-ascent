using Ascent.Core;
using Ascent.Core.Platform;
using Ascent.Core.Time;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Storage;

namespace Ascent.Engine.Tests.Storage;

public sealed class ClockGuardTests
{
    [Fact]
    public void The_clock_is_monotonic_and_backward_jumps_are_flagged()
    {
        using var temp = new TempDirectory();
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        using var database = ProgressDatabase.Open(new EnginePaths(temp.Path), clock, OwnerOnlyFiles.ForCurrentOs());

        var first = ClockGuard.Read(database.Connection, clock);
        first.MovedBackwards.ShouldBeFalse();
        first.EffectiveNow.ShouldBe(first.Now);

        // A small step back (within tolerance) isn't flagged, but time never runs backwards.
        clock.Now = first.Now - TimeSpan.FromMinutes(2);
        var small = ClockGuard.Read(database.Connection, clock);
        small.MovedBackwards.ShouldBeFalse();
        small.EffectiveNow.ShouldBe(first.Now);

        clock.Now = first.Now - TimeSpan.FromHours(3);
        var big = ClockGuard.Read(database.Connection, clock);
        big.MovedBackwards.ShouldBeTrue();
        big.EffectiveNow.ShouldBe(first.Now);

        clock.Now = first.Now + TimeSpan.FromHours(1);
        var later = ClockGuard.Read(database.Connection, clock);
        later.EffectiveNow.ShouldBe(first.Now + TimeSpan.FromHours(1));
        Utc.Parse(MetaStore.Get(database.Connection, MetaStore.LastSeenUtc)!).ShouldBe(later.EffectiveNow);
    }

    [Fact]
    public void Paths_are_derived_from_the_repository_root()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);
        paths.Database.ShouldBe(Path.Join(temp.Path, ".ascent", "progress.db"));
        paths.Backups.ShouldBe(Path.Join(temp.Path, ".ascent", "backups"));
        paths.Logs.ShouldBe(Path.Join(temp.Path, ".ascent", "logs"));
        paths.Unsealed.ShouldBe(Path.Join(temp.Path, ".ascent", "unsealed"));
        paths.Workspace.ShouldBe(Path.Join(temp.Path, "my-work"));
        paths.DefaultJournal.ShouldBe(Path.Join(temp.Path, "journal"));
    }

    [Fact]
    public void The_repository_root_is_found_from_a_subfolder()
    {
        using var temp = new TempDirectory();
        temp.WriteFile(EnginePaths.SolutionFile, "<Solution />");
        var nested = temp.Combine("engine", "src");
        Directory.CreateDirectory(nested);

        EnginePaths.FindRepoRoot(null, nested).ShouldBe(Path.GetFullPath(temp.Path));
        EnginePaths.FindRepoRoot(nested, temp.Path).ShouldBe(Path.GetFullPath(nested));
        using var other = new TempDirectory();
        EnginePaths.FindRepoRoot(null, other.Path).ShouldBe(Path.GetFullPath(other.Path));
    }
}
