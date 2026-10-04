using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using Ascent.Core.Errors;
using Ascent.Maintainer.Authoring;
using Ascent.Maintainer.Hosting;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Crypto;
using Spectre.Console.Cli;

namespace Ascent.Maintainer.Commands;

/// <summary>Options every maintainer command accepts.</summary>
public class MaintainerSettings : CommandSettings
{
    /// <summary>The public repository root.</summary>
    [CommandOption("--public <PATH>")]
    [Description("The public repository root. Defaults to the nearest parent folder containing AppSecAscent.slnx.")]
    public string? Public { get; init; }
}

/// <summary><c>ascent-maint init-shield</c>: creates the public shield key file.</summary>
public sealed class InitShieldCommand(MaintainerContext maintainer) : Command<MaintainerSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, MaintainerSettings settings, CancellationToken cancellationToken)
    {
        var path = ShieldKeyFile.Create(maintainer.RepoRoot(settings.Public), maintainer.Random);
        maintainer.Out.WriteLine("Created " + path + ". It is public by design (ADR 0005); commit it.");
        return ExitCodes.Ok;
    }
}

/// <summary>Options for <c>keygen</c>.</summary>
public sealed class KeygenSettings : MaintainerSettings
{
    /// <summary>Replace an existing key.</summary>
    [CommandOption("--rotate")]
    [Description("Replace an existing key. Every bundle must then be signed again, and Learners need the new Engine.")]
    public bool Rotate { get; init; }
}

/// <summary>
/// <c>ascent-maint keygen</c>: creates the ECDSA P-256 signing key, stores it with DPAPI, writes an encrypted backup
/// for the password manager, and writes the Engine's trusted public key (P6).
/// </summary>
public sealed class KeygenCommand(MaintainerContext maintainer) : Command<KeygenSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, KeygenSettings settings, CancellationToken cancellationToken)
    {
        var repoRoot = maintainer.RepoRoot(settings.Public);
        var store = maintainer.KeyStore();
        if (store.Exists && !settings.Rotate)
        {
            throw new AscentException(
                "A signing key already exists at " + store.Location + ".",
                "Use --rotate only to replace it deliberately; every bundle must then be signed again.");
        }

        var passphrase = maintainer.Prompter.AskSecret("Backup passphrase (at least 12 characters):");
        if (!string.Equals(passphrase, maintainer.Prompter.AskSecret("Repeat the passphrase:"), StringComparison.Ordinal))
        {
            throw new UsageException("The passphrases don't match.", "Run keygen again.");
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var backupPath = Path.Join(maintainer.MaintainerDirectory, "signing-key-backup.p8.pem");
        maintainer.Files.CreateDirectory(maintainer.MaintainerDirectory);
        Ascent.Core.Platform.OwnerOnlyFiles.WriteAllText(maintainer.Files, backupPath, KeyBackup.Export(key, passphrase));

        var pkcs8 = key.ExportPkcs8PrivateKey();
        try
        {
            store.Save(pkcs8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        var publicKeyPath = MaintainerContext.PublicKeyPath(repoRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(publicKeyPath)!);
        File.WriteAllText(publicKeyPath, key.ExportSubjectPublicKeyInfoPem().ReplaceLineEndings("\n") + "\n");
        var fingerprint = SignatureVerifier.FingerprintOf(key);
        maintainer.Ledger.Append(new LedgerEntry { Time = maintainer.Time.GetUtcNow(), Event = "keygen", KeyFingerprint = fingerprint });

        maintainer.Out.WriteLine("Signing key stored (DPAPI, current user): " + store.Location);
        maintainer.Out.WriteLine("Fingerprint (SHA-256): " + fingerprint);
        maintainer.Out.WriteLine();
        maintainer.Out.WriteLine("KEEP this file. It is the Engine's trusted public key; it will be committed:");
        maintainer.Out.WriteLine("  " + publicKeyPath);
        maintainer.Out.WriteLine();
        maintainer.Out.WriteLine("MOVE this file into your password manager with the passphrase, then delete only this file:");
        maintainer.Out.WriteLine("  " + backupPath);
        return ExitCodes.Ok;
    }
}

/// <summary>Options for <c>key import</c>.</summary>
public sealed class KeyImportSettings : MaintainerSettings
{
    /// <summary>The encrypted backup file.</summary>
    [CommandArgument(0, "<BACKUP>")]
    [Description("The encrypted backup file written by keygen.")]
    public string Backup { get; init; } = string.Empty;
}

/// <summary><c>ascent-maint key import</c>: restores the signing key from its backup (P6).</summary>
public sealed class KeyImportCommand(MaintainerContext maintainer) : Command<KeyImportSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, KeyImportSettings settings, CancellationToken cancellationToken)
    {
        var store = maintainer.KeyStore();
        var passphrase = maintainer.Prompter.AskSecret("Backup passphrase:");
        using var key = KeyBackup.Import(File.ReadAllText(settings.Backup), passphrase);
        var pkcs8 = key.ExportPkcs8PrivateKey();
        try
        {
            store.Save(pkcs8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        var fingerprint = SignatureVerifier.FingerprintOf(key);
        maintainer.Ledger.Append(new LedgerEntry { Time = maintainer.Time.GetUtcNow(), Event = "import", KeyFingerprint = fingerprint });
        maintainer.Out.WriteLine("Signing key restored: " + store.Location);
        maintainer.Out.WriteLine("Fingerprint (SHA-256): " + fingerprint);
        var publicKeyPath = MaintainerContext.PublicKeyPath(maintainer.RepoRoot(settings.Public));
        if (File.Exists(publicKeyPath))
        {
            using var trusted = new SignatureVerifier(File.ReadAllText(publicKeyPath));
            if (trusted.Fingerprint != fingerprint)
            {
                maintainer.Out.WriteLine("WARN: This key doesn't match the Engine's trusted key (" + trusted.Fingerprint + ").");
            }
        }

        return ExitCodes.Ok;
    }
}

/// <summary>Options for <c>seal</c>.</summary>
public sealed class SealSettings : MaintainerSettings
{
    /// <summary>The sealed repository's sources folder.</summary>
    [CommandOption("--sources <PATH>")]
    [Description("The private sealed repository's sources/ folder.")]
    public string? Sources { get; init; }

    /// <summary>Delete bundles whose source no longer exists.</summary>
    [CommandOption("--prune")]
    [Description("Delete bundles whose source no longer exists.")]
    public bool Prune { get; init; }

    /// <inheritdoc />
    public override Spectre.Console.ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(Sources) ? Spectre.Console.ValidationResult.Error("--sources is required.") : base.Validate();
}

/// <summary><c>ascent-maint seal</c>: encrypts new or changed items into unsigned bundles (P7). Never prints content.</summary>
public sealed class SealCommand(MaintainerContext maintainer) : Command<SealSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, SealSettings settings, CancellationToken cancellationToken)
    {
        var repoRoot = maintainer.RepoRoot(settings.Public);
        var sources = Path.GetFullPath(settings.Sources!);
        if (!Directory.Exists(sources))
        {
            throw new UsageException("The sources folder doesn't exist: " + sources, "Point --sources at the sealed repository's sources/ folder.");
        }

        var plan = SealPlanner.Plan(sources, repoRoot);
        var sealer = new Sealer(repoRoot, new KeyHierarchy(ShieldKeyFile.Load(repoRoot)), maintainer.Random, maintainer.Time);
        var results = plan.Select(item => (item.ItemId, Outcome: sealer.Seal(item))).ToList();
        foreach (var (itemId, outcome) in results.Where(r => r.Outcome == SealOutcome.Sealed))
        {
            maintainer.Out.WriteLine("sealed " + itemId);
        }

        var orphans = sealer.FindOrphans(plan);
        foreach (var orphan in orphans)
        {
            if (settings.Prune)
            {
                File.Delete(Path.Join(repoRoot, orphan));
                maintainer.Out.WriteLine("pruned " + orphan);
            }
            else
            {
                maintainer.Out.WriteLine("WARN: orphaned bundle " + orphan + " (use --prune to delete)");
            }
        }

        maintainer.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Sealed {results.Count(r => r.Outcome == SealOutcome.Sealed)}, unchanged {results.Count(r => r.Outcome == SealOutcome.Unchanged)}, orphaned {orphans.Count}. New bundles are unsigned: run 'ascent-maint sign'."));
        return ExitCodes.Ok;
    }
}

/// <summary><c>ascent-maint sign</c>: checks and signs bundles; agents may run it unattended (ND-U2-1 = B).</summary>
public sealed class SignCommand(MaintainerContext maintainer) : Command<MaintainerSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, MaintainerSettings settings, CancellationToken cancellationToken)
    {
        var repoRoot = maintainer.RepoRoot(settings.Public);
        var publicKeyPath = MaintainerContext.PublicKeyPath(repoRoot);
        if (!File.Exists(publicKeyPath))
        {
            throw new AscentException("The Engine has no trusted public key yet.", "Run 'ascent-maint keygen' first.");
        }

        var store = maintainer.KeyStore();
        if (!store.Exists)
        {
            throw new AscentException("No signing key is stored at " + store.Location + ".", "Run 'ascent-maint keygen', or restore it with 'ascent-maint key import'.");
        }

        using var trusted = new SignatureVerifier(File.ReadAllText(publicKeyPath));
        using var key = ECDsa.Create();
        var pkcs8 = store.Load();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out _);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        var signer = new Signer(key, trusted, new KeyHierarchy(ShieldKeyFile.Load(repoRoot)), maintainer.Ledger, maintainer.Time);
        var signed = 0;
        var already = 0;
        foreach (var bundle in BundleFiles(repoRoot))
        {
            if (signer.Sign(bundle) == SignOutcome.NewSignature)
            {
                signed++;
                maintainer.Out.WriteLine("signed " + Path.GetFileName(bundle)[..^SealedBundle.FileSuffix.Length]);
            }
            else
            {
                already++;
            }
        }

        maintainer.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Signed {signed}, already signed {already}. Key {trusted.Fingerprint}. Ledger: {maintainer.Ledger.Path}"));
        return ExitCodes.Ok;
    }

    internal static IEnumerable<string> BundleFiles(string repoRoot)
    {
        var sealedRoot = Path.Join(repoRoot, "sealed");
        return Directory.Exists(sealedRoot)
            ? Directory.EnumerateFiles(sealedRoot, "*" + SealedBundle.FileSuffix, SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            : [];
    }
}

/// <summary><c>ascent-maint verify</c>: every bundle verifies with the trusted key and decrypts.</summary>
public sealed class VerifyCommand(MaintainerContext maintainer) : Command<MaintainerSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, MaintainerSettings settings, CancellationToken cancellationToken)
    {
        var repoRoot = maintainer.RepoRoot(settings.Public);
        using var trusted = new SignatureVerifier(File.ReadAllText(MaintainerContext.PublicKeyPath(repoRoot)));
        var keys = new KeyHierarchy(ShieldKeyFile.Load(repoRoot));
        var failures = 0;
        var total = 0;
        foreach (var path in SignCommand.BundleFiles(repoRoot))
        {
            total++;
            var problem = Problem(path, trusted, keys);
            if (problem is not null)
            {
                failures++;
                maintainer.Out.WriteLine("FAIL: " + Path.GetRelativePath(repoRoot, path).Replace('\\', '/') + ": " + problem);
            }
        }

        maintainer.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Verified {total - failures} of {total} bundle(s) with key {trusted.Fingerprint}."));
        return failures == 0 ? ExitCodes.Ok : ExitCodes.CheckFailed;
    }

    private static string? Problem(string path, SignatureVerifier trusted, KeyHierarchy keys)
    {
        SealedBundle bundle;
        try
        {
            bundle = SealedBundle.Read(path);
        }
        catch (BundleFormatException ex)
        {
            return ex.Message;
        }

        if (!bundle.IsSigned)
        {
            return "unsigned";
        }

        if (!trusted.Verify(bundle.SigningInput(), bundle.Signature))
        {
            return "bad signature";
        }

        var key = keys.ItemKey(bundle.Header.Tier, bundle.Header.ItemId);
        try
        {
            var plaintext = BundleCipher.TryDecrypt(key, bundle.Header.Nonce, bundle.Ciphertext, bundle.Tag, bundle.Header.CanonicalBytes());
            if (plaintext is null)
            {
                return "doesn't decrypt";
            }

            CryptographicOperations.ZeroMemory(plaintext);
            return bundle.Header.KdfInfo == KeyHierarchy.KdfInfoFor(bundle.Header.ItemId) ? null : "wrong key-derivation label";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
