using System.Globalization;
using Ascent.Cli.Hosting;
using Ascent.Cli.Rendering;
using Ascent.Core.Errors;
using Ascent.Labs.Cloud;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary><c>ascent teardown</c>: deletes every resource group tagged <c>ascent:lab</c> and checks none is left (CLD-04, CLD-05).</summary>
public sealed class TeardownCommand(EngineHost host) : AsyncCommand<EngineSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, EngineSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var renderer = host.Renderer;
        if (!services.Az.Available)
        {
            throw new AscentException("The Azure CLI isn't installed, so there's nothing the Engine can tear down.", "Install it if you used a Cloud Stage: https://learn.microsoft.com/cli/azure/install-azure-cli");
        }

        await Activities.WarnAboutOverrunsAsync(host, cancellationToken);
        renderer.Line("Every resource group tagged " + CloudStage.LabTag + " in your signed-in subscription will be deleted, and then checked.");
        if (!host.Prompter.Confirm("Tear down now?", defaultValue: true))
        {
            renderer.Line("Nothing was deleted.");
            return ExitCodes.Ok;
        }

        var result = await services.Cloud.TeardownAsync(cancellationToken);
        foreach (var group in result.Deleted)
        {
            renderer.Line("Deleted " + group + ".");
        }

        if (!result.Clean)
        {
            renderer.Status(Outcome.Fail, "Still there: " + string.Join(", ", result.Remaining) + ". Check the Azure portal, then run 'ascent teardown' again.");
            return ExitCodes.CheckFailed;
        }

        renderer.Status(Outcome.Pass, result.Deleted.Count == 0 ? "Nothing tagged " + CloudStage.LabTag + " is deployed." : "Torn down, and nothing tagged " + CloudStage.LabTag + " is left.");
        if (result.Bonuses > 0)
        {
            renderer.Line(string.Create(CultureInfo.InvariantCulture, $"Teardown Bonus: +{result.Bonuses * 10} XP."));
        }

        return ExitCodes.Ok;
    }
}

/// <summary><c>ascent guardrails check</c>: are the budgets and the Policy assignment in place (CLD-01)?</summary>
public sealed class GuardrailsCheckCommand(EngineHost host) : AsyncCommand<EngineSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, EngineSettings settings, CancellationToken cancellationToken)
    {
        var status = await host.Services.CostGuard.CheckAsync(cancellationToken);
        Show(host.Renderer, status);
        return status.Ready ? ExitCodes.Ok : ExitCodes.CheckFailed;
    }

    /// <summary>Shows a guardrail status, one requirement per line.</summary>
    internal static void Show(IRenderer renderer, GuardrailStatus status)
    {
        renderer.Heading("Cloud Stage guardrails");
        renderer.Status(status.AzureCliFound ? Outcome.Pass : Outcome.Fail, status.AzureCliFound ? "Azure CLI installed." : "Azure CLI not installed: https://learn.microsoft.com/cli/azure/install-azure-cli");
        if (!status.AzureCliFound)
        {
            return;
        }

        renderer.Status(status.SignedIn ? Outcome.Pass : Outcome.Fail, status.SignedIn ? "Signed in to " + (status.Subscription ?? "a subscription") + "." : "Not signed in: run 'az login'.");
        if (!status.SignedIn)
        {
            return;
        }

        foreach (var amount in CostGuard.BudgetAmounts)
        {
            var found = !status.MissingBudgets.Contains(amount);
            renderer.Status(found ? Outcome.Pass : Outcome.Fail, string.Create(CultureInfo.InvariantCulture, $"${amount} budget alert ") + (found ? "found." : "missing."));
        }

        renderer.Status(status.PolicyAssigned ? Outcome.Pass : Outcome.Fail, "Policy assignment '" + CostGuard.PolicyAssignmentName + "' " + (status.PolicyAssigned ? "found." : "missing."));
        if (!status.Ready)
        {
            renderer.Line("Set them up with 'ascent guardrails apply'.");
        }
    }
}

/// <summary><c>ascent guardrails apply</c>: deploys the budgets and the Policy allow-list, after confirmation (CLD-01, E1-06).</summary>
public sealed class GuardrailsApplyCommand(EngineHost host) : AsyncCommand<EngineSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, EngineSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var guard = services.CostGuard;
        var before = await guard.CheckAsync(cancellationToken);
        if (!before.AzureCliFound || !before.SignedIn)
        {
            GuardrailsCheckCommand.Show(host.Renderer, before);
            return ExitCodes.CheckFailed;
        }

        var template = guard.Template ?? throw new AscentException("The guardrail template isn't in your workspace yet.", "It arrives with the Throughline System; 'git pull', then try again.");
        var location = services.Profile.AzureLocation;
        host.Renderer.Line("This deploys budget alerts at $5, $10 and $20 and a Policy allow-list to " + (before.Subscription ?? "your subscription") + ":");
        host.Renderer.Line("  " + AzureCli.Display(CostGuard.ApplyArguments(location, template)));
        if (!host.Prompter.Confirm("Deploy the guardrails?"))
        {
            host.Renderer.Line("Nothing was deployed.");
            return ExitCodes.Ok;
        }

        await guard.ApplyAsync(location, cancellationToken);
        var after = await guard.CheckAsync(cancellationToken);
        GuardrailsCheckCommand.Show(host.Renderer, after);
        return after.Ready ? ExitCodes.Ok : ExitCodes.CheckFailed;
    }
}
