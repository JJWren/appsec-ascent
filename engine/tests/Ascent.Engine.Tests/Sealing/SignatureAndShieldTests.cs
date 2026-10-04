using System.Security.Cryptography;
using Ascent.Core.Errors;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Sealing.Crypto;

namespace Ascent.Engine.Tests.Sealing;

public sealed class SignatureAndShieldTests
{
    [Fact]
    public void Signatures_verify_only_for_the_signed_data_and_the_trusted_key()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var verifier = new SignatureVerifier(key.ExportSubjectPublicKeyInfoPem());
        var data = "signed bytes"u8.ToArray();
        var signature = key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        verifier.Verify(data, signature).ShouldBeTrue();
        verifier.Verify("other bytes"u8, signature).ShouldBeFalse();
        verifier.Verify(data, other.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)).ShouldBeFalse();
        verifier.Verify(data, signature.AsSpan(0, 63)).ShouldBeFalse();
        verifier.Verify(data, key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)).ShouldBeFalse();
    }

    [Fact]
    public void The_fingerprint_is_the_sha256_of_the_public_key_info()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var verifier = new SignatureVerifier(key.ExportSubjectPublicKeyInfoPem());

        verifier.Fingerprint.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo())));
        SignatureVerifier.FingerprintOf(key).ShouldBe(verifier.Fingerprint);
        verifier.Fingerprint.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Only_p256_keys_are_trusted()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Should.Throw<ArgumentException>(() => new SignatureVerifier(p384.ExportSubjectPublicKeyInfoPem()));
        Should.Throw<ArgumentException>(() => new SignatureVerifier("not a key"));
    }

    [Fact]
    public void The_embedded_key_is_used_when_present_and_explained_when_absent()
    {
        if (SignatureVerifier.HasEmbeddedKey)
        {
            using var embedded = SignatureVerifier.Embedded();
            embedded.Fingerprint.ShouldMatch("^[0-9a-f]{64}$");
        }
        else
        {
            var error = Should.Throw<AscentException>(SignatureVerifier.Embedded);
            error.NextStep!.ShouldContain("ascent-maint keygen");
        }
    }

    [Fact]
    public void The_shield_key_file_is_created_once_and_loaded()
    {
        using var temp = new TempDirectory();
        var path = ShieldKeyFile.Create(temp.Path, new SeededRandom(1));

        File.ReadAllText(path).ShouldContain("Public by design");
        ShieldKeyFile.Load(temp.Path).Length.ShouldBe(32);
        Should.Throw<AscentException>(() => ShieldKeyFile.Create(temp.Path, new SeededRandom(2))).Message.ShouldContain("already exists");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"shieldKey\":\"AAAA\"}")]
    [InlineData("{\"shieldKey\":\"%%%\"}")]
    [InlineData("{\"shieldKey\":7}")]
    [InlineData("{}")]
    public void A_damaged_shield_key_is_reported(string content)
    {
        using var temp = new TempDirectory();
        temp.WriteFile(ShieldKeyFile.RelativePath, content);
        Should.Throw<AscentException>(() => ShieldKeyFile.Load(temp.Path)).Message.ShouldContain("damaged");
    }

    [Fact]
    public void A_missing_shield_key_is_reported()
    {
        using var temp = new TempDirectory();
        Should.Throw<AscentException>(() => ShieldKeyFile.Load(temp.Path)).NextStep!.ShouldContain("git checkout");
    }
}
