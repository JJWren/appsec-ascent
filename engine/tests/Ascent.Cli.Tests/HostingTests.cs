using System.Text.Json;
using Ascent.Cli.Hosting;
using Ascent.Cli.Tests.TestSupport;
using Ascent.Core.Errors;
using Ascent.Storage;

namespace Ascent.Cli.Tests;

public sealed class HostingTests
{
    [Fact]
    public async Task An_unknown_command_is_a_usage_error_and_leaves_no_trace()
    {
        using var engine = TestEngine.Empty();

        var (exitCode, output) = await engine.RunAsync("frobnicate");

        exitCode.ShouldBe(ExitCodes.Usage);
        output.ShouldContain("FAIL: Unknown command 'frobnicate'.");
        output.ShouldContain("Next: Run 'ascent --help'.");
        Directory.Exists(engine.Paths.StateDirectory).ShouldBeFalse();
    }

    [Fact]
    public async Task An_invalid_option_value_is_a_usage_error()
    {
        using var engine = TestEngine.Empty();

        var (exitCode, output) = await engine.RunAsync("exceptions", "check", "--today", "yesterday");

        exitCode.ShouldBe(ExitCodes.Usage);
        output.ShouldContain("--today must be a date");
    }

    [Fact]
    public void Ascent_exceptions_print_what_happened_and_what_to_do()
    {
        using var engine = TestEngine.Empty();
        using var output = new StringWriter();
        using var host = engine.CreateHost(output);

        var exitCode = ErrorHandler.Handle(new AscentException("The Flag didn't match.", "Look again.", ExitCodes.CheckFailed), host);

        exitCode.ShouldBe(ExitCodes.CheckFailed);
        output.ToString().ShouldBe("FAIL: The Flag didn't match.\nNext: Look again.\n".ReplaceLineEndings());
        Directory.Exists(engine.Paths.StateDirectory).ShouldBeFalse();
    }

    [Fact]
    public void Unexpected_errors_exit_1_and_log_the_type_and_stack_but_never_the_message()
    {
        using var engine = TestEngine.Empty();
        using var output = new StringWriter();
        using var host = engine.CreateHost(output);
        host.BeginCommand("boom", null);

        Exception thrown;
        try
        {
            throw new InvalidOperationException("secret content that must not be logged");
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        var exitCode = ErrorHandler.Handle(thrown, host);

        exitCode.ShouldBe(ExitCodes.CheckFailed);
        output.ToString().ShouldContain("Unexpected error (InvalidOperationException).");
        output.ToString().ShouldNotContain("secret content");
        var log = File.ReadAllText(Directory.GetFiles(engine.Paths.Logs).Single());
        log.ShouldNotContain("secret content");
        using var entry = JsonDocument.Parse(log);
        entry.RootElement.GetProperty("command").GetString().ShouldBe("boom");
        entry.RootElement.GetProperty("errorType").GetString().ShouldBe("System.InvalidOperationException");
        entry.RootElement.GetProperty("stackTrace").GetString()!.ShouldContain(nameof(Unexpected_errors_exit_1_and_log_the_type_and_stack_but_never_the_message));
    }

    [Fact]
    public void Cancellation_and_damaged_databases_have_their_own_messages()
    {
        using var engine = TestEngine.Empty();
        using var output = new StringWriter();
        using var host = engine.CreateHost(output);

        ErrorHandler.Handle(new OperationCanceledException(), host).ShouldBe(ExitCodes.CheckFailed);
        output.ToString().ShouldContain("WARN: Cancelled.");

        Directory.CreateDirectory(engine.Paths.StateDirectory);
        File.WriteAllText(engine.Paths.Database, string.Concat(Enumerable.Repeat("not a database ", 300)));
        var corrupt = Should.Throw<CorruptDatabaseException>(() => _ = host.Database);
        ErrorHandler.Handle(corrupt, host).ShouldBe(ExitCodes.CheckFailed);
        ErrorHandler.Handle(corrupt.InnerException!, host).ShouldBe(ExitCodes.CheckFailed);
        output.ToString().ShouldContain("is damaged");
        output.ToString().ShouldContain("Next: Copy the newest file from .ascent/backups/");
    }

    [Fact]
    public async Task Timings_are_printed_on_request()
    {
        using var engine = TestEngine.Empty();

        var (exitCode, output) = await engine.RunAsync("verify-bundles", "--timings");

        exitCode.ShouldBe(0);
        output.ShouldContain("timing content:");
        output.ShouldContain("timing command:");
    }

    [Fact]
    public void Opening_the_database_reports_a_clock_that_moved_backwards()
    {
        using var engine = TestEngine.Empty();
        using (var output = new StringWriter())
        using (var host = engine.CreateHost(output))
        {
            _ = host.Database;
            host.Clock!.MovedBackwards.ShouldBeFalse();
        }

        using var later = new StringWriter();
        using var again = engine.CreateHost(later, new MutableClock(engine.Clock.GetUtcNow() - TimeSpan.FromDays(1)));
        _ = again.Database;

        again.Clock!.MovedBackwards.ShouldBeTrue();
        later.ToString().ShouldContain("WARN: Your system clock is behind");
    }

    [Fact]
    public void Commands_that_use_learner_state_are_logged()
    {
        using var engine = TestEngine.Empty();
        using var output = new StringWriter();
        using var host = engine.CreateHost(output);
        host.BeginCommand("status", null);
        _ = host.Database;

        host.EndCommand(0, null);

        var line = File.ReadAllLines(Directory.GetFiles(engine.Paths.Logs).Single()).Single();
        using var entry = JsonDocument.Parse(line);
        entry.RootElement.GetProperty("command").GetString().ShouldBe("status");
        entry.RootElement.TryGetProperty("stackTrace", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_host_creates_services_lazily()
    {
        using var engine = TestEngine.Empty();
        using var output = new StringWriter();
        using var host = engine.CreateHost(output);

        EngineHost.Version.ShouldNotBeNullOrWhiteSpace();
        host.Time.ShouldBe(engine.Clock);
        host.Random.ShouldNotBeNull();
        host.Files.ShouldNotBeNull();
        host.Processes.ShouldNotBeNull();
        host.Paths.RepoRoot.ShouldBe(Path.GetFullPath(engine.Root));
        host.Out.ShouldBe(output);
        host.Prompter.ShouldNotBeNull();
        Directory.Exists(engine.Paths.StateDirectory).ShouldBeFalse();
    }
}
