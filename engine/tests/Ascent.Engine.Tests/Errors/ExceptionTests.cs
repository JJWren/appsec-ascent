using Ascent.Core;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Storage;

namespace Ascent.Engine.Tests.Errors;

public sealed class ExceptionTests
{
    [Fact]
    public void Exit_codes_are_fixed()
    {
        ExitCodes.Ok.ShouldBe(0);
        ExitCodes.CheckFailed.ShouldBe(1);
        ExitCodes.Usage.ShouldBe(2);
    }

    [Fact]
    public void Ascent_exceptions_carry_a_next_step_and_an_exit_code()
    {
        var error = new AscentException("What happened.", "What to do.", ExitCodes.Usage);
        error.Message.ShouldBe("What happened.");
        error.NextStep.ShouldBe("What to do.");
        error.ExitCode.ShouldBe(ExitCodes.Usage);

        new AscentException().ExitCode.ShouldBe(ExitCodes.CheckFailed);
        new AscentException("m").NextStep.ShouldBeNull();
        new AscentException("m", new InvalidOperationException()).InnerException.ShouldNotBeNull();
    }

    [Fact]
    public void Usage_exceptions_exit_with_2()
    {
        new UsageException().ExitCode.ShouldBe(ExitCodes.Usage);
        new UsageException("m").NextStep!.ShouldContain("--help");
        new UsageException("m", new InvalidOperationException()).ExitCode.ShouldBe(ExitCodes.Usage);
        new UsageException("m", "do this").NextStep.ShouldBe("do this");
    }

    [Fact]
    public void Engine_exceptions_have_standard_constructors()
    {
        new ToolNotFoundException().NextStep.ShouldNotBeNull();
        new ToolNotFoundException("m", new InvalidOperationException()).InnerException.ShouldNotBeNull();
        new ToolNotFoundException(ExternalTool.Uv).Message.ShouldContain("'uv'");

        new CorruptDatabaseException().NextStep!.ShouldContain(".ascent/backups/");
        new CorruptDatabaseException("m").Message.ShouldBe("m");
        new CorruptDatabaseException("m", new InvalidOperationException()).InnerException.ShouldNotBeNull();
        new CorruptDatabaseException(new EnginePaths(Path.GetTempPath()), new InvalidOperationException()).Message.ShouldContain("progress.db");

        new NewerDatabaseException().NextStep!.ShouldContain("git pull");
        new NewerDatabaseException("m").Message.ShouldBe("m");
        new NewerDatabaseException("m", new InvalidOperationException()).InnerException.ShouldNotBeNull();
        new NewerDatabaseException(3, 1).Message.ShouldContain("v3");
    }
}
