using System.Text.Json;
using System.Text.Json.Nodes;
using Ascent.Core.Errors;
using Ascent.Core.Platform;

namespace Ascent.Labs;

/// <summary>How a Lab's Flag reaches the running system (FLAG-04, P5).</summary>
public enum PlantKind
{
    /// <summary>The Engine writes the Flag to an owner-only file under <c>throughline/.flags/</c>.</summary>
    File,

    /// <summary>The Engine runs a planter tool and hands it the Flag on standard input, never as an argument.</summary>
    Command,
}

/// <summary>
/// A Lab's plant spec (tier <c>start</c>), decrypted from <c>&lt;labId&gt;.plant</c>. Paths are relative to the
/// workspace root (<c>my-work/</c>). The contract U3's Labs are written to:
/// <code>
/// { "kind": "file", "path": "throughline/.flags/lab-d5-01.txt" }
/// { "kind": "command", "tool": "dotnet", "arguments": ["run", "--project", "throughline/tools/Planter"], "workingDirectory": "throughline" }
/// </code>
/// </summary>
/// <param name="Kind">File or command.</param>
/// <param name="Path">For a file: where the Flag goes.</param>
/// <param name="Tool">For a command: <c>dotnet</c> or <c>docker</c>.</param>
/// <param name="Arguments">For a command: its arguments.</param>
/// <param name="WorkingDirectory">For a command: where it runs.</param>
public sealed record PlantSpec(PlantKind Kind, string? Path, ExternalTool? Tool, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    /// <summary>Parses and checks a plant spec.</summary>
    public static PlantSpec Parse(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw Unsupported("it isn't a JSON object");
        }
        catch (JsonException ex)
        {
            throw new AscentException("The Lab's plant spec couldn't be read.", "Report it with 'ascent bug'.", ExitCodes.CheckFailed, ex);
        }

        return Text(root, "kind") switch
        {
            "file" => File(Text(root, "path") ?? throw Unsupported("a file plant needs a path")),
            "command" => Command(root),
            var other => throw Unsupported("its kind '" + SafeText.Sanitize(other ?? "(none)", 40) + "' isn't one this Engine knows"),
        };
    }

    private static PlantSpec File(string path)
    {
        const string Prefix = "throughline/" + Workspace.FlagsFolder + "/";
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith(Prefix, StringComparison.Ordinal) && normalized.Length > Prefix.Length
            ? new PlantSpec(PlantKind.File, normalized, null, [], ".")
            : throw Unsupported("a file plant must write under " + Prefix + ", which git ignores");
    }

    private static PlantSpec Command(JsonObject root)
    {
        var tool = Text(root, "tool") switch
        {
            "dotnet" => ExternalTool.Dotnet,
            "docker" => ExternalTool.Docker,
            _ => throw Unsupported("a command plant may only run dotnet or docker"),
        };
        var arguments = root["arguments"] is JsonArray array
            ? array.Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw Unsupported("every argument must be text")).ToList()
            : [];
        return new PlantSpec(PlantKind.Command, null, tool, arguments, Text(root, "workingDirectory") ?? ".");
    }

    private static string? Text(JsonObject root, string name) => root[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static AscentException Unsupported(string reason) =>
        new("The Lab's plant spec isn't usable: " + reason + ".", "Update the Engine (git pull). If that doesn't help, report it with 'ascent bug'.");
}

/// <summary>Plants a Flag as a plant spec says (FLAG-01, FLAG-04, P5). The Flag is never printed or passed as an argument.</summary>
public sealed class Planter
{
    private readonly IProcessRunner processes;
    private readonly IOwnerOnlyFiles files;

    /// <summary>Creates the planter.</summary>
    public Planter(IProcessRunner processes, IOwnerOnlyFiles files)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(files);
        this.processes = processes;
        this.files = files;
    }

    /// <summary>Plants <paramref name="flag"/> in the workspace at <paramref name="workspaceRoot"/>.</summary>
    public async Task PlantAsync(PlantSpec spec, string flag, string workspaceRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrEmpty(flag);
        if (spec.Kind == PlantKind.File)
        {
            var path = SafePath.Resolve(workspaceRoot, spec.Path!);
            SafePath.EnsureNoLinks(workspaceRoot, path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            files.WriteAllText(path, flag);
            return;
        }

        var directory = spec.WorkingDirectory is "." or "" ? workspaceRoot : SafePath.Resolve(workspaceRoot, spec.WorkingDirectory);
        var result = await processes.RunAsync(
            new ToolCommand(spec.Tool!.Value, spec.Arguments, directory, ToolTimeouts.LocalStageUp) { StandardInput = flag + "\n" },
            cancellationToken);
        if (!result.Succeeded)
        {
            throw new AscentException(
                "Planting the Flag failed" + (result.TimedOut ? " (it timed out)." : "."),
                "Check that the Local Stage is running ('ascent doctor'), then run 'ascent lab up' again for a new Flag.");
        }
    }
}
