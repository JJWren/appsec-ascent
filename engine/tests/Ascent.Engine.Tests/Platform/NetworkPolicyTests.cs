using System.Net;
using Ascent.Core.Platform;

namespace Ascent.Engine.Tests.Platform;

public sealed class NetworkPolicyTests
{
    [Theory]
    [InlineData("https://api.github.com/search/issues?q=x", NetworkPurpose.ContentBugSync)]
    [InlineData("https://API.GITHUB.COM/repos/x", NetworkPurpose.ContentBugSync)]
    [InlineData("https://prices.azure.com/api/retail/prices", NetworkPurpose.PriceRefresh)]
    [InlineData("http://localhost:11434/v1/chat/completions", NetworkPurpose.AiReviewer)]
    public void Allowed_requests_pass(string url, NetworkPurpose purpose) =>
        new NetworkPolicy(new Uri("http://localhost:11434/v1")).Check(new Uri(url), purpose).ShouldBeNull();

    [Theory]
    [InlineData("http://api.github.com/search/issues", NetworkPurpose.ContentBugSync, "HTTPS")]
    [InlineData("https://evil.example/search/issues", NetworkPurpose.ContentBugSync, "only api.github.com")]
    [InlineData("https://api.github.com.evil.example/x", NetworkPurpose.ContentBugSync, "only api.github.com")]
    [InlineData("https://api.github.com/x", NetworkPurpose.PriceRefresh, "only prices.azure.com")]
    [InlineData("http://localhost:9999/v1/chat/completions", NetworkPurpose.AiReviewer, "not the configured")]
    [InlineData("https://api.github.com/x", NetworkPurpose.AiReviewer, "not the configured")]
    [InlineData("https://api.github.com/x", (NetworkPurpose)42, "unknown")]
    public void Other_requests_are_refused(string url, NetworkPurpose purpose, string reason) =>
        new NetworkPolicy(new Uri("http://localhost:11434/v1")).Check(new Uri(url), purpose)!.ShouldContain(reason);

    [Fact]
    public void Plain_http_is_allowed_only_for_a_loopback_ai_endpoint()
    {
        new NetworkPolicy(new Uri("http://127.0.0.1:5272/")).Check(new Uri("http://127.0.0.1:5272/v1/chat/completions"), NetworkPurpose.AiReviewer).ShouldBeNull();
        new NetworkPolicy(new Uri("http://[::1]:5272/")).Check(new Uri("http://[::1]:5272/v1/models"), NetworkPurpose.AiReviewer).ShouldBeNull();
        new NetworkPolicy(new Uri("http://ai.example.com/")).Check(new Uri("http://ai.example.com/v1/chat/completions"), NetworkPurpose.AiReviewer)!
            .ShouldContain("only for an AI endpoint on this machine");
        new NetworkPolicy(new Uri("https://ai.example.com/")).Check(new Uri("https://ai.example.com/v1/chat/completions"), NetworkPurpose.AiReviewer).ShouldBeNull();
    }

    [Fact]
    public void Without_an_ai_endpoint_the_reviewer_cannot_call_out() =>
        new NetworkPolicy().Check(new Uri("http://localhost:11434/v1"), NetworkPurpose.AiReviewer)!.ShouldContain("no AI endpoint");

    [Fact]
    public void Relative_addresses_are_refused() =>
        new NetworkPolicy().Check(new Uri("/relative", UriKind.Relative), NetworkPurpose.ContentBugSync)!.ShouldContain("not absolute");

    [Fact]
    public async Task The_client_enforces_the_policy_before_any_request_leaves()
    {
        using var recorder = new RecordingHandler();
        using var client = EngineHttp.Create(new NetworkPolicy(), "1.2.3", recorder);

        using (var allowed = EngineHttp.Request(HttpMethod.Get, new Uri("https://api.github.com/x"), NetworkPurpose.ContentBugSync))
        {
            using var response = await client.SendAsync(allowed, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var refused = EngineHttp.Request(HttpMethod.Get, new Uri("https://evil.example/x"), NetworkPurpose.ContentBugSync))
        {
            var error = await Should.ThrowAsync<NetworkPolicyException>(() => client.SendAsync(refused, TestContext.Current.CancellationToken));
            error.Message.ShouldContain("evil.example");
        }

        using (var undeclared = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.github.com/x")))
        {
            var error = await Should.ThrowAsync<NetworkPolicyException>(() => client.SendAsync(undeclared, TestContext.Current.CancellationToken));
            error.Message.ShouldContain("did not declare a purpose");
        }

        recorder.Requests.Count.ShouldBe(1);
        recorder.Requests[0].Headers.UserAgent.ToString().ShouldBe("AppSecAscent/1.2.3");
    }

    [Fact]
    public void The_default_transport_never_follows_redirects()
    {
        using var client = EngineHttp.Create(new NetworkPolicy(), "1.0.0");
        client.Timeout.ShouldBe(Timeout.InfiniteTimeSpan);
        EngineHttp.ConnectTimeout.ShouldBe(TimeSpan.FromSeconds(10));
        EngineHttp.RequestTimeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Exceptions_have_standard_constructors()
    {
        new NetworkPolicyException().NextStep.ShouldNotBeNull();
        new NetworkPolicyException("m").Message.ShouldBe("m");
        new NetworkPolicyException("m", new InvalidOperationException()).InnerException.ShouldNotBeNull();
        new NetworkPolicyException("host\u001b[31m", "why").Message.ShouldNotContain("\u001b");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
