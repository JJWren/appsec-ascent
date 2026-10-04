using System.Security.Cryptography;
using Ascent.Cli.Tests.TestSupport;
using Ascent.Core.Domain;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Crypto;

namespace Ascent.Cli.Tests;

/// <summary>
/// <c>verify-bundles</c> checks signatures against the embedded maintainer key (BND-01, P8). These tests hold before
/// the key ceremony (no key embedded) and after it.
/// </summary>
public sealed class VerifyBundlesTests
{
    [Fact]
    public async Task No_bundles_means_nothing_to_verify()
    {
        using var engine = TestEngine.Empty().WithSchemas();

        var (exitCode, output) = await engine.RunAsync("verify-bundles");

        exitCode.ShouldBe(0);
        output.ShouldContain("Checked 0 Sealed Bundle(s).");
        output.ShouldContain("Trusted key (SHA-256): ");
    }

    [Fact]
    public async Task An_unsigned_bundle_fails()
    {
        using var engine = TestEngine.Empty().WithSchemas();
        WriteBundle(engine, sign: null);

        var (exitCode, output) = await engine.RunAsync("verify-bundles");

        exitCode.ShouldBe(1);
        output.ShouldContain("BND-01");
        output.ShouldContain(SignatureVerifier.HasEmbeddedKey ? "The bundle is unsigned." : "no maintainer key");
    }

    [Fact]
    public async Task A_bundle_signed_by_another_key_fails()
    {
        using var engine = TestEngine.Empty().WithSchemas();
        using var impostor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        WriteBundle(engine, sign: impostor);

        var (exitCode, output) = await engine.RunAsync("verify-bundles", "--json");

        exitCode.ShouldBe(1);
        using var report = System.Text.Json.JsonDocument.Parse(output);
        var finding = report.RootElement.GetProperty("findings").EnumerateArray().Single();
        finding.GetProperty("ruleId").GetString().ShouldBe("BND-01");
        finding.GetProperty("message").GetString()!.ShouldContain(SignatureVerifier.HasEmbeddedKey ? "doesn't verify" : "no maintainer key");
    }

    [Fact]
    public async Task A_bundle_that_cannot_be_read_fails()
    {
        if (!SignatureVerifier.HasEmbeddedKey)
        {
            return; // Without a key every bundle already fails as unverifiable (see above).
        }

        using var engine = TestEngine.Empty().WithSchemas();
        engine.WriteFile("sealed/questions/qb-1.1-001.bundle.json", "{\"header\":{},\"ciphertext\":\"\",\"tag\":\"\",\"signature\":\"\"}");

        var (exitCode, output) = await engine.RunAsync("verify-bundles");

        exitCode.ShouldBe(1);

        // The finding is longer than 80 characters and must stay on one line in plain output.
        output.ReplaceLineEndings("\n").Split('\n').ShouldContain(
            "sealed/questions/qb-1.1-001.bundle.json: ERROR BND-01: The bundle can't be verified: the header has no format version.");
    }

    private static void WriteBundle(TestEngine engine, ECDsa? sign)
    {
        var header = BundleHeader.Create("qb-1.1-001", "question", SealTier.Practice, "application/json", new byte[12], DateTimeOffset.UnixEpoch, "1.1", "D1", "practice");
        var bundle = new SealedBundle(header, [1, 2, 3], new byte[16], []);
        if (sign is not null)
        {
            bundle = bundle.WithSignature(sign.SignData(bundle.SigningInput(), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }

        engine.WriteFile("sealed/questions/qb-1.1-001.bundle.json", bundle.ToJsonText());
    }
}
