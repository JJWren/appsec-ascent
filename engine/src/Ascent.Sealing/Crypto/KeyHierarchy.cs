using System.Security.Cryptography;
using System.Text;
using Ascent.Core.Domain;

namespace Ascent.Sealing.Crypto;

/// <summary>
/// Versioned key derivation (P2): tier key = HKDF-SHA256(shield key, salt <c>appsec-ascent/v1</c>, info
/// <c>tier:&lt;tier&gt;</c>); item key = HKDF-Expand(tier key, info <c>item:&lt;itemId&gt;</c>). The shield key is
/// public by design (ADR 0005): this is a spoiler shield, not DRM.
/// </summary>
public sealed class KeyHierarchy
{
    /// <summary>The HKDF salt for tier keys.</summary>
    public const string Salt = "appsec-ascent/v1";

    private readonly byte[] shieldKey;

    /// <summary>Creates the hierarchy over a 32-byte shield key.</summary>
    public KeyHierarchy(ReadOnlySpan<byte> shieldKey)
    {
        if (shieldKey.Length != BundleCipher.KeySize)
        {
            throw new ArgumentException("The shield key must be 32 bytes.", nameof(shieldKey));
        }

        this.shieldKey = shieldKey.ToArray();
    }

    /// <summary>The HKDF info for an item key, which a bundle's <c>kdfInfo</c> must equal.</summary>
    public static string KdfInfoFor(string itemId) => "item:" + itemId;

    /// <summary>Derives a tier key. The caller clears it after use.</summary>
    public byte[] TierKey(SealTier tier) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, shieldKey, BundleCipher.KeySize, Encoding.UTF8.GetBytes(Salt), Encoding.UTF8.GetBytes("tier:" + SealTiers.Name(tier)));

    /// <summary>Derives an item key. The caller clears it after use.</summary>
    public byte[] ItemKey(SealTier tier, string itemId)
    {
        var tierKey = TierKey(tier);
        try
        {
            return HKDF.Expand(HashAlgorithmName.SHA256, tierKey, BundleCipher.KeySize, Encoding.UTF8.GetBytes(KdfInfoFor(itemId)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tierKey);
        }
    }
}
