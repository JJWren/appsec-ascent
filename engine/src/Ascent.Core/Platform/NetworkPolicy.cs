using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using Ascent.Core.Errors;

namespace Ascent.Core.Platform;

/// <summary>Why the Engine is making a network call. Every request must declare one (P13).</summary>
public enum NetworkPurpose
{
    /// <summary><c>ascent sync</c>: public Content Bug issues on GitHub.</summary>
    ContentBugSync,

    /// <summary>A cost-estimate refresh from the Azure Retail Prices API.</summary>
    PriceRefresh,

    /// <summary>The optional AI reviewer at the configured endpoint.</summary>
    AiReviewer,
}

/// <summary>
/// The Engine's outbound allowlist (SEC-U2-07 as amended, PRV-01): each purpose may reach one destination, over HTTPS,
/// except an AI endpoint on this machine, which may use HTTP.
/// </summary>
public sealed class NetworkPolicy
{
    /// <summary>GitHub's REST API host.</summary>
    public const string GitHubApiHost = "api.github.com";

    /// <summary>The Azure Retail Prices API host.</summary>
    public const string RetailPricesHost = "prices.azure.com";

    /// <summary>The request option that carries a request's <see cref="NetworkPurpose"/>.</summary>
    public static readonly HttpRequestOptionsKey<NetworkPurpose> PurposeKey = new("ascent.network-purpose");

    /// <summary>Creates the policy, optionally with the configured AI endpoint.</summary>
    public NetworkPolicy(Uri? aiEndpoint = null) => AiEndpoint = aiEndpoint;

    /// <summary>The configured AI endpoint, if any.</summary>
    public Uri? AiEndpoint { get; }

    /// <summary>Returns why a request is refused, or null when it is allowed.</summary>
    public string? Check(Uri uri, NetworkPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            return "the address is not absolute";
        }

        return purpose switch
        {
            NetworkPurpose.ContentBugSync => HttpsTo(uri, GitHubApiHost),
            NetworkPurpose.PriceRefresh => HttpsTo(uri, RetailPricesHost),
            NetworkPurpose.AiReviewer => CheckAiEndpoint(uri),
            _ => "the purpose is unknown",
        };
    }

    /// <summary>Throws <see cref="NetworkPolicyException"/> when a request is refused.</summary>
    public void EnsureAllowed(Uri uri, NetworkPurpose purpose)
    {
        var reason = Check(uri, purpose);
        if (reason is not null)
        {
            throw new NetworkPolicyException(uri.IsAbsoluteUri ? uri.Host : "(relative address)", reason);
        }
    }

    private static string? HttpsTo(Uri uri, string host)
    {
        if (!string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase))
        {
            return "only " + host + " is allowed for this purpose";
        }

        return uri.Scheme == Uri.UriSchemeHttps ? null : "HTTPS is required";
    }

    private string? CheckAiEndpoint(Uri uri)
    {
        if (AiEndpoint is null)
        {
            return "no AI endpoint is configured";
        }

        var sameOrigin = string.Equals(uri.Scheme, AiEndpoint.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, AiEndpoint.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == AiEndpoint.Port;
        if (!sameOrigin)
        {
            return "it is not the configured AI endpoint";
        }

        if (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            return null;
        }

        return "plain HTTP is allowed only for an AI endpoint on this machine";
    }
}

/// <summary>A request was refused by the outbound allowlist.</summary>
public sealed class NetworkPolicyException : AscentException
{
    private const string Hint = "The Engine only contacts GitHub (sync), Azure Retail Prices (estimates) and your configured AI endpoint.";

    /// <summary>Creates the error with a default message.</summary>
    public NetworkPolicyException()
        : this("A network request was refused by the Engine's allowlist.")
    {
    }

    /// <summary>Creates the error with a message.</summary>
    public NetworkPolicyException(string message)
        : base(message, Hint)
    {
    }

    /// <summary>Creates the error with a message and a cause.</summary>
    public NetworkPolicyException(string message, Exception innerException)
        : base(message, Hint, ExitCodes.CheckFailed, innerException)
    {
    }

    /// <summary>Creates the error for a refused host.</summary>
    public NetworkPolicyException(string host, string reason)
        : base("Refused a network request to '" + SafeText.Sanitize(host, 200) + "': " + reason + ".", Hint)
    {
    }
}

/// <summary>Creates the Engine's single <see cref="HttpClient"/> behind the allowlist (P13).</summary>
public static class EngineHttp
{
    /// <summary>The connect timeout.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The total timeout for GitHub and Retail Prices requests.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Creates a client whose every request is checked against <paramref name="policy"/>.</summary>
    /// <param name="policy">The allowlist.</param>
    /// <param name="version">The Engine version, sent in the User-Agent header.</param>
    /// <param name="transport">The transport; tests pass a recording handler. Defaults to a hardened <see cref="SocketsHttpHandler"/>.</param>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The returned HttpClient owns both handlers (disposeHandler: true).")]
    public static HttpClient Create(NetworkPolicy policy, string version, HttpMessageHandler? transport = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var inner = transport ?? new SocketsHttpHandler
        {
            ConnectTimeout = ConnectTimeout,
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        var client = new HttpClient(new PolicyHandler(policy, inner), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AppSecAscent", version));
        return client;
    }

    /// <summary>Creates a request that declares its purpose.</summary>
    public static HttpRequestMessage Request(HttpMethod method, Uri uri, NetworkPurpose purpose)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Options.Set(NetworkPolicy.PurposeKey, purpose);
        return request;
    }

    private sealed class PolicyHandler(NetworkPolicy policy, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null || !request.Options.TryGetValue(NetworkPolicy.PurposeKey, out var purpose))
            {
                throw new NetworkPolicyException(request.RequestUri?.Host ?? "(no address)", "the request did not declare a purpose");
            }

            policy.EnsureAllowed(request.RequestUri, purpose);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
