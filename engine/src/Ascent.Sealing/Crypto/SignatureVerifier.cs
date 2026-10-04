using System.Security.Cryptography;
using Ascent.Core.Errors;

namespace Ascent.Sealing.Crypto;

/// <summary>
/// Verifies maintainer signatures: ECDSA P-256 with SHA-256 in IEEE P1363 format (P1). The Engine trusts exactly one
/// key, embedded at build time (P8); tests pass their own key through the constructor.
/// </summary>
public sealed class SignatureVerifier : IDisposable
{
    /// <summary>The signature size in bytes.</summary>
    public const int SignatureSize = 64;

    private const string EmbeddedKeyResource = "keys/maintainer.pub.pem";
    private const string NistP256Oid = "1.2.840.10045.3.1.7";

    private readonly ECDsa key;

    /// <summary>Creates a verifier for a PEM public key (SubjectPublicKeyInfo).</summary>
    public SignatureVerifier(string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        key = ECDsa.Create();
        try
        {
            key.ImportFromPem(publicKeyPem);
            if (key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value != NistP256Oid)
            {
                throw new ArgumentException("The maintainer key must be an ECDSA P-256 key.", nameof(publicKeyPem));
            }

            Fingerprint = FingerprintOf(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>The SHA-256 fingerprint of the key's SubjectPublicKeyInfo, as lowercase hex.</summary>
    public string Fingerprint { get; }

    /// <summary>True when an Engine build carries an embedded maintainer key.</summary>
    public static bool HasEmbeddedKey => typeof(SignatureVerifier).Assembly.GetManifestResourceInfo(EmbeddedKeyResource) is not null;

    /// <summary>The verifier for the embedded maintainer key.</summary>
    public static SignatureVerifier Embedded()
    {
        using var stream = typeof(SignatureVerifier).Assembly.GetManifestResourceStream(EmbeddedKeyResource)
            ?? throw new AscentException(
                "This Engine build has no maintainer key, so it can't verify Sealed Bundles.",
                "Use an official build, or ask the maintainer: the key is created with 'ascent-maint keygen'.");
        using var reader = new StreamReader(stream);
        return new SignatureVerifier(reader.ReadToEnd());
    }

    /// <summary>The fingerprint of any ECDSA key's public part.</summary>
    public static string FingerprintOf(ECDsa publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        return Convert.ToHexStringLower(SHA256.HashData(publicKey.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>True when <paramref name="signature"/> is a valid signature over <paramref name="data"/>.</summary>
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature) =>
        signature.Length == SignatureSize
        && key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    /// <inheritdoc />
    public void Dispose() => key.Dispose();
}
