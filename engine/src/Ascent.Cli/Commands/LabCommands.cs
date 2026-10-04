using System.ComponentModel;
using System.Globalization;
using Ascent.Cli.Hosting;
using Ascent.Cli.Rendering;
using Ascent.Core.Curriculum;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Labs;
using Ascent.Labs.Cloud;
using Ascent.Sealing.Flags;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>Helpers shared by the activity commands.</summary>
internal static class Activities
{
    /// <summary>Completes any Quest whose last open activity was <paramref name="activityId"/>, and says so.</summary>
    public static void CompleteQuests(EngineHost host, string activityId)
    {
        var services = host.Services;
        var quests = services.Quests;
        foreach (var quest in services.Catalog.Quests.Where(q => q.Labs.Contains(activityId) || q.Drills.Contains(activityId) || q.Deliverables.Contains(activityId)))
        {
            var before = quests.Status(quest.Id);
            if (before != QuestStatus.Complete && quests.TryComplete(quest).Status == QuestStatus.Complete)
            {
                host.Renderer.Status(Outcome.Pass, "Quest complete: " + quest.Id + " · " + quest.Title + ".");
            }
        }
    }

    /// <summary>Warns about Cloud Stage deployments past their expiry and charges the penalty once (CLD-05).</summary>
    public static async Task WarnAboutOverrunsAsync(EngineHost host, CancellationToken cancellationToken)
    {
        foreach (var overrun in await host.Services.Cloud.CheckOverrunsAsync(cancellationToken))
        {
            host.Renderer.Status(
                Outcome.Warn,
                "The Cloud Stage '" + overrun.ResourceGroup + "' expired at " + overrun.ExpiresUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + " UTC and is still running: -20 XP (once). Remove it with 'ascent teardown'.");
        }
    }
}

/// <summary>Options for <c>lab up</c>.</summary>
public sealed class LabUpSettings : ItemSettings
{
    /// <summary>Also deploy the Cloud Stage.</summary>
    [CommandOption("--cloud")]
    [Description("Also deploy the Lab's Cloud Stage to your Azure subscription, after the cost checks.")]
    public bool Cloud { get; init; }

    /// <summary>Refresh the estimate.</summary>
    [CommandOption("--refresh-estimate")]
    [Description("Refresh the Cloud Stage's estimate from the Azure Retail Prices API.")]
    public bool RefreshEstimate { get; init; }
}

/// <summary><c>ascent lab up &lt;id&gt;</c>: starts a Lab, plants a fresh Flag and brings its Local Stage up (LABE-01, FLAG-01, CLD-01..03).</summary>
public sealed class LabUpCommand(EngineHost host) : AsyncCommand<LabUpSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, LabUpSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        await Activities.WarnAboutOverrunsAsync(host, cancellationToken);
        var result = await services.Labs.UpAsync(settings.Id, cancellationToken);
        renderer.Heading(result.Lab.Id);
        renderer.Status(
            Outcome.Pass,
            result.FirstStart
                ? string.Create(CultureInfo.InvariantCulture, $"Lab started: {result.ModuleFiles} file(s) unpacked into my-work/throughline/, and a Flag is planted.")
                : "A fresh Flag is planted. Your work in my-work/ is untouched.");
        renderer.Line(result.Stage.Message);
        renderer.Line("Read the brief in labs/" + result.Lab.Id + "/BRIEF.md. When you've captured the Flag, run 'ascent flag " + result.Lab.Id + "'.");
        return settings.Cloud ? await CloudUpAsync(result.Lab, settings.RefreshEstimate, cancellationToken) : ExitCodes.Ok;
    }

    private async Task<int> CloudUpAsync(LabInfo lab, bool refresh, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        var cloud = lab.Cloud ?? throw new UsageException("This Lab has no Cloud Stage.", "Its Local Stage is all there is to it.");
        renderer.Heading("Cloud Stage");
        var guardrails = await services.CostGuard.CheckAsync(cancellationToken);
        if (!guardrails.Ready)
        {
            GuardrailsCheckCommand.Show(renderer, guardrails);
            throw new AscentException("Cloud Stages stay blocked until the guardrails are in place (CLD-01).", "Run 'ascent guardrails apply', then try again.");
        }

        var plan = services.Cloud.Plan(lab, services.Profile.AzureLocation);
        renderer.Line(string.Create(CultureInfo.InvariantCulture, $"Estimate: ${cloud.EstimateUsd:0.00}") + (cloud.FreeTier ? ", within free grants." : "."));
        if (refresh)
        {
            var sheet = PriceSheet.Read(plan.Template);
            if (sheet is null)
            {
                renderer.Line("This Lab has no price sheet, so the manifest's estimate stands.");
            }
            else
            {
                var refreshed = await services.RetailPrices.EstimateAsync(sheet, cancellationToken);
                renderer.Line(string.Create(CultureInfo.InvariantCulture, $"Refreshed from the Azure Retail Prices API: ${refreshed:0.00}."));
            }
        }

        renderer.Line("It runs in resource group " + plan.ResourceGroup + ", tagged to expire at "
            + plan.ExpiresUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC. These commands run:");
        foreach (var command in plan.Commands)
        {
            renderer.Line("  " + AzureCli.Display(command));
        }

        var confirmed = cloud.PaidSideQuest
            ? host.Prompter.ConfirmTyped("This is a paid side-quest. Type the Lab ID to deploy it:", lab.Id)
            : host.Prompter.Confirm("Deploy it now?");
        if (!confirmed)
        {
            renderer.Line("Nothing was deployed.");
            return ExitCodes.Ok;
        }

        await services.Cloud.DeployAsync(plan, cancellationToken);
        renderer.Status(Outcome.Pass, "Deployed. Tear it down before it expires for the Teardown Bonus: ascent teardown");
        return ExitCodes.Ok;
    }
}

/// <summary><c>ascent lab down &lt;id&gt;</c>: stops the Lab's Local Stage.</summary>
public sealed class LabDownCommand(EngineHost host) : AsyncCommand<ItemSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, ItemSettings settings, CancellationToken cancellationToken)
    {
        var result = await host.Services.Labs.DownAsync(settings.Id, cancellationToken);
        host.Renderer.Line(result.Message);
        return ExitCodes.Ok;
    }
}

/// <summary><c>ascent lab reset &lt;id&gt;</c>: unpacks the Lab Module again, after confirmation (LABE-04).</summary>
public sealed class LabResetCommand(EngineHost host) : Command<ItemSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, ItemSettings settings, CancellationToken cancellationToken)
    {
        var labs = host.Services.Labs;
        var lab = labs.Find(settings.Id);
        host.Renderer.Line("This puts the original module files back in my-work/throughline/, overwriting your changes to them. Your progress and XP are kept.");
        if (!host.Prompter.Confirm("Reset " + lab.Id + "?"))
        {
            host.Renderer.Line("Nothing was changed.");
            return ExitCodes.Ok;
        }

        var files = labs.Reset(lab.Id);
        host.Renderer.Status(Outcome.Pass, string.Create(CultureInfo.InvariantCulture, $"Reset {files} file(s)."));
        return ExitCodes.Ok;
    }
}

/// <summary><c>ascent flag &lt;id&gt;</c>: submits a captured Flag (FLAG-02, FLAG-03).</summary>
public sealed class FlagCommand(EngineHost host) : Command<ItemSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, ItemSettings settings, CancellationToken cancellationToken)
    {
        var labs = host.Services.Labs;
        var lab = labs.Find(settings.Id);
        var candidate = host.Prompter.Ask("Flag:", value => value.Trim().Length > 0 ? null : "Paste the Flag you captured.");
        var check = labs.SubmitFlag(lab.Id, candidate);
        switch (check.Outcome)
        {
            case FlagOutcome.Accepted:
                host.Renderer.Status(Outcome.Pass, string.Create(CultureInfo.InvariantCulture, $"Flag accepted: Red step done, +{lab.Red} XP."));
                host.Renderer.Line("The security tests are unlocked. Fix the vulnerability in my-work/throughline/, then run 'ascent verify " + lab.Id + "'.");
                return ExitCodes.Ok;
            case FlagOutcome.CoolingDown:
                host.Renderer.Status(Outcome.Fail, "Too many wrong tries. Try again after " + check.RetryAfter!.Value.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC.");
                return ExitCodes.CheckFailed;
            case FlagOutcome.NoFlag:
                host.Renderer.Status(Outcome.Fail, "No Flag is planted for this Lab. Run 'ascent lab up " + lab.Id + "'.");
                return ExitCodes.CheckFailed;
            default:
                host.Renderer.Status(Outcome.Fail, "That isn't the Flag.");
                return ExitCodes.CheckFailed;
        }
    }
}

/// <summary><c>ascent verify &lt;id&gt;</c>: runs the Lab's security tests against your fix (LABE-02).</summary>
public sealed class VerifyCommand(EngineHost host) : AsyncCommand<ItemSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, ItemSettings settings, CancellationToken cancellationToken)
    {
        var labs = host.Services.Labs;
        var lab = labs.Find(settings.Id);
        var renderer = host.Renderer;
        renderer.Line("Building and running the security tests against my-work/throughline/ ...");
        var outcome = await labs.VerifyAsync(lab.Id, cancellationToken);
        var run = outcome.Run;
        if (!run.Built)
        {
            renderer.Status(Outcome.Fail, "The tests didn't run:");
            foreach (var line in (run.Problem ?? string.Empty).Split('\n'))
            {
                renderer.Line("  " + line);
            }

            return ExitCodes.CheckFailed;
        }

        if (!run.AllPassed)
        {
            renderer.Status(Outcome.Fail, string.Create(CultureInfo.InvariantCulture, $"{run.Failed} of {run.Total} security test(s) fail:"));
            foreach (var name in run.FailedTests)
            {
                renderer.Line("  " + name);
            }

            return ExitCodes.CheckFailed;
        }

        renderer.Status(Outcome.Pass, string.Create(CultureInfo.InvariantCulture, $"All {run.Total} security test(s) pass."));
        renderer.Line(outcome.NowFixed
            ? string.Create(CultureInfo.InvariantCulture, $"Blue step done, +{lab.Blue} XP. Now explain what you found and fixed: ascent teachback {lab.Id}")
            : "This Lab was already fixed.");
        return ExitCodes.Ok;
    }
}
