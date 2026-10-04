using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Ascent.Core.Errors;
using Ascent.Core.Platform;

namespace Ascent.Maintainer.Authoring;

/// <summary>Holds the maintainer's private key (PKCS#8). Callers clear the bytes they load.</summary>
public interface IKeyStore
{
    /// <summary>Where the key is kept.</summary>
    string Location { get; }

    /// <summary>True when a key is stored.</summary>
    bool Exists { get; }

    /// <summary>Stores a key, replacing any existing one.</summary>
    void Save(ReadOnlySpan<byte> pkcs8);

    /// <summary>Loads the key.</summary>
    byte[] Load();
}

/// <summary>
/// The private key protected with DPAPI for the current Windows user (ND-U2-1 = B, P6). Any process running as the
/// maintainer can use it; the signing ledger records every signature.
/// </summary>
[SupportedOSPlatform("windows")]
[ExcludeFromCodeCoverage(Justification = "Windows-only adapter. It runs in the Windows test job, outside the Linux coverage run (P24).")]
public sealed class DpapiKeyStore : IKeyStore
{
    private readonly IOwnerOnlyFiles files;

    /// <summary>Creates the store at <paramref name="path"/>.</summary>
    public DpapiKeyStore(string path, IOwnerOnlyFiles files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(files);
        Location = path;
        this.files = files;
    }

    /// <inheritdoc />
    public string Location { get; }

    /// <inheritdoc />
    public bool Exists => File.Exists(Location);

    /// <inheritdoc />
    public void Save(ReadOnlySpan<byte> pkcs8)
    {
        var copy = pkcs8.ToArray();
        try
        {
            files.CreateDirectory(Path.GetDirectoryName(Location)!);
            files.WriteAllBytes(Location, ProtectedData.Protect(copy, optionalEntropy: null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }

    /// <inheritdoc />
    public byte[] Load()
    {
        try
        {
            return ProtectedData.Unprotect(File.ReadAllBytes(Location), optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new AscentException(
                "The signing key at " + Location + " can't be unprotected for this Windows account.",
                "Restore it from your backup with 'ascent-maint key import <backup-file>'.",
                ExitCodes.CheckFailed,
                ex);
        }
    }
}
