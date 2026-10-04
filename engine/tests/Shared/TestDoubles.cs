using System.Net;
using System.Text;
using Ascent.Core.Domain;
using Ascent.Labs;

namespace Ascent.Tests.Shared;

/// <summary>An HTTP transport for tests: it records every request with its body and answers from <see cref="Respond"/>.</summary>
internal sealed class RecordingHttpHandler : HttpMessageHandler
{
    /// <summary>Requests received: method, address and body.</summary>
    public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = [];

    /// <summary>Answers a request; null means 200 with an empty JSON object.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Respond { get; set; }

    /// <summary>A JSON response.</summary>
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!, body));
        return Respond?.Invoke(request) ?? Json("{}");
    }
}

/// <summary>The Local Stage for tests: nothing starts, and every call is recorded.</summary>
internal sealed class FixtureOrchestrator : IOrchestrator
{
    /// <summary>Labs brought up, in order.</summary>
    public List<string> Up { get; } = [];

    /// <summary>Labs brought down, in order.</summary>
    public List<string> Down { get; } = [];

    /// <inheritdoc />
    public Task<StageResult> UpAsync(StageRequest request, CancellationToken cancellationToken)
    {
        Up.Add(request.Lab.Id);
        return Task.FromResult(new StageResult(true, "Fixture stage up."));
    }

    /// <inheritdoc />
    public Task<StageResult> DownAsync(StageRequest request, CancellationToken cancellationToken)
    {
        Down.Add(request.Lab.Id);
        return Task.FromResult(new StageResult(false, "Fixture stage down."));
    }
}

/// <summary>
/// The hello-lab fixture (P28): a Quest with one Lab whose module, plant spec, tests and fix come from
/// <c>engine/tests/fixtures/hello-lab/</c>, sealed and signed at test time.
/// </summary>
internal static class HelloLab
{
    /// <summary>The Lab's ID.</summary>
    public const string LabId = "lab-hello";

    /// <summary>The Quest's ID.</summary>
    public const string QuestId = "q-1.1";

    /// <summary>The fixture's source folder.</summary>
    public static string Source => Path.Join(CurriculumFixture.RealRoot, "engine", "tests", "fixtures", "hello-lab");

    /// <summary>Where the plant spec puts the Flag, relative to the repository.</summary>
    public static string FlagFile(string root) => Path.Join(root, "my-work", "throughline", ".flags", "hello.txt");

    /// <summary>The Learner's copy of the vulnerable code.</summary>
    public static string Greeter(string root) => Path.Join(root, "my-work", "throughline", "hello", "Greeter.cs");

    /// <summary>A repository with the hello-lab Quest and Lab, sealed with the fixture's key.</summary>
    public static CurriculumFixture Create()
    {
        var fixture = CurriculumFixture.Create()
            .Quest(QuestId, "1.1", "D1", labs: [LabId])
            .Lab(LabId, "1.1")
            .Write("labs/" + LabId + "/BRIEF.md", "---\nlabId: " + LabId + "\nobjectiveId: \"1.1\"\nstages: [local]\nestimatedCost: \"$0\"\n---\n# Lab: hello\n");
        fixture.SealFolder(LabId + ".module", "lab-module", SealTier.Start, Path.Join(Source, "module"));
        fixture.Seal(LabId + ".plant", "lab-plant", SealTier.Start, File.ReadAllBytes(Path.Join(Source, "plant.json")));
        fixture.SealFolder(LabId + ".tests", "lab-tests", SealTier.Earned, Path.Join(Source, "tests"));
        fixture.SealFolder(LabId + ".fix", "lab-fix", SealTier.Earned, Path.Join(Source, "fix"));
        return fixture;
    }

    /// <summary>Applies the reference fix to the Learner's workspace, as a Learner would by hand.</summary>
    public static void ApplyFix(string root) => File.Copy(Path.Join(Source, "fix", "hello", "Greeter.cs"), Greeter(root), overwrite: true);
}
