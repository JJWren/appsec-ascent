using System.Globalization;
using Ascent.Core.Platform;
using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>Owner-only online backups of the progress database, taken before migrations; the newest 10 are kept (P16).</summary>
public sealed class DatabaseBackups
{
    /// <summary>How many backups are kept.</summary>
    public const int Keep = 10;

    private const string Prefix = "progress-";

    private readonly string directory;
    private readonly TimeProvider time;
    private readonly IOwnerOnlyFiles files;

    /// <summary>Creates the backup store in <paramref name="directory"/>.</summary>
    public DatabaseBackups(string directory, TimeProvider time, IOwnerOnlyFiles files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(files);
        this.directory = directory;
        this.time = time;
        this.files = files;
    }

    /// <summary>Backs up <paramref name="source"/> and returns the backup's path.</summary>
    public string Create(SqliteConnection source, int schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(source);
        files.CreateDirectory(directory);
        var stamp = time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var baseName = string.Create(CultureInfo.InvariantCulture, $"{Prefix}{stamp}-v{schemaVersion}");
        var path = Path.Join(directory, baseName + ".db");
        for (var suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Join(directory, string.Create(CultureInfo.InvariantCulture, $"{baseName}-{suffix}.db"));
        }

        files.CreateFile(path, FileMode.CreateNew).Dispose();
        using (var destination = new SqliteConnection(ProgressDatabase.ConnectionString(path, SqliteOpenMode.ReadWrite)))
        {
            destination.Open();
            source.BackupDatabase(destination);
        }

        Prune();
        return path;
    }

    /// <summary>The backups, newest first.</summary>
    public IReadOnlyList<string> List() =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, Prefix + "*.db").OrderDescending(StringComparer.Ordinal).ToList()
            : [];

    private void Prune()
    {
        foreach (var old in List().Skip(Keep))
        {
            File.Delete(old);
        }
    }
}
