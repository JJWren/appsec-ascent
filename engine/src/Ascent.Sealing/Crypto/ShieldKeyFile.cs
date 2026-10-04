using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ascent.Core.Errors;
using Ascent.Core.Platform;

namespace Ascent.Sealing.Crypto;

/// <summary>
/// <c>sealed/shield.json</c>: the 32-byte shield key every Engine uses to derive tier and item keys. It is public by
/// design and kept as data rather than code (ADR 0005).
/// </summary>
public static class ShieldKeyFile
{
    /// <summary>The path of the file, relative to the repository root.</summary>
    public const string RelativePath = "sealed/shield.json";

    private const string Note = "Public by design (ADR 0005): a spoiler shield against accidental exposure, not DRM.";

    /// <summary>Loads and validates the shield key.</summary>
    public static byte[] Load(string repoRoot)
    {
        var path = Path.Join(repoRoot, RelativePath);
        if (!File.Exists(path))
        {
            throw new AscentException("The shield key (" + RelativePath + ") is missing, so Sealed items can't be opened.", "Restore it with 'git checkout -- " + RelativePath + "'.");
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var key = Convert.FromBase64String(root?["shieldKey"]?.GetValue<string>() ?? string.Empty);
            return key.Length == BundleCipher.KeySize ? key : throw new FormatException("The shield key must be 32 bytes.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new AscentException("The shield key (" + RelativePath + ") is damaged.", "Restore it with 'git checkout -- " + RelativePath + "'.", ExitCodes.CheckFailed, ex);
        }
    }

    /// <summary>Creates the file with a fresh random key. It refuses to replace an existing key, which would orphan every bundle.</summary>
    public static string Create(string repoRoot, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var path = Path.GetFullPath(Path.Join(repoRoot, "sealed", "shield.json"));
        if (File.Exists(path))
        {
            throw new AscentException(
                RelativePath + " already exists. Replacing it would make every existing bundle unreadable.",
                "Keep the existing key. To start over, delete it and every bundle together, deliberately.");
        }

        var json = new JsonObject
        {
            ["formatVersion"] = 1,
            ["shieldKey"] = Convert.ToBase64String(random.Bytes(BundleCipher.KeySize)),
            ["note"] = Note,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
        return path;
    }
}
