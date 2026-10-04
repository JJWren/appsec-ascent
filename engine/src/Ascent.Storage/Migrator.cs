using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>One schema migration, embedded as <c>Migrations/NNNN_name.sql</c>.</summary>
/// <param name="Version">The schema version this migration produces.</param>
/// <param name="Name">The migration's name.</param>
/// <param name="Script">The SQL script.</param>
public sealed record Migration(int Version, string Name, string Script);

/// <summary>What happened to the schema when the database was opened.</summary>
/// <param name="FromVersion">The version before opening (0 for a new database).</param>
/// <param name="ToVersion">The version after opening.</param>
/// <param name="BackupPath">The backup taken before migrating, if any.</param>
public sealed record MigrationReport(int FromVersion, int ToVersion, string? BackupPath);

/// <summary>
/// Applies embedded migrations in order, each in its own transaction (P16). A database that already has data is
/// backed up first, and a database from a newer Engine is refused.
/// </summary>
public sealed partial class Migrator
{
    private const string ResourcePrefix = "migrations/";

    private readonly DatabaseBackups backups;
    private readonly IReadOnlyList<Migration> migrations;

    /// <summary>Creates a migrator over the embedded migrations.</summary>
    public Migrator(DatabaseBackups backups)
        : this(backups, All)
    {
    }

    internal Migrator(DatabaseBackups backups, IReadOnlyList<Migration> migrations)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(migrations);
        this.backups = backups;
        this.migrations = migrations;
    }

    /// <summary>The embedded migrations, ordered by version.</summary>
    public static IReadOnlyList<Migration> All { get; } = Load();

    /// <summary>The newest schema version this Engine knows.</summary>
    public static int LatestVersion => All[^1].Version;

    /// <summary>Brings the database up to the newest version.</summary>
    public MigrationReport Migrate(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var latest = migrations[^1].Version;
        var current = ReadVersion(connection);
        if (current > latest)
        {
            throw new NewerDatabaseException(current, latest);
        }

        var pending = migrations.Where(m => m.Version > current).ToList();
        var backup = pending.Count > 0 && current > 0 ? backups.Create(connection, current) : null;
        foreach (var migration in pending)
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            using (var command = Statements.Command(connection, transaction))
            {
#pragma warning disable CA2100 // The script is an embedded resource compiled into this assembly, not user input.
                command.CommandText = migration.Script;
#pragma warning restore CA2100
                command.ExecuteNonQuery();
            }

            MetaStore.Set(connection, MetaStore.SchemaVersion, migration.Version.ToString(CultureInfo.InvariantCulture), transaction);
            transaction.Commit();
        }

        return new MigrationReport(current, latest, backup);
    }

    /// <summary>Reads the schema version: 0 for a database without the <c>meta</c> table.</summary>
    public static int ReadVersion(SqliteConnection connection)
    {
        using var probe = Statements.Command(connection);
        probe.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'meta';";
        if (Convert.ToInt64(probe.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            return 0;
        }

        var value = MetaStore.Get(connection, MetaStore.SchemaVersion);
        return value is null ? 0 : int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static List<Migration> Load()
    {
        var assembly = typeof(Migrator).Assembly;
        var loaded = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => Read(assembly, name))
            .OrderBy(m => m.Version)
            .ToList();

        for (var i = 0; i < loaded.Count; i++)
        {
            if (loaded[i].Version != i + 1)
            {
                throw new InvalidOperationException("Migrations must be numbered 1, 2, 3… without gaps.");
            }
        }

        return loaded.Count > 0 ? loaded : throw new InvalidOperationException("No migrations are embedded.");
    }

    private static Migration Read(Assembly assembly, string resourceName)
    {
        var fileName = resourceName[ResourcePrefix.Length..];
        var match = MigrationFileName().Match(fileName);
        if (!match.Success)
        {
            throw new InvalidOperationException("Migration file names must look like 0001_name.sql.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Missing embedded migration.");
        using var reader = new StreamReader(stream);
        return new Migration(
            int.Parse(match.Groups["version"].Value, NumberStyles.None, CultureInfo.InvariantCulture),
            match.Groups["name"].Value,
            reader.ReadToEnd());
    }

    [GeneratedRegex(@"^(?<version>\d{4})_(?<name>[a-z0-9_]+)\.sql$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex MigrationFileName();
}
