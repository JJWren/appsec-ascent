using Ascent.Core;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>
/// The Learner's progress store, <c>.ascent/progress.db</c> (P14–P16): WAL journaling, full sync, foreign keys,
/// a 5-second busy timeout, owner-only files, guarded migrations, and a refusal to write to a damaged database.
/// <see cref="Run{T}"/> starts each transaction with <c>BEGIN IMMEDIATE</c>, so the write lock is taken up front.
/// </summary>
public sealed class ProgressDatabase : IDisposable, Ascent.Core.Progress.IProgressTransactions
{
    /// <summary>How long a command waits for another process's write lock, in seconds.</summary>
    public const int BusyTimeoutSeconds = 5;

    private SqliteTransaction? current;

    private ProgressDatabase(SqliteConnection connection, MigrationReport migration)
    {
        Connection = connection;
        Migration = migration;
    }

    /// <summary>Creates a command that joins the current transaction, if any. Callers set a constant <c>CommandText</c>.</summary>
    public SqliteCommand Command() => Statements.Command(Connection, current);

    /// <inheritdoc />
    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Run(() =>
        {
            action();
            return true;
        });
    }

    /// <inheritdoc />
    public T Run<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (current is not null)
        {
            return action();
        }

        using var transaction = Connection.BeginTransaction(deferred: false);
        current = transaction;
        try
        {
            var result = action();
            transaction.Commit();
            return result;
        }
        finally
        {
            current = null;
        }
    }

    /// <summary>The open connection.</summary>
    public SqliteConnection Connection { get; }

    /// <summary>What happened to the schema when the database was opened.</summary>
    public MigrationReport Migration { get; }

    /// <summary>Opens (creating if needed) and migrates the progress database.</summary>
    public static ProgressDatabase Open(EnginePaths paths, TimeProvider time, IOwnerOnlyFiles files) =>
        Open(paths, time, files, Migrator.All);

    /// <summary>
    /// Moves a damaged database and its WAL files into the backups folder as <c>damaged-progress-*.db</c>, so a fresh
    /// one can be created for <c>progress import</c> (REL-U2-03). Backup pruning never touches these files.
    /// </summary>
    public static string MoveAside(EnginePaths paths, TimeProvider time, IOwnerOnlyFiles files)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(files);
        files.CreateDirectory(paths.Backups);
        var stamp = time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var target = Path.Join(paths.Backups, "damaged-progress-" + stamp + ".db");
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            if (File.Exists(paths.Database + suffix))
            {
                File.Move(paths.Database + suffix, target + suffix, overwrite: false);
            }
        }

        return target;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        current?.Dispose();
        Connection.Dispose();
    }

    internal static ProgressDatabase Open(EnginePaths paths, TimeProvider time, IOwnerOnlyFiles files, IReadOnlyList<Migration> migrations)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(files);

        files.CreateDirectory(paths.StateDirectory);
        if (!File.Exists(paths.Database))
        {
            // Create the file owner-only before SQLite opens it; SQLite gives the -wal and -shm files the same mode.
            files.CreateFile(paths.Database, FileMode.CreateNew).Dispose();
        }

        var connection = new SqliteConnection(ConnectionString(paths.Database, SqliteOpenMode.ReadWrite));
        try
        {
            connection.Open();
            Statements.Execute(connection, "PRAGMA journal_mode = WAL;");
            Statements.Execute(connection, "PRAGMA synchronous = FULL;");
            Statements.Execute(connection, "PRAGMA foreign_keys = ON;");
            Statements.Execute(connection, "SELECT count(*) FROM sqlite_master;");

            var report = new Migrator(new DatabaseBackups(paths.Backups, time, files), migrations).Migrate(connection);
            foreach (var path in new[] { paths.Database + "-wal", paths.Database + "-shm" })
            {
                if (File.Exists(path))
                {
                    files.Restrict(path);
                }
            }

            return new ProgressDatabase(connection, report);
        }
        catch (SqliteException ex) when (StorageErrors.IsCorruption(ex))
        {
            connection.Dispose();
            throw new CorruptDatabaseException(paths, ex);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal static string ConnectionString(string path, SqliteOpenMode mode) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = mode,
        Pooling = false,
        DefaultTimeout = BusyTimeoutSeconds,
    }.ToString();
}

/// <summary>Helpers for SQLite errors.</summary>
public static class StorageErrors
{
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    /// <summary>True when SQLite reports a damaged file or a file that isn't a database.</summary>
    public static bool IsCorruption(Exception exception) =>
        exception is SqliteException { SqliteErrorCode: SqliteCorrupt or SqliteNotADatabase };
}

/// <summary>The progress database is damaged; the Engine refuses to write to it (REL-U2-03).</summary>
public sealed class CorruptDatabaseException : AscentException
{
    private const string Hint =
        "Copy the newest file from .ascent/backups/ over .ascent/progress.db, or restore a JSON export with 'ascent progress import <file>'.";

    /// <summary>Creates the error with a default message.</summary>
    public CorruptDatabaseException()
        : this("The progress database is damaged, so the Engine won't write to it.")
    {
    }

    /// <summary>Creates the error with a message.</summary>
    public CorruptDatabaseException(string message)
        : base(message, Hint)
    {
    }

    /// <summary>Creates the error with a message and a cause.</summary>
    public CorruptDatabaseException(string message, Exception innerException)
        : base(message, Hint, ExitCodes.CheckFailed, innerException)
    {
    }

    /// <summary>Creates the error for a database.</summary>
    public CorruptDatabaseException(EnginePaths paths, Exception innerException)
        : base(
            "The progress database (" + Path.GetRelativePath(paths?.RepoRoot ?? ".", paths?.Database ?? "progress.db") + ") is damaged, so the Engine won't write to it.",
            Hint,
            ExitCodes.CheckFailed,
            innerException)
    {
    }
}

/// <summary>The progress database was written by a newer Engine (P16).</summary>
public sealed class NewerDatabaseException : AscentException
{
    private const string Hint = "Update the Engine (git pull), then try again.";

    /// <summary>Creates the error with a default message.</summary>
    public NewerDatabaseException()
        : this("The progress database was written by a newer Engine.")
    {
    }

    /// <summary>Creates the error with a message.</summary>
    public NewerDatabaseException(string message)
        : base(message, Hint)
    {
    }

    /// <summary>Creates the error with a message and a cause.</summary>
    public NewerDatabaseException(string message, Exception innerException)
        : base(message, Hint, ExitCodes.CheckFailed, innerException)
    {
    }

    /// <summary>Creates the error for the two schema versions.</summary>
    public NewerDatabaseException(int databaseVersion, int engineVersion)
        : this(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"The progress database uses schema v{databaseVersion}, but this Engine only knows up to v{engineVersion}."))
    {
    }
}
