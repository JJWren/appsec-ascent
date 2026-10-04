using System.Text;
using System.Text.Json.Nodes;
using Ascent.Core.Domain;
using Ascent.Core.Progress;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Sealing;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.KeyRelease;
using Microsoft.Extensions.Time.Testing;

namespace Ascent.Engine.Tests.Sealing;

/// <summary>The verify-then-decrypt pipeline (P1) and its single refusal message (SEC-U2-02).</summary>
public sealed class SealedStoreTests
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    [Trait("Rule", "SEAL-05")]
    public void A_released_item_opens_and_the_release_is_recorded_once()
    {
        using var bundles = new TestBundles();
        bundles.Write("lab-d5-01.tests", "lab-tests", SealTier.Earned, "the tests"u8.ToArray());
        var facts = new FakeFacts { Labs = { ["lab-d5-01"] = LabStage.FlagCaptured } };
        var log = new FakeReleaseLog();
        var store = Store(bundles, facts, log);

        using (var item = store.Open("lab-d5-01.tests", ReleaseContext.FlagVerified("lab-d5-01")))
        {
            item.Text().ShouldBe("the tests");
            item.Header.ItemType.ShouldBe("lab-tests");
        }

        using (store.Open("lab-d5-01.tests", ReleaseContext.FlagVerified("lab-d5-01")))
        {
        }

        log.Records.ShouldBe([("lab-d5-01.tests", SealTier.Earned, ReleaseReason.FlagVerified)]);
        store.VerifySignature("lab-d5-01.tests").ShouldBeTrue();
    }

    [Fact]
    public void Disposing_an_item_clears_its_plaintext()
    {
        using var bundles = new TestBundles();
        bundles.Write("dlv-d3-01.reference", "reference", SealTier.Submitted, "reference answer"u8.ToArray());
        var store = Store(bundles, new FakeFacts { Submitted = { "dlv-d3-01" } }, new FakeReleaseLog());
        var item = store.Open("dlv-d3-01.reference", ReleaseContext.WorkSubmitted("dlv-d3-01"));

        item.Dispose();

        item.Content.ToArray().ShouldAllBe(b => b == 0);
    }

    [Fact]
    public void A_missing_item_is_unavailable() => AssertUnavailable(SealedFailure.Missing, (bundles, _) => "nothing-here");

    [Fact]
    [Trait("Rule", "SEAL-01")]
    public void An_unsigned_item_is_unavailable() => AssertUnavailable(SealedFailure.NoSignature, (bundles, plaintext) =>
        Id(bundles.Write("qb-1.1-001", "question", SealTier.Practice, plaintext, pool: "practice", sign: false)));

    [Fact]
    [Trait("Rule", "SEAL-01")]
    public void An_item_signed_by_another_key_is_unavailable() => AssertUnavailable(SealedFailure.BadSignature, (bundles, plaintext) =>
    {
        using var impostor = new TestBundles();
        var path = impostor.Write("qb-1.1-001", "question", SealTier.Practice, plaintext, pool: "practice");
        var target = bundles.Repo.Combine("sealed", "qb-1.1-001.bundle.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(path, target);
        bundles.Paths["qb-1.1-001"] = target;
        return "qb-1.1-001";
    });

    [Fact]
    [Trait("Rule", "SEAL-01")]
    public void A_header_edited_after_signing_is_unavailable() => AssertUnavailable(SealedFailure.BadSignature, (bundles, plaintext) =>
    {
        var path = bundles.Write("qb-1.1-001", "question", SealTier.Practice, plaintext, pool: "practice");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"pool\": \"practice\"", "\"pool\": \"diagnostic\"", StringComparison.Ordinal));
        return "qb-1.1-001";
    });

    [Fact]
    public void An_item_the_claim_does_not_release_is_unavailable() => AssertUnavailable(SealedFailure.NotReleased, (bundles, plaintext) =>
        Id(bundles.Write("qb-9.9-001", "question", SealTier.Simulation, plaintext, pool: "simulation")));

    [Fact]
    [Trait("Rule", "SEAL-02")]
    public void A_wrong_key_derivation_label_is_unavailable() => AssertUnavailable(SealedFailure.WrongKdfInfo, (bundles, plaintext) =>
    {
        var path = bundles.Write("qb-1.1-001", "question", SealTier.Practice, plaintext, pool: "practice", sign: false);
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json["header"]!["kdfInfo"] = "item:qb-1.1-002";
        File.WriteAllText(path, json.ToJsonString());
        bundles.Sign(SealedBundle.Read(path)).Write(path);
        return "qb-1.1-001";
    });

    [Fact]
    [Trait("Rule", "SEAL-02")]
    public void Ciphertext_altered_before_signing_is_unavailable() => AssertUnavailable(SealedFailure.TagMismatch, (bundles, plaintext) =>
        Id(bundles.Write("qb-1.1-001", "question", SealTier.Practice, plaintext, pool: "practice", tamper: bundle =>
        {
            var altered = (byte[])bundle.Ciphertext.Clone();
            altered[0] ^= 0xff;
            return bundle with { Ciphertext = altered };
        })));

    [Fact]
    public void A_file_holding_another_item_is_unavailable() => AssertUnavailable(SealedFailure.Malformed, (bundles, plaintext) =>
        Id(bundles.Write("qb-1.1-002", "question", SealTier.Practice, plaintext, pool: "practice", fileItemId: "qb-1.1-001")));

    [Fact]
    public void A_corrupt_file_is_unavailable() => AssertUnavailable(SealedFailure.Malformed, (bundles, _) =>
    {
        var path = bundles.Repo.WriteFile("sealed/qb-1.1-001.bundle.json", "{ not json");
        bundles.Paths["qb-1.1-001"] = path;
        return "qb-1.1-001";
    });

    [Fact]
    [Trait("Rule", "SEAL-01")]
    public void Unavailable_items_have_one_message_and_a_next_step()
    {
        var error = new SealedItemUnavailableException("qb-1.1-001\u001b[31m", SealedFailure.BadSignature);
        error.Message.ShouldBe("The Sealed item 'qb-1.1-001' is unavailable.");
        error.Failure.ShouldBe(SealedFailure.BadSignature);
        error.NextStep!.ShouldContain("ascent doctor");
        new SealedItemUnavailableException().Failure.ShouldBeNull();
        new SealedItemUnavailableException("m").Message.ShouldBe("m");
        new SealedItemUnavailableException("m", new InvalidOperationException()).InnerException.ShouldNotBeNull();
    }

    private static string Id(string path) => Path.GetFileName(path)[..^SealedBundle.FileSuffix.Length];

    private void AssertUnavailable(SealedFailure expected, Func<TestBundles, byte[], string> arrange)
    {
        using var bundles = new TestBundles();
        var itemId = arrange(bundles, Encoding.UTF8.GetBytes("{\"stem\":\"secret question\"}"));
        var log = new FakeReleaseLog();
        var store = Store(bundles, new FakeFacts(), log);

        var error = Should.Throw<SealedItemUnavailableException>(() => store.Open(itemId, ReleaseContext.Served(ServeContext.StandUp)));

        error.Failure.ShouldBe(expected);
        error.Message.ShouldBe("The Sealed item '" + itemId + "' is unavailable.");
        error.Message.ShouldNotContain("secret");
        log.Records.ShouldBeEmpty();
    }

    private SealedStore Store(TestBundles bundles, IReleaseFacts facts, IKeyReleaseLog log) =>
        new(bundles.Locate, bundles.Verifier, bundles.Keys, new KeyReleasePolicy(facts), log, clock);

    internal sealed class FakeReleaseLog : IKeyReleaseLog
    {
        public List<(string ItemId, SealTier Tier, ReleaseReason Reason)> Records { get; } = [];

        public void Record(string itemId, SealTier tier, ReleaseReason reason, DateTimeOffset releasedAt)
        {
            if (!IsReleased(itemId, tier))
            {
                Records.Add((itemId, tier, reason));
            }
        }

        public bool IsReleased(string itemId, SealTier tier) => Records.Any(r => r.ItemId == itemId && r.Tier == tier);
    }
}

/// <summary>Configurable progress facts for policy tests.</summary>
internal sealed class FakeFacts : IReleaseFacts
{
    public bool Accepted { get; set; } = true;

    public Dictionary<string, LabStage> Labs { get; } = [];

    public HashSet<string> Submitted { get; } = [];

    public HashSet<(long Attempt, string Item)> Simulation { get; } = [];

    public HashSet<string> Unlocked { get; } = [];

    public bool RulesAccepted => Accepted;

    public LabStage LabStage(string labId) => Labs.GetValueOrDefault(labId, Core.Domain.LabStage.NotStarted);

    public bool WorkSubmitted(string workId) => Submitted.Contains(workId);

    public bool SimulationIncludes(long attemptId, string itemId) => Simulation.Contains((attemptId, itemId));

    public bool ReleaseUnlocked(string examDomain) => Unlocked.Contains(examDomain);
}
