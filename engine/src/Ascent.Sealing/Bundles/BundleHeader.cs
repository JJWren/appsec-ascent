using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ascent.Content.Rules;
using Ascent.Core.Domain;

namespace Ascent.Sealing.Bundles;

/// <summary>
/// The allowlisted, non-spoiler metadata of a Sealed Bundle (BND-02). The header is authenticated twice: it is part
/// of the signed bytes and it is the AES-GCM associated data (SEAL-01, SEAL-02).
/// </summary>
public sealed partial class BundleHeader
{
    /// <summary>The only bundle format this Engine understands.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Item types a bundle may carry.</summary>
    public static readonly IReadOnlySet<string> ItemTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "question", "lab-module", "lab-tests", "lab-fix", "lab-plant", "reference", "release", "drill-answer", "deep-dive-solution",
    };

    private static readonly string[] RequiredFields = ["formatVersion", "itemId", "itemType", "tier", "contentType", "kdfInfo", "nonce", "createdUtc"];
    private static readonly HashSet<string> Pools = new(StringComparer.Ordinal) { "practice", "diagnostic", "simulation" };

    private readonly SortedDictionary<string, string> fields;

    private BundleHeader(SortedDictionary<string, string> fields, SealTier tier, byte[] nonce)
    {
        this.fields = fields;
        Tier = tier;
        Nonce = nonce;
    }

    /// <summary>The item's catalog ID, such as <c>lab-d5-01.tests</c>.</summary>
    public string ItemId => fields["itemId"];

    /// <summary>The item type, such as <c>lab-tests</c>.</summary>
    public string ItemType => fields["itemType"];

    /// <summary>The key-release tier.</summary>
    public SealTier Tier { get; }

    /// <summary>The plaintext's media type.</summary>
    public string ContentType => fields["contentType"];

    /// <summary>The HKDF info label; must be <c>item:&lt;itemId&gt;</c> (P2).</summary>
    public string KdfInfo => fields["kdfInfo"];

    /// <summary>The 12-byte AES-GCM nonce.</summary>
    public byte[] Nonce { get; }

    /// <summary>When the item was sealed (UTC text).</summary>
    public string CreatedUtc => fields["createdUtc"];

    /// <summary>The question pool, for questions.</summary>
    public string? Pool => fields.GetValueOrDefault("pool");

    /// <summary>The Objective, when known.</summary>
    public string? ObjectiveId => fields.GetValueOrDefault("objectiveId");

    /// <summary>The exam Domain, when known.</summary>
    public string? ExamDomain => fields.GetValueOrDefault("examDomain");

    /// <summary>The AI-guidance topic, when the item covers one.</summary>
    public string? AiTopic => fields.GetValueOrDefault("aiTopic");

    /// <summary>All string fields, sorted by name (formatVersion is always 1 and not listed).</summary>
    public IReadOnlyDictionary<string, string> Fields => fields;

    /// <summary>Creates a header for sealing.</summary>
    public static BundleHeader Create(
        string itemId,
        string itemType,
        SealTier tier,
        string contentType,
        byte[] nonce,
        DateTimeOffset created,
        string? objectiveId = null,
        string? examDomain = null,
        string? pool = null,
        string? aiTopic = null)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["itemId"] = itemId,
            ["itemType"] = itemType,
            ["tier"] = SealTiers.Name(tier),
            ["contentType"] = contentType,
            ["kdfInfo"] = "item:" + itemId,
            ["nonce"] = Convert.ToBase64String(nonce),
            ["createdUtc"] = created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        AddOptional(values, "objectiveId", objectiveId);
        AddOptional(values, "examDomain", examDomain);
        AddOptional(values, "pool", pool);
        AddOptional(values, "aiTopic", aiTopic);
        return Validate(values);
    }

    /// <summary>Reads and validates a header from bundle JSON.</summary>
    public static BundleHeader Parse(JsonElement header)
    {
        if (header.ValueKind != JsonValueKind.Object)
        {
            throw new BundleFormatException("the header is not an object");
        }

        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var sawVersion = false;
        foreach (var property in header.EnumerateObject())
        {
            if (!BundleRules.HeaderAllowlist.Contains(property.Name))
            {
                throw new BundleFormatException("the header field '" + property.Name + "' is not allowlisted");
            }

            if (property.Name == "formatVersion")
            {
                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var version) || version != CurrentFormatVersion)
                {
                    throw new BundleFormatException("the format version is not supported");
                }

                sawVersion = true;
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String || values.ContainsKey(property.Name))
            {
                throw new BundleFormatException("the header field '" + property.Name + "' is malformed");
            }

            values[property.Name] = property.Value.GetString()!;
        }

        if (!sawVersion)
        {
            throw new BundleFormatException("the header has no format version");
        }

        return Validate(values);
    }

    /// <summary>The canonical header bytes that are signed and used as AES-GCM associated data (P1).</summary>
    public byte[] CanonicalBytes() => HeaderCanonicalizer.Canonicalize(fields, CurrentFormatVersion);

    /// <summary>The header as a JSON object, keys in canonical order.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject();
        var withVersion = fields.Keys.Append("formatVersion").Order(StringComparer.Ordinal);
        foreach (var key in withVersion)
        {
            json[key] = key == "formatVersion" ? CurrentFormatVersion : fields[key];
        }

        return json;
    }

    private static void AddOptional(SortedDictionary<string, string> values, string key, string? value)
    {
        if (value is not null)
        {
            values[key] = value;
        }
    }

    private static BundleHeader Validate(SortedDictionary<string, string> values)
    {
        foreach (var required in RequiredFields.Where(f => f != "formatVersion"))
        {
            if (!values.ContainsKey(required))
            {
                throw new BundleFormatException("the header field '" + required + "' is missing");
            }
        }

        if (!ItemIdPattern().IsMatch(values["itemId"]))
        {
            throw new BundleFormatException("the item ID is malformed");
        }

        if (!ItemTypes.Contains(values["itemType"]))
        {
            throw new BundleFormatException("the item type is unknown");
        }

        if (!SealTiers.TryParse(values["tier"], out var tier))
        {
            throw new BundleFormatException("the tier is unknown");
        }

        if (values.TryGetValue("pool", out var pool) && !Pools.Contains(pool))
        {
            throw new BundleFormatException("the pool is unknown");
        }

        byte[] nonce;
        try
        {
            nonce = Convert.FromBase64String(values["nonce"]);
        }
        catch (FormatException)
        {
            throw new BundleFormatException("the nonce is not base64");
        }

        return nonce.Length == Crypto.BundleCipher.NonceSize
            ? new BundleHeader(values, tier, nonce)
            : throw new BundleFormatException("the nonce has the wrong length");
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ItemIdPattern();
}

/// <summary>A bundle couldn't be read; the reason is internal and never shown with content (P1).</summary>
public sealed class BundleFormatException : Exception
{
    /// <summary>Creates the error with a default message.</summary>
    public BundleFormatException()
        : this("the bundle is malformed")
    {
    }

    /// <summary>Creates the error with a reason.</summary>
    public BundleFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the error with a reason and a cause.</summary>
    public BundleFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
