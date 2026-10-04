using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Ascent.Core;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Sealing;
using Ascent.Sealing.KeyRelease;
using Ascent.Sealing.Unsealing;
using Microsoft.Extensions.Time.Testing;

namespace Ascent.Engine.Tests.Sealing;

/// <summary>Unsealed scopes (P3) and safe archives (P9).</summary>
public sealed class UnsealingTests
{
    private readonly IOwnerOnlyFiles files = OwnerOnlyFiles.ForCurrentOs();

    [Fact]
    [Trait("Rule", "SEAL-04")]
    public void A_scope_is_owner_only_isolated_and_deleted_on_dispose()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);
        string directory;
        using (var scope = UnsealedScope.Create(paths, files, new SeededRandom(3)))
        {
            directory = scope.Directory;
            Path.GetDirectoryName(directory).ShouldBe(paths.Unsealed);
            Path.GetFileName(directory).ShouldMatch("^[0-9a-f]{32}$");
            var written = scope.WriteFile("nested/answer.txt", "decrypted"u8, files);
            File.ReadAllText(written).ShouldBe("decrypted");
            Should.Throw<UnsafePathException>(() => scope.WriteFile("../escape.txt", "x"u8, files));
            foreach (var stopper in UnsealedScope.StopperFiles)
            {
                File.ReadAllText(Path.Join(paths.Unsealed, stopper)).ShouldContain("<Project>");
            }

            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(directory).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.GetUnixFileMode(written).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            scope.Dispose();
        }

        Directory.Exists(directory).ShouldBeFalse();
    }

    [Fact]
    [Trait("Rule", "SEAL-04")]
    public void The_sweeper_removes_abandoned_scopes_but_not_scopes_in_use()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);
        UnsealedSweeper.Sweep(paths).ShouldBe(0);

        using var live = UnsealedScope.Create(paths, files, new SeededRandom(4));
        var abandoned = Path.Join(paths.Unsealed, "deadbeefdeadbeefdeadbeefdeadbeef");
        Directory.CreateDirectory(abandoned);
        File.WriteAllText(Path.Join(abandoned, ".lock"), string.Empty);
        File.WriteAllText(Path.Join(abandoned, "leftover.txt"), "secret");
        var unlocked = Path.Join(paths.Unsealed, "0000000000000000000000000000000f");
        Directory.CreateDirectory(unlocked);

        UnsealedSweeper.Sweep(paths).ShouldBe(2);

        Directory.Exists(abandoned).ShouldBeFalse();
        Directory.Exists(unlocked).ShouldBeFalse();
        Directory.Exists(live.Directory).ShouldBeTrue();
    }

    [Fact]
    public void Released_archives_extract_into_a_scope()
    {
        using var bundles = new TestBundles();
        using var source = new TempDirectory();
        source.WriteFile("src/Lab.cs", "class Lab {}");
        source.WriteFile("README.md", "readme");
        bundles.Write("lab-d5-01.module", "lab-module", SealTier.Start, SafeArchive.CreateTarGz(source.Path));
        var facts = new FakeFacts { Labs = { ["lab-d5-01"] = LabStage.Started } };
        var store = new SealedStore(bundles.Locate, bundles.Verifier, bundles.Keys, new KeyReleasePolicy(facts), new SealedStoreTests.FakeReleaseLog(), new FakeTimeProvider());

        using var item = store.Open("lab-d5-01.module", ReleaseContext.LabStarted("lab-d5-01"));
        using var scope = UnsealedScope.Create(new EnginePaths(bundles.Repo.Path), files, new SeededRandom(5));
        var folder = scope.Extract(item, "module");

        File.ReadAllText(Path.Join(folder, "src", "Lab.cs")).ShouldBe("class Lab {}");
        File.ReadAllText(Path.Join(folder, "README.md")).ShouldBe("readme");
    }

    [Fact]
    public void Archives_are_deterministic()
    {
        using var source = new TempDirectory();
        source.WriteFile("b.txt", "bee");
        source.WriteFile("a/a.txt", "ay");

        SafeArchive.CreateTarGz(source.Path).ShouldBe(SafeArchive.CreateTarGz(source.Path));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/etc/cron.d/evil")]
    [InlineData("a/../../escape.txt")]
    [InlineData("C:/Windows/evil.txt")]
    public void Entries_cannot_escape_the_destination(string name)
    {
        using var temp = new TempDirectory();
        var archive = TarGz(writer => AddFile(writer, name, "x"));

        Should.Throw<UnsafePathException>(() => SafeArchive.ExtractTarGz(archive, temp.Combine("out")));
        Directory.EnumerateFiles(temp.Path, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(TarEntryType.SymbolicLink)]
    [InlineData(TarEntryType.HardLink)]
    [InlineData(TarEntryType.Fifo)]
    [InlineData(TarEntryType.CharacterDevice)]
    public void Links_and_devices_are_refused(TarEntryType type)
    {
        using var temp = new TempDirectory();
        var archive = TarGz(writer =>
        {
            var entry = new PaxTarEntry(type, "link");
            if (type is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            {
                entry.LinkName = "/etc/passwd";
            }

            writer.WriteEntry(entry);
        });

        Should.Throw<AscentException>(() => SafeArchive.ExtractTarGz(archive, temp.Combine("out"))).Message.ShouldContain(type.ToString());
    }

    [Fact]
    public void Archive_limits_are_enforced()
    {
        using var temp = new TempDirectory();
        var archive = TarGz(writer =>
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "folder/"));
            AddFile(writer, "folder/one.txt", new string('1', 100));
            AddFile(writer, "folder/two.txt", new string('2', 100));
            AddFile(writer, "folder/three.txt", new string('3', 100));
        });

        SafeArchive.ExtractTarGz(archive, temp.Combine("all"), new SafeArchive.ArchiveLimits(10, 1000, 200)).ShouldBe(3);
        Should.Throw<AscentException>(() => SafeArchive.ExtractTarGz(archive, temp.Combine("entries"), new SafeArchive.ArchiveLimits(2, 1000, 200)))
            .Message.ShouldContain("too many entries");
        Should.Throw<AscentException>(() => SafeArchive.ExtractTarGz(archive, temp.Combine("file"), new SafeArchive.ArchiveLimits(10, 1000, 50)))
            .Message.ShouldContain("per-file limit");
        Should.Throw<AscentException>(() => SafeArchive.ExtractTarGz(archive, temp.Combine("total"), new SafeArchive.ArchiveLimits(10, 250, 200)))
            .Message.ShouldContain("total size limit");
    }

    [Fact]
    public void Executable_files_keep_their_mode_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var source = new TempDirectory();
        var script = source.WriteFile("run.sh", "#!/bin/sh\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        source.WriteFile("data.txt", "data");

        using var reader = new TarReader(new GZipStream(new MemoryStream(SafeArchive.CreateTarGz(source.Path)), CompressionMode.Decompress));
        var modes = new Dictionary<string, UnixFileMode>();
        while (reader.GetNextEntry() is { } entry)
        {
            modes[entry.Name] = entry.Mode;
        }

        modes["run.sh"].HasFlag(UnixFileMode.UserExecute).ShouldBeTrue();
        modes["data.txt"].HasFlag(UnixFileMode.UserExecute).ShouldBeFalse();
    }

    private static byte[] TarGz(Action<TarWriter> write)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            write(writer);
        }

        return output.ToArray();
    }

    private static void AddFile(TarWriter writer, string name, string content) =>
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
}
