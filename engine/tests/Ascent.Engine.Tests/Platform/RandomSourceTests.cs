using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;

namespace Ascent.Engine.Tests.Platform;

public sealed class RandomSourceTests
{
    [Fact]
    public void Crypto_source_fills_buffers_and_stays_in_range()
    {
        var random = CryptoRandomSource.Instance;
        random.Bytes(32).Length.ShouldBe(32);
        random.Hex(16).Length.ShouldBe(32);
        random.Hex(16).ShouldMatch("^[0-9a-f]{32}$");
        Enumerable.Range(0, 200).Select(_ => random.NextInt(7)).ShouldAllBe(n => n >= 0 && n < 7);
    }

    [Fact]
    public void Crypto_source_does_not_repeat_itself()
    {
        CryptoRandomSource.Instance.Hex(16).ShouldNotBe(CryptoRandomSource.Instance.Hex(16));
    }

    [Fact]
    public void Shuffle_is_a_permutation_and_is_deterministic_for_a_seed()
    {
        var first = Enumerable.Range(0, 50).ToList();
        var second = Enumerable.Range(0, 50).ToList();
        new SeededRandom(42).Shuffle(first);
        new SeededRandom(42).Shuffle(second);

        first.ShouldBe(second);
        first.OrderBy(n => n).ShouldBe(Enumerable.Range(0, 50));
        first.ShouldNotBe(Enumerable.Range(0, 50).ToList());
    }

    [Fact]
    public void Shuffle_handles_empty_and_single_item_lists()
    {
        var empty = new List<int>();
        var single = new List<int> { 1 };
        CryptoRandomSource.Instance.Shuffle(empty);
        CryptoRandomSource.Instance.Shuffle(single);
        empty.ShouldBeEmpty();
        single.ShouldBe([1]);
    }
}
