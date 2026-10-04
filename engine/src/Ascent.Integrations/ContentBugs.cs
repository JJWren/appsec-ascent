using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Progress;

namespace Ascent.Integrations;

/// <summary>The public repository that Content Bugs are filed against.</summary>
public static class Upstream
{
    /// <summary>The owner.</summary>
    public const string Owner = "JJWren";

    /// <summary>The repository.</summary>
    public const string Repository = "appsec-ascent";

    /// <summary>The label maintainers add when they confirm a Content Bug.</summary>
    public const string ConfirmedLabel = "content-bug:confirmed";
}

/// <summary>
/// Builds the new-issue URL for a Content Bug (BUG-01): the repository's issue form, prefilled with only the item ID and
/// the citation ID. Nothing personal is ever in it.
/// </summary>
public static partial class IssueUrlBuilder
{
    /// <summary>The URL for a Content Bug about <paramref name="itemId"/>.</summary>
    public static Uri Build(string itemId, string? citationId)
    {
        if (!ItemIdPattern().IsMatch(itemId ?? string.Empty))
        {
            throw new UsageException("'" + SafeText.Sanitize(itemId ?? string.Empty, 60) + "' isn't an item ID.", "Use the ID the Engine shows, such as q-4.4, lab-d5-01 or qb-5.1-007.");
        }

        if (citationId is not null && !CitationPattern().IsMatch(citationId))
        {
            throw new UsageException("'" + SafeText.Sanitize(citationId, 60) + "' isn't a citation ID.", "Citation IDs look like c2.");
        }

        var query = "template=content-bug.yml"
            + "&title=" + Uri.EscapeDataString("[Content Bug] " + itemId + ": ")
            + "&item-id=" + Uri.EscapeDataString(itemId!)
            + (citationId is null ? string.Empty : "&citation-id=" + Uri.EscapeDataString(citationId));
        return new Uri("https://github.com/" + Upstream.Owner + "/" + Upstream.Repository + "/issues/new?" + query);
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ItemIdPattern();

    [GeneratedRegex("^c[0-9]{1,3}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CitationPattern();
}

/// <summary>A confirmed Content Bug found on GitHub.</summary>
/// <param name="Number">The issue number.</param>
/// <param name="ItemId">The item it reports, from the issue form.</param>
/// <param name="Closed">True once the issue is closed: the bug is resolved.</param>
public sealed record ConfirmedIssue(int Number, string ItemId, bool Closed);

/// <summary>
/// Reads a user's confirmed Content Bugs from the public GitHub API without authentication (BUG-02, P13). The only
/// identifying value sent is the configured GitHub user name.
/// </summary>
public sealed partial class GitHubIssuesClient
{
    private readonly HttpClient http;
    private readonly TimeProvider time;

    /// <summary>Creates the client over the Engine's single, policy-checked <see cref="HttpClient"/>.</summary>
    public GitHubIssuesClient(HttpClient http, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(time);
        this.http = http;
        this.time = time;
    }

    /// <summary>The user's issues labelled confirmed, open or closed.</summary>
    public async Task<IReadOnlyList<ConfirmedIssue>> ConfirmedAsync(string githubUser, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(githubUser);
        var uri = new Uri("https://" + NetworkPolicy.GitHubApiHost + "/repos/" + Upstream.Owner + "/" + Upstream.Repository
            + "/issues?state=all&per_page=100&creator=" + Uri.EscapeDataString(githubUser) + "&labels=" + Uri.EscapeDataString(Upstream.ConfirmedLabel));
        var json = await GetAsync(uri, cancellationToken);
        return (json as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(issue => issue["pull_request"] is null)
            .Select(issue => (Number: Int(issue["number"]), State: Text(issue["state"]), Item: ItemFrom(Text(issue["body"]))))
            .Where(issue => issue.Number > 0 && issue.Item is not null)
            .Select(issue => new ConfirmedIssue(issue.Number, issue.Item!, issue.State == "closed"))
            .ToList();
    }

    // The issue form renders each field as "### Label" followed by the value.
    internal static string? ItemFrom(string? body)
    {
        if (body is null)
        {
            return null;
        }

        var match = ItemField().Match(body.ReplaceLineEndings("\n"));
        return match.Success ? SafeText.Sanitize(match.Groups["id"].Value, 128) : null;
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private async Task<JsonNode?> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = EngineHttp.Request(HttpMethod.Get, uri, NetworkPurpose.ContentBugSync);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(EngineHttp.RequestTimeout);
            try
            {
                using var response = await http.SendAsync(request, timeout.Token);
                if ((int)response.StatusCode >= 500 && attempt == 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), time, cancellationToken);
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new AscentException(
                        string.Create(CultureInfo.InvariantCulture, $"GitHub answered with HTTP {(int)response.StatusCode}."),
                        response.StatusCode == HttpStatusCode.Forbidden ? "GitHub limits unauthenticated requests; try again in an hour." : "Try again later.");
                }

                return JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), time, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AscentException("GitHub didn't answer in time.", "Try again later.");
            }
            catch (JsonException ex)
            {
                throw new AscentException("GitHub sent something unexpected.", "Try again later.", ExitCodes.CheckFailed, ex);
            }
        }
    }

    [GeneratedRegex(@"###\s*Item ID\s*\n+\s*(?<id>[A-Za-z0-9][A-Za-z0-9._-]{0,127})", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ItemField();
}

/// <summary>What <c>sync</c> did.</summary>
/// <param name="Confirmed">Confirmed bugs found.</param>
/// <param name="NewlyAwarded">Bugs that earned XP for the first time.</param>
/// <param name="Resumed">Items whose review cards were resumed because their bug is resolved (SU-05).</param>
public sealed record SyncResult(int Confirmed, IReadOnlyList<int> NewlyAwarded, IReadOnlyList<string> Resumed);

/// <summary>Content Bugs: reporting suspends an item's card (SU-05); syncing pays 25 XP once per confirmed bug (BUG-02) and resumes cards whose bug is closed.</summary>
public sealed class ContentBugService
{
    private readonly IContentBugStore bugs;
    private readonly IReviewCardStore cards;
    private readonly XpLedger ledger;
    private readonly IProgressTransactions transactions;
    private readonly TimeProvider time;

    /// <summary>Creates the service.</summary>
    public ContentBugService(IContentBugStore bugs, IReviewCardStore cards, XpLedger ledger, IProgressTransactions transactions, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(bugs);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(time);
        this.bugs = bugs;
        this.cards = cards;
        this.ledger = ledger;
        this.transactions = transactions;
        this.time = time;
    }

    /// <summary>Suspends the item's review card while its bug is open (SU-05). Returns true when there was a card.</summary>
    public bool Suspend(string itemId)
    {
        if (cards.Find(itemId) is null)
        {
            return false;
        }

        cards.SetSuspended(itemId, true);
        return true;
    }

    /// <summary>Records confirmed bugs, pays their XP once, and resumes the cards of resolved ones.</summary>
    public SyncResult Apply(IReadOnlyList<ConfirmedIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return transactions.Run(() =>
        {
            var now = time.GetUtcNow();
            var awarded = new List<int>();
            var resumed = new List<string>();
            foreach (var issue in issues)
            {
                bugs.Add(new ContentBugRecord(issue.Number, issue.ItemId, now));
                if (ledger.Award(XpKind.ContentBug, issue.Number.ToString(CultureInfo.InvariantCulture)))
                {
                    awarded.Add(issue.Number);
                }

                if (issue.Closed && cards.Find(issue.ItemId) is { Suspended: true })
                {
                    cards.SetSuspended(issue.ItemId, false);
                    resumed.Add(issue.ItemId);
                }
            }

            return new SyncResult(issues.Count, awarded, resumed);
        });
    }
}
