using System.ComponentModel;
using System.Globalization;
using Ascent.Cli.Hosting;
using Ascent.Cli.Rendering;
using Ascent.Core.Errors;
using Ascent.Storage;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>Options for <c>progress export</c>.</summary>
public sealed class ProgressExportSettings : EngineSettings
{
    /// <summary>The file to write.</summary>
    [CommandArgument(0, "[FILE]")]
    [Description("Where to write the export. Defaults to a new file in .ascent/exports/, which git ignores.")]
    public string? File { get; init; }
}

/// <summary><c>ascent progress export [file]</c>: writes all local progress to a private JSON file (BAK-01, P31).</summary>
public sealed class ProgressExportCommand(EngineHost host) : Command<ProgressExportSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, ProgressExportSettings settings, CancellationToken cancellationToken)
    {
        var exporter = host.Services.Exporter;
        var path = Path.GetFullPath(settings.File ?? exporter.DefaultPath());
        var counts = exporter.Export(path);
        host.Renderer.Status(Outcome.Pass, string.Create(CultureInfo.InvariantCulture, $"Exported {counts.Values.Sum()} rows to {path}."));
        host.Renderer.Status(Outcome.Warn, "This file is private: it holds your progress and settings. Don't commit or share it.");
        host.Renderer.Line("Flags aren't exported. If you're part-way through a Lab's Red step when you import, run 'ascent lab up' again for a fresh Flag.");
        return ExitCodes.Ok;
    }
}

/// <summary>Options for <c>progress import</c>.</summary>
public sealed class ProgressImportSettings : EngineSettings
{
    /// <summary>The file to read.</summary>
    [CommandArgument(0, "<FILE>")]
    [Description("A file written by 'ascent progress export'.")]
    public string File { get; init; } = string.Empty;
}

/// <summary>
/// <c>ascent progress import &lt;file&gt;</c>: replaces all local progress with an export, after checking the file and
/// backing up the database (BAK-01, P31). A damaged database is moved aside first (REL-U2-03).
/// </summary>
public sealed class ProgressImportCommand(EngineHost host) : Command<ProgressImportSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, ProgressImportSettings settings, CancellationToken cancellationToken)
    {
        var document = ProgressFile.Read(settings.File, host.Paths.RepoRoot);
        var renderer = host.Renderer;
        renderer.Line("This replaces all progress on this machine with the export from " + document["exportedUtc"]!.GetValue<string>() + ". The current progress is backed up first.");
        if (!host.Prompter.Confirm("Replace your progress with this export?"))
        {
            renderer.Line("Nothing was changed.");
            return ExitCodes.Ok;
        }

        ProgressExporter exporter;
        try
        {
            exporter = host.Services.Exporter;
        }
        catch (CorruptDatabaseException)
        {
            var moved = ProgressDatabase.MoveAside(host.Paths, host.Time, host.Files);
            renderer.Status(Outcome.Warn, "The damaged progress database was moved to " + Path.GetRelativePath(host.Paths.RepoRoot, moved) + ".");
            exporter = host.Services.Exporter;
        }

        var summary = exporter.Import(document);
        renderer.Status(Outcome.Pass, string.Create(CultureInfo.InvariantCulture, $"Imported {summary.Rows.Values.Sum()} rows."));
        renderer.Line("Your previous progress was backed up to " + Path.GetRelativePath(host.Paths.RepoRoot, summary.BackupPath) + ".");
        if (summary.Rows.GetValueOrDefault("lab_state") > 0)
        {
            renderer.Line("Flags aren't carried over. For a Lab whose Red step isn't done, run 'ascent lab up' again for a fresh Flag.");
        }

        return ExitCodes.Ok;
    }
}
