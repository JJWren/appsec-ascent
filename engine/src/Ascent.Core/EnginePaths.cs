namespace Ascent.Core;

/// <summary>Where the Engine keeps Learner state. Everything here is gitignored in the public repository.</summary>
public sealed class EnginePaths
{
    /// <summary>The file that marks the repository root.</summary>
    public const string SolutionFile = "AppSecAscent.slnx";

    /// <summary>Creates the paths for a repository root.</summary>
    public EnginePaths(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        RepoRoot = Path.GetFullPath(repoRoot);
    }

    /// <summary>The repository root.</summary>
    public string RepoRoot { get; }

    /// <summary><c>.ascent/</c>: progress, backups, logs, unsealed scopes.</summary>
    public string StateDirectory => Path.Join(RepoRoot, ".ascent");

    /// <summary>The progress database.</summary>
    public string Database => Path.Join(StateDirectory, "progress.db");

    /// <summary>Pre-migration database backups (P16).</summary>
    public string Backups => Path.Join(StateDirectory, "backups");

    /// <summary>Private progress exports (BAK-01).</summary>
    public string Exports => Path.Join(StateDirectory, "exports");

    /// <summary>The allowlisted local log (P30).</summary>
    public string Logs => Path.Join(StateDirectory, "logs");

    /// <summary>Ephemeral unsealed scopes (P3).</summary>
    public string Unsealed => Path.Join(StateDirectory, "unsealed");

    /// <summary>The Learner workspace repository, <c>my-work/</c> (P4).</summary>
    public string Workspace => Path.Join(RepoRoot, "my-work");

    /// <summary>The default Teach-back folder.</summary>
    public string DefaultJournal => Path.Join(RepoRoot, "journal");

    /// <summary>
    /// Finds the repository root: <paramref name="explicitRoot"/> when given, otherwise the nearest folder at or above
    /// <paramref name="startDirectory"/> that contains <see cref="SolutionFile"/>, otherwise <paramref name="startDirectory"/>.
    /// </summary>
    public static string FindRepoRoot(string? explicitRoot, string startDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot);
        }

        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        return Path.GetFullPath(startDirectory);
    }
}
