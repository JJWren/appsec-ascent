using System.Security.Cryptography;

namespace Ascent.Sealing.Crypto;

/// <summary>AES-256-GCM with a 12-byte nonce and a 16-byte tag; the canonical header is the associated data (SEAL-02).</summary>
public static class BundleCipher
{
    /// <summary>The key size in bytes.</summary>
    public const int KeySize = 32;

    /// <summary>The nonce size in bytes.</summary>
    public const int NonceSize = 12;

    /// <summary>The tag size in bytes.</summary>
    public const int TagSize = 16;

    /// <summary>Encrypts <paramref name="plaintext"/>; returns the ciphertext and the tag.</summary>
    public static (byte[] Ciphertext, byte[] Tag) Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        using var aes = new AesGcm(key, TagSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return (ciphertext, tag);
    }

    /// <summary>Decrypts into a new buffer, or returns null when authentication fails.</summary>
    public static byte[]? TryDecrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> associatedData)
    {
        if (key.Length != KeySize || nonce.Length != NonceSize || tag.Length != TagSize)
        {
            return null;
        }

        using var aes = new AesGcm(key, TagSize);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }
    }
}
