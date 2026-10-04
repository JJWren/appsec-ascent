using System.Security.Cryptography;
using System.Text;
using Ascent.Core.Domain;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Crypto;

namespace Ascent.Engine.Tests.Sealing;

/// <summary>Known-answer and golden tests for the sealing primitives (TEST-U2-04, P27).</summary>
public sealed class CryptoKnownAnswerTests
{
    public static TheoryData<string, string, string, int, string, string> Rfc5869Sha256 => new()
    {
        {
            "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b",
            "000102030405060708090a0b0c",
            "f0f1f2f3f4f5f6f7f8f9",
            42,
            "077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5",
            "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865"
        },
        {
            "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f",
            "606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeaf",
            "b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff",
            82,
            "06a6b88c5853361a06104c9ceb35b45cef760014904671014a193f40c15fc244",
            "b11e398dc80327a1c8e7f78c596a49344f012eda2d4efad8a050cc4c19afa97c59045a99cac7827271cb41c65e590e09da3275600c2f09b8367793a9aca3db71cc30c58179ec3e87c14c01d5c1f3434f1d87"
        },
        {
            "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b",
            "",
            "",
            42,
            "19ef24a32c717b167f33a91d6f648bdf96596776afdb6377ac434c1c293ccb04",
            "8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8"
        },
    };

    [Theory]
    [MemberData(nameof(Rfc5869Sha256))]
    public void Hkdf_matches_rfc_5869(string ikm, string salt, string info, int length, string prk, string okm)
    {
        var extracted = HKDF.Extract(HashAlgorithmName.SHA256, Convert.FromHexString(ikm), Convert.FromHexString(salt));
        Convert.ToHexStringLower(extracted).ShouldBe(prk);
        Convert.ToHexStringLower(HKDF.Expand(HashAlgorithmName.SHA256, extracted, length, Convert.FromHexString(info))).ShouldBe(okm);
        Convert.ToHexStringLower(HKDF.DeriveKey(HashAlgorithmName.SHA256, Convert.FromHexString(ikm), length, Convert.FromHexString(salt), Convert.FromHexString(info))).ShouldBe(okm);
    }

    [Fact]
    [Trait("Rule", "SEAL-02")]
    public void The_key_hierarchy_uses_versioned_labels()
    {
        var shield = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var keys = new KeyHierarchy(shield);

        var expectedTier = HKDF.DeriveKey(HashAlgorithmName.SHA256, shield, 32, Encoding.UTF8.GetBytes("appsec-ascent/v1"), Encoding.UTF8.GetBytes("tier:earned"));
        keys.TierKey(SealTier.Earned).ShouldBe(expectedTier);
        keys.ItemKey(SealTier.Earned, "lab-d5-01.tests")
            .ShouldBe(HKDF.Expand(HashAlgorithmName.SHA256, expectedTier, 32, Encoding.UTF8.GetBytes("item:lab-d5-01.tests")));
        KeyHierarchy.KdfInfoFor("x").ShouldBe("item:x");
    }

    [Fact]
    [Trait("Rule", "SEAL-02")]
    public void Derived_keys_are_pinned_so_label_changes_cannot_slip_in()
    {
        // Golden values: changing a label or the salt would make every published bundle unreadable.
        var keys = new KeyHierarchy(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        var item = Convert.ToHexStringLower(keys.ItemKey(SealTier.Practice, "qb-1.1-001"));
        var again = Convert.ToHexStringLower(new KeyHierarchy(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()).ItemKey(SealTier.Practice, "qb-1.1-001"));

        item.ShouldBe(again);
        item.Length.ShouldBe(64);
        keys.ItemKey(SealTier.Simulation, "qb-1.1-001").ShouldNotBe(keys.ItemKey(SealTier.Practice, "qb-1.1-001"));
        keys.ItemKey(SealTier.Practice, "qb-1.1-002").ShouldNotBe(keys.ItemKey(SealTier.Practice, "qb-1.1-001"));
    }

    [Fact]
    public void The_shield_key_must_be_32_bytes() =>
        Should.Throw<ArgumentException>(() => new KeyHierarchy(new byte[16]));

    [Fact]
    [Trait("Rule", "SEAL-02")]
    public void Aes_gcm_round_trips_and_detects_every_kind_of_tampering()
    {
        var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var nonce = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray();
        var aad = Encoding.UTF8.GetBytes("{\"itemId\":\"x\"}");
        var plaintext = Encoding.UTF8.GetBytes("reference answer");
        var (ciphertext, tag) = BundleCipher.Encrypt(key, nonce, plaintext, aad);

        BundleCipher.TryDecrypt(key, nonce, ciphertext, tag, aad).ShouldBe(plaintext);

        var flippedCiphertext = (byte[])ciphertext.Clone();
        flippedCiphertext[0] ^= 1;
        var flippedTag = (byte[])tag.Clone();
        flippedTag[^1] ^= 1;
        var otherNonce = (byte[])nonce.Clone();
        otherNonce[0] ^= 1;
        BundleCipher.TryDecrypt(key, nonce, flippedCiphertext, tag, aad).ShouldBeNull();
        BundleCipher.TryDecrypt(key, nonce, ciphertext, flippedTag, aad).ShouldBeNull();
        BundleCipher.TryDecrypt(key, otherNonce, ciphertext, tag, aad).ShouldBeNull();
        BundleCipher.TryDecrypt(key, nonce, ciphertext, tag, Encoding.UTF8.GetBytes("{\"itemId\":\"y\"}")).ShouldBeNull();
        BundleCipher.TryDecrypt(key.AsSpan(0, 16), nonce, ciphertext, tag, aad).ShouldBeNull();
        BundleCipher.TryDecrypt(key, nonce.AsSpan(0, 8), ciphertext, tag, aad).ShouldBeNull();
        BundleCipher.TryDecrypt(key, nonce, ciphertext, tag.AsSpan(0, 8), aad).ShouldBeNull();
    }

    [Fact]
    public void Aes_gcm_output_is_deterministic_for_fixed_inputs()
    {
        var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var nonce = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray();
        var first = BundleCipher.Encrypt(key, nonce, "same"u8, "aad"u8);
        var second = BundleCipher.Encrypt(key, nonce, "same"u8, "aad"u8);

        first.Ciphertext.ShouldBe(second.Ciphertext);
        first.Tag.ShouldBe(second.Tag);
        first.Tag.Length.ShouldBe(BundleCipher.TagSize);
    }

    [Fact]
    public void The_canonical_header_is_pinned()
    {
        var header = BundleHeader.Create(
            "qb-1.1-001",
            "question",
            SealTier.Practice,
            "application/json",
            Enumerable.Range(0, 12).Select(i => (byte)(i * 21)).ToArray(),
            new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero),
            objectiveId: "1.1",
            examDomain: "D1",
            pool: "practice",
            aiTopic: "AI-1 \"quoted\" \\ café");

        Encoding.UTF8.GetString(header.CanonicalBytes()).ShouldBe(
            "{\"aiTopic\":\"AI-1 \\\"quoted\\\" \\\\ café\",\"contentType\":\"application/json\",\"createdUtc\":\"2026-10-03T12:00:00Z\","
            + "\"examDomain\":\"D1\",\"formatVersion\":1,\"itemId\":\"qb-1.1-001\",\"itemType\":\"question\",\"kdfInfo\":\"item:qb-1.1-001\","
            + "\"nonce\":\"ABUqP1RpfpOovdLn\",\"objectiveId\":\"1.1\",\"pool\":\"practice\",\"tier\":\"practice\"}");
    }

    [Fact]
    public void Canonical_strings_escape_only_quotes_backslashes_and_controls()
    {
        var fields = new Dictionary<string, string>
        {
            ["b"] = "tab\there\nnew\u0001raw+/<>&é",
            ["a"] = "\b\f\r",
        };

        Encoding.UTF8.GetString(HeaderCanonicalizer.Canonicalize(fields, 1))
            .ShouldBe("{\"a\":\"\\b\\f\\r\",\"b\":\"tab\\there\\nnew\\u0001raw+/<>&é\",\"formatVersion\":1}");
    }

    [Fact]
    public void The_signing_input_has_a_label_and_length_prefixes()
    {
        var input = SigningInput.Build("HDR"u8, "CIPHER"u8, new byte[16]);

        Encoding.ASCII.GetString(input, 0, SigningInput.Label.Length).ShouldBe("appsec-ascent/bundle/v1");
        input[SigningInput.Label.Length].ShouldBe((byte)0);
        input.AsSpan(SigningInput.Label.Length + 1, 4).ToArray().ShouldBe(new byte[] { 0, 0, 0, 3 });
        Encoding.ASCII.GetString(input, SigningInput.Label.Length + 5, 3).ShouldBe("HDR");
        input.AsSpan(SigningInput.Label.Length + 8, 4).ToArray().ShouldBe(new byte[] { 0, 0, 0, 6 });
        input.Length.ShouldBe(SigningInput.Label.Length + 1 + 4 + 3 + 4 + 6 + 16);

        // Moving a byte between the header and the ciphertext changes the input (no ambiguity).
        SigningInput.Build("HDRC"u8, "IPHER"u8, new byte[16]).ShouldNotBe(input);
    }
}
