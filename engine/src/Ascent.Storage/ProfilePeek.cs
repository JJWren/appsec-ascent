using Ascent.Core;
using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>
/// Reads one profile value without creating or migrating the database. Commands that don't touch Learner state
/// (such as <c>lint</c> in CI) can honor the profile's plain-mode setting without creating <c>.ascent/</c>.
/// </summary>
public static class ProfilePeek
{
    /// <summary>The profile key for plain output.</summary>
    public const string PlainModeKey = "plainMode";

    /// <summary>The stored plain-mode setting, or null when there is no database or no setting.</summary>
    public static bool? PlainMode(EnginePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!File.Exists(paths.Database))
        {
            return null;
        }

        try
        {
            using var connection = new SqliteConnection(ProgressDatabase.ConnectionString(paths.Database, SqliteOpenMode.ReadOnly));
            connection.Open();
            using var command = Statements.Command(connection);
            command.CommandText = "SELECT value FROM profile WHERE key = $key;";
            return command.With("$key", PlainModeKey).ExecuteScalar() is string value ? bool.TryParse(value, out var plain) && plain : null;
        }
        catch (SqliteException)
        {
            // No profile table yet, or a damaged file: the command that needs the database will report it.
            return null;
        }
    }
}
