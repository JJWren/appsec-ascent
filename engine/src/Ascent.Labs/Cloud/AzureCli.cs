using System.Text.Json;
using System.Text.Json.Nodes;
using Ascent.Core.Errors;
using Ascent.Core.Platform;

namespace Ascent.Labs.Cloud;

/// <summary>
/// Runs the Azure CLI through the safe process runner (P10). Output is JSON or nothing; the CLI's own telemetry is
/// switched off for these runs, in keeping with PRV-01.
/// </summary>
public sealed class AzureCli
{
    private readonly IProcessRunner processes;
    private readonly string workingDirectory;

    /// <summary>Creates the wrapper; commands run in <paramref name="workingDirectory"/>.</summary>
    public AzureCli(IProcessRunner processes, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        this.processes = processes;
        this.workingDirectory = workingDirectory;
    }

    /// <summary>True when <c>az</c> is installed.</summary>
    public bool Available => processes.IsAvailable(ExternalTool.Az);

    /// <summary>The command as the Learner would type it, for confirmations.</summary>
    public static string Display(IReadOnlyList<string> arguments) =>
        "az " + string.Join(' ', (arguments ?? throw new ArgumentNullException(nameof(arguments))).Select(a => a.Contains(' ', StringComparison.Ordinal) ? "\"" + a + "\"" : a));

    /// <summary>Runs a command.</summary>
    public Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) =>
        processes.RunAsync(
            new ToolCommand(ExternalTool.Az, [.. arguments, "--only-show-errors"], workingDirectory, timeout)
            {
                Environment = new Dictionary<string, string> { ["AZURE_CORE_COLLECT_TELEMETRY"] = "false" },
            },
            cancellationToken);

    /// <summary>Runs a read-only query and parses its JSON, or returns null when the command fails.</summary>
    public async Task<JsonNode?> QueryAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await RunAsync([.. arguments, "--output", "json"], ToolTimeouts.Probe * 3, cancellationToken);
        if (!result.Succeeded)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(result.StandardOutput.Trim().Length == 0 ? "null" : result.StandardOutput);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Runs a state-changing command and throws with az's last error line when it fails.</summary>
    public async Task RunOrThrowAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await RunAsync(arguments, timeout, cancellationToken);
        if (!result.Succeeded)
        {
            var lines = SafeText.Sanitize(result.StandardError.Trim().Length > 0 ? result.StandardError : result.StandardOutput, 20_000)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            throw new AscentException(
                "'" + Display(arguments) + "' failed" + (result.TimedOut ? " (it timed out)" : lines.Length > 0 ? ": " + lines[^1] : string.Empty) + ".",
                "Check 'az account show' and the Azure portal, then try again. Anything tagged ascent:lab is removed by 'ascent teardown'.");
        }
    }
}
