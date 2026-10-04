using System.Globalization;
using Ascent.Core.Platform;

namespace Ascent.Integrations;

/// <summary>Which part of the Program a check is about (E1-03).</summary>
public enum DoctorArea
{
    /// <summary>Required for Labs: the SDK and Docker.</summary>
    LocalStage,

    /// <summary>Optional: Azure.</summary>
    CloudStage,

    /// <summary>Optional: Python Deep Dives.</summary>
    DeepDives,

    /// <summary>Optional: the AI reviewer.</summary>
    AiReviewer,

    /// <summary>The Engine's own state.</summary>
    Engine,
}

/// <summary>One check's result.</summary>
public enum DoctorStatus
{
    /// <summary>Ready.</summary>
    Ok,

    /// <summary>Not set up, but optional.</summary>
    Optional,

    /// <summary>A required piece is missing or broken.</summary>
    Problem,
}

/// <summary>One line of the doctor's report.</summary>
/// <param name="Area">The area.</param>
/// <param name="Name">What was checked.</param>
/// <param name="Status">The result.</param>
/// <param name="Detail">What was found.</param>
/// <param name="Hint">For anything not ready: how to fix it.</param>
public sealed record DoctorCheck(DoctorArea Area, string Name, DoctorStatus Status, string Detail, string? Hint = null);

/// <summary>
/// Probes the tools each Stage needs (E1-03, P10): read-only commands with a 10-second limit each. Optional tools are
/// never presented as required, and each missing one comes with an install hint.
/// </summary>
public sealed class EnvironmentDoctor
{
    private readonly IProcessRunner processes;
    private readonly string workingDirectory;

    /// <summary>Creates the doctor; probes run in <paramref name="workingDirectory"/>.</summary>
    public EnvironmentDoctor(IProcessRunner processes, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        this.processes = processes;
        this.workingDirectory = workingDirectory;
    }

    /// <summary>Runs every tool probe.</summary>
    public async Task<IReadOnlyList<DoctorCheck>> CheckToolsAsync(CancellationToken cancellationToken)
    {
        var checks = new List<DoctorCheck>
        {
            await DotnetAsync(cancellationToken),
            await ProbeAsync(DoctorArea.LocalStage, "Docker", ExternalTool.Docker, ["info", "--format", "{{.ServerVersion}}"], required: true, "Install Docker Desktop or Docker Engine and start it: https://docs.docker.com/get-docker/", cancellationToken),
            await ProbeAsync(DoctorArea.CloudStage, "Azure CLI", ExternalTool.Az, ["--version"], required: false, "Install the Azure CLI to use Cloud Stages: https://learn.microsoft.com/cli/azure/install-azure-cli", cancellationToken),
            await ProbeAsync(DoctorArea.DeepDives, "uv (Python)", ExternalTool.Uv, ["--version"], required: false, "Install uv to run Deep Dives: https://docs.astral.sh/uv/getting-started/installation/", cancellationToken),
            await ProbeAsync(DoctorArea.DeepDives, "NVIDIA GPU", ExternalTool.NvidiaSmi, ["--query-gpu=name", "--format=csv,noheader"], required: false, "A GPU is optional; Deep Dives also run on the CPU, more slowly.", cancellationToken),
            await ProbeAsync(DoctorArea.LocalStage, "git", ExternalTool.Git, ["--version"], required: false, "Install git to keep your Lab work in its own repository: https://git-scm.com/downloads", cancellationToken),
        };
        return checks;
    }

    /// <summary>
    /// Checks that the configured AI endpoint answers <c>GET /models</c> within 5 seconds. Nothing is sent when no
    /// endpoint is configured (PRV-01).
    /// </summary>
    public static async Task<DoctorCheck> CheckAiEndpointAsync(HttpClient http, Uri? endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        const string Hint = "The AI reviewer is optional. To use it, run a local model server (Foundry Local or Ollama) and set 'ascent config ai.endpoint'.";
        if (endpoint is null)
        {
            return new DoctorCheck(DoctorArea.AiReviewer, "AI endpoint", DoctorStatus.Optional, "not configured", Hint);
        }

        using var request = EngineHttp.Request(HttpMethod.Get, new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/models"), NetworkPurpose.AiReviewer);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            return response.IsSuccessStatusCode
                ? new DoctorCheck(DoctorArea.AiReviewer, "AI endpoint", DoctorStatus.Ok, endpoint.GetLeftPart(UriPartial.Authority))
                : new DoctorCheck(DoctorArea.AiReviewer, "AI endpoint", DoctorStatus.Optional, string.Create(CultureInfo.InvariantCulture, $"answered HTTP {(int)response.StatusCode}"), Hint);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new DoctorCheck(DoctorArea.AiReviewer, "AI endpoint", DoctorStatus.Optional, "not reachable", Hint);
        }
    }

    private async Task<DoctorCheck> DotnetAsync(CancellationToken cancellationToken)
    {
        var check = await ProbeAsync(DoctorArea.LocalStage, ".NET SDK", ExternalTool.Dotnet, ["--version"], required: true, "Install the .NET 10 SDK: https://dotnet.microsoft.com/download", cancellationToken);
        if (check.Status != DoctorStatus.Ok)
        {
            return check;
        }

        var major = check.Detail.Split('.')[0];
        return int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 10
            ? check
            : check with { Status = DoctorStatus.Problem, Hint = "The Labs need the .NET 10 SDK or later: https://dotnet.microsoft.com/download" };
    }

    private async Task<DoctorCheck> ProbeAsync(
        DoctorArea area,
        string name,
        ExternalTool tool,
        IReadOnlyList<string> arguments,
        bool required,
        string hint,
        CancellationToken cancellationToken)
    {
        var missing = required ? DoctorStatus.Problem : DoctorStatus.Optional;
        if (!processes.IsAvailable(tool))
        {
            return new DoctorCheck(area, name, missing, "not found", hint);
        }

        ProcessResult result;
        try
        {
            result = await processes.RunAsync(new ToolCommand(tool, arguments, workingDirectory, ToolTimeouts.Probe), cancellationToken);
        }
        catch (Ascent.Core.Errors.AscentException)
        {
            return new DoctorCheck(area, name, missing, "couldn't be run", hint);
        }

        var first = SafeText.Sanitize(result.StandardOutput, 2000).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        first = string.Join(' ', first.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return result.Succeeded
            ? new DoctorCheck(area, name, DoctorStatus.Ok, first.Length > 0 ? first : "found")
            : new DoctorCheck(area, name, missing, result.TimedOut ? "didn't answer in time" : "installed, but not working", hint);
    }
}
