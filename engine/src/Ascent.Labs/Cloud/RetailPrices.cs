using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ascent.Core.Errors;
using Ascent.Core.Platform;

namespace Ascent.Labs.Cloud;

/// <summary>One priced quantity in a Cloud Stage's price sheet.</summary>
/// <param name="Filter">An Azure Retail Prices OData filter that selects one meter.</param>
/// <param name="Quantity">How many of the meter's units the Lab uses.</param>
public sealed record PriceItem(string Filter, decimal Quantity);

/// <summary>
/// A Cloud Stage's optional price sheet, <c>&lt;template&gt;.prices.json</c> next to its Bicep template, used when the
/// Learner asks for a refreshed estimate (CLD-02): <c>{ "items": [ { "filter": "...", "quantity": 730 } ] }</c>.
/// </summary>
public static class PriceSheet
{
    /// <summary>The sheet's path for a template.</summary>
    public static string PathFor(string template) => System.IO.Path.ChangeExtension(template, ".prices.json");

    /// <summary>Reads a sheet, or returns null when the Lab has none.</summary>
    public static IReadOnlyList<PriceItem>? Read(string template)
    {
        var path = PathFor(template);
        if (!File.Exists(path))
        {
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new AscentException("The Lab's price sheet couldn't be read.", "Use the manifest's estimate, and report it with 'ascent bug'.", ExitCodes.CheckFailed, ex);
        }

        var items = new List<PriceItem>();
        foreach (var item in (root?["items"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (item["filter"] is not JsonValue filterNode || !filterNode.TryGetValue<string>(out var filter)
                || item["quantity"] is not JsonValue quantityNode || !quantityNode.TryGetValue<decimal>(out var quantity))
            {
                throw new AscentException("The Lab's price sheet has an item without a filter and a quantity.", "Use the manifest's estimate, and report it with 'ascent bug'.");
            }

            items.Add(new PriceItem(filter, quantity));
        }

        return items;
    }
}

/// <summary>
/// The Azure Retail Prices API (P13): GET only, no authentication, HTTPS to <c>prices.azure.com</c>, a 30-second limit
/// and one retry after 2 seconds on a timeout or a server error.
/// </summary>
public sealed class RetailPricesClient
{
    /// <summary>The API's address.</summary>
    public static readonly Uri Endpoint = new("https://prices.azure.com/api/retail/prices");

    private readonly HttpClient http;
    private readonly TimeProvider time;

    /// <summary>Creates the client over the Engine's single, policy-checked <see cref="HttpClient"/>.</summary>
    public RetailPricesClient(HttpClient http, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(time);
        this.http = http;
        this.time = time;
    }

    /// <summary>Prices a sheet in US dollars.</summary>
    public async Task<decimal> EstimateAsync(IReadOnlyList<PriceItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        var total = 0m;
        foreach (var item in items)
        {
            total += await UnitPriceAsync(item.Filter, cancellationToken) * item.Quantity;
        }

        return decimal.Round(total, 2);
    }

    /// <summary>The retail unit price of the first consumption meter the filter selects.</summary>
    public async Task<decimal> UnitPriceAsync(string filter, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);
        var uri = new Uri(Endpoint + "?currencyCode=" + Uri.EscapeDataString("'USD'") + "&$filter=" + Uri.EscapeDataString(filter));
        for (var attempt = 1; ; attempt++)
        {
            using var request = EngineHttp.Request(HttpMethod.Get, uri, NetworkPurpose.PriceRefresh);
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
                    throw Failed(((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                }

                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                var price = (json?["Items"] as JsonArray ?? [])
                    .OfType<JsonObject>()
                    .Where(i => i["type"] is JsonValue type && type.TryGetValue<string>(out var kind) && kind == "Consumption")
                    .Select(i => i["retailPrice"] is JsonValue retail && retail.TryGetValue<decimal>(out var value) ? value : (decimal?)null)
                    .FirstOrDefault(p => p is not null);
                return price ?? throw new AscentException("No retail price matched one of the Lab's meters.", "Use the manifest's estimate, and report it with 'ascent bug'.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), time, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Failed("a timeout");
            }
            catch (JsonException ex)
            {
                throw new AscentException("The Retail Prices API sent something unexpected.", "Use the manifest's estimate instead.", ExitCodes.CheckFailed, ex);
            }
        }
    }

    private static AscentException Failed(string reason) =>
        new("The Retail Prices API didn't answer (" + reason + ").", "Use the manifest's estimate, or try again later.");
}
