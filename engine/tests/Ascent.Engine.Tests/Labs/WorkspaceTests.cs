using Ascent.Core;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Labs;
using Ascent.Sealing;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Unsealing;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Labs;

/// <summary>The Learner workspace (P4), plant specs and planting (FLAG-04, P5), and safe extraction into it (P9).</summary>
public sealed class WorkspaceTests
{
    [Fact]
    public void The_workspace_starts_from_release_zero_with_its_own_ignore_file()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("throughline/src/Api/Program.cs", "// release 0");
        temp.WriteFile("throughline/src/Api/bin/Debug/app.dll", "binary");
        var workspace = new Workspace(new EnginePaths(temp.Path), new RecordingProcessRunner());

        workspace.EnsureCreated().ShouldBeTrue();
        workspace.EnsureCreated().ShouldBeFalse();

        File.ReadAllText(temp.Combine("my-work", "throughline", "src", "Api", "Program.cs")).ShouldBe("// release 0");
        File.Exists(temp.Combine("my-work", "throughline", "src", "Api", "bin", "Debug", "app.dll")).ShouldBeFalse();
        File.ReadAllText(temp.Combine("my-work", ".gitignore")).ShouldContain(".flags/");
        workspace.IsRepository.ShouldBeFalse();
        workspace.GitAvailable.ShouldBeFalse();
        workspace.Deliverables.ShouldBe(temp.Combine("my-work", "deliverables"));
        workspace.Drills.ShouldBe(temp.Combine("my-work", "drills"));

        using var bare = new TempDirectory();
        new Workspace(new EnginePaths(bare.Path), new RecordingProcessRunner()).EnsureCreated().ShouldBeTrue();
        Directory.Exists(Path.Join(bare.Path, "my-work", "throughline")).ShouldBeTrue();
    }

    [Fact]
    public async Task Git_steps_run_in_the_workspace_and_stop_at_the_first_failure()
    {
        using var temp = new TempDirectory();
        var git = new RecordingProcessRunner { Installed = { ExternalTool.Git } };
        var workspace = new Workspace(new EnginePaths(temp.Path), git);
        var ct = TestContext.Current.CancellationToken;

        await workspace.RunAsync(Workspace.InitSteps, ct);
        git.Lines(ExternalTool.Git).ShouldBe(["init", "add --all", "commit --quiet --message Start my AppSec Ascent workspace"]);
        git.Commands.ShouldAllBe(c => c.WorkingDirectory == temp.Combine("my-work"));
        Workspace.InitSteps[2].ToString().ShouldBe("git commit --quiet --message \"Start my AppSec Ascent workspace\"");

        git.Respond = c => c.Arguments[0] == "status" ? RecordingProcessRunner.Ok(" M throughline/x.cs\n") : null;
        (await workspace.HasChangesAsync(ct)).ShouldBeTrue();
        git.Respond = c => c.Arguments[0] == "commit" ? RecordingProcessRunner.Fail("fatal: empty ident name not allowed\n") : null;
        var error = await Should.ThrowAsync<AscentException>(() => workspace.RunAsync([new(["add", "--all"]), new(["commit", "-m", "x"]), new(["branch", "b"])], ct));
        error.Message.ShouldContain("fatal: empty ident name not allowed");
        error.NextStep!.ShouldContain("git config --global user.name");
        git.Lines(ExternalTool.Git).ShouldNotContain("branch b");

        await Should.ThrowAsync<ToolNotFoundException>(() => new Workspace(new EnginePaths(temp.Path), new RecordingProcessRunner()).HasChangesAsync(ct));
    }

    [Fact]
    public void Releases_replace_the_workspace_only_once_they_unpack_cleanly()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(new EnginePaths(temp.Path), new RecordingProcessRunner());
        workspace.EnsureCreated();
        temp.WriteFile("my-work/throughline/old.txt", "release 0");
        temp.WriteFile("release/new.txt", "release 1");
        var release = SafeArchive.CreateTarGz(temp.Combine("release"));

        using (var item = Item(release))
        {
            workspace.ReplaceRelease(item).ShouldBe(1);
        }

        File.Exists(temp.Combine("my-work", "throughline", "old.txt")).ShouldBeFalse();
        File.ReadAllText(temp.Combine("my-work", "throughline", "new.txt")).ShouldBe("release 1");

        using (var damaged = Item([1, 2, 3]))
        {
            Should.Throw<Exception>(() => workspace.ReplaceRelease(damaged));
        }

        File.ReadAllText(temp.Combine("my-work", "throughline", "new.txt")).ShouldBe("release 1");
        Directory.Exists(temp.Combine("my-work", ".release-staging")).ShouldBeFalse();
    }

    [Fact]
    public void Modules_overwrite_their_own_files_but_never_write_through_a_link()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("module/src/Search.cs", "// vulnerable");
        var module = SafeArchive.CreateTarGz(temp.Combine("module"));
        var workspace = new Workspace(new EnginePaths(temp.Path), new RecordingProcessRunner());
        workspace.EnsureCreated();
        temp.WriteFile("my-work/throughline/src/Search.cs", "// release");
        temp.WriteFile("my-work/throughline/src/Other.cs", "// untouched");

        using (var item = Item(module))
        {
            workspace.ExtractModule(item).ShouldBe(1);
        }

        File.ReadAllText(temp.Combine("my-work", "throughline", "src", "Search.cs")).ShouldBe("// vulnerable");
        File.ReadAllText(temp.Combine("my-work", "throughline", "src", "Other.cs")).ShouldBe("// untouched");

        // A link inside the workspace can't redirect a write outside it.
        Directory.Delete(temp.Combine("my-work", "throughline", "src"), recursive: true);
        Directory.CreateDirectory(temp.Combine("outside"));
        try
        {
            Directory.CreateSymbolicLink(temp.Combine("my-work", "throughline", "src"), temp.Combine("outside"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Creating links needs Developer Mode or admin rights on Windows.
        }

        using (var item = Item(module))
        {
            Should.Throw<UnsafePathException>(() => workspace.ExtractModule(item)).Message.ShouldContain("is a link");
        }

        File.Exists(temp.Combine("outside", "Search.cs")).ShouldBeFalse();
    }

    [Theory]
    [Trait("Rule", "FLAG-04")]
    [InlineData("{\"kind\":\"file\",\"path\":\"throughline/.flags/x.txt\"}", PlantKind.File)]
    [InlineData("{\"kind\":\"command\",\"tool\":\"dotnet\",\"arguments\":[\"run\",\"--project\",\"throughline/tools/Planter\"],\"workingDirectory\":\"throughline\"}", PlantKind.Command)]
    [InlineData("{\"kind\":\"command\",\"tool\":\"docker\"}", PlantKind.Command)]
    public void Plant_specs_parse(string json, PlantKind kind) => PlantSpec.Parse(json).Kind.ShouldBe(kind);

    [Theory]
    [InlineData("not json", "couldn't be read")]
    [InlineData("[]", "isn't a JSON object")]
    [InlineData("{\"kind\":\"sql\"}", "kind 'sql'")]
    [InlineData("{\"kind\":\"file\"}", "needs a path")]
    [InlineData("{\"kind\":\"file\",\"path\":\"throughline/src/flag.txt\"}", "under throughline/.flags/")]
    [InlineData("{\"kind\":\"file\",\"path\":\"throughline/.flags/\"}", "under throughline/.flags/")]
    [InlineData("{\"kind\":\"command\",\"tool\":\"bash\"}", "only run dotnet or docker")]
    [InlineData("{\"kind\":\"command\",\"tool\":\"dotnet\",\"arguments\":[1]}", "must be text")]
    public void Unusable_plant_specs_are_refused(string json, string problem) =>
        Should.Throw<AscentException>(() => PlantSpec.Parse(json)).Message.ShouldContain(problem);

    [Fact]
    [Trait("Rule", "FLAG-04")]
    public async Task Flags_reach_the_planter_on_standard_input_never_as_an_argument()
    {
        using var temp = new TempDirectory();
        var runner = new RecordingProcessRunner { Installed = { ExternalTool.Dotnet } };
        var planter = new Planter(runner, OwnerOnlyFiles.ForCurrentOs());
        const string Flag = "ASCENT{SECRETSECRETSECRETSECRETSE}";
        var ct = TestContext.Current.CancellationToken;

        await planter.PlantAsync(PlantSpec.Parse("{\"kind\":\"command\",\"tool\":\"dotnet\",\"arguments\":[\"run\"],\"workingDirectory\":\"throughline\"}"), Flag, temp.Path, ct);
        var command = runner.Commands.Single();
        command.StandardInput.ShouldBe(Flag + "\n");
        command.Arguments.ShouldNotContain(a => a.Contains("SECRET", StringComparison.Ordinal));
        command.WorkingDirectory.ShouldBe(temp.Combine("throughline"));

        await planter.PlantAsync(PlantSpec.Parse("{\"kind\":\"file\",\"path\":\"throughline/.flags/x.txt\"}"), Flag, temp.Path, ct);
        File.ReadAllText(temp.Combine("throughline", ".flags", "x.txt")).ShouldBe(Flag);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(temp.Combine("throughline", ".flags", "x.txt")).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        runner.Respond = _ => RecordingProcessRunner.Fail();
        (await Should.ThrowAsync<AscentException>(() => planter.PlantAsync(PlantSpec.Parse("{\"kind\":\"command\",\"tool\":\"dotnet\"}"), Flag, temp.Path, ct)))
            .Message.ShouldBe("Planting the Flag failed.");
    }

    private static UnsealedItem Item(byte[] content) =>
        new(BundleHeader.Create("x.module", "lab-module", SealTier.Start, SafeArchive.ContentType, new byte[12], DateTimeOffset.UnixEpoch), content);
}
