using System.Text;
using Ascent.Core;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Sealing;
using Ascent.Sealing.Unsealing;

namespace Ascent.Labs;

/// <summary>One git command in the Learner workspace, shown to the Learner before it runs (P4, REL-03).</summary>
/// <param name="Arguments">git's arguments.</param>
public sealed record GitStep(IReadOnlyList<string> Arguments)
{
    /// <summary>The command as the Learner would type it.</summary>
    public override string ToString() => "git " + string.Join(' ', Arguments.Select(a => a.Contains(' ', StringComparison.Ordinal) ? "\"" + a + "\"" : a));
}

/// <summary>
/// The Learner workspace, <c>my-work/</c> (P4, ADR 0007): the current Release in <c>throughline/</c>, Lab Modules
/// unpacked into it, Deliverables and drills, and, when the Learner agrees, its own git repository. The public
/// repository ignores it.
/// </summary>
public sealed class Workspace
{
    /// <summary>The folder inside <c>throughline/</c> where planted Flags go; the workspace's git ignores it.</summary>
    public const string FlagsFolder = ".flags";

    /// <summary>The workspace's own ignore file.</summary>
    internal const string IgnoreFile = "# Written by AppSec Ascent. Planted Flags and build output never belong in git.\n.flags/\nbin/\nobj/\nTestResults/\n";

    private readonly EnginePaths paths;
    private readonly IProcessRunner processes;

    /// <summary>Creates the workspace over the repository's paths.</summary>
    public Workspace(EnginePaths paths, IProcessRunner processes)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(processes);
        this.paths = paths;
        this.processes = processes;
    }

    /// <summary><c>my-work/</c>.</summary>
    public string Root => paths.Workspace;

    /// <summary><c>my-work/throughline/</c>: the current Release.</summary>
    public string Throughline => Path.Join(Root, "throughline");

    /// <summary><c>my-work/deliverables/</c>.</summary>
    public string Deliverables => Path.Join(Root, "deliverables");

    /// <summary><c>my-work/drills/</c>.</summary>
    public string Drills => Path.Join(Root, "drills");

    /// <summary>True once the workspace exists.</summary>
    public bool Exists => Directory.Exists(Throughline);

    /// <summary>True when the workspace is its own git repository.</summary>
    public bool IsRepository => Directory.Exists(Path.Join(Root, ".git")) || File.Exists(Path.Join(Root, ".git"));

    /// <summary>True when git is installed.</summary>
    public bool GitAvailable => processes.IsAvailable(ExternalTool.Git);

    /// <summary>The commands that make the workspace a repository, listed before they run (P4).</summary>
    public static IReadOnlyList<GitStep> InitSteps { get; } =
    [
        new(["init"]),
        new(["add", "--all"]),
        new(["commit", "--quiet", "--message", "Start my AppSec Ascent workspace"]),
    ];

    /// <summary>
    /// Creates the workspace if it doesn't exist: Release 0 is copied from the public <c>throughline/</c> folder (or an
    /// empty folder is made while there is none), with the workspace's ignore file. Returns true when it was created.
    /// </summary>
    public bool EnsureCreated()
    {
        if (Exists)
        {
            return false;
        }

        Directory.CreateDirectory(Root);
        var publicRelease = Path.Join(paths.RepoRoot, "throughline");
        if (Directory.Exists(publicRelease))
        {
            CopyFolder(publicRelease, Throughline);
        }
        else
        {
            Directory.CreateDirectory(Throughline);
        }

        var ignore = Path.Join(Root, ".gitignore");
        if (!File.Exists(ignore))
        {
            File.WriteAllText(ignore, IgnoreFile, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return true;
    }

    /// <summary>Unpacks a Lab Module into <c>throughline/</c>, replacing the module's files (P4, P9).</summary>
    public int ExtractModule(UnsealedItem module)
    {
        ArgumentNullException.ThrowIfNull(module);
        EnsureCreated();
        return SafeArchive.ExtractTarGz(module.Content, Throughline, overwrite: true);
    }

    /// <summary>
    /// Replaces <c>throughline/</c> with a Release (P4, REL-03). The Release is unpacked into a staging folder first,
    /// so a damaged archive leaves the current Release untouched. Planted Flags go with the old Release.
    /// </summary>
    public int ReplaceRelease(UnsealedItem release)
    {
        ArgumentNullException.ThrowIfNull(release);
        EnsureCreated();
        var staging = Path.Join(Root, ".release-staging");
        DeleteTree(staging);
        int files;
        try
        {
            files = SafeArchive.ExtractTarGz(release.Content, staging);
        }
        catch
        {
            DeleteTree(staging);
            throw;
        }

        DeleteTree(Throughline);
        Directory.Move(staging, Throughline);
        return files;
    }

    /// <summary>True when the workspace repository has uncommitted changes.</summary>
    public async Task<bool> HasChangesAsync(CancellationToken cancellationToken)
    {
        var status = await GitAsync(new GitStep(["status", "--porcelain"]), cancellationToken);
        return status.StandardOutput.Trim().Length > 0;
    }

    /// <summary>Runs git steps in order, stopping at the first failure.</summary>
    public async Task RunAsync(IEnumerable<GitStep> steps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(steps);
        foreach (var step in steps)
        {
            var result = await GitAsync(step, cancellationToken);
            if (!result.Succeeded)
            {
                throw new AscentException(
                    "'" + step + "' failed: " + SafeText.Sanitize(LastLine(result), 300),
                    step.Arguments[0] == "commit"
                        ? "If git asked who you are, set your name and email with 'git config --global user.name' and 'user.email', then try again."
                        : "Fix the problem in my-work/, then try again.");
            }
        }
    }

    private async Task<ProcessResult> GitAsync(GitStep step, CancellationToken cancellationToken)
    {
        if (!GitAvailable)
        {
            throw new ToolNotFoundException(ExternalTool.Git);
        }

        Directory.CreateDirectory(Root);
        return await processes.RunAsync(new ToolCommand(ExternalTool.Git, step.Arguments, Root, ToolTimeouts.Git), cancellationToken);
    }

    private static string LastLine(ProcessResult result)
    {
        var text = (result.StandardError.Trim().Length > 0 ? result.StandardError : result.StandardOutput).Trim();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? (result.TimedOut ? "it timed out" : "exit code " + result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)) : lines[^1];
    }

    // Deletes a folder without following links inside it: a link is removed, never its target.
    private static void DeleteTree(string path)
    {
        var folder = new DirectoryInfo(path);
        if (folder.LinkTarget is not null)
        {
            folder.Delete();
            return;
        }

        if (!folder.Exists)
        {
            return;
        }

        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child && child.LinkTarget is null)
            {
                DeleteTree(child.FullName);
            }
            else
            {
                if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    entry.Attributes &= ~FileAttributes.ReadOnly;
                }

                entry.Delete();
            }
        }

        folder.Delete();
    }

    private static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(s => s is "bin" or "obj" or ".vs"))
            {
                continue;
            }

            var target = Path.Join(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
