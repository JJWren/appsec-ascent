using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ascent.Core.Platform;

namespace Ascent.Maintainer.Authoring;

/// <summary>One ledger line: what was signed (or which key was created), when, and with which key.</summary>
public sealed record LedgerEntry
{
    /// <summary>When it happened (UTC).</summary>
    public DateTimeOffset Time { get; init; }

    /// <summary><c>keygen</c>, <c>import</c> or <c>sign</c>.</summary>
    public required string Event { get; init; }

    /// <summary>The signed item's catalog ID.</summary>
    public string? ItemId { get; init; }

    /// <summary>SHA-256 of the signed bundle file.</summary>
    public string? BundleSha256 { get; init; }

    /// <summary>The key's SHA-256 fingerprint.</summary>
    public required string KeyFingerprint { get; init; }
}

/// <summary>
/// An append-only, owner-only JSON-lines record of every key event and signature (P6). Because agents may sign
/// unattended, this is how the maintainer audits what was signed.
/// </summary>
public sealed class SigningLedger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IOwnerOnlyFiles files;

    /// <summary>Creates the ledger at <paramref name="path"/>.</summary>
    public SigningLedger(string path, IOwnerOnlyFiles files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(files);
        Path = path;
        this.files = files;
    }

    /// <summary>The ledger file.</summary>
    public string Path { get; }

    /// <summary>Appends an entry.</summary>
    public void Append(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        files.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, JsonOptions) + "\n");
        using var stream = File.Exists(Path)
            ? new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read)
            : files.CreateFile(Path, FileMode.CreateNew);
        stream.Seek(0, SeekOrigin.End);
        stream.Write(line);
    }

    /// <summary>Reads every entry.</summary>
    public IReadOnlyList<LedgerEntry> Read() =>
        File.Exists(Path)
            ? File.ReadAllLines(Path).Where(line => line.Length > 0).Select(line => JsonSerializer.Deserialize<LedgerEntry>(line, JsonOptions)!).ToList()
            : [];
}
