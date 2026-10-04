using Ascent.Core.Curriculum;

namespace Ascent.Labs;

/// <summary>What the Local Stage needs to start or stop a Lab.</summary>
/// <param name="Lab">The Lab.</param>
/// <param name="Workspace">The workspace root, <c>my-work/</c>.</param>
/// <param name="Throughline">The current Release, <c>my-work/throughline/</c>.</param>
public sealed record StageRequest(LabInfo Lab, string Workspace, string Throughline);

/// <summary>The outcome of a Local Stage step.</summary>
/// <param name="Running">True when the Lab's services are running.</param>
/// <param name="Message">What happened, or what the Learner should do next.</param>
public sealed record StageResult(bool Running, string Message);

/// <summary>
/// The Local Stage port (P10, logical components): it runs the current Release with the started Lab Modules. The
/// Throughline System (U3) supplies the AppHost; tests use a fixture adapter.
/// </summary>
public interface IOrchestrator
{
    /// <summary>Brings the Lab's Local Stage up.</summary>
    Task<StageResult> UpAsync(StageRequest request, CancellationToken cancellationToken);

    /// <summary>Stops the Lab's Local Stage.</summary>
    Task<StageResult> DownAsync(StageRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The production adapter. The Throughline System's AppHost runs in the foreground of its own terminal, so the Engine
/// points the Learner at it rather than holding it open.
/// </summary>
public sealed class LocalStageOrchestrator : IOrchestrator
{
    /// <summary>The AppHost folder, relative to the Release.</summary>
    public const string AppHostFolder = "apphost";

    /// <inheritdoc />
    public Task<StageResult> UpAsync(StageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var appHost = Path.Join(request.Throughline, AppHostFolder);
        var services = string.Join(", ", request.Lab.Services);
        return Task.FromResult(Directory.Exists(appHost) && Directory.EnumerateFiles(appHost, "*.csproj").Any()
            ? new StageResult(false, "Start the Local Stage (" + services + ") in another terminal: dotnet run --project " + Path.Join("my-work", "throughline", AppHostFolder) + ". It runs your current Release with this Lab's module.")
            : new StageResult(false, "Your workspace has no AppHost yet, so there are no services (" + services + ") to start. Work on the module's files directly."));
    }

    /// <inheritdoc />
    public Task<StageResult> DownAsync(StageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new StageResult(false, "Stop the Local Stage with Ctrl+C in the terminal that runs it."));
    }
}
