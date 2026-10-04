using Ascent.Core.Domain;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Release;

namespace Ascent.Engine.Tests.Sealing;

/// <summary>SEAL-03: each tier opens only for its own kind of claim, and only when progress backs the claim.</summary>
public sealed class KeyReleasePolicyTests
{
    [Fact]
    [Trait("Rule", "SEAL-03")]
    public void Start_tier_needs_accepted_rules_and_a_started_lab()
    {
        var facts = new FakeFacts { Accepted = false, Labs = { ["lab-d5-01"] = LabStage.Started } };
        var policy = new KeyReleasePolicy(facts);
        var module = Header("lab-d5-01.module", "lab-module", SealTier.Start);

        policy.Check(module, ReleaseContext.LabStarted("lab-d5-01"))!.ShouldContain("rules of engagement");
        facts.Accepted = true;
        policy.Check(module, ReleaseContext.LabStarted("lab-d5-01")).ShouldBeNull();
        policy.Check(module, ReleaseContext.LabStarted("lab-d5-02"))!.ShouldContain("another Lab");
        policy.Check(Header("lab-d5-03.module", "lab-module", SealTier.Start), ReleaseContext.LabStarted("lab-d5-03"))!.ShouldContain("hasn't been started");
    }

    [Fact]
    [Trait("Rule", "SEAL-03")]
    public void Earned_tier_needs_a_verified_flag()
    {
        var facts = new FakeFacts { Labs = { ["lab-d5-01"] = LabStage.Started } };
        var policy = new KeyReleasePolicy(facts);
        var tests = Header("lab-d5-01.tests", "lab-tests", SealTier.Earned);

        policy.Check(tests, ReleaseContext.FlagVerified("lab-d5-01"))!.ShouldContain("Flag hasn't been verified");
        facts.Labs["lab-d5-01"] = LabStage.FlagCaptured;
        policy.Check(tests, ReleaseContext.FlagVerified("lab-d5-01")).ShouldBeNull();
        facts.Labs["lab-d5-01"] = LabStage.Explained;
        policy.Check(tests, ReleaseContext.FlagVerified("lab-d5-01")).ShouldBeNull();
        policy.Check(tests, ReleaseContext.FlagVerified("lab-d5-0"))!.ShouldContain("another Lab");
        policy.Check(tests, ReleaseContext.LabStarted("lab-d5-01"))!.ShouldContain("doesn't release the earned tier");
    }

    [Fact]
    [Trait("Rule", "SEAL-03")]
    public void Submitted_tier_needs_submitted_work()
    {
        var facts = new FakeFacts();
        var policy = new KeyReleasePolicy(facts);
        var reference = Header("dlv-d3-01.reference", "reference", SealTier.Submitted);

        policy.Check(reference, ReleaseContext.WorkSubmitted("dlv-d3-01"))!.ShouldContain("hasn't been submitted");
        facts.Submitted.Add("dlv-d3-01");
        policy.Check(reference, ReleaseContext.WorkSubmitted("dlv-d3-01")).ShouldBeNull();
        policy.Check(reference, ReleaseContext.WorkSubmitted("dlv-d3-02"))!.ShouldContain("other work");
    }

    [Theory]
    [Trait("Rule", "SEAL-03")]
    [InlineData(ServeContext.StandUp, "practice", true)]
    [InlineData(ServeContext.Quest, "practice", true)]
    [InlineData(ServeContext.BossFight, "practice", true)]
    [InlineData(ServeContext.Diagnostic, "diagnostic", true)]
    [InlineData(ServeContext.Diagnostic, "practice", false)]
    [InlineData(ServeContext.StandUp, "diagnostic", false)]
    [InlineData(ServeContext.BossFight, null, false)]
    public void Practice_tier_opens_only_in_the_right_serving_context(ServeContext context, string? pool, bool allowed)
    {
        var policy = new KeyReleasePolicy(new FakeFacts());
        var question = Header("qb-1.1-001", "question", SealTier.Practice, pool);

        (policy.Check(question, ReleaseContext.Served(context)) is null).ShouldBe(allowed);
    }

    [Fact]
    [Trait("Rule", "SEAL-03")]
    public void A_served_claim_without_a_context_is_refused() =>
        new KeyReleasePolicy(new FakeFacts())
            .Check(Header("qb-1.1-001", "question", SealTier.Practice, "practice"), new ReleaseContext(ReleaseReason.Served, "StandUp"))!
            .ShouldContain("serving context is missing");

    [Fact]
    [Trait("Rule", "SIM-06")]
    public void Simulation_tier_opens_only_inside_its_active_attempt()
    {
        var facts = new FakeFacts { Simulation = { (7, "qb-1.1-900") } };
        var policy = new KeyReleasePolicy(facts);
        var item = Header("qb-1.1-900", "question", SealTier.Simulation, "simulation");

        policy.Check(item, ReleaseContext.Simulation(7)).ShouldBeNull();
        policy.Check(item, ReleaseContext.Simulation(8))!.ShouldContain("active Simulation");
        policy.Check(item, ReleaseContext.Served(ServeContext.StandUp))!.ShouldContain("doesn't release");
        policy.Check(item, new ReleaseContext(ReleaseReason.SimulationStarted, "7"))!.ShouldContain("active Simulation");
    }

    [Fact]
    [Trait("Rule", "SEAL-03")]
    public void Release_tier_opens_for_the_unlocked_domain_only()
    {
        var facts = new FakeFacts();
        var policy = new KeyReleasePolicy(facts);
        var release = Header("release-3", "release", SealTier.Release);

        policy.Check(release, ReleaseContext.ReleaseUnlocked("D3"))!.ShouldContain("hasn't been unlocked");
        facts.Unlocked.Add("D3");
        policy.Check(release, ReleaseContext.ReleaseUnlocked("D3")).ShouldBeNull();
        policy.Check(release, ReleaseContext.ReleaseUnlocked("D4"))!.ShouldContain("isn't the Release");
        KeyReleasePolicy.ReleaseItemFor("D8").ShouldBe("release-8");
        KeyReleasePolicy.ReleaseItemFor("CAP").ShouldBe("release-CAP");
    }

    [Fact]
    public void Release_contexts_carry_their_subjects()
    {
        ReleaseContext.Simulation(42).ShouldBe(new ReleaseContext(ReleaseReason.SimulationStarted, "42", AttemptId: 42));
        ReleaseContext.Served(ServeContext.Quest).Serving.ShouldBe(ServeContext.Quest);
        SealTiers.TryParse("unknown", out _).ShouldBeFalse();
        foreach (var tier in Enum.GetValues<SealTier>())
        {
            SealTiers.TryParse(SealTiers.Name(tier), out var parsed).ShouldBeTrue();
            parsed.ShouldBe(tier);
        }

        Should.Throw<ArgumentOutOfRangeException>(() => SealTiers.Name((SealTier)99));
    }

    private static BundleHeader Header(string itemId, string itemType, SealTier tier, string? pool = null) =>
        BundleHeader.Create(itemId, itemType, tier, "text/plain", new byte[12], DateTimeOffset.UnixEpoch, pool: pool);
}
