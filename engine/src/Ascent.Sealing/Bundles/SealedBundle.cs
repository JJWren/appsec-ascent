using System.Buffers.Binary;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ascent.Sealing.Bundles;

/// <summary>
/// The public, encrypted and signed form of a Sealed item: <c>sealed/**/&lt;itemId&gt;.bundle.json</c>
/// (ADR 0005). A bundle with an empty signature has been sealed but not yet signed.
/// </summary>
/// <param name="Header">The authenticated header.</param>
/// <param name="Ciphertext">The AES-256-GCM ciphertext.</param>
/// <param name="Tag">The 16-byte authentication tag.</param>
/// <param name="Signature">The 64-byte ECDSA P-256 signature (IEEE P1363), or empty when unsigned.</param>
public sealed record SealedBundle(BundleHeader Header, byte[] Ciphertext, byte[] Tag, byte[] Signature)
{
    /// <summary>The largest bundle file the Engine reads.</summary>
    public const long MaxFileBytes = 64L * 1024 * 1024;

    /// <summary>The file-name suffix of bundles.</summary>
    public const string FileSuffix = ".bundle.json";

    private static readonly string[] TopLevelFields = ["ciphertext", "header", "signature", "tag"];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>True when the bundle carries a signature.</summary>
    public bool IsSigned => Signature.Length > 0;

    /// <summary>The bytes the maintainer signs (P1).</summary>
    public byte[] SigningInput() => Bundles.SigningInput.Build(Header.CanonicalBytes(), Ciphertext, Tag);

    /// <summary>Reads a bundle file, enforcing the size cap and the exact top-level shape.</summary>
    public static SealedBundle Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The bundle file does not exist.", path);
        }

        if (info.Length > MaxFileBytes)
        {
            throw new BundleFormatException("the bundle is larger than the 64 MB cap");
        }

        using var stream = info.OpenRead();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException ex)
        {
            throw new BundleFormatException("the bundle is not valid JSON", ex);
        }

        using (document)
        {
            return FromJson(document.RootElement);
        }
    }

    /// <summary>Parses a bundle from its JSON root.</summary>
    public static SealedBundle FromJson(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new BundleFormatException("the bundle is not an object");
        }

        var names = root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual(TopLevelFields, StringComparer.Ordinal))
        {
            throw new BundleFormatException("the bundle must have exactly header, ciphertext, tag and signature");
        }

        var header = BundleHeader.Parse(root.GetProperty("header"));
        var tag = Base64(root, "tag");
        var signature = Base64(root, "signature");
        if (tag.Length != Crypto.BundleCipher.TagSize)
        {
            throw new BundleFormatException("the tag has the wrong length");
        }

        if (signature.Length is not 0 and not Crypto.SignatureVerifier.SignatureSize)
        {
            throw new BundleFormatException("the signature has the wrong length");
        }

        return new SealedBundle(header, Base64(root, "ciphertext"), tag, signature);
    }

    /// <summary>The bundle as indented JSON (the header in canonical key order).</summary>
    public string ToJsonText()
    {
        var json = new JsonObject
        {
            ["header"] = Header.ToJson(),
            ["ciphertext"] = Convert.ToBase64String(Ciphertext),
            ["tag"] = Convert.ToBase64String(Tag),
            ["signature"] = Convert.ToBase64String(Signature),
        };
        return json.ToJsonString(WriteOptions) + "\n";
    }

    /// <summary>Writes the bundle as UTF-8 without a byte-order mark, with LF line endings.</summary>
    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToJsonText().ReplaceLineEndings("\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>A copy carrying <paramref name="signature"/>.</summary>
    public SealedBundle WithSignature(byte[] signature) => this with { Signature = signature };

    private static byte[] Base64(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new BundleFormatException("the field '" + name + "' is not a string");
        }

        try
        {
            return Convert.FromBase64String(value.GetString()!);
        }
        catch (FormatException ex)
        {
            throw new BundleFormatException("the field '" + name + "' is not base64", ex);
        }
    }
}

/// <summary>
/// The signed bytes (P1): the label <c>appsec-ascent/bundle/v1</c> and a zero byte, then the canonical header,
/// then the ciphertext, each prefixed with its length as a big-endian 32-bit integer, then the 16-byte tag.
/// </summary>
public static class SigningInput
{
    /// <summary>The domain-separation label.</summary>
    public const string Label = "appsec-ascent/bundle/v1";

    /// <summary>Builds the signing input.</summary>
    public static byte[] Build(ReadOnlySpan<byte> canonicalHeader, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag)
    {
        var label = Encoding.ASCII.GetBytes(Label);
        var result = new byte[label.Length + 1 + 4 + canonicalHeader.Length + 4 + ciphertext.Length + tag.Length];
        var span = result.AsSpan();
        label.CopyTo(span);
        span = span[(label.Length + 1)..];
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)canonicalHeader.Length);
        canonicalHeader.CopyTo(span[4..]);
        span = span[(4 + canonicalHeader.Length)..];
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)ciphertext.Length);
        ciphertext.CopyTo(span[4..]);
        span = span[(4 + ciphertext.Length)..];
        tag.CopyTo(span);
        return result;
    }
}
