using System.Security.Cryptography;
using System.Text;
using Ascent.Core.Platform;
using Ascent.Core.Progress;

namespace Ascent.Sealing.Flags;

/// <summary>The outcome of a Flag submission.</summary>
public enum FlagOutcome
{
    /// <summary>The Flag matches.</summary>
    Accepted,

    /// <summary>The Flag doesn't match; no hint of how close it was (FLAG-03).</summary>
    Rejected,

    /// <summary>Too many wrong attempts: wait until <see cref="FlagCheck.RetryAfter"/>.</summary>
    CoolingDown,

    /// <summary>No Flag has been planted for this Lab.</summary>
    NoFlag,
}

/// <summary>The result of a Flag check.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="RetryAfter">For <see cref="FlagOutcome.CoolingDown"/>: when the next attempt is allowed.</param>
public sealed record FlagCheck(FlagOutcome Outcome, DateTimeOffset? RetryAfter = null);

/// <summary>
/// Generates and verifies Flags (FLAG-01..03, P5): <c>ASCENT{</c> + Base32 of 16 CSPRNG bytes + <c>}</c>, stored as a
/// salted SHA-256 and compared in constant time, with a cooldown after repeated wrong attempts.
/// </summary>
public sealed class FlagService
{
    /// <summary>Wrong attempts that trigger the cooldown.</summary>
    public const int CooldownAttempts = 5;

    /// <summary>The window those attempts must fall in.</summary>
    public static readonly TimeSpan CooldownWindow = TimeSpan.FromMinutes(10);

    /// <summary>How long the cooldown lasts.</summary>
    public static readonly TimeSpan CooldownLength = TimeSpan.FromSeconds(60);

    private const string Prefix = "ASCENT{";
    private const string Suffix = "}";

    private readonly IRandomSource random;
    private readonly TimeProvider time;
    private readonly ILabFlags store;

    /// <summary>Creates the service.</summary>
    public FlagService(IRandomSource random, TimeProvider time, ILabFlags store)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(store);
        this.random = random;
        this.time = time;
        this.store = store;
    }

    /// <summary>
    /// Plants a new Flag for <paramref name="labId"/>, replacing any earlier one, and returns it for the planter only.
    /// The Engine never prints a Flag (FLAG-04).
    /// </summary>
    public string Generate(string labId)
    {
        var secret = random.Bytes(16);
        try
        {
            var flag = Prefix + Base32.Encode(secret) + Suffix;
            var salt = random.Bytes(16);
            store.SetFlag(labId, salt, Hash(salt, flag));
            return flag;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>Checks a submitted Flag.</summary>
    public FlagCheck Verify(string labId, string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var record = store.Find(labId);
        if (record is null)
        {
            return new FlagCheck(FlagOutcome.NoFlag);
        }

        var now = time.GetUtcNow();
        var recent = record.WrongAttempts.Where(t => t > now - CooldownWindow).Order().ToList();
        if (recent.Count >= CooldownAttempts && now < recent[^1] + CooldownLength)
        {
            return new FlagCheck(FlagOutcome.CoolingDown, recent[^1] + CooldownLength);
        }

        var computed = Hash(record.Salt, candidate.Trim());
        if (CryptographicOperations.FixedTimeEquals(computed, record.Hash))
        {
            store.SetWrongAttempts(labId, []);
            return new FlagCheck(FlagOutcome.Accepted);
        }

        recent.Add(now);
        store.SetWrongAttempts(labId, recent.TakeLast(CooldownAttempts).ToList());
        return new FlagCheck(FlagOutcome.Rejected);
    }

    private static byte[] Hash(ReadOnlySpan<byte> salt, string flag)
    {
        var flagBytes = Encoding.UTF8.GetBytes(flag);
        var input = new byte[salt.Length + flagBytes.Length];
        salt.CopyTo(input);
        flagBytes.CopyTo(input, salt.Length);
        try
        {
            return SHA256.HashData(input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(flagBytes);
        }
    }
}

/// <summary>RFC 4648 Base32 (uppercase alphabet, no padding).</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Encodes bytes.</summary>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                builder.Append(Alphabet[(buffer >> bits) & 31]);
            }

            // Keep only the bits not yet written, so the buffer never overflows.
            buffer &= (1 << bits) - 1;
        }

        if (bits > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return builder.ToString();
    }
}
