using Ascent.Core;
using Ascent.Core.Platform;

namespace Ascent.Sealing.Unsealing;

/// <summary>
/// A short-lived, owner-only folder for decrypted items that must exist on disk, such as released tests (P3, SEAL-04).
/// It lives at <c>.ascent/unsealed/&lt;random&gt;/</c>, holds a lock while in use, and is deleted on dispose; the
/// sweeper removes scopes left behind by a killed process.
/// </summary>
public sealed class UnsealedScope : IDisposable
{
    private const string LockFile = ".lock";

    /// <summary>MSBuild stopper files, so released projects don't inherit the public repository's build settings.</summary>
    internal static readonly string[] StopperFiles = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];

    private readonly FileStream lockHandle;
    private bool disposed;

    private UnsealedScope(string directory, FileStream lockHandle)
    {
        Directory = directory;
        this.lockHandle = lockHandle;
    }

    /// <summary>The scope's folder.</summary>
    public string Directory { get; }

    /// <summary>Creates a scope under <see cref="EnginePaths.Unsealed"/>.</summary>
    public static UnsealedScope Create(EnginePaths paths, IOwnerOnlyFiles files, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(random);
        files.CreateDirectory(paths.Unsealed);
        foreach (var stopper in StopperFiles)
        {
            var path = Path.Join(paths.Unsealed, stopper);
            if (!File.Exists(path))
            {
                files.WriteAllText(path, "<Project>\n  <!-- Stops MSBuild from importing the public repository's settings into released projects (P3). -->\n</Project>\n");
            }
        }

        var directory = Path.Join(paths.Unsealed, random.Hex(16));
        files.CreateDirectory(directory);
        return new UnsealedScope(directory, files.CreateFile(Path.Join(directory, LockFile), FileMode.CreateNew));
    }

    /// <summary>Writes a file inside the scope.</summary>
    public string WriteFile(string relativePath, ReadOnlySpan<byte> content, IOwnerOnlyFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var path = SafePath.Resolve(Directory, relativePath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        files.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Extracts a tar.gz item into a subfolder of the scope (P9).</summary>
    public string Extract(UnsealedItem archive, string subfolder)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var destination = SafePath.Resolve(Directory, subfolder);
        SafeArchive.ExtractTarGz(archive.Content, destination);
        return destination;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lockHandle.Dispose();
        UnsealedSweeper.TryDelete(Directory);
    }
}

/// <summary>Removes unsealed scopes whose process is gone: any scope whose lock can be taken (P3).</summary>
public static class UnsealedSweeper
{
    /// <summary>Deletes abandoned scopes and returns how many were removed.</summary>
    public static int Sweep(EnginePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!Directory.Exists(paths.Unsealed))
        {
            return 0;
        }

        var removed = 0;
        foreach (var scope in Directory.EnumerateDirectories(paths.Unsealed))
        {
            if (!IsInUse(scope) && TryDelete(scope))
            {
                removed++;
            }
        }

        return removed;
    }

    internal static bool TryDelete(string directory)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return true;
            }
            catch (IOException)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }

        return false;
    }

    private static bool IsInUse(string scope)
    {
        var lockPath = Path.Join(scope, ".lock");
        if (!File.Exists(lockPath))
        {
            return false;
        }

        try
        {
            using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
