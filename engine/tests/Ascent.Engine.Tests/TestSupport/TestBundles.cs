using System.Security.Cryptography;
using Ascent.Core.Domain;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Crypto;

namespace Ascent.Engine.Tests.TestSupport;

/// <summary>Builds signed Sealed Bundles in a throwaway repository, with a test shield key and a test signing key.</summary>
internal sealed class TestBundles : IDisposable
{
    public TestBundles()
    {
        ShieldKey = Enumerable.Range(0, 32).Select(i => (byte)(i * 7)).ToArray();
        Keys = new KeyHierarchy(ShieldKey);
        Verifier = new SignatureVerifier(SigningKey.ExportSubjectPublicKeyInfoPem());
    }

    public TempDirectory Repo { get; } = new();

    public byte[] ShieldKey { get; }

    public KeyHierarchy Keys { get; }

    public ECDsa SigningKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public SignatureVerifier Verifier { get; }

    public Dictionary<string, string> Paths { get; } = new(StringComparer.Ordinal);

    public string? Locate(string itemId) => Paths.GetValueOrDefault(itemId);

    public string Write(
        string itemId,
        string itemType,
        SealTier tier,
        byte[] plaintext,
        string? pool = null,
        bool sign = true,
        Func<SealedBundle, SealedBundle>? tamper = null,
        string? fileItemId = null)
    {
        var header = BundleHeader.Create(itemId, itemType, tier, "application/octet-stream", RandomNumberGenerator.GetBytes(12), DateTimeOffset.UnixEpoch, pool: pool);
        var key = Keys.ItemKey(tier, itemId);
        var (ciphertext, tag) = BundleCipher.Encrypt(key, header.Nonce, plaintext, header.CanonicalBytes());
        var bundle = new SealedBundle(header, ciphertext, tag, []);
        bundle = tamper?.Invoke(bundle) ?? bundle;
        if (sign)
        {
            bundle = Sign(bundle);
        }

        var path = Repo.Combine("sealed", (fileItemId ?? itemId) + SealedBundle.FileSuffix);
        bundle.Write(path);
        Paths[fileItemId ?? itemId] = path;
        return path;
    }

    public SealedBundle Sign(SealedBundle bundle) =>
        bundle.WithSignature(SigningKey.SignData(bundle.SigningInput(), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    public void Dispose()
    {
        Verifier.Dispose();
        SigningKey.Dispose();
        Repo.Dispose();
    }
}
