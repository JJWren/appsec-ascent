using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>
/// Small helpers for parameterized SQL. Callers set <see cref="SqliteCommand.CommandText"/> to a literal, so every
/// statement is constant text and values always travel as parameters (P14).
/// </summary>
public static class Statements
{
    /// <summary>Creates a command on a connection, optionally inside a transaction.</summary>
    public static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        return command;
    }

    /// <summary>Adds a parameter; null becomes <see cref="DBNull"/>.</summary>
    public static SqliteCommand With(this SqliteCommand command, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    // Only for fixed PRAGMA and probe statements inside this assembly.
    internal static void Execute(SqliteConnection connection, string constantSql, SqliteTransaction? transaction = null)
    {
        using var command = Command(connection, transaction);
#pragma warning disable CA2100 // Internal callers pass compile-time constant statements only.
        command.CommandText = constantSql;
#pragma warning restore CA2100
        command.ExecuteNonQuery();
    }
}
