using System.Globalization;
using System.Text.Json.Nodes;
using Ascent.Core.Platform;

namespace Ascent.Labs.Cloud;

/// <summary>Whether the guardrails a Cloud Stage needs are in place (CLD-01).</summary>
/// <param name="AzureCliFound">True when <c>az</c> is installed.</param>
/// <param name="SignedIn">True when <c>az</c> is signed in.</param>
/// <param name="Subscription">The signed-in subscription's name.</param>
/// <param name="MissingBudgets">Budget amounts (USD) not found.</param>
/// <param name="PolicyAssigned">True when the guardrail Policy assignment exists.</param>
public sealed record GuardrailStatus(bool AzureCliFound, bool SignedIn, string? Subscription, IReadOnlyList<int> MissingBudgets, bool PolicyAssigned)
{
    /// <summary>True when every guardrail holds.</summary>
    public bool Ready => AzureCliFound && SignedIn && MissingBudgets.Count == 0 && PolicyAssigned;
}

/// <summary>
/// The cost guard (CLD-01, E1-06): budgets of $5, $10 and $20 and the guardrail Policy assignment must exist before any
/// Cloud Stage deploys. <c>guardrails apply</c> deploys the Throughline System's guardrail template at subscription
/// scope after the Learner confirms the command.
/// </summary>
public sealed class CostGuard
{
    /// <summary>The Policy assignment's name.</summary>
    public const string PolicyAssignmentName = "ascent-guardrails";

    /// <summary>The guardrail template, relative to the workspace's <c>throughline/</c> folder.</summary>
    public const string TemplatePath = "infra/guardrails/main.bicep";

    private readonly AzureCli az;
    private readonly Workspace workspace;

    /// <summary>Creates the guard.</summary>
    public CostGuard(AzureCli az, Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(az);
        ArgumentNullException.ThrowIfNull(workspace);
        this.az = az;
        this.workspace = workspace;
    }

    /// <summary>The budget amounts required, in USD.</summary>
    public static IReadOnlyList<int> BudgetAmounts { get; } = [5, 10, 20];

    /// <summary>The guardrail template in the workspace, or null when the current Release has none.</summary>
    public string? Template
    {
        get
        {
            var path = SafePath.Resolve(workspace.Throughline, TemplatePath);
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>Checks the guardrails with read-only <c>az</c> queries.</summary>
    public async Task<GuardrailStatus> CheckAsync(CancellationToken cancellationToken)
    {
        if (!az.Available)
        {
            return new GuardrailStatus(false, false, null, BudgetAmounts, false);
        }

        if (await az.QueryAsync(["account", "show"], cancellationToken) is not JsonObject account)
        {
            return new GuardrailStatus(true, false, null, BudgetAmounts, false);
        }

        var amounts = (await az.QueryAsync(["consumption", "budget", "list"], cancellationToken) as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(budget => Amount(budget["amount"]))
            .OfType<decimal>()
            .ToHashSet();
        var assignments = (await az.QueryAsync(["policy", "assignment", "list"], cancellationToken) as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(assignment => assignment["name"]?.GetValue<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new GuardrailStatus(
            true,
            true,
            account["name"] is JsonValue name && name.TryGetValue<string>(out var text) ? SafeText.Sanitize(text, 200) : null,
            BudgetAmounts.Where(amount => !amounts.Contains(amount)).ToList(),
            assignments.Contains(PolicyAssignmentName));
    }

    /// <summary>The command <c>guardrails apply</c> runs, shown to the Learner first.</summary>
    public static IReadOnlyList<string> ApplyArguments(string location, string template) =>
        ["deployment", "sub", "create", "--name", PolicyAssignmentName, "--location", location, "--template-file", template];

    /// <summary>Deploys the guardrail template. The caller has confirmed <see cref="ApplyArguments"/>.</summary>
    public Task ApplyAsync(string location, CancellationToken cancellationToken) =>
        az.RunOrThrowAsync(
            ApplyArguments(location, Template ?? throw new Core.Errors.AscentException("The guardrail template isn't in your workspace yet.", "It arrives with the Throughline System; 'git pull' to check for it.")),
            ToolTimeouts.CloudDeploy,
            cancellationToken);

    private static decimal? Amount(JsonNode? node) =>
        node is JsonValue value && (value.TryGetValue<decimal>(out var number) || (value.TryGetValue<string>(out var text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number)))
            ? decimal.Round(number, 0)
            : null;
}
