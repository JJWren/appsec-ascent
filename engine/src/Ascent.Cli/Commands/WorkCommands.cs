using System.Globalization;
using System.Text;
using Ascent.Cli.Hosting;
using Ascent.Cli.Rendering;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Deliverables;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>
/// <c>ascent deliver &lt;id&gt;</c>: a Deliverable is scaffolded, then validated, self-scored and submitted, which
/// releases its reference answer (DLE-01..03). A drill works the same way with a Markdown answer file.
/// </summary>
public sealed class DeliverCommand(EngineHost host) : Command<ItemSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, ItemSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        return services.DeliverableCatalog.IsDrill(settings.Id) ? Drill(settings.Id) : Deliverable(settings.Id);
    }

    private int Drill(string drillId)
    {
        var deliverables = host.Services.Deliverables;
        if (deliverables.OpenDrill(drillId, out var path))
        {
            host.Renderer.Status(Outcome.Info, "Created " + path + " with the drill's prompt. Write your answer there, then run 'ascent deliver " + drillId + "' again.");
            return ExitCodes.Ok;
        }

        var submission = deliverables.SubmitDrill(drillId);
        host.Renderer.Status(Outcome.Pass, "Drill submitted" + (submission.XpAwarded > 0 ? string.Create(CultureInfo.InvariantCulture, $": +{submission.XpAwarded} XP.") : "."));
        ShowReference(host.Renderer, "Answer key", submission.Reference);
        Activities.CompleteQuests(host, drillId);
        return ExitCodes.Ok;
    }

    private int Deliverable(string deliverableId)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        var check = services.Deliverables.Check(deliverableId);
        switch (check.State)
        {
            case WorkState.Scaffolded:
                renderer.Status(Outcome.Info, "Created " + check.Path + " from the template. Fill it in, then run 'ascent deliver " + deliverableId + "' again.");
                return ExitCodes.Ok;
            case WorkState.Invalid:
                renderer.Status(Outcome.Fail, check.Path + " isn't ready yet:");
                foreach (var problem in check.Problems)
                {
                    renderer.Line("  " + problem);
                }

                return ExitCodes.CheckFailed;
        }

        var template = services.DeliverableCatalog.Template(deliverableId);
        var rubric = services.DeliverableCatalog.RubricFor(template);
        renderer.Status(Outcome.Pass, "Your work is complete. Score it against the rubric (pass mark " + rubric.PassThreshold.ToString(CultureInfo.InvariantCulture) + "%):");
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var criterion in rubric.Criteria)
        {
            var choice = host.Prompter.Choose(
                criterion.Description + string.Create(CultureInfo.InvariantCulture, $" (weight {criterion.Weight})"),
                criterion.Levels.Select(l => l.Score.ToString(CultureInfo.InvariantCulture) + ": " + l.Descriptor).ToList());
            levels[criterion.Key] = criterion.Levels[choice].Score;
        }

        var score = DeliverableService.Score(rubric, levels);
        if (!host.Prompter.Confirm(string.Create(CultureInfo.InvariantCulture, $"Submit with a self-score of {score}%? The reference answer is shown next."), defaultValue: true))
        {
            renderer.Line("Not submitted.");
            return ExitCodes.Ok;
        }

        var submission = services.Deliverables.Submit(deliverableId, levels);
        renderer.Status(
            submission.Passed ? Outcome.Pass : Outcome.Info,
            string.Create(CultureInfo.InvariantCulture, $"Submitted at {submission.ScorePercent}%") + (submission.Passed ? ", at or above the pass mark." : ", below the pass mark.")
            + (submission.XpAwarded > 0 ? string.Create(CultureInfo.InvariantCulture, $" +{submission.XpAwarded} XP.") : string.Empty));
        ShowReference(renderer, "Reference answer", submission.Reference);
        Activities.CompleteQuests(host, deliverableId);
        return ExitCodes.Ok;
    }

    private static void ShowReference(IRenderer renderer, string heading, string text)
    {
        renderer.Heading(heading);
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            renderer.Line(line);
        }
    }
}

/// <summary>
/// <c>ascent review &lt;id&gt;</c>: the optional AI reviewer reads your Deliverable and comments against the rubric. It
/// never changes XP, except that making it reveal its secret earns the Prompt Breaker bonus (DLE-04, PRV-02).
/// </summary>
public sealed class ReviewCommand(EngineHost host) : AsyncCommand<ItemSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, ItemSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        var profile = services.Profile;
        if (profile.AiEndpoint is not { } endpoint || profile.AiModel is not { } model)
        {
            throw new UsageException(
                "The AI reviewer isn't set up.",
                "Run a local model server, then set 'ascent config ai.endpoint http://localhost:11434/v1' and 'ascent config ai.model <model>'.");
        }

        var template = services.DeliverableCatalog.Template(settings.Id);
        var rubric = services.DeliverableCatalog.RubricFor(template);
        var path = services.Deliverables.WorkPath(template.Id);
        if (!File.Exists(path))
        {
            throw new UsageException("There's no work to review yet.", "Start it with 'ascent deliver " + template.Id + "'.");
        }

        if (!endpoint.IsLoopback && profile.Values.GetValueOrDefault(Core.Profile.ProfileKeys.AiRemoteConfirmed) != endpoint.GetLeftPart(UriPartial.Authority))
        {
            renderer.Status(Outcome.Warn, "Your Deliverable's text will be sent to " + endpoint.Host + ", which isn't on this machine.");
            if (!host.Prompter.Confirm("Send it?"))
            {
                renderer.Line("Nothing was sent.");
                return ExitCodes.Ok;
            }

            services.ProfileService.ConfirmRemoteEndpoint(endpoint);
        }

        renderer.Heading("Review of " + template.Id + " by " + model);
        var pending = new StringBuilder();
        var outcome = await services.Reviewer.ReviewAsync(
            template,
            rubric,
            await File.ReadAllTextAsync(path, cancellationToken),
            endpoint,
            model,
            text =>
            {
                pending.Append(text);
                var content = pending.ToString();
                var end = content.LastIndexOf('\n');
                if (end >= 0)
                {
                    foreach (var line in content[..end].Split('\n'))
                    {
                        renderer.Line(line);
                    }

                    pending.Clear().Append(content[(end + 1)..]);
                }
            },
            cancellationToken);
        if (pending.Length > 0)
        {
            renderer.Line(pending.ToString());
        }

        renderer.Line();
        if (outcome.FlagAwarded)
        {
            renderer.Status(Outcome.Pass, "Prompt Breaker! The reviewer revealed its secret: +30 bonus XP.");
        }
        else if (outcome.SecretRevealed)
        {
            renderer.Status(Outcome.Info, "The reviewer revealed its secret again; the bonus is paid once per Deliverable.");
        }

        renderer.Line("The review is advice only: it never changes your XP or score, and it isn't saved.");
        return ExitCodes.Ok;
    }
}
