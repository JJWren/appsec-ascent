using System.ComponentModel;
using System.Globalization;
using Ascent.Cli.Hosting;
using Ascent.Cli.Rendering;
using Ascent.Core.Curriculum;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Profile;
using Ascent.Core.Progress;
using Ascent.Sealing.Unsealing;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>The rules of engagement shown by <c>ascent rules</c> (E1-04).</summary>
public static class RulesOfEngagement
{
    /// <summary>The rules, one per line.</summary>
    public static IReadOnlyList<string> Rules { get; } =
    [
        "Attack only the Throughline System running on your own machine or in your own Azure subscription.",
        "Never point Lab techniques or tools at systems you don't own or have written permission to test.",
        "Use synthetic data only. Never load real patient, payment or personal data into a Lab.",
        "Keep Cloud Stages inside the guardrails, and tear them down when you're done.",
        "Don't publish Flags, exploits, fixes or other Sealed content; let other Learners find them.",
    ];
}

/// <summary>Options for <c>start</c>.</summary>
public sealed class StartSettings : EngineSettings
{
    /// <summary>Snooze the exam-date prompt until tomorrow.</summary>
    [CommandOption("--later")]
    [Description("Snooze the exam-date prompt until tomorrow (EXM-01).")]
    public bool Later { get; init; }
}

/// <summary><c>ascent start</c>: creates the profile, prompts for the exam date when due, and shows what's next.</summary>
public sealed class StartCommand(EngineHost host) : Command<StartSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, StartSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        var profile = services.Profile;
        UnsealedSweeper.Sweep(host.Paths);

        renderer.Heading("AppSec Ascent");
        if (!profile.RulesAccepted)
        {
            renderer.Status(Outcome.Info, "Before any Lab, read and accept the rules of engagement: ascent rules");
        }

        var today = services.Calendar.Today;
        if (ExamPrompt.ShouldPrompt(services.Catalog.SeasonComplete, profile, today))
        {
            PromptForExamDate(services, settings.Later, today);
        }

        var week = services.WeekGoal.Status(profile.WeeklyGoal);
        renderer.Line(string.Create(CultureInfo.InvariantCulture, $"This week: {week.Days}/{week.Goal} Stand-up days. Week streak: {week.Streak}."));
        var next = services.Quests.Next();
        renderer.Line(next is null
            ? "Every Quest is complete. Run 'ascent status' to see where you stand."
            : "Next Quest: " + next.Id + " · " + next.Title + " (" + next.ExamDomain + ", Objective " + next.ObjectiveId + "). Open it with 'ascent quest " + next.Id + "'.");
        return ExitCodes.Ok;
    }

    private void PromptForExamDate(EngineServices services, bool later, DateOnly today)
    {
        if (later)
        {
            services.ProfileService.SnoozeExamPrompt(today);
            host.Renderer.Line("OK. I'll ask about your exam date again tomorrow.");
            return;
        }

        host.Renderer.Status(Outcome.Info, "Season 1 is complete. Time to book your exam.");
        if (SuggestedWindow(services.Attempts.LatestDiagnostic()?.Plan) is var (from, to))
        {
            host.Renderer.Line("Your study plan suggests booking between " + from + " and " + to + ".");
        }

        var answer = host.Prompter.Ask(
            "Exam date (yyyy-MM-dd), or 'later':",
            value => value.Equals("later", StringComparison.OrdinalIgnoreCase) || ConfigCatalog.Find(ProfileKeys.ExamDate)!.Validate(value) is null
                ? null
                : "Use a date like 2027-02-15, or 'later'.");
        if (answer.Equals("later", StringComparison.OrdinalIgnoreCase))
        {
            services.ProfileService.SnoozeExamPrompt(today);
        }
        else
        {
            services.ProfileService.Set(ProfileKeys.ExamDate, answer);
            host.Renderer.Status(Outcome.Pass, "Exam date saved: " + answer + ".");
        }
    }

    // The exam window from the latest study plan, or null when there's no readable plan.
    private static (string From, string To)? SuggestedWindow(string? plan)
    {
        try
        {
            return plan is not null
                && System.Text.Json.Nodes.JsonNode.Parse(plan) is System.Text.Json.Nodes.JsonObject json
                && json["examWindowStart"]?.ToString() is { } from
                && json["examWindowEnd"]?.ToString() is { } to
                ? (from, to)
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

/// <summary><c>ascent rules</c>: shows the rules of engagement and records acceptance (E1-04).</summary>
public sealed class RulesCommand(EngineHost host) : Command<EngineSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, EngineSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        host.Renderer.Heading("Rules of engagement");
        var number = 1;
        foreach (var rule in RulesOfEngagement.Rules)
        {
            host.Renderer.Line(string.Create(CultureInfo.InvariantCulture, $"{number++}. ") + rule);
        }

        if (services.Profile.RulesAccepted)
        {
            host.Renderer.Status(Outcome.Pass, "You've already accepted these rules.");
            return ExitCodes.Ok;
        }

        if (!host.Prompter.Confirm("Do you accept these rules?"))
        {
            host.Renderer.Status(Outcome.Warn, "Not accepted. Labs stay locked until you accept them.");
            return ExitCodes.CheckFailed;
        }

        services.ProfileService.AcceptRules();
        host.Renderer.Status(Outcome.Pass, "Accepted. Labs are unlocked.");
        return ExitCodes.Ok;
    }
}

/// <summary>Options for <c>config</c>.</summary>
public sealed class ConfigSettings : EngineSettings
{
    /// <summary>The setting.</summary>
    [CommandArgument(0, "[KEY]")]
    [Description("The setting to show or change.")]
    public string? Key { get; init; }

    /// <summary>The new value.</summary>
    [CommandArgument(1, "[VALUE]")]
    [Description("The new value.")]
    public string? Value { get; init; }

    /// <summary>Removes the setting.</summary>
    [CommandOption("--unset")]
    [Description("Remove the setting so its default applies.")]
    public bool Unset { get; init; }
}

/// <summary><c>ascent config</c>: lists, shows or changes settings (E1-07, E2-05).</summary>
public sealed class ConfigCommand(EngineHost host) : Command<ConfigSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, ConfigSettings settings, CancellationToken cancellationToken)
    {
        var service = host.Services.ProfileService;
        if (settings.Key is null)
        {
            var values = host.Services.Profile.Values;
            host.Renderer.Table(
                ["Setting", "Value", "What it does"],
                ConfigCatalog.Settings.Select(s => (IReadOnlyList<string>)[s.Key, values.GetValueOrDefault(s.Key) ?? "(default)", s.Description]));
            return ExitCodes.Ok;
        }

        if (settings.Unset)
        {
            service.Unset(settings.Key);
            host.Renderer.Status(Outcome.Pass, settings.Key + " is back to its default.");
            return ExitCodes.Ok;
        }

        if (settings.Value is null)
        {
            _ = ConfigCatalog.Find(settings.Key) ?? throw new UsageException("There is no setting called '" + settings.Key + "'.", "Run 'ascent config' to see every setting.");
            host.Renderer.Line(settings.Key + " = " + (host.Services.Profile.Values.GetValueOrDefault(settings.Key) ?? "(default)"));
            return ExitCodes.Ok;
        }

        service.Set(settings.Key, settings.Value);
        host.Renderer.Status(Outcome.Pass, settings.Key + " = " + settings.Value.Trim());
        return ExitCodes.Ok;
    }
}

/// <summary><c>ascent next</c>: the next Quest in exam order (E2-01).</summary>
public sealed class NextCommand(EngineHost host) : Command<EngineSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, EngineSettings settings, CancellationToken cancellationToken)
    {
        var next = host.Services.Quests.Next();
        if (next is null)
        {
            host.Renderer.Status(Outcome.Pass, "Every Quest is complete.");
            return ExitCodes.Ok;
        }

        host.Renderer.Line(next.Id + " · " + next.Title);
        host.Renderer.Line(string.Create(CultureInfo.InvariantCulture, $"{next.ExamDomain}, Objective {next.ObjectiveId}, about {next.EstimatedMinutes} minutes."));
        host.Renderer.Line("Open it with 'ascent quest " + next.Id + "'.");
        return ExitCodes.Ok;
    }
}

/// <summary>Options naming a Quest or Lab.</summary>
public class ItemSettings : EngineSettings
{
    /// <summary>The Quest or Lab ID.</summary>
    [CommandArgument(0, "<ID>")]
    [Description("The Quest or Lab ID, such as q-1.1.")]
    public string Id { get; init; } = string.Empty;
}

/// <summary><c>ascent quest &lt;id&gt;</c>: the lesson and the Quest's activities (E2-01).</summary>
public sealed class QuestCommand(EngineHost host) : Command<ItemSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, ItemSettings settings, CancellationToken cancellationToken)
    {
        var view = host.Services.Quests.Open(settings.Id);
        var quest = view.Quest;
        host.Renderer.Heading(quest.Id + " · " + quest.Title);
        var document = host.Content.OfKind(Content.Loading.DocumentKind.Quest).FirstOrDefault(d => d.Id == quest.Id);
        host.Renderer.Line(document?.Markdown?.Body.Trim() ?? string.Empty);
        host.Renderer.Heading("Activities");
        if (view.Activities.Count == 0)
        {
            host.Renderer.Line("None. The lesson and its Teach-back complete this Quest.");
        }

        foreach (var activity in view.Activities)
        {
            host.Renderer.Status(activity.Done ? Outcome.Pass : Outcome.Info, activity.Kind + " " + activity.Id + (activity.Bonus ? " (bonus)" : string.Empty) + (activity.Done ? " done" : " to do"));
        }

        host.Renderer.Line(view.LessonDone
            ? "Lesson complete."
            : "When you've read the lesson, explain it in your own words: ascent teachback " + quest.Id);
        if (view.Status == QuestStatus.Complete)
        {
            host.Renderer.Status(Outcome.Pass, "Quest complete.");
        }

        return ExitCodes.Ok;
    }
}

/// <summary>Options for <c>teachback</c>.</summary>
public sealed class TeachBackSettings : ItemSettings
{
    /// <summary>The Teach-back text.</summary>
    [CommandOption("--text <TEXT>")]
    [Description("The Teach-back, in your own words (up to 150 words).")]
    public string? Text { get; init; }

    /// <summary>A file holding the Teach-back.</summary>
    [CommandOption("--file <PATH>")]
    [Description("Read the Teach-back from a file.")]
    public string? File { get; init; }
}

/// <summary><c>ascent teachback &lt;id&gt;</c>: saves a Teach-back and completes the lesson (E2-04, XP-01).</summary>
public sealed class TeachBackCommand(EngineHost host) : Command<TeachBackSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, TeachBackSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var quest = services.Quests.Find(settings.Id);
        var text = settings.File is { } file
            ? ReadFile(file)
            : settings.Text ?? host.Prompter.Ask("Explain the lesson in your own words (up to 150 words):", value => TeachBackService.CountWords(value) is >= 1 and <= TeachBackService.MaxWords ? null : "Use 1–150 words.");

        var path = services.TeachBacks.Save(quest.Id, text, forLab: false);
        var view = services.Quests.CompleteLesson(quest.Id);
        host.Renderer.Status(Outcome.Pass, "Teach-back saved to " + path + ".");
        host.Renderer.Line(view.Status == QuestStatus.Complete
            ? "Quest complete."
            : "Lesson complete. Finish the remaining activities to complete the Quest.");
        return ExitCodes.Ok;
    }

    // A Teach-back is at most 150 words, so a large file is a mistake rather than something to read into memory.
    private static string ReadFile(string path)
    {
        const long MaxBytes = 64 * 1024;
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new UsageException("'" + path + "' doesn't exist.", "Give the path of a text file holding your Teach-back.");
        }

        return info.Length <= MaxBytes
            ? System.IO.File.ReadAllText(info.FullName)
            : throw new UsageException("'" + path + "' is larger than 64 KB; a Teach-back is at most 150 words.", "Give the path of a short text file.");
    }
}

/// <summary><c>ascent status</c>: Rank, XP, badges, weekly goal, rematches and Season 2 (WG-04, RNK-*, S2-01).</summary>
public sealed class StatusCommand(EngineHost host) : Command<EngineSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, EngineSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        var bosses = services.Bosses;
        var best = bosses.BestScores();
        var (rank, promoted, total, coreXpMax) = services.EvaluateRank(best);

        renderer.Heading("Status");
        if (promoted)
        {
            renderer.Status(Outcome.Pass, "Promoted to " + RankCalculator.DisplayName(rank) + "!");
        }

        renderer.Line("Rank: " + RankCalculator.DisplayName(rank));
        var next = RankCalculator.Next(rank);
        renderer.Line(next is { } nextRank
            ? string.Create(CultureInfo.InvariantCulture, $"XP: {total} (next: {RankCalculator.DisplayName(nextRank)} at {RankCalculator.Threshold(nextRank, coreXpMax)})")
            : string.Create(CultureInfo.InvariantCulture, $"XP: {total} (top Rank)"));

        var week = services.WeekGoal.Status(services.Profile.WeeklyGoal);
        renderer.Line(string.Create(CultureInfo.InvariantCulture, $"This week ({week.Week}): {week.Days}/{week.Goal} Stand-up days{(week.Met ? ", goal met" : string.Empty)}. Week streak: {week.Streak}."));

        var domainsCleared = services.Quests.DomainsComplete().Where(d => best.GetValueOrDefault(d) >= 70).ToList();
        var (badges, cleared) = Badges.Compute(new BadgeFacts(
            services.Ledger.Events,
            best,
            domainsCleared,
            services.WeekGoal.LongestStreak(),
            services.Simulations.ExamReady()));
        var badgeNames = badges.Select(b => b.ToString()).Concat(cleared.Select(d => "DomainCleared(" + d + ")")).ToList();
        renderer.Line("Badges: " + (badgeNames.Count == 0 ? "none yet" : string.Join(", ", badgeNames)));

        if (best.Count > 0)
        {
            renderer.Table(["Boss Fight", "Best"], best.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (IReadOnlyList<string>)[p.Key, p.Value.ToString(CultureInfo.InvariantCulture) + "%"]));
        }

        var rematches = bosses.RematchDomains();
        if (rematches.Count > 0)
        {
            renderer.Status(Outcome.Warn, "Rematch due: " + string.Join(", ", rematches.Order(StringComparer.Ordinal)) + ". Your Stand-ups favour these Domains until you pass.");
        }

        var season2 = services.Season2.All();
        if (season2.Count > 0)
        {
            renderer.Line("Season 2: " + string.Join(", ", season2.Select(s => s.ItemId)));
        }

        return ExitCodes.Ok;
    }
}
