using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;

namespace Ascent.Engine.Tests.Platform;

public sealed class ProcessRunnerTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Runs_dotnet_with_an_argument_list()
    {
        using var temp = new TempDirectory();
        var result = await new ProcessRunner().RunAsync(
            new ToolCommand(ExternalTool.Dotnet, ["--version"], temp.Path, Generous),
            TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.StandardError);
        result.TimedOut.ShouldBeFalse();
        result.StandardOutput.Trim().ShouldMatch(@"^\d+\.\d+\.\d+");
    }

    [Fact]
    public async Task Arguments_are_passed_verbatim_without_a_shell()
    {
        using var temp = new TempDirectory();
        var (echo, prefix) = CreateEchoTool(temp);
        var runner = new ProcessRunner(new ExecutableResolver(Environment.GetEnvironmentVariable, new Dictionary<ExternalTool, string> { [ExternalTool.Git] = echo }));

        var result = await runner.RunAsync(
            new ToolCommand(ExternalTool.Git, [.. prefix, "two words", "$HOME", "semi;colon"], temp.Path, Generous),
            TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue(result.StandardError);
        var lines = result.StandardOutput.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.ShouldContain("two words");
        lines.ShouldContain("$HOME");
        lines.ShouldContain("semi;colon");
    }

    [Fact]
    public async Task Standard_input_reaches_the_tool()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // The Unix 'cat' check is enough; Windows has no equivalent without a batch file.
        }

        using var temp = new TempDirectory();
        var runner = new ProcessRunner(new ExecutableResolver(Environment.GetEnvironmentVariable, new Dictionary<ExternalTool, string> { [ExternalTool.Git] = "/bin/cat" }));
        var result = await runner.RunAsync(
            new ToolCommand(ExternalTool.Git, [], temp.Path, Generous) { StandardInput = "piped secret" },
            TestContext.Current.CancellationToken);

        result.StandardOutput.ShouldBe("piped secret");
    }

    [Fact]
    public async Task A_timeout_kills_the_process_and_is_reported()
    {
        using var temp = new TempDirectory();
        var (tool, arguments) = OperatingSystem.IsWindows()
            ? (Path.Join(Environment.SystemDirectory, "ping.exe"), new[] { "-n", "30", "127.0.0.1" })
            : ("/bin/sleep", new[] { "30" });
        var runner = new ProcessRunner(new ExecutableResolver(Environment.GetEnvironmentVariable, new Dictionary<ExternalTool, string> { [ExternalTool.Git] = tool }));

        var started = DateTime.UtcNow;
        var result = await runner.RunAsync(
            new ToolCommand(ExternalTool.Git, arguments, temp.Path, TimeSpan.FromMilliseconds(500)),
            TestContext.Current.CancellationToken);

        result.TimedOut.ShouldBeTrue();
        result.ExitCode.ShouldBe(-1);
        result.Succeeded.ShouldBeFalse();
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Cancellation_kills_the_process_and_throws()
    {
        using var temp = new TempDirectory();
        var (tool, arguments) = OperatingSystem.IsWindows()
            ? (Path.Join(Environment.SystemDirectory, "ping.exe"), new[] { "-n", "30", "127.0.0.1" })
            : ("/bin/sleep", new[] { "30" });
        var runner = new ProcessRunner(new ExecutableResolver(Environment.GetEnvironmentVariable, new Dictionary<ExternalTool, string> { [ExternalTool.Git] = tool }));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            runner.RunAsync(new ToolCommand(ExternalTool.Git, arguments, temp.Path, Generous), cancellation.Token));
    }

    [Fact]
    public async Task A_missing_tool_is_a_clear_error()
    {
        using var temp = new TempDirectory();
        var runner = new ProcessRunner(new ExecutableResolver(_ => null, null));
        var error = await Should.ThrowAsync<ToolNotFoundException>(() =>
            runner.RunAsync(new ToolCommand(ExternalTool.Docker, [], temp.Path, Generous), TestContext.Current.CancellationToken));

        error.Message.ShouldContain("'docker'");
        error.NextStep!.ShouldContain("ascent doctor");
        runner.IsAvailable(ExternalTool.Docker).ShouldBeFalse();
    }

    [Fact]
    public async Task Output_beyond_the_cap_keeps_the_tail()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var big = temp.WriteFile("big.txt", new string('a', ProcessRunner.OutputLimit) + new string('z', 1000));
        var runner = new ProcessRunner(new ExecutableResolver(Environment.GetEnvironmentVariable, new Dictionary<ExternalTool, string> { [ExternalTool.Git] = "/bin/cat" }));

        var result = await runner.RunAsync(new ToolCommand(ExternalTool.Git, [big], temp.Path, Generous), TestContext.Current.CancellationToken);

        result.OutputTruncated.ShouldBeTrue();
        result.StandardOutput.Length.ShouldBe(ProcessRunner.OutputLimit);
        result.StandardOutput.ShouldEndWith(new string('z', 1000));
    }

    [Fact]
    public void Timeouts_match_the_design()
    {
        ToolTimeouts.Git.ShouldBe(TimeSpan.FromSeconds(60));
        ToolTimeouts.Probe.ShouldBe(TimeSpan.FromSeconds(10));
        ToolTimeouts.LocalStageUp.ShouldBe(TimeSpan.FromMinutes(10));
        ToolTimeouts.Verify.ShouldBe(TimeSpan.FromMinutes(15));
        ToolTimeouts.CloudDeploy.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Tool_names_are_known() =>
        Enum.GetValues<ExternalTool>().Select(ExecutableResolver.CommandName).ShouldBe(["dotnet", "docker", "az", "git", "uv", "nvidia-smi"]);

    [Fact]
    public void Unknown_tools_are_rejected() =>
        Should.Throw<ArgumentOutOfRangeException>(() => ExecutableResolver.CommandName((ExternalTool)99));

    // A tool that prints each argument it receives on its own line, plus any leading arguments it needs.
    private static (string Executable, string[] Prefix) CreateEchoTool(TempDirectory temp)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = temp.WriteFile("echo-args.ps1", "foreach ($a in $args) { [Console]::Out.WriteLine($a) }");
            var powershell = Path.Join(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            return (powershell, ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script]);
        }

        var path = temp.WriteFile("echo-args", "#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\"; done\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return (path, []);
    }
}
