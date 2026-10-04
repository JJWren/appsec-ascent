using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ascent.Core;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Profile;
using Ascent.Core.Time;
using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>What an import replaced.</summary>
/// <param name="ExportedUtc">When the file was exported.</param>
/// <param name="Rows">Rows imported per table.</param>
/// <param name="BackupPath">The backup taken before anything was replaced.</param>
public sealed record ImportSummary(DateTimeOffset ExportedUtc, IReadOnlyDictionary<string, int> Rows, string BackupPath);

/// <summary>
/// The private progress file (BAK-01, P31): its format, and the checks an import must pass before the database is
/// touched. Table and column names come only from <see cref="Tables"/> and the embedded schema, never from the file.
/// </summary>
public static class ProgressFile
{
    /// <summary>The file's format marker.</summary>
    public const string Format = "appsec-ascent/progress";

    /// <summary>The largest file an import reads.</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>The longest text value an import accepts.</summary>
    public const int MaxTextLength = 65_536;

    private static readonly Lazy<Dictionary<string, IReadOnlyList<(string Name, string Type)>>> Schema = new(LoadSchema);

    /// <summary>Every progress table, in schema order.</summary>
    public static IReadOnlyList<string> Tables { get; } =
    [
        "profile", "xp_events", "rank_history", "standups", "quest_progress", "teachbacks", "review_cards", "reviews",
        "diagnostic", "boss_attempts", "simulation_attempts", "attempt_answers", "lab_state", "cloud_deployments",
        "releases", "season2", "deliverables", "key_releases", "content_bugs",
    ];

    /// <summary>Profile keys never exported or imported: a remote AI endpoint is confirmed again on first use (PRV-02).</summary>
    public static IReadOnlySet<string> ExcludedProfileKeys { get; } = new HashSet<string>(StringComparer.Ordinal) { ProfileKeys.AiRemoteConfirmed };

    /// <summary>The columns carried for a table: everything except Flag salts and hashes (P31).</summary>
    public static IReadOnlyList<(string Name, string Type)> Columns(string table) =>
        Schema.Value.TryGetValue(table, out var columns) ? columns : throw new ArgumentException("Unknown table.", nameof(table));

    /// <summary>Reads and checks a file without touching the database; throws with the problems found.</summary>
    public static JsonObject Read(string path, string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new UsageException("'" + path + "' doesn't exist.", "Give the path of a file written by 'ascent progress export'.");
        }

        if (info.Length > MaxBytes)
        {
            throw Invalid("it's larger than 64 MB");
        }

        JsonObject document;
        try
        {
            document = JsonNode.Parse(File.ReadAllBytes(info.FullName), documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) as JsonObject
                ?? throw Invalid("it isn't a JSON object");
        }
        catch (JsonException ex)
        {
            throw new AscentException("That file can't be imported: it isn't valid JSON.", "Give the path of a file written by 'ascent progress export'.", ExitCodes.CheckFailed, ex);
        }

        var problems = Validate(document, repoRoot);
        return problems.Count == 0
            ? document
            : throw Invalid(string.Join("; ", problems.Take(5)) + (problems.Count > 5 ? string.Create(CultureInfo.InvariantCulture, $"; and {problems.Count - 5} more") : string.Empty));
    }

    /// <summary>The problems that stop a document being imported; empty when it's valid.</summary>
    public static IReadOnlyList<string> Validate(JsonObject document, string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        var problems = new List<string>();
        if (Text(document["format"]) != Format)
        {
            problems.Add("its format isn't " + Format);
            return problems;
        }

        if (document["schemaVersion"] is not JsonValue versionNode || versionNode.GetValueKind() != JsonValueKind.Number || !versionNode.TryGetValue<int>(out var version) || version < 1)
        {
            problems.Add("schemaVersion is missing");
        }
        else if (version > Migrator.LatestVersion)
        {
            throw new NewerDatabaseException(string.Create(CultureInfo.InvariantCulture, $"The export was written by a newer Engine (schema v{version})."));
        }
        else if (version < Migrator.LatestVersion)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"it uses schema v{version}, and this Engine imports v{Migrator.LatestVersion}"));
        }

        if (Utc.TryParse(Text(document["exportedUtc"])) is null)
        {
            problems.Add("exportedUtc is missing");
        }

        if (document["tables"] is not JsonObject tables)
        {
            problems.Add("tables is missing");
            return problems;
        }

        foreach (var (name, _) in tables)
        {
            if (!Tables.Contains(name, StringComparer.Ordinal))
            {
                problems.Add("unknown table '" + SafeText.Sanitize(name, 60) + "'");
            }
        }

        foreach (var table in Tables)
        {
            ValidateTable(table, tables[table], repoRoot, problems);
        }

        return problems;
    }

    internal static AscentException Invalid(string reason) =>
        new("That file can't be imported: " + reason + ".", "Give the path of an unedited file written by 'ascent progress export'.", ExitCodes.CheckFailed);

    internal static string Quote(string identifier) =>
        identifier.Length > 0 && char.IsAsciiLetterLower(identifier[0]) && identifier.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
            ? "\"" + identifier + "\""
            : throw new InvalidOperationException("Unexpected identifier.");

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    private static void ValidateTable(string table, JsonNode? node, string repoRoot, List<string> problems)
    {
        if (node is null)
        {
            return;
        }

        if (node is not JsonArray rows)
        {
            problems.Add(table + " isn't a list");
            return;
        }

        var columns = Columns(table).ToDictionary(c => c.Name, StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            var where = string.Create(CultureInfo.InvariantCulture, $"{table}[{index}]");
            if (rows[index] is not JsonObject row)
            {
                problems.Add(where + " isn't an object");
                continue;
            }

            foreach (var (name, value) in row)
            {
                if (!columns.TryGetValue(name, out var column))
                {
                    problems.Add(where + " has an unknown column '" + SafeText.Sanitize(name, 60) + "'");
                }
                else if (ValueProblem(column.Type, value) is { } problem)
                {
                    problems.Add(where + "." + name + " " + problem);
                }
            }

            if (table == "deliverables" && Text(row["work_path"]) is { } workPath)
            {
                try
                {
                    SafePath.Resolve(repoRoot, workPath);
                }
                catch (UnsafePathException)
                {
                    problems.Add(where + ".work_path must stay inside the repository");
                }
            }
        }
    }

    private static string? ValueProblem(string type, JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        var kind = value.GetValueKind();
        return type switch
        {
            "INTEGER" => kind == JsonValueKind.Number && value.AsValue().TryGetValue<long>(out _) ? null : "must be a whole number",
            "REAL" => kind == JsonValueKind.Number ? null : "must be a number",
            "BLOB" => kind == JsonValueKind.String && Convert.TryFromBase64String(value.GetValue<string>(), new byte[value.GetValue<string>().Length], out _) ? null : "must be base64 text",
            _ => kind != JsonValueKind.String ? "must be text"
                : value.GetValue<string>() is var text && (text.Length > MaxTextLength || text.Any(char.IsControl)) ? "is too long or has control characters"
                : null,
        };
    }

    // The schema the Engine writes, read from an in-memory database built by the embedded migrations.
    private static Dictionary<string, IReadOnlyList<(string Name, string Type)>> LoadSchema()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        foreach (var migration in Migrator.All)
        {
            using var script = Statements.Command(connection);
#pragma warning disable CA2100 // The script is an embedded resource compiled into this assembly, not user input.
            script.CommandText = migration.Script;
#pragma warning restore CA2100
            script.ExecuteNonQuery();
        }

        var schema = new Dictionary<string, IReadOnlyList<(string Name, string Type)>>(StringComparer.Ordinal);
        foreach (var table in Tables)
        {
            using var command = Statements.Command(connection);
            command.CommandText = "SELECT name, upper(type) FROM pragma_table_info($table) ORDER BY cid;";
            using var reader = command.With("$table", table).ExecuteReader();
            var columns = new List<(string Name, string Type)>();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (table != "lab_state" || name is not ("flag_salt" or "flag_hash"))
                {
                    columns.Add((name, reader.GetString(1)));
                }
            }

            schema[table] = columns.Count > 0 ? columns : throw new InvalidOperationException("A progress table is missing from the schema.");
        }

        return schema;
    }
}

/// <summary>Writes all local progress to a private JSON file and restores it (BAK-01, P31).</summary>
public sealed class ProgressExporter
{
    private readonly ProgressDatabase database;
    private readonly EnginePaths paths;
    private readonly TimeProvider time;
    private readonly IOwnerOnlyFiles files;

    /// <summary>Creates the exporter.</summary>
    public ProgressExporter(ProgressDatabase database, EnginePaths paths, TimeProvider time, IOwnerOnlyFiles files)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(files);
        this.database = database;
        this.paths = paths;
        this.time = time;
        this.files = files;
    }

    /// <summary>The default export file: <c>.ascent/exports/progress-yyyyMMddTHHmmssZ.json</c>, which git ignores.</summary>
    public string DefaultPath() =>
        Path.Join(paths.Exports, "progress-" + time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + ".json");

    /// <summary>Writes the export to a new owner-only file and returns the rows written per table.</summary>
    public IReadOnlyDictionary<string, int> Export(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (File.Exists(full) || Directory.Exists(full))
        {
            throw new UsageException("'" + path + "' already exists.", "Choose a new file name, or leave it out to use .ascent/exports/.");
        }

        var (document, counts) = database.Run(Build);
        if (Path.GetDirectoryName(full) is { } directory && !Directory.Exists(directory))
        {
            files.CreateDirectory(directory);
        }

        using (var stream = files.CreateFile(full, FileMode.CreateNew))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            document.WriteTo(writer);
        }

        return counts;
    }

    /// <summary>
    /// Replaces all progress with a checked export, in one transaction, after backing up the database (P16). Any
    /// constraint failure rolls everything back.
    /// </summary>
    public ImportSummary Import(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (ProgressFile.Validate(document, paths.RepoRoot) is { Count: > 0 } problems)
        {
            throw ProgressFile.Invalid(problems[0]);
        }

        var backup = new DatabaseBackups(paths.Backups, time, files).Create(database.Connection, Migrator.LatestVersion);
        var tables = (JsonObject)document["tables"]!;
        try
        {
            var rows = database.Run(() => ProgressFile.Tables.ToDictionary(table => table, table => Replace(table, tables[table] as JsonArray ?? []), StringComparer.Ordinal));
            return new ImportSummary(Utc.Parse(document["exportedUtc"]!.GetValue<string>()), rows, backup);
        }
        catch (SqliteException ex) when (!StorageErrors.IsCorruption(ex))
        {
            throw new AscentException(
                "The export couldn't be imported, so nothing was changed: a row breaks the schema's rules.",
                "Check that the file came from 'ascent progress export' and hasn't been edited.",
                ExitCodes.CheckFailed,
                ex);
        }
    }

    private (JsonObject Document, IReadOnlyDictionary<string, int> Counts) Build()
    {
        var tables = new JsonObject();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var table in ProgressFile.Tables)
        {
            var columns = ProgressFile.Columns(table);
            var rows = new JsonArray();
            using var command = database.Command();
#pragma warning disable CA2100 // Table and column names come from the fixed table list and the embedded schema, never from input.
            command.CommandText = "SELECT " + string.Join(", ", columns.Select(c => ProgressFile.Quote(c.Name))) + " FROM " + ProgressFile.Quote(table) + " ORDER BY rowid;";
#pragma warning restore CA2100
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new JsonObject();
                for (var i = 0; i < columns.Count; i++)
                {
                    row[columns[i].Name] = reader.IsDBNull(i) ? null : reader.GetValue(i) switch
                    {
                        long number => JsonValue.Create(number),
                        double number => JsonValue.Create(number),
                        byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
                        var other => JsonValue.Create(Convert.ToString(other, CultureInfo.InvariantCulture)),
                    };
                }

                if (table != "profile" || !ProgressFile.ExcludedProfileKeys.Contains(row["key"]!.GetValue<string>()))
                {
                    rows.Add(row);
                }
            }

            tables[table] = rows;
            counts[table] = rows.Count;
        }

        var document = new JsonObject
        {
            ["format"] = ProgressFile.Format,
            ["schemaVersion"] = Migrator.LatestVersion,
            ["exportedUtc"] = Utc.ToText(time.GetUtcNow()),
            ["note"] = "Private: your AppSec Ascent progress. Don't commit or share this file.",
            ["tables"] = tables,
        };
        return (document, counts);
    }

    private int Replace(string table, JsonArray rows)
    {
        var columns = ProgressFile.Columns(table);
        using (var delete = database.Command())
        {
#pragma warning disable CA2100 // The table name comes from the fixed table list, never from input.
            delete.CommandText = "DELETE FROM " + ProgressFile.Quote(table) + ";";
#pragma warning restore CA2100
            delete.ExecuteNonQuery();
        }

        var count = 0;
        foreach (var row in rows.OfType<JsonObject>())
        {
            if (table == "profile" && row["key"] is JsonValue key && key.TryGetValue<string>(out var name) && ProgressFile.ExcludedProfileKeys.Contains(name))
            {
                continue;
            }

            var present = columns.Where(c => row.ContainsKey(c.Name)).ToList();
            using var insert = database.Command();
#pragma warning disable CA2100 // Names come from the fixed table list and the embedded schema; values travel as parameters.
            insert.CommandText = "INSERT INTO " + ProgressFile.Quote(table) + " (" + string.Join(", ", present.Select(c => ProgressFile.Quote(c.Name))) + ") VALUES ("
                + string.Join(", ", present.Select((_, i) => "$p" + i.ToString(CultureInfo.InvariantCulture))) + ");";
#pragma warning restore CA2100
            for (var i = 0; i < present.Count; i++)
            {
                insert.With("$p" + i.ToString(CultureInfo.InvariantCulture), ToDatabase(present[i].Type, row[present[i].Name]));
            }

            insert.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static object? ToDatabase(string type, JsonNode? value) => value is null ? null : type switch
    {
        "INTEGER" => value.GetValue<long>(),
        "REAL" => value.GetValue<double>(),
        "BLOB" => Convert.FromBase64String(value.GetValue<string>()),
        _ => value.GetValue<string>(),
    };
}
