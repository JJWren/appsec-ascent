using System.Security.Cryptography;
using Ascent.Core.Errors;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Crypto;

namespace Ascent.Maintainer.Authoring;

/// <summary>What happened to one bundle during signing.</summary>
public enum SignOutcome
{
    /// <summary>The bundle was signed now.</summary>
    NewSignature,

    /// <summary>The bundle already carried a valid signature.</summary>
    AlreadyValid,
}

/// <summary>
/// Signs bundles with the maintainer key after checking each one (P6): the header must be allowlisted, the key
/// derivation label right, and the ciphertext must decrypt. Every signature is written to the signing ledger.
/// </summary>
public sealed class Signer
{
    private readonly ECDsa key;
    private readonly SignatureVerifier verifier;
    private readonly KeyHierarchy keys;
    private readonly SigningLedger ledger;
    private readonly TimeProvider time;

    /// <summary>Creates a signer. <paramref name="trusted"/> must be the Engine's trusted key, matching <paramref name="key"/>.</summary>
    public Signer(ECDsa key, SignatureVerifier trusted, KeyHierarchy keys, SigningLedger ledger, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(trusted);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(time);
        if (SignatureVerifier.FingerprintOf(key) != trusted.Fingerprint)
        {
            throw new AscentException(
                "The signing key doesn't match the key the Engine trusts (" + trusted.Fingerprint + ").",
                "Import the right key with 'ascent-maint key import', or rotate deliberately with 'ascent-maint keygen --rotate'.");
        }

        this.key = key;
        verifier = trusted;
        this.keys = keys;
        this.ledger = ledger;
        this.time = time;
    }

    /// <summary>Signs one bundle file unless it is already validly signed.</summary>
    public SignOutcome Sign(string bundlePath)
    {
        var bundle = SealedBundle.Read(bundlePath);
        var input = bundle.SigningInput();
        if (bundle.IsSigned && verifier.Verify(input, bundle.Signature))
        {
            return SignOutcome.AlreadyValid;
        }

        CheckBeforeSigning(bundle);
        var signature = key.SignData(input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        bundle.WithSignature(signature).Write(bundlePath);
        ledger.Append(new LedgerEntry
        {
            Time = time.GetUtcNow(),
            Event = "sign",
            ItemId = bundle.Header.ItemId,
            BundleSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(bundlePath))),
            KeyFingerprint = verifier.Fingerprint,
        });
        return SignOutcome.NewSignature;
    }

    private void CheckBeforeSigning(SealedBundle bundle)
    {
        var header = bundle.Header;
        if (header.KdfInfo != KeyHierarchy.KdfInfoFor(header.ItemId))
        {
            throw new AscentException("Refused to sign '" + header.ItemId + "': its key-derivation label is wrong.", "Seal it again with 'ascent-maint seal'.");
        }

        var itemKey = keys.ItemKey(header.Tier, header.ItemId);
        try
        {
            var plaintext = BundleCipher.TryDecrypt(itemKey, header.Nonce, bundle.Ciphertext, bundle.Tag, header.CanonicalBytes())
                ?? throw new AscentException("Refused to sign '" + header.ItemId + "': it doesn't decrypt.", "Seal it again with 'ascent-maint seal'.");
            CryptographicOperations.ZeroMemory(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(itemKey);
        }
    }
}
