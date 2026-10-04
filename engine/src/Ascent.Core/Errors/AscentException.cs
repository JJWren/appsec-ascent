namespace Ascent.Core.Errors;

/// <summary>The Engine's exit codes (UX-U2-02).</summary>
public static class ExitCodes
{
    /// <summary>Success.</summary>
    public const int Ok = 0;

    /// <summary>A check failed (a wrong Flag, a failing <c>verify</c>, an unavailable Sealed item), or an unexpected error.</summary>
    public const int CheckFailed = 1;

    /// <summary>The command was used incorrectly.</summary>
    public const int Usage = 2;
}

/// <summary>
/// An error the Learner can act on: the message says what happened and <see cref="NextStep"/> says what to do (P23).
/// </summary>
public class AscentException : Exception
{
    /// <summary>Creates an error with a default message.</summary>
    public AscentException()
        : this("The Engine could not complete the command.")
    {
    }

    /// <summary>Creates an error with a message.</summary>
    public AscentException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an error with a message and a cause.</summary>
    public AscentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an error with a message, the next step and an exit code.</summary>
    public AscentException(string message, string? nextStep, int exitCode = ExitCodes.CheckFailed, Exception? innerException = null)
        : base(message, innerException)
    {
        NextStep = nextStep;
        ExitCode = exitCode;
    }

    /// <summary>What the Learner should do next, if anything.</summary>
    public string? NextStep { get; }

    /// <summary>The process exit code.</summary>
    public int ExitCode { get; } = ExitCodes.CheckFailed;
}

/// <summary>The command was used incorrectly (exit code 2).</summary>
public sealed class UsageException : AscentException
{
    /// <summary>Creates a usage error with a default message.</summary>
    public UsageException()
        : this("The command was used incorrectly.", "Run 'ascent --help'.")
    {
    }

    /// <summary>Creates a usage error with a message.</summary>
    public UsageException(string message)
        : this(message, "Run 'ascent --help'.")
    {
    }

    /// <summary>Creates a usage error with a message and a cause.</summary>
    public UsageException(string message, Exception innerException)
        : base(message, "Run 'ascent --help'.", ExitCodes.Usage, innerException)
    {
    }

    /// <summary>Creates a usage error with a message and the next step.</summary>
    public UsageException(string message, string? nextStep)
        : base(message, nextStep, ExitCodes.Usage)
    {
    }
}
