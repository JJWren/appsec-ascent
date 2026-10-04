using System.Globalization;
using System.Text.Json.Nodes;
using Ascent.Core.Curriculum;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Progress;

namespace Ascent.Labs.Cloud;

/// <summary>A Cloud Stage deployment, planned and shown to the Learner before it runs (CLD-02, CLD-03).</summary>
/// <param name="Lab">The Lab.</param>
/// <param name="ResourceGroup">The tagged resource group.</param>
/// <param name="ExpiresUtc">The <c>expires-on</c> tag.</param>
/// <param name="Template">The Bicep template.</param>
/// <param name="Commands">The <c>az</c> commands, in order.</param>
public sealed record CloudPlan(LabInfo Lab, string ResourceGroup, DateTimeOffset ExpiresUtc, string Template, IReadOnlyList<IReadOnlyList<string>> Commands);

/// <summary>What <c>teardown</c> did.</summary>
/// <param name="Deleted">Resource groups deleted.</param>
/// <param name="Remaining">Tagged resource groups still there afterwards.</param>
/// <param name="Bonuses">Deployments that earned the Teardown Bonus.</param>
public sealed record TeardownResult(IReadOnlyList<string> Deleted, IReadOnlyList<string> Remaining, int Bonuses)
{
    /// <summary>True when nothing tagged <c>ascent:lab</c> is left (CLD-04).</summary>
    public bool Clean => Remaining.Count == 0;
}

/// <summary>Everything the Cloud Stage works with.</summary>
/// <param name="Az">The Azure CLI.</param>
/// <param name="Workspace">The Learner workspace, which holds the templates.</param>
/// <param name="Deployments">Recorded deployments.</param>
/// <param name="Ledger">XP.</param>
/// <param name="Transactions">One transaction per outcome and its XP.</param>
/// <param name="Time">The clock.</param>
public sealed record CloudDependencies(AzureCli Az, Workspace Workspace, ICloudDeploymentStore Deployments, XpLedger Ledger, IProgressTransactions Transactions, TimeProvider Time);

/// <summary>
/// Cloud Stages (CLD-02..05): tagged deployments with an expiry, one teardown for everything tagged, a Teardown Bonus
/// for prompt teardowns and a penalty, once, for overruns.
/// </summary>
public sealed class CloudStage
{
    /// <summary>The tag every Cloud Stage resource group carries.</summary>
    public const string LabTag = "ascent:lab";

    /// <summary>The expiry tag.</summary>
    public const string ExpiryTag = "expires-on";

    private readonly CloudDependencies d;

    /// <summary>Creates the stage.</summary>
    public CloudStage(CloudDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        d = dependencies;
    }

    /// <summary>Plans a deployment: the resource group, its tags and expiry, and the commands (CLD-03).</summary>
    public CloudPlan Plan(LabInfo lab, string location)
    {
        ArgumentNullException.ThrowIfNull(lab);
        var cloud = lab.Cloud ?? throw new UsageException("This Lab has no Cloud Stage.", "Run it locally with 'ascent lab up " + lab.Id + "'.");
        var template = SafePath.Resolve(d.Workspace.Throughline, cloud.Bicep);
        if (!File.Exists(template))
        {
            throw new AscentException("The Cloud Stage template (" + cloud.Bicep + ") isn't in your workspace.", "Run 'ascent lab up " + lab.Id + "' first, or report it with 'ascent bug " + lab.Id + "'.");
        }

        var now = d.Time.GetUtcNow();
        var expires = now.AddHours(cloud.TeardownWindowHours);
        var group = "ascent-" + lab.Id + "-" + now.UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        return new CloudPlan(
            lab,
            group,
            expires,
            template,
            [
                ["group", "create", "--name", group, "--location", location, "--tags", LabTag + "=" + lab.Id, ExpiryTag + "=" + Tag(expires)],
                ["deployment", "group", "create", "--resource-group", group, "--name", lab.Id, "--template-file", template],
            ]);
    }

    /// <summary>
    /// Deploys a confirmed plan. The resource group is recorded as soon as it exists, so a failed deployment is still
    /// torn down. A first successful deployment of a Lab earns the Cloud Stage bonus.
    /// </summary>
    public async Task DeployAsync(CloudPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await d.Az.RunOrThrowAsync(plan.Commands[0], ToolTimeouts.CloudDeploy, cancellationToken);
        d.Deployments.Add(plan.Lab.Id, plan.ResourceGroup, d.Time.GetUtcNow(), plan.ExpiresUtc);
        await d.Az.RunOrThrowAsync(plan.Commands[1], ToolTimeouts.CloudDeploy, cancellationToken);
        d.Ledger.Award(XpKind.CloudStage, plan.Lab.Id);
    }

    /// <summary>
    /// Deletes every resource group tagged <c>ascent:lab</c>, then checks that none is left (CLD-04). Deployments torn
    /// down before their expiry earn the Teardown Bonus.
    /// </summary>
    public async Task<TeardownResult> TeardownAsync(CancellationToken cancellationToken)
    {
        var groups = await TaggedGroupsAsync(cancellationToken);
        foreach (var group in groups)
        {
            await d.Az.RunOrThrowAsync(["group", "delete", "--name", group, "--yes"], ToolTimeouts.CloudDeploy, cancellationToken);
        }

        var remaining = await TaggedGroupsAsync(cancellationToken);
        var now = d.Time.GetUtcNow();
        var bonuses = 0;
        foreach (var deployment in d.Deployments.Open().Where(dep => !remaining.Contains(dep.ResourceGroup, StringComparer.OrdinalIgnoreCase)))
        {
            var bonus = deployment.Outcome == CloudOutcome.None && now <= deployment.ExpiresUtc;
            d.Transactions.Run(() =>
            {
                d.Deployments.Close(deployment.Id, now);
                if (bonus)
                {
                    d.Deployments.SetOutcome(deployment.Id, CloudOutcome.Bonus);
                    d.Ledger.Award(XpKind.TeardownBonus, RefId(deployment));
                }
            });
            bonuses += bonus ? 1 : 0;
        }

        return new TeardownResult(groups, remaining, bonuses);
    }

    /// <summary>
    /// Finds deployments past their <c>expires-on</c> that still exist and charges each one's penalty once (CLD-05).
    /// Deployments already gone are closed. Without <c>az</c> nothing can be checked, so nothing is charged.
    /// </summary>
    public async Task<IReadOnlyList<CloudDeploymentRecord>> CheckOverrunsAsync(CancellationToken cancellationToken)
    {
        var now = d.Time.GetUtcNow();
        var expired = d.Deployments.Open().Where(dep => now > dep.ExpiresUtc).ToList();
        if (expired.Count == 0 || !d.Az.Available)
        {
            return [];
        }

        var overruns = new List<CloudDeploymentRecord>();
        foreach (var deployment in expired)
        {
            var exists = await d.Az.QueryAsync(["group", "exists", "--name", deployment.ResourceGroup], cancellationToken);
            if (exists is JsonValue value && value.TryGetValue<bool>(out var found) && found)
            {
                if (deployment.Outcome == CloudOutcome.None)
                {
                    d.Transactions.Run(() =>
                    {
                        d.Deployments.SetOutcome(deployment.Id, CloudOutcome.Penalty);
                        d.Ledger.Award(XpKind.TeardownPenalty, RefId(deployment));
                    });
                }

                overruns.Add(deployment);
            }
            else if (exists is not null)
            {
                d.Deployments.Close(deployment.Id, now);
            }
        }

        return overruns;
    }

    private static string RefId(CloudDeploymentRecord deployment) => "deploy-" + deployment.Id.ToString(CultureInfo.InvariantCulture);

    private static string Tag(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<IReadOnlyList<string>> TaggedGroupsAsync(CancellationToken cancellationToken)
    {
        var groups = await d.Az.QueryAsync(["group", "list", "--tag", LabTag], cancellationToken) as JsonArray
            ?? throw new AscentException("Couldn't list your resource groups.", "Check that 'az account show' works, then run 'ascent teardown' again.");
        return groups.OfType<JsonObject>()
            .Select(group => group["name"] is JsonValue name && name.TryGetValue<string>(out var text) ? text : null)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
