using System.Security.Cryptography;
using Ascent.Core.Platform;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Crypto;

namespace Ascent.Maintainer.Authoring;

/// <summary>What happened to one item during sealing.</summary>
public enum SealOutcome
{
    /// <summary>The item was new or changed, and a fresh unsigned bundle was written.</summary>
    Sealed,

    /// <summary>The existing bundle already holds the same plaintext and metadata; it was left untouched (P7).</summary>
    Unchanged,
}

/// <summary>
/// Encrypts items into unsigned bundles (P7). Sealing needs no secret, because the shield key is public; only
/// signing does. An unchanged item keeps its bytes and signature, so pull requests show only real changes.
/// </summary>
public sealed class Sealer
{
    private readonly string repoRoot;
    private readonly KeyHierarchy keys;
    private readonly IRandomSource random;
    private readonly TimeProvider time;

    /// <summary>Creates a sealer for a public repository.</summary>
    public Sealer(string repoRoot, KeyHierarchy keys, IRandomSource random, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(time);
        this.repoRoot = repoRoot;
        this.keys = keys;
        this.random = random;
        this.time = time;
    }

    /// <summary>Seals one item.</summary>
    public SealOutcome Seal(SealItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var target = SafePath.Resolve(repoRoot, item.BundlePath);
        var plaintext = item.Plaintext();
        try
        {
            if (File.Exists(target) && IsUnchanged(SealedBundle.Read(target), item, plaintext))
            {
                return SealOutcome.Unchanged;
            }

            var header = BundleHeader.Create(
                item.ItemId,
                item.ItemType,
                item.Tier,
                item.ContentType,
                random.Bytes(BundleCipher.NonceSize),
                time.GetUtcNow(),
                item.ObjectiveId,
                item.ExamDomain,
                item.Pool,
                item.AiTopic);
            var key = keys.ItemKey(item.Tier, item.ItemId);
            try
            {
                var (ciphertext, tag) = BundleCipher.Encrypt(key, header.Nonce, plaintext, header.CanonicalBytes());
                new SealedBundle(header, ciphertext, tag, []).Write(target);
                return SealOutcome.Sealed;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Bundles under <c>sealed/</c> whose item is no longer planned.</summary>
    public IReadOnlyList<string> FindOrphans(IReadOnlyList<SealItem> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var sealedRoot = Path.Join(repoRoot, "sealed");
        if (!Directory.Exists(sealedRoot))
        {
            return [];
        }

        var planned = plan.Select(item => Path.GetFullPath(Path.Join(repoRoot, item.BundlePath))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(sealedRoot, "*" + SealedBundle.FileSuffix, SearchOption.AllDirectories)
            .Where(path => !planned.Contains(Path.GetFullPath(path)))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private bool IsUnchanged(SealedBundle existing, SealItem item, byte[] plaintext)
    {
        var header = existing.Header;
        var sameMetadata = header.ItemId == item.ItemId
            && header.ItemType == item.ItemType
            && header.Tier == item.Tier
            && header.ContentType == item.ContentType
            && header.KdfInfo == KeyHierarchy.KdfInfoFor(item.ItemId)
            && header.ObjectiveId == item.ObjectiveId
            && header.ExamDomain == item.ExamDomain
            && header.Pool == item.Pool
            && header.AiTopic == item.AiTopic;
        if (!sameMetadata)
        {
            return false;
        }

        var key = keys.ItemKey(header.Tier, header.ItemId);
        try
        {
            var previous = BundleCipher.TryDecrypt(key, header.Nonce, existing.Ciphertext, existing.Tag, header.CanonicalBytes());
            var same = previous is not null && previous.AsSpan().SequenceEqual(plaintext);
            if (previous is not null)
            {
                CryptographicOperations.ZeroMemory(previous);
            }

            return same;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
