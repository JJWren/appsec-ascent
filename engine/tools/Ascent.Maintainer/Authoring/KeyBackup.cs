using System.Security.Cryptography;
using Ascent.Core.Errors;

namespace Ascent.Maintainer.Authoring;

/// <summary>
/// The password-manager backup of the signing key: encrypted PKCS#8 PEM (AES-256-CBC, PBKDF2-SHA256, 600,000
/// iterations). The passphrase is always typed by the maintainer at a hidden prompt, never passed as an argument (P6).
/// </summary>
public static class KeyBackup
{
    /// <summary>The shortest passphrase accepted.</summary>
    public const int MinimumPassphraseLength = 12;

    /// <summary>The encryption parameters.</summary>
    public static readonly PbeParameters Parameters = new(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000);

    private const string NistP256Oid = "1.2.840.10045.3.1.7";

    /// <summary>Exports <paramref name="key"/> encrypted under <paramref name="passphrase"/>.</summary>
    public static string Export(ECDsa key, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(passphrase);
        if (passphrase.Length < MinimumPassphraseLength)
        {
            throw new UsageException("The backup passphrase must have at least 12 characters.", "Use a long passphrase from your password manager.");
        }

        return key.ExportEncryptedPkcs8PrivateKeyPem(passphrase, Parameters);
    }

    /// <summary>Opens a backup; the caller disposes the key.</summary>
    public static ECDsa Import(string pem, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(pem);
        ArgumentNullException.ThrowIfNull(passphrase);
        var key = ECDsa.Create();
        try
        {
            key.ImportFromEncryptedPem(pem, passphrase);
            if (key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value != NistP256Oid)
            {
                throw new AscentException("The backup doesn't hold an ECDSA P-256 key.", "Check that you chose the AppSec Ascent signing-key backup.");
            }

            return key;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            key.Dispose();
            throw new AscentException("The backup couldn't be opened. The passphrase may be wrong.", "Try again with the passphrase stored next to the backup.", ExitCodes.CheckFailed, ex);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}
