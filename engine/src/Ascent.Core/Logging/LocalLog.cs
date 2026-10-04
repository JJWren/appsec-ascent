using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ascent.Core.Platform;

namespace Ascent.Core.Logging;

/// <summary>
/// One line of the local log (P30). The fields are a fixed allowlist: no content, answers, Flags, AI output,
/// process output or exception messages ever appear.
/// </summary>
public sealed record LogEntry
{
    /// <summary>When the command finished (UTC); set by <see cref="LocalLog"/>.</summary>
    public DateTimeOffset? Time { get; init; }

    /// <summary>The command name, such as <c>standup</c>.</summary>
    public required string Command { get; init; }

    /// <summary>How long the command ran.</summary>
    public long DurationMs { get; init; }

    /// <summary>The exit code.</summary>
    public int ExitCode { get; init; }

    /// <summary>The exception type, for failures.</summary>
    public string? ErrorType { get; init; }

    /// <summary>The stack trace, for unexpected failures. Messages are never logged, because they can quote content.</summary>
    public string? StackTrace { get; init; }

    /// <summary>Business-rule IDs involved, if any.</summary>
    public IReadOnlyList<string>? RuleIds { get; init; }

    /// <summary>Why a Sealed item was refused, as an enum name (P1).</summary>
    public string? SealedFailure { get; init; }
}

/// <summary>An append-only JSON-lines log in <c>.ascent/logs/</c>, kept for 14 days (PRIV-U2-01, P30).</summary>
public sealed class LocalLog
{
    /// <summary>How long log files are kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    private const string Prefix = "ascent-";
    private const string Extension = ".log";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string directory;
    private readonly TimeProvider time;
    private readonly IOwnerOnlyFiles files;

    /// <summary>Creates a log in <paramref name="directory"/>.</summary>
    public LocalLog(string directory, TimeProvider time, IOwnerOnlyFiles files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(files);
        this.directory = directory;
        this.time = time;
        this.files = files;
    }

    /// <summary>Appends an entry. Logging is best effort: an I/O failure never fails the command.</summary>
    public void Write(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var now = time.GetUtcNow();
        var line = JsonSerializer.Serialize(entry with { Time = now }, JsonOptions) + "\n";
        var path = Path.Join(directory, Prefix + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Extension);
        try
        {
            files.CreateDirectory(directory);
            using var stream = File.Exists(path)
                ? new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)
                : files.CreateFile(path, FileMode.CreateNew);
            stream.Seek(0, SeekOrigin.End);
            stream.Write(Encoding.UTF8.GetBytes(line));
        }
        catch (IOException)
        {
            // Another process holds the file, or the disk is full: skip this entry.
        }
        catch (UnauthorizedAccessException)
        {
            // Permissions changed underneath the Engine: skip this entry.
        }
    }

    /// <summary>Deletes log files older than <see cref="Retention"/>.</summary>
    public int Prune()
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var cutoff = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-Retention.Days);
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(directory, Prefix + "*" + Extension))
        {
            var stamp = Path.GetFileNameWithoutExtension(path)[Prefix.Length..];
            if (DateOnly.TryParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < cutoff)
            {
                try
                {
                    File.Delete(path);
                    deleted++;
                }
                catch (IOException)
                {
                    // In use by another process; try again next time.
                }
            }
        }

        return deleted;
    }
}
