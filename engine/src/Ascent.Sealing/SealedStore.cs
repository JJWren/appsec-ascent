using System.Security.Cryptography;
using System.Text;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Progress;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Crypto;
using Ascent.Sealing.Release;

namespace Ascent.Sealing;

/// <summary>Why a Sealed item couldn't be opened. Logged as an enum name only; the Learner sees one message (P1).</summary>
public enum SealedFailure
{
    /// <summary>No bundle has that item ID.</summary>
    Missing,

    /// <summary>The bundle couldn't be read or its header isn't allowlisted.</summary>
    Malformed,

    /// <summary>The bundle has no signature.</summary>
    NoSignature,

    /// <summary>The signature doesn't verify with the trusted key.</summary>
    BadSignature,

    /// <summary>The claim doesn't release the item's tier (SEAL-03).</summary>
    NotReleased,

    /// <summary>The header's <c>kdfInfo</c> isn't <c>item:&lt;itemId&gt;</c>.</summary>
    WrongKdfInfo,

    /// <summary>AES-GCM authentication failed.</summary>
    TagMismatch,
}

/// <summary>A Sealed item couldn't be opened. Verification and decryption failures look the same (SEC-U2-02).</summary>
public sealed class SealedItemUnavailableException : AscentException
{
    private const string Hint = "If you haven't changed the repository, run 'ascent doctor'. Otherwise restore the original files with git.";

    /// <summary>Creates the error with a default message.</summary>
    public SealedItemUnavailableException()
        : this("A Sealed item is unavailable.")
    {
    }

    /// <summary>Creates the error with a message.</summary>
    public SealedItemUnavailableException(string message)
        : base(message, Hint)
    {
    }

    /// <summary>Creates the error with a message and a cause.</summary>
    public SealedItemUnavailableException(string message, Exception innerException)
        : base(message, Hint, ExitCodes.CheckFailed, innerException)
    {
    }

    /// <summary>Creates the error for an item and an internal cause.</summary>
    public SealedItemUnavailableException(string itemId, SealedFailure failure)
        : base("The Sealed item '" + SafeText.Sanitize(itemId, 128) + "' is unavailable.", Hint)
    {
        Failure = failure;
    }

    /// <summary>The internal cause, for the local log only.</summary>
    public SealedFailure? Failure { get; }
}

/// <summary>Decrypted content held in memory; disposing clears it.</summary>
public sealed class UnsealedItem : IDisposable
{
    private readonly byte[] content;

    internal UnsealedItem(BundleHeader header, byte[] content)
    {
        Header = header;
        this.content = content;
    }

    /// <summary>The item's header.</summary>
    public BundleHeader Header { get; }

    /// <summary>The plaintext bytes.</summary>
    public ReadOnlySpan<byte> Content => content;

    /// <summary>The plaintext as UTF-8 text.</summary>
    public string Text() => Encoding.UTF8.GetString(content);

    /// <inheritdoc />
    public void Dispose() => CryptographicOperations.ZeroMemory(content);
}

/// <summary>
/// Opens Sealed items through the verify-then-decrypt pipeline (P1): read with a size cap, check the header
/// allowlist, verify the signature, check the release claim, derive keys, decrypt with the header as AAD, and record
/// the release (SEAL-05). Any failure is <see cref="SealedItemUnavailableException"/>.
/// </summary>
public sealed class SealedStore
{
    private readonly Func<string, string?> locate;
    private readonly SignatureVerifier verifier;
    private readonly KeyHierarchy keys;
    private readonly KeyReleasePolicy policy;
    private readonly IKeyReleaseLog releases;
    private readonly TimeProvider time;

    /// <summary>Creates the store.</summary>
    /// <param name="locate">Maps an item ID to its bundle file, or null when there's none.</param>
    /// <param name="verifier">The trusted maintainer key.</param>
    /// <param name="keys">The key hierarchy over the shield key.</param>
    /// <param name="policy">The release policy.</param>
    /// <param name="releases">The release log.</param>
    /// <param name="time">The clock.</param>
    public SealedStore(Func<string, string?> locate, SignatureVerifier verifier, KeyHierarchy keys, KeyReleasePolicy policy, IKeyReleaseLog releases, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(locate);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(time);
        this.locate = locate;
        this.verifier = verifier;
        this.keys = keys;
        this.policy = policy;
        this.releases = releases;
        this.time = time;
    }

    /// <summary>Opens an item for a release claim.</summary>
    public UnsealedItem Open(string itemId, ReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bundle = Read(itemId);

        if (!bundle.IsSigned)
        {
            throw Fail(itemId, SealedFailure.NoSignature);
        }

        if (!verifier.Verify(bundle.SigningInput(), bundle.Signature))
        {
            throw Fail(itemId, SealedFailure.BadSignature);
        }

        if (policy.Check(bundle.Header, context) is not null)
        {
            throw Fail(itemId, SealedFailure.NotReleased);
        }

        if (bundle.Header.KdfInfo != KeyHierarchy.KdfInfoFor(itemId))
        {
            throw Fail(itemId, SealedFailure.WrongKdfInfo);
        }

        var key = keys.ItemKey(bundle.Header.Tier, itemId);
        byte[]? plaintext;
        try
        {
            plaintext = BundleCipher.TryDecrypt(key, bundle.Header.Nonce, bundle.Ciphertext, bundle.Tag, bundle.Header.CanonicalBytes());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        if (plaintext is null)
        {
            throw Fail(itemId, SealedFailure.TagMismatch);
        }

        releases.Record(itemId, bundle.Header.Tier, context.Reason, time.GetUtcNow());
        return new UnsealedItem(bundle.Header, plaintext);
    }

    /// <summary>Verifies an item's signature without decrypting it (used by <c>verify-bundles</c>).</summary>
    public bool VerifySignature(string itemId)
    {
        var bundle = Read(itemId);
        return bundle.IsSigned && verifier.Verify(bundle.SigningInput(), bundle.Signature);
    }

    private SealedBundle Read(string itemId)
    {
        var path = locate(itemId) ?? throw Fail(itemId, SealedFailure.Missing);
        try
        {
            var bundle = SealedBundle.Read(path);
            return bundle.Header.ItemId == itemId ? bundle : throw Fail(itemId, SealedFailure.Malformed);
        }
        catch (Exception ex) when (ex is BundleFormatException or IOException)
        {
            throw Fail(itemId, SealedFailure.Malformed);
        }
    }

    private static SealedItemUnavailableException Fail(string itemId, SealedFailure failure) => new(itemId, failure);
}
