using System.Security.Cryptography;
using System.Text;
using Ascent.Core.Progress;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Sealing.Flags;

namespace Ascent.Engine.Tests.Sealing;

/// <summary>FLAG-01..03 and P5.</summary>
public sealed class FlagServiceTests
{
    private readonly MutableClock clock = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly MemoryFlags store = new();

    [Fact]
    [Trait("Rule", "FLAG-01")]
    public void Flags_are_128_bit_base32_and_a_new_one_replaces_the_old()
    {
        var service = new FlagService(CryptoRandomSourceForTests(), clock, store);

        var first = service.Generate("lab-d5-01");
        var second = service.Generate("lab-d5-01");

        first.ShouldMatch("^ASCENT\\{[A-Z2-7]{26}\\}$");
        second.ShouldNotBe(first);
        service.Verify("lab-d5-01", first).Outcome.ShouldBe(FlagOutcome.Rejected);
        service.Verify("lab-d5-01", second).Outcome.ShouldBe(FlagOutcome.Accepted);
    }

    [Fact]
    [Trait("Rule", "FLAG-02")]
    public void Only_a_salted_hash_is_stored()
    {
        var flag = new FlagService(new SeededRandom(9), clock, store).Generate("lab-d5-01");
        var record = store.Find("lab-d5-01")!;

        record.Salt.Length.ShouldBe(16);
        record.Hash.ShouldBe(SHA256.HashData([.. record.Salt, .. Encoding.UTF8.GetBytes(flag)]));
        Encoding.UTF8.GetString(record.Hash).ShouldNotContain("ASCENT");
    }

    [Fact]
    [Trait("Rule", "FLAG-02")]
    public void Submissions_are_trimmed_but_otherwise_exact()
    {
        var service = new FlagService(new SeededRandom(10), clock, store);
        var flag = service.Generate("lab-d5-01");

        service.Verify("lab-d5-01", "  " + flag + "\n").Outcome.ShouldBe(FlagOutcome.Accepted);
        service.Verify("lab-d5-01", flag.ToLowerInvariant()).Outcome.ShouldBe(FlagOutcome.Rejected);
        service.Verify("lab-d5-02", flag).Outcome.ShouldBe(FlagOutcome.NoFlag);
    }

    [Fact]
    [Trait("Rule", "FLAG-03")]
    public void Five_wrong_attempts_in_ten_minutes_trigger_a_60_second_cooldown()
    {
        var service = new FlagService(new SeededRandom(11), clock, store);
        var flag = service.Generate("lab-d5-01");

        for (var i = 0; i < 5; i++)
        {
            service.Verify("lab-d5-01", "ASCENT{WRONG}").Outcome.ShouldBe(FlagOutcome.Rejected);
            clock.Now += TimeSpan.FromSeconds(30);
        }

        clock.Now -= TimeSpan.FromSeconds(30);
        var cooling = service.Verify("lab-d5-01", flag);
        cooling.Outcome.ShouldBe(FlagOutcome.CoolingDown);
        cooling.RetryAfter.ShouldBe(clock.Now + TimeSpan.FromSeconds(60));

        clock.Now += TimeSpan.FromSeconds(61);
        service.Verify("lab-d5-01", flag).Outcome.ShouldBe(FlagOutcome.Accepted);
        store.Find("lab-d5-01")!.WrongAttempts.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Rule", "FLAG-03")]
    public void Wrong_attempts_spread_over_more_than_ten_minutes_never_cool_down()
    {
        var service = new FlagService(new SeededRandom(12), clock, store);
        service.Generate("lab-d5-01");

        for (var i = 0; i < 8; i++)
        {
            service.Verify("lab-d5-01", "ASCENT{WRONG}").Outcome.ShouldBe(FlagOutcome.Rejected);
            clock.Now += TimeSpan.FromMinutes(3);
        }

        store.Find("lab-d5-01")!.WrongAttempts.Count.ShouldBeLessThanOrEqualTo(FlagService.CooldownAttempts);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_matches_rfc_4648_without_padding(string input, string expected) =>
        Base32.Encode(Encoding.ASCII.GetBytes(input)).ShouldBe(expected);

    [Fact]
    public void Base32_handles_long_inputs_without_overflow() =>
        // 512 one-bits: 102 full groups of 31 ('7'), then the last two bits padded to 0b11000 (24, 'Y').
        Base32.Encode(Enumerable.Repeat((byte)0xff, 64).ToArray()).ShouldBe(new string('7', 102) + "Y");

    private static SeededRandom CryptoRandomSourceForTests() => new(Environment.TickCount);

    private sealed class MemoryFlags : ILabFlags
    {
        private readonly Dictionary<string, LabFlagRecord> records = [];

        public LabFlagRecord? Find(string labId) => records.GetValueOrDefault(labId);

        public void SetFlag(string labId, byte[] salt, byte[] hash) => records[labId] = new LabFlagRecord(salt, hash, []);

        public void SetWrongAttempts(string labId, IReadOnlyList<DateTimeOffset> attempts) =>
            records[labId] = records[labId] with { WrongAttempts = attempts };
    }
}
