using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Ascent.Assessment.Questions;
using Ascent.Assessment.Services;
using Ascent.Cli.Hosting;
using Ascent.Cli.Rendering;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Progress;
using Ascent.Sealing;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary><c>ascent standup</c>: today's spaced-repetition review (SU-01..05, WG-02).</summary>
public sealed class StandUpCommand(EngineHost host) : Command<EngineSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, EngineSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        var calendar = services.Calendar;
        var today = calendar.Today;
        var cards = services.Cards.All();
        var plan = StandUpPlanner.Plan(
            services.Now,
            cards,
            services.Catalog.Questions,
            services.Quests.ObjectivesWithLessonDone(),
            services.Bosses.RematchDomains(),
            cards.Count(c => calendar.ToLocalDate(c.IntroducedUtc) == today));

        renderer.Heading("Stand-up");
        if (plan.All.Count == 0)
        {
            renderer.Line("Nothing is due today.");
            if (!host.Prompter.Confirm("Did you review your notes today? It counts toward your weekly goal.", defaultValue: true))
            {
                return ExitCodes.Ok;
            }
        }

        var questions = services.Catalog.Questions.ToDictionary(q => q.ItemId, StringComparer.Ordinal);
        var fresh = plan.New.ToHashSet(StringComparer.Ordinal);
        var reviews = services.Reviews;
        var answered = 0;
        var correct = 0;
        for (var index = 0; index < plan.All.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var itemId = plan.All[index];
            Question question;
            try
            {
                question = services.Questions.Serve(itemId, ServeContext.StandUp);
            }
            catch (SealedItemUnavailableException ex)
            {
                // One unreadable item shouldn't cost the Learner the day; it's reported and skipped.
                renderer.Status(Outcome.Warn, ex.Message + " Skipped for today.");
                continue;
            }

            var timer = Stopwatch.StartNew();
            var heading = string.Create(CultureInfo.InvariantCulture, $"{index + 1}/{plan.All.Count} · Objective {question.ObjectiveId}{(fresh.Contains(itemId) ? " · new" : string.Empty)}");
            var result = QuestionPresenter.Ask(renderer, host.Prompter, question, heading);
            QuestionPresenter.Feedback(renderer, question, result.Correct);
            reviews.Record(questions[itemId], result.Correct, ReviewContext.StandUp, timer.ElapsedMilliseconds);
            answered++;
            correct += result.Correct ? 1 : 0;
        }

        var week = services.WeekGoal.RecordStandUp(answered, services.Profile.WeeklyGoal);
        renderer.Heading("Done");
        if (answered > 0)
        {
            renderer.Line(string.Create(CultureInfo.InvariantCulture, $"{correct}/{answered} correct."));
        }

        renderer.Line(string.Create(CultureInfo.InvariantCulture, $"This week: {week.Days}/{week.Goal} Stand-up days{(week.Met ? ", goal met" : string.Empty)}. Week streak: {week.Streak}."));
        return ExitCodes.Ok;
    }
}

/// <summary><c>ascent diagnostic</c>: the placement test and study plan (DX-01..03).</summary>
public sealed class DiagnosticCommand(EngineHost host) : Command<EngineSettings>
{
    /// <summary>The weekly hours assumed when the Learner hasn't set any (D2: 6–10 hours a week).</summary>
    public const int DefaultWeeklyHours = 8;

    /// <inheritdoc />
    public override int Execute(CommandContext context, EngineSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        var diagnostic = services.Diagnostic;
        var attempt = diagnostic.StartOrResume();
        var answered = diagnostic.Answered(attempt);
        renderer.Heading("Diagnostic");
        renderer.Line(string.Create(CultureInfo.InvariantCulture, $"{attempt.ItemIds.Count} questions, untimed. Results come at the end, and every answer is saved as you go."));

        var number = answered.Count;
        foreach (var itemId in attempt.ItemIds.Where(id => !answered.Contains(id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var question = services.Questions.Serve(itemId, ServeContext.Diagnostic);
            var result = QuestionPresenter.Ask(renderer, host.Prompter, question, string.Create(CultureInfo.InvariantCulture, $"{++number}/{attempt.ItemIds.Count}"));
            diagnostic.Answer(attempt, itemId, result.Normalized);
        }

        var hours = services.Profile.WeeklyHours ?? DefaultWeeklyHours;
        var plan = diagnostic.Finish(attempt, id => services.Questions.Serve(id, ServeContext.Diagnostic), hours, services.Calendar.Today);
        renderer.Heading("Your study plan");
        renderer.Table(
            ["Domain", "Score", "Weeks"],
            plan.Domains.Select(d => (IReadOnlyList<string>)[d.Domain, string.Create(CultureInfo.InvariantCulture, $"{d.Correct}/{d.Total} ({d.ScorePercent}%)"), d.Weeks.ToString(CultureInfo.InvariantCulture)]));
        renderer.Line(string.Create(CultureInfo.InvariantCulture, $"About {plan.EstimatedHours} hours: {plan.TotalWeeks} weeks at {hours} hours a week, then 2 Simulation weeks."));
        renderer.Line("Suggested exam window: " + PlanText.Date(plan.ExamWindowStart) + " to " + PlanText.Date(plan.ExamWindowEnd) + ".");
        if (services.Profile.WeeklyHours is null)
        {
            renderer.Line(string.Create(CultureInfo.InvariantCulture, $"This assumes {DefaultWeeklyHours} hours a week. Change it with 'ascent config weeklyHours <hours>', then retake the Diagnostic for a new plan."));
        }

        return ExitCodes.Ok;
    }
}

/// <summary>Options for <c>boss</c>.</summary>
public sealed class BossSettings : EngineSettings
{
    /// <summary>The Domain.</summary>
    [CommandArgument(0, "<DOMAIN>")]
    [Description("The Domain, D1 to D8.")]
    public string Domain { get; init; } = string.Empty;

    /// <inheritdoc />
    public override Spectre.Console.ValidationResult Validate() =>
        Domain.Length == 2 && char.ToUpperInvariant(Domain[0]) == 'D' && Domain[1] is >= '1' and <= '8'
            ? base.Validate()
            : Spectre.Console.ValidationResult.Error("The Domain must be D1 to D8.");
}

/// <summary><c>ascent boss &lt;domain&gt;</c>: the timed, exam-style test that closes a Domain (BF-01..06).</summary>
public sealed class BossCommand(EngineHost host) : Command<BossSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, BossSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        var bosses = services.Bosses;
        var attempt = bosses.StartOrResume(settings.Domain.ToUpperInvariant());
        var answered = bosses.Answered(attempt);
        var questions = new Dictionary<string, Question>(StringComparer.Ordinal);
        Question Load(string id) => questions.TryGetValue(id, out var loaded) ? loaded : questions[id] = services.Questions.Serve(id, ServeContext.BossFight);

        renderer.Heading(attempt.ExamDomain + " Boss Fight");
        renderer.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"{attempt.ItemIds.Count} questions in {BossFightService.Duration.TotalMinutes:0} minutes, pass at {BossFightService.PassPercent}%. No feedback until the end; unanswered questions count as wrong."));
        var number = answered.Count;
        foreach (var itemId in attempt.ItemIds.Where(id => !answered.Contains(id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Answer(attempt, itemId, Load(itemId), ++number))
            {
                renderer.Status(Outcome.Warn, "Time's up.");
                break;
            }
        }

        var outcome = bosses.Finish(attempt, Load);
        renderer.Heading("Result");
        renderer.Status(
            outcome.Passed ? Outcome.Pass : Outcome.Fail,
            string.Create(CultureInfo.InvariantCulture, $"{outcome.Correct}/{outcome.Total} ({outcome.ScorePercent}%). ") + (outcome.Passed
                ? "Passed."
                : "Not passed yet. A rematch is due, and your Stand-ups favour " + outcome.ExamDomain + " until you pass."));
        if (outcome.XpAwarded > 0)
        {
            renderer.Line(string.Create(CultureInfo.InvariantCulture, $"+{outcome.XpAwarded} XP"));
        }

        QuestionPresenter.Review(renderer, outcome.Items.Select(i => (Load(i.ItemId), i.Answer, i.Correct)));
        return ExitCodes.Ok;
    }

    // Saves one answer; false once the deadline has passed (BF-02).
    private bool Answer(BossAttemptRecord attempt, string itemId, Question question, int number)
    {
        var services = host.Services;
        if (services.Now > attempt.DeadlineUtc)
        {
            return false;
        }

        var heading = string.Create(CultureInfo.InvariantCulture, $"{number}/{attempt.ItemIds.Count} · {QuestionPresenter.TimeLeft(attempt.DeadlineUtc - services.Now)}");
        var result = QuestionPresenter.Ask(host.Renderer, host.Prompter, question, heading);
        return services.Bosses.Answer(attempt, itemId, result.Normalized, services.Now);
    }
}

/// <summary>Options for <c>sim</c>.</summary>
public sealed class SimSettings : EngineSettings
{
    /// <summary>The form.</summary>
    [CommandArgument(0, "[FORM]")]
    [Description("A or B. Defaults to the first form you haven't taken.")]
    public string? Form { get; init; }
}

/// <summary><c>ascent sim [A|B]</c>: a full 125-question, 3-hour mock exam (SIM-01..06).</summary>
public sealed class SimCommand(EngineHost host) : Command<SimSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, SimSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        var simulations = services.Simulations;
        var attempt = simulations.Active();
        if (attempt is null)
        {
            var locked = simulations.LockedBy(services.Bosses.BestScores());
            if (locked.Count > 0)
            {
                throw new AscentException(
                    "Simulations unlock once every Boss Fight's best score is at least 70%. Still below: " + string.Join(", ", locked) + ".",
                    "Run 'ascent boss " + locked[0] + "'.");
            }

            renderer.Line(string.Create(CultureInfo.InvariantCulture, $"A Simulation is {SimulationService.FormSize} questions in {SimulationService.Duration.TotalHours:0} hours. The clock keeps running if you stop; you can resume until the deadline."));
            if (!host.Prompter.Confirm("Start now?"))
            {
                return ExitCodes.Ok;
            }

            attempt = simulations.Start(settings.Form);
        }
        else if (settings.Form is { } requested && !requested.Equals(attempt.Form, StringComparison.OrdinalIgnoreCase))
        {
            renderer.Status(Outcome.Info, "Simulation " + attempt.Form + " is still in progress, so it resumes first.");
        }

        // Simulation items are released only while the attempt is in progress (SIM-06), so each one is kept for the review.
        var questions = new Dictionary<string, Question>(StringComparer.Ordinal);
        var current = attempt;
        Question Load(string id) => questions.TryGetValue(id, out var loaded) ? loaded : questions[id] = services.Questions.ServeInSimulation(id, current.Id);

        renderer.Heading("Simulation " + attempt.Form);
        var answered = simulations.Answered(attempt);
        var number = answered.Count;
        var expired = false;
        foreach (var itemId in attempt.ItemIds.Where(id => !answered.Contains(id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Answer(attempt, itemId, Load(itemId), ++number))
            {
                expired = true;
                break;
            }
        }

        expired |= services.Now > attempt.DeadlineUtc;
        foreach (var itemId in attempt.ItemIds)
        {
            Load(itemId);
        }

        var outcome = simulations.Finish(attempt, Load, expired);
        renderer.Heading("Result");
        renderer.Status(
            outcome.ReachedTarget ? Outcome.Pass : Outcome.Fail,
            string.Create(CultureInfo.InvariantCulture, $"Form {outcome.Form}: {outcome.Correct}/{outcome.Total} ({outcome.ScorePercent}%), target {SimulationService.TargetPercent}%.{(expired ? " Time ran out; unanswered questions count as wrong." : string.Empty)}"));
        if (simulations.ExamReady())
        {
            renderer.Status(Outcome.Pass, "Exam Ready: both forms reached the target on the first attempt.");
        }

        QuestionPresenter.Review(renderer, outcome.Items.Select(i => (Load(i.ItemId), i.Answer, i.Correct)));
        return ExitCodes.Ok;
    }

    // Saves one answer; false once the deadline has passed (SIM-03).
    private bool Answer(SimulationAttemptRecord attempt, string itemId, Question question, int number)
    {
        var services = host.Services;
        if (services.Now > attempt.DeadlineUtc)
        {
            return false;
        }

        var heading = string.Create(CultureInfo.InvariantCulture, $"{number}/{attempt.ItemIds.Count} · {QuestionPresenter.TimeLeft(attempt.DeadlineUtc - services.Now)}");
        var result = QuestionPresenter.Ask(host.Renderer, host.Prompter, question, heading);
        return services.Simulations.Answer(attempt, itemId, result.Normalized, services.Now);
    }
}
