using System.Security.Cryptography;

namespace Ascent.Core.Platform;

/// <summary>The Engine's only source of randomness (SEC-U2-01, P26). Production always uses the CSPRNG.</summary>
public interface IRandomSource
{
    /// <summary>Fills <paramref name="buffer"/> with random bytes.</summary>
    void Fill(Span<byte> buffer);

    /// <summary>Returns a uniformly distributed integer in [0, <paramref name="maxExclusive"/>).</summary>
    int NextInt(int maxExclusive);
}

/// <summary>Production randomness: the operating system's cryptographically secure generator.</summary>
public sealed class CryptoRandomSource : IRandomSource
{
    /// <summary>The shared instance.</summary>
    public static CryptoRandomSource Instance { get; } = new();

    /// <inheritdoc />
    public void Fill(Span<byte> buffer) => RandomNumberGenerator.Fill(buffer);

    /// <inheritdoc />
    public int NextInt(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
}

/// <summary>Helpers over <see cref="IRandomSource"/>.</summary>
public static class RandomSourceExtensions
{
    /// <summary>Returns <paramref name="count"/> random bytes.</summary>
    public static byte[] Bytes(this IRandomSource random, int count)
    {
        ArgumentNullException.ThrowIfNull(random);
        var bytes = new byte[count];
        random.Fill(bytes);
        return bytes;
    }

    /// <summary>Returns <paramref name="byteCount"/> random bytes as lowercase hexadecimal.</summary>
    public static string Hex(this IRandomSource random, int byteCount) => Convert.ToHexStringLower(random.Bytes(byteCount));

    /// <summary>Shuffles <paramref name="items"/> in place (Fisher–Yates).</summary>
    public static void Shuffle<T>(this IRandomSource random, IList<T> items)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(items);
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
