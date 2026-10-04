using System.ComponentModel;
using System.Globalization;
using Ascent.Cli.Hosting;
using Ascent.Cli.Rendering;
using Ascent.Core.Errors;
using Ascent.Labs;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>Options for <c>release next</c>.</summary>
public sealed class ReleaseNextSettings : EngineSettings
{
    /// <summary>Skip the rest of the current Domain.</summary>
    [CommandOption("--skip")]
    [Description("Move on before the Domain is finished. Unfinished Labs go to Season 2 and their XP is forfeited.")]
    public bool Skip { get; init; }
}

/// <summary>
/// <c>ascent release next</c>: archives your Domain work to a <c>portfolio/d&lt;n&gt;</c> branch in my-work/, then
/// switches the workspace to the next Release (ADR 0006, REL-01..03). Every git command is listed and confirmed first.
/// </summary>
public sealed class ReleaseNextCommand(EngineHost host) : AsyncCommand<ReleaseNextSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, ReleaseNextSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        var releases = services.Releases;
        var check = releases.Check();
        renderer.Heading(string.Create(CultureInfo.InvariantCulture, $"Release {check.Next} ({check.NextDomain})"));
        ReleaseManager.EnsureCanUnlock(check, settings.Skip);
        var skipping = settings.Skip && !check.Ready;
        if (skipping)
        {
            renderer.Status(Outcome.Warn, "Skipping the rest of " + check.FromDomain + ": unfinished Labs go to Season 2, their XP is forfeited, and the next Release includes the reference fixes.");
            if (!host.Prompter.ConfirmTyped("Type " + check.FromDomain + " to skip it:", check.FromDomain))
            {
                renderer.Line("Nothing was changed.");
                return ExitCodes.Ok;
            }
        }

        var workspace = services.Workspace;
        var archive = workspace.IsRepository;
        if (!archive && workspace.GitAvailable && workspace.Exists)
        {
            renderer.Line("my-work/ isn't a git repository, so your " + check.FromDomain + " work can't be archived to a branch. These commands would make it one:");
            ShowSteps(Workspace.InitSteps);
            if (host.Prompter.Confirm("Make my-work/ a repository first?", defaultValue: true))
            {
                await workspace.RunAsync(Workspace.InitSteps, cancellationToken);
                archive = true;
            }
        }

        if (archive)
        {
            renderer.Line("These steps run in my-work/:");
            ShowSteps(ReleaseManager.ArchiveSteps(check, hasChanges: true));
            renderer.Line(string.Create(CultureInfo.InvariantCulture, $"  (my-work/throughline/ is replaced with Release {check.Next})"));
            ShowSteps(ReleaseManager.BaselineSteps(check));
        }
        else
        {
            renderer.Status(Outcome.Warn, "my-work/throughline/ will be replaced without an archive of your " + check.FromDomain + " work.");
        }

        if (!host.Prompter.Confirm(string.Create(CultureInfo.InvariantCulture, $"Switch to Release {check.Next}?")))
        {
            renderer.Line("Nothing was changed.");
            return ExitCodes.Ok;
        }

        var outcome = await releases.UnlockAsync(settings.Skip, archive, cancellationToken);
        renderer.Status(Outcome.Pass, string.Create(CultureInfo.InvariantCulture, $"Release {check.Next} is in my-work/throughline/ ({outcome.Files} files)."));
        if (archive)
        {
            renderer.Line("Your " + check.FromDomain + " work is on the branch " + ReleaseManager.ArchiveSteps(check, false)[0].Arguments[^1] + ".");
        }

        if (outcome.Season2.Count > 0)
        {
            renderer.Line("Added to Season 2: " + string.Join(", ", outcome.Season2) + ".");
        }

        return ExitCodes.Ok;

        void ShowSteps(IEnumerable<GitStep> steps)
        {
            foreach (var step in steps)
            {
                renderer.Line("  " + step);
            }
        }
    }
}
