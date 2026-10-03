using System.Buffers;
using System.Diagnostics;
using System.Text;
using Ascent.Core.Errors;

namespace Ascent.Core.Platform;

/// <summary>External tools the Engine is allowed to run (P10).</summary>
public enum ExternalTool
{
    /// <summary>The .NET SDK host.</summary>
    Dotnet,

    /// <summary>Docker.</summary>
    Docker,

    /// <summary>The Azure CLI.</summary>
    Az,

    /// <summary>Git.</summary>
    Git,

    /// <summary>uv, for Python Deep Dives.</summary>
    Uv,

    /// <summary>NVIDIA's GPU probe (read-only, used by <c>doctor</c>).</summary>
    NvidiaSmi,
}

/// <summary>One run of an external tool.</summary>
/// <param name="Tool">The tool to run.</param>
/// <param name="Arguments">Arguments, passed one by one through <see cref="ProcessStartInfo.ArgumentList"/>.</param>
/// <param name="WorkingDirectory">The working directory.</param>
/// <param name="Timeout">How long the run may take before the whole process tree is killed.</param>
public sealed record ToolCommand(ExternalTool Tool, IReadOnlyList<string> Arguments, string WorkingDirectory, TimeSpan Timeout)
{
    /// <summary>Text written to standard input, then closed. Secrets travel this way, never as arguments (P5).</summary>
    public string? StandardInput { get; init; }
}

/// <summary>The outcome of a tool run. Output is untrusted (P11) and capped at <see cref="ProcessRunner.OutputLimit"/> per stream.</summary>
/// <param name="ExitCode">The exit code, or -1 when the run timed out.</param>
/// <param name="StandardOutput">Standard output (the tail, when truncated).</param>
/// <param name="StandardError">Standard error (the tail, when truncated).</param>
/// <param name="TimedOut">True when the timeout killed the process tree.</param>
/// <param name="OutputTruncated">True when either stream exceeded the cap.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, bool OutputTruncated)
{
    /// <summary>True when the tool finished in time with exit code 0.</summary>
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

/// <summary>Default timeouts per kind of run (P10).</summary>
public static class ToolTimeouts
{
    /// <summary>git commands.</summary>
    public static readonly TimeSpan Git = TimeSpan.FromSeconds(60);

    /// <summary>Read-only probes run by <c>doctor</c>.</summary>
    public static readonly TimeSpan Probe = TimeSpan.FromSeconds(10);

    /// <summary>Bringing a Local Stage up.</summary>
    public static readonly TimeSpan LocalStageUp = TimeSpan.FromMinutes(10);

    /// <summary>Running released security tests (<c>verify</c>).</summary>
    public static readonly TimeSpan Verify = TimeSpan.FromMinutes(15);

    /// <summary>An Azure deployment.</summary>
    public static readonly TimeSpan CloudDeploy = TimeSpan.FromMinutes(30);
}

/// <summary>Runs external tools safely (SEC-U2-05).</summary>
public interface IProcessRunner
{
    /// <summary>True when the tool can be found.</summary>
    bool IsAvailable(ExternalTool tool);

    /// <summary>Runs a tool and returns its capped output.</summary>
    Task<ProcessResult> RunAsync(ToolCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Runs external tools without a shell (P10): the executable is resolved from PATH only, arguments go through
/// <see cref="ProcessStartInfo.ArgumentList"/>, batch files get a strict argument check, and a timeout or
/// cancellation kills the whole process tree.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>The most characters kept per output stream.</summary>
    public const int OutputLimit = 1024 * 1024;

    private readonly ExecutableResolver resolver;

    /// <summary>Creates a runner that resolves tools from the real PATH.</summary>
    public ProcessRunner()
        : this(new ExecutableResolver())
    {
    }

    /// <summary>Creates a runner with a specific resolver.</summary>
    public ProcessRunner(ExecutableResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        this.resolver = resolver;
    }

    /// <inheritdoc />
    public bool IsAvailable(ExternalTool tool) => resolver.Resolve(tool) is not null;

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(ToolCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var executable = resolver.Resolve(command.Tool) ?? throw new ToolNotFoundException(command.Tool);
        BatchArgumentGuard.Validate(executable, command.Arguments);

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = command.StandardInput is not null,
            WorkingDirectory = command.WorkingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = ReadCappedAsync(process.StandardOutput);
        var error = ReadCappedAsync(process.StandardError);
        if (command.StandardInput is not null)
        {
            await process.StandardInput.WriteAsync(command.StandardInput.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(command.Timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            timedOut = true;
        }

        var (standardOutput, outputTruncated) = await output;
        var (standardError, errorTruncated) = await error;
        return new ProcessResult(timedOut ? -1 : process.ExitCode, standardOutput, standardError, timedOut, outputTruncated || errorTruncated);
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadCappedAsync(StreamReader reader)
    {
        var buffer = new char[8192];
        var builder = new StringBuilder();
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            builder.Append(buffer, 0, read);
            if (builder.Length > 2 * OutputLimit)
            {
                builder.Remove(0, builder.Length - OutputLimit);
                truncated = true;
            }
        }

        if (builder.Length > OutputLimit)
        {
            builder.Remove(0, builder.Length - OutputLimit);
            truncated = true;
        }

        return (builder.ToString(), truncated);
    }
}

/// <summary>Finds tool executables on PATH only, never in the current or working directory (P10).</summary>
public sealed class ExecutableResolver
{
    private readonly Func<string, string?> environment;
    private readonly IReadOnlyDictionary<ExternalTool, string> overrides;

    /// <summary>Creates a resolver over the real environment.</summary>
    public ExecutableResolver()
        : this(Environment.GetEnvironmentVariable, null)
    {
    }

    /// <summary>Creates a resolver over a given environment, with optional fixed paths (used by tests).</summary>
    public ExecutableResolver(Func<string, string?> environment, IReadOnlyDictionary<ExternalTool, string>? overrides)
    {
        ArgumentNullException.ThrowIfNull(environment);
        this.environment = environment;
        this.overrides = overrides ?? new Dictionary<ExternalTool, string>();
    }

    /// <summary>The command name of a tool.</summary>
    public static string CommandName(ExternalTool tool) => tool switch
    {
        ExternalTool.Dotnet => "dotnet",
        ExternalTool.Docker => "docker",
        ExternalTool.Az => "az",
        ExternalTool.Git => "git",
        ExternalTool.Uv => "uv",
        ExternalTool.NvidiaSmi => "nvidia-smi",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown tool."),
    };

    /// <summary>The absolute path of a tool, or null when it isn't installed.</summary>
    public string? Resolve(ExternalTool tool)
    {
        if (overrides.TryGetValue(tool, out var fixedPath))
        {
            return fixedPath;
        }

        var found = SearchPath(CommandName(tool));
        return found is null && tool == ExternalTool.Dotnet ? CurrentDotnetHost() : found;
    }

    private string? SearchPath(string name)
    {
        var path = environment("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string[] extensions = OperatingSystem.IsWindows()
            ? (environment("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A relative PATH entry (such as ".") would search the current directory, so it is skipped.
            if (!Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                var candidate = Path.Join(directory, name + extension);
                if (IsExecutable(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private string? CurrentDotnetHost()
    {
        var hostPath = environment("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(hostPath) && Path.IsPathFullyQualified(hostPath) && File.Exists(hostPath))
        {
            return hostPath;
        }

        var processPath = Environment.ProcessPath;
        return processPath is not null && Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? processPath
            : null;
    }

    private static bool IsExecutable(string candidate)
    {
        if (!File.Exists(candidate))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(candidate) & anyExecute) != 0;
    }
}

/// <summary>
/// cmd.exe re-parses the arguments of <c>.cmd</c> and <c>.bat</c> files, so quoting alone can't make them safe
/// (the "BatBadBut" class of injection). Arguments for batch files must avoid cmd's special characters (P10).
/// </summary>
public static class BatchArgumentGuard
{
    /// <summary>The characters refused in arguments to a batch file.</summary>
    public const string UnsafeCharacters = "\"%!^&|<>()\r\n";

    private static readonly SearchValues<char> Unsafe = SearchValues.Create(UnsafeCharacters);

    /// <summary>True for <c>.cmd</c> and <c>.bat</c> files.</summary>
    public static bool IsBatchFile(string executable) =>
        executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    /// <summary>The index of the first unsafe argument, or -1.</summary>
    public static int FindUnsafeArgument(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].AsSpan().ContainsAny(Unsafe))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Throws when <paramref name="executable"/> is a batch file and an argument is unsafe for it.</summary>
    public static void Validate(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(executable);
        if (!IsBatchFile(executable))
        {
            return;
        }

        var index = FindUnsafeArgument(arguments);
        if (index >= 0)
        {
            throw new AscentException(
                "Refused to run " + Path.GetFileName(executable) + ": argument " + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " contains characters that a Windows batch file would interpret.",
                "Avoid the characters \" % ! ^ & | < > ( ) in names and paths (for example, move the repository to a simpler path).");
        }
    }
}

/// <summary>A required tool isn't installed or isn't on PATH.</summary>
public sealed class ToolNotFoundException : AscentException
{
    /// <summary>Creates the error with a default message.</summary>
    public ToolNotFoundException()
        : this("A required tool was not found on PATH.")
    {
    }

    /// <summary>Creates the error with a message.</summary>
    public ToolNotFoundException(string message)
        : base(message, "Run 'ascent doctor' for install hints.")
    {
    }

    /// <summary>Creates the error with a message and a cause.</summary>
    public ToolNotFoundException(string message, Exception innerException)
        : base(message, "Run 'ascent doctor' for install hints.", ExitCodes.CheckFailed, innerException)
    {
    }

    /// <summary>Creates the error for a tool.</summary>
    public ToolNotFoundException(ExternalTool tool)
        : this("'" + ExecutableResolver.CommandName(tool) + "' was not found on PATH.")
    {
    }
}
