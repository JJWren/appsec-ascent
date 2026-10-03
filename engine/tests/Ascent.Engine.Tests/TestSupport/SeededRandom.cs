using Ascent.Core.Platform;

namespace Ascent.Engine.Tests.TestSupport;

/// <summary>Deterministic randomness for tests only (P26). Production code can never use this type.</summary>
internal sealed class SeededRandom(int seed) : IRandomSource
{
    private readonly Random random = new(seed);

    public void Fill(Span<byte> buffer) => random.NextBytes(buffer);

    public int NextInt(int maxExclusive) => random.Next(maxExclusive);
}
