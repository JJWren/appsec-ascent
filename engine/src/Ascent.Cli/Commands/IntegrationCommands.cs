using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Ascent.Cli.Hosting;
using Ascent.Cli.Rendering;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Progress;
using Ascent.Integrations;
using Ascent.Sealing.Unsealing;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary><c>ascent doctor</c>: which Stages your environment supports, with install hints for anything missing (E1-03).</summary>
public sealed class DoctorCommand(EngineHost host) : AsyncCommand<EngineSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, EngineSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var checks = new List<DoctorCheck>(await services.Doctor.CheckToolsAsync(cancellationToken))
        {
            await EnvironmentDoctor.CheckAiEndpointAsync(services.Http, services.Profile.AiEndpoint, cancellationToken),
        };
        if (services.Az.Available)
        {
            var guardrails = await services.CostGuard.CheckAsync(cancellationToken);
            checks.Add(new DoctorCheck(
                DoctorArea.CloudStage,
                "Guardrails",
                guardrails.Ready ? DoctorStatus.Ok : DoctorStatus.Optional,
                guardrails.Ready ? "budgets and Policy in place" : guardrails.SignedIn ? "not in place" : "not signed in",
                guardrails.Ready ? null : guardrails.SignedIn ? "Run 'ascent guardrails apply' before any Cloud Stage." : "Run 'az login', then 'ascent guardrails check'."));
        }

        var integrity = services.Database.IntegrityCheck();
        checks.Add(new DoctorCheck(DoctorArea.Engine, "Progress database", integrity == "ok" ? DoctorStatus.Ok : DoctorStatus.Problem, integrity, integrity == "ok" ? null : "Restore a backup or an export: ascent progress import <file>"));
        var swept = UnsealedSweeper.Sweep(host.Paths);
        checks.Add(new DoctorCheck(DoctorArea.Engine, "Unsealed leftovers", DoctorStatus.Ok, swept == 0 ? "none" : string.Create(CultureInfo.InvariantCulture, $"removed {swept}")));
        checks.Add(new DoctorCheck(DoctorArea.Engine, "Trusted key", DoctorStatus.Ok, "SHA-256 " + services.Verifier.Fingerprint));
        var workspace = services.Workspace;
        checks.Add(new DoctorCheck(
            DoctorArea.Engine,
            "Workspace",
            DoctorStatus.Ok,
            !workspace.Exists ? "not created yet (the first 'ascent lab up' creates it)"
                : string.Create(CultureInfo.InvariantCulture, $"my-work/ on Release {services.Labs.CurrentRelease}") + (workspace.IsRepository ? ", a git repository" : ", not a git repository")));

        host.Renderer.Table(
            ["Area", "Check", "Status", "Found"],
            checks.Select(c => (IReadOnlyList<string>)[Area(c.Area), c.Name, Word(c.Status), c.Detail]));
        foreach (var check in checks.Where(c => c.Hint is not null))
        {
            host.Renderer.Status(check.Status == DoctorStatus.Problem ? Outcome.Fail : Outcome.Info, check.Name + ": " + check.Hint);
        }

        return checks.Any(c => c.Status == DoctorStatus.Problem) ? ExitCodes.CheckFailed : ExitCodes.Ok;
    }

    private static string Area(DoctorArea area) => area switch
    {
        DoctorArea.LocalStage => "Local Stage",
        DoctorArea.CloudStage => "Cloud Stage (optional)",
        DoctorArea.DeepDives => "Deep Dives (optional)",
        DoctorArea.AiReviewer => "AI reviewer (optional)",
        _ => "Engine",
    };

    private static string Word(DoctorStatus status) => status switch
    {
        DoctorStatus.Ok => "ok",
        DoctorStatus.Optional => "optional",
        _ => "PROBLEM",
    };
}

/// <summary>Options for <c>bug</c>.</summary>
public sealed class BugSettings : ItemSettings
{
    /// <summary>The citation.</summary>
    [CommandArgument(1, "[CITATION]")]
    [Description("The citation ID, such as c2, if the problem is with a source.")]
    public string? Citation { get; init; }
}

/// <summary>
/// <c>ascent bug &lt;id&gt; [citation]</c>: opens a prefilled Content Bug report with only the item and citation IDs
/// (BUG-01), and pauses the item's review card until the bug is resolved (SU-05).
/// </summary>
public sealed class BugCommand(EngineHost host) : Command<BugSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, BugSettings settings, CancellationToken cancellationToken)
    {
        var url = IssueUrlBuilder.Build(settings.Id, settings.Citation);
        host.Renderer.Line("Report it here (the form is already filled in with the item ID):");
        host.Renderer.Line(url.AbsoluteUri);
        if (host.Services.ContentBugs.Suspend(settings.Id))
        {
            host.Renderer.Line("Its review card is paused until the bug is resolved; 'ascent sync' resumes it.");
        }

        if (!host.Renderer.IsPlain)
        {
            host.OpenUrl(url);
        }

        return ExitCodes.Ok;
    }
}

/// <summary><c>ascent sync</c>: confirmed Content Bugs earn 25 XP each, once (BUG-02).</summary>
public sealed class SyncCommand(EngineHost host) : AsyncCommand<EngineSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, EngineSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var user = services.Profile.GitHubUser ?? throw new UsageException("Your GitHub user name isn't set.", "Run 'ascent config githubUser <name>'.");
        host.Renderer.Line("Checking " + Upstream.Owner + "/" + Upstream.Repository + " for your confirmed Content Bugs (public data, no sign-in) ...");
        var issues = await services.GitHub.ConfirmedAsync(user, cancellationToken);
        var result = services.ContentBugs.Apply(issues);
        host.Renderer.Status(Outcome.Pass, string.Create(CultureInfo.InvariantCulture, $"{result.Confirmed} confirmed Content Bug(s); {result.NewlyAwarded.Count} new, +{result.NewlyAwarded.Count * XpAwards.Points(XpKind.ContentBug)} XP."));
        if (result.Resumed.Count > 0)
        {
            host.Renderer.Line("Resumed review cards: " + string.Join(", ", result.Resumed) + ".");
        }

        return ExitCodes.Ok;
    }
}

/// <summary>Options for <c>portfolio</c>.</summary>
public sealed class PortfolioSettings : EngineSettings
{
    /// <summary>A Deliverable to include.</summary>
    [CommandOption("--include <ID>")]
    [Description("Copy one of your submitted Deliverables into portfolio/ as well.")]
    public string? Include { get; init; }
}

/// <summary>
/// <c>ascent portfolio [--include &lt;id&gt;]</c>: writes <c>portfolio/PORTFOLIO.md</c> for your fork: Rank, badges,
/// Domains cleared, Exam Ready and the Deliverables you chose. Never Teach-backs, scores or Sealed content (PORT-01..03).
/// </summary>
public sealed class PortfolioCommand(EngineHost host) : Command<PortfolioSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, PortfolioSettings settings, CancellationToken cancellationToken)
    {
        var services = host.Services;
        var store = new Storage.DeliverableStore(services.Database);
        if (settings.Include is { } id)
        {
            var template = services.DeliverableCatalog.Template(id);
            if (!template.PortfolioEligible)
            {
                throw new UsageException("'" + id + "' isn't eligible for the portfolio.", "Only templates marked portfolio-eligible can be published; they collect no personal data.");
            }

            var record = store.Find(id);
            if (record?.SubmittedUtc is null)
            {
                throw new UsageException("'" + id + "' hasn't been submitted yet.", "Submit it with 'ascent deliver " + id + "' first.");
            }

            var copied = services.Portfolio.Include(id, services.Deliverables.WorkPath(id));
            store.Save(record with { Published = true });
            host.Renderer.Status(Outcome.Pass, "Copied to " + Path.GetRelativePath(host.Paths.RepoRoot, copied) + ".");
        }

        var best = services.Bosses.BestScores();
        var (rank, _, _, _) = services.EvaluateRank(best);
        var cleared = services.Quests.DomainsComplete().Where(d => best.GetValueOrDefault(d) >= 70).ToList();
        var (badges, _) = Badges.Compute(new BadgeFacts(services.Ledger.Events, best, cleared, services.WeekGoal.LongestStreak(), services.Simulations.ExamReady()));
        var published = store.All().Where(r => r.Published)
            .Select(r => new PortfolioDeliverable(r.DeliverableId, services.DeliverableCatalog.Templates.FirstOrDefault(t => t.Id == r.DeliverableId)?.Title ?? r.DeliverableId))
            .ToList();
        var path = services.Portfolio.Write(new PortfolioFacts(
            RankCalculator.DisplayName(rank),
            badges.Select(Badges.Name).ToList(),
            cleared,
            services.Simulations.ExamReady(),
            published,
            services.Calendar.Today));
        host.Renderer.Status(Outcome.Pass, "Wrote " + Path.GetRelativePath(host.Paths.RepoRoot, path) + ". Read it through, then commit it to your fork if you'd like to share it.");
        return ExitCodes.Ok;
    }
}

/// <summary>Opens URLs in the default browser, for <c>bug</c>. Failures are ignored: the URL is printed anyway.</summary>
internal static class Browser
{
    /// <summary>Opens a GitHub URL.</summary>
    public static void Open(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com")
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // No browser is available; the printed URL is enough.
        }
    }
}
