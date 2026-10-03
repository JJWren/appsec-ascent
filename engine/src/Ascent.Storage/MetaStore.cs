using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>Key/value settings about the database itself (the <c>meta</c> table).</summary>
public static class MetaStore
{
    /// <summary>The schema version key.</summary>
    public const string SchemaVersion = "schemaVersion";

    /// <summary>The latest UTC instant any command has seen (P17).</summary>
    public const string LastSeenUtc = "lastSeenUtc";

    /// <summary>The outline version that progress is keyed to (E10-06).</summary>
    public const string OutlineVersion = "outlineVersion";

    /// <summary>Reads a value, or null.</summary>
    public static string? Get(SqliteConnection connection, string key, SqliteTransaction? transaction = null)
    {
        using var command = Statements.Command(connection, transaction);
        command.CommandText = "SELECT value FROM meta WHERE key = $key;";
        return command.With("$key", key).ExecuteScalar() as string;
    }

    /// <summary>Writes a value.</summary>
    public static void Set(SqliteConnection connection, string key, string value, SqliteTransaction? transaction = null)
    {
        using var command = Statements.Command(connection, transaction);
        command.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value;";
        command.With("$key", key).With("$value", value).ExecuteNonQuery();
    }
}
