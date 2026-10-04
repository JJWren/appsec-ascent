using System.Text.Json;
using Ascent.Core.Domain;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Sealing.Bundles;

namespace Ascent.Engine.Tests.Sealing;

public sealed class BundleFormatTests
{
    private const string ValidHeader =
        "{\"formatVersion\":1,\"itemId\":\"lab-d5-01.tests\",\"itemType\":\"lab-tests\",\"tier\":\"earned\",\"contentType\":\"application/gzip+tar\","
        + "\"kdfInfo\":\"item:lab-d5-01.tests\",\"nonce\":\"AAAAAAAAAAAAAAAA\",\"createdUtc\":\"2026-10-03T12:00:00Z\"}";

    [Fact]
    public void Headers_round_trip_through_json()
    {
        var header = BundleHeader.Create("qb-2.1-004", "question", SealTier.Practice, "application/json", new byte[12], DateTimeOffset.UnixEpoch, "2.1", "D2", "practice", "AI-3");

        using var document = JsonDocument.Parse(header.ToJson().ToJsonString());
        var parsed = BundleHeader.Parse(document.RootElement);

        parsed.CanonicalBytes().ShouldBe(header.CanonicalBytes());
        parsed.ItemId.ShouldBe("qb-2.1-004");
        parsed.ItemType.ShouldBe("question");
        parsed.Tier.ShouldBe(SealTier.Practice);
        parsed.ContentType.ShouldBe("application/json");
        parsed.KdfInfo.ShouldBe("item:qb-2.1-004");
        parsed.Nonce.ShouldBe(new byte[12]);
        parsed.CreatedUtc.ShouldBe("1970-01-01T00:00:00Z");
        parsed.ObjectiveId.ShouldBe("2.1");
        parsed.ExamDomain.ShouldBe("D2");
        parsed.Pool.ShouldBe("practice");
        parsed.AiTopic.ShouldBe("AI-3");
        parsed.Fields.Keys.ShouldBe(parsed.Fields.Keys.Order(StringComparer.Ordinal));
        header.ToJson().Select(p => p.Key).ShouldBe(header.ToJson().Select(p => p.Key).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("\"secretAnswer\":\"B\",", "not allowlisted")]
    [InlineData("\"formatVersion\":2,", "not supported")]
    [InlineData("\"formatVersion\":\"1\",", "not supported")]
    [InlineData("\"pool\":7,", "malformed")]
    [InlineData("\"pool\":\"exam\",", "pool is unknown")]
    public void Headers_with_bad_fields_are_refused(string replacementOrExtra, string reason)
    {
        var json = replacementOrExtra.StartsWith("\"formatVersion\"", StringComparison.Ordinal)
            ? ValidHeader.Replace("\"formatVersion\":1,", replacementOrExtra, StringComparison.Ordinal)
            : ValidHeader.Replace("{", "{" + replacementOrExtra, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json);
        Should.Throw<BundleFormatException>(() => BundleHeader.Parse(document.RootElement)).Message.ShouldContain(reason);
    }

    [Theory]
    [InlineData("\"formatVersion\":1,", "", "no format version")]
    [InlineData("\"itemType\":\"lab-tests\",", "", "'itemType' is missing")]
    [InlineData("lab-d5-01.tests\",\"itemType", "../escape\",\"itemType", "item ID is malformed")]
    [InlineData("\"lab-tests\"", "\"lab-secrets\"", "item type is unknown")]
    [InlineData("\"earned\"", "\"whenever\"", "tier is unknown")]
    [InlineData("AAAAAAAAAAAAAAAA", "not base64!", "not base64")]
    [InlineData("AAAAAAAAAAAAAAAA", "AAAA", "wrong length")]
    public void Headers_with_missing_or_invalid_values_are_refused(string find, string replace, string reason)
    {
        using var document = JsonDocument.Parse(ValidHeader.Replace(find, replace, StringComparison.Ordinal));
        Should.Throw<BundleFormatException>(() => BundleHeader.Parse(document.RootElement)).Message.ShouldContain(reason);
    }

    [Fact]
    public void A_header_must_be_an_object()
    {
        using var document = JsonDocument.Parse("[1]");
        Should.Throw<BundleFormatException>(() => BundleHeader.Parse(document.RootElement));
    }

    [Fact]
    public void Bundles_round_trip_and_report_their_signature_state()
    {
        using var bundles = new TestBundles();
        var path = bundles.Write("dlv-d3-01.reference", "reference", SealTier.Submitted, "answer"u8.ToArray(), sign: false);

        var unsigned = SealedBundle.Read(path);
        unsigned.IsSigned.ShouldBeFalse();
        var signed = bundles.Sign(unsigned);
        signed.IsSigned.ShouldBeTrue();
        signed.Signature.Length.ShouldBe(64);

        signed.Write(path);
        var reread = SealedBundle.Read(path);
        reread.Signature.ShouldBe(signed.Signature);
        reread.SigningInput().ShouldBe(signed.SigningInput());
        File.ReadAllText(path).ShouldNotContain("\r");
        File.ReadAllText(path).ShouldNotContain("\\u002B");
    }

    [Theory]
    [InlineData("{\"header\":{},\"ciphertext\":\"\",\"tag\":\"\"}", "exactly")]
    [InlineData("{\"header\":{},\"ciphertext\":\"\",\"tag\":\"\",\"signature\":\"\",\"extra\":1}", "exactly")]
    [InlineData("[]", "not an object")]
    [InlineData("not json", "not valid JSON")]
    public void Malformed_bundle_files_are_refused(string content, string reason)
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("x.bundle.json", content);
        Should.Throw<BundleFormatException>(() => SealedBundle.Read(path)).Message.ShouldContain(reason);
    }

    [Theory]
    [InlineData("tag", "AAAA", "tag has the wrong length")]
    [InlineData("signature", "AAAA", "signature has the wrong length")]
    [InlineData("ciphertext", "%%%", "not base64")]
    [InlineData("ciphertext", "7", "not a string")]
    public void Bundle_fields_are_checked(string field, string value, string reason)
    {
        using var bundles = new TestBundles();
        var path = bundles.Write("qb-1.1-001", "question", SealTier.Practice, "{}"u8.ToArray(), pool: "practice");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json[field] = value == "7" ? System.Text.Json.Nodes.JsonValue.Create(7) : System.Text.Json.Nodes.JsonValue.Create(value);
        File.WriteAllText(path, json.ToJsonString());

        Should.Throw<BundleFormatException>(() => SealedBundle.Read(path)).Message.ShouldContain(reason);
    }

    [Fact]
    public void Oversized_and_missing_files_are_refused()
    {
        using var temp = new TempDirectory();
        Should.Throw<FileNotFoundException>(() => SealedBundle.Read(temp.Combine("absent.bundle.json")));

        var big = temp.Combine("big.bundle.json");
        using (var stream = File.Create(big))
        {
            stream.SetLength(SealedBundle.MaxFileBytes + 1);
        }

        Should.Throw<BundleFormatException>(() => SealedBundle.Read(big)).Message.ShouldContain("64 MB");
    }

    [Fact]
    public void Format_exceptions_have_standard_constructors()
    {
        new BundleFormatException().Message.ShouldBe("the bundle is malformed");
        new BundleFormatException("why", new InvalidOperationException()).InnerException.ShouldNotBeNull();
    }
}
