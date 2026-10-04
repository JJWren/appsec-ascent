using System.Formats.Tar;
using System.IO.Compression;
using Ascent.Core.Errors;
using Ascent.Core.Platform;

namespace Ascent.Sealing.Unsealing;

/// <summary>
/// Reads and writes the tar.gz archives inside multi-file Sealed items (P9). Extraction refuses links and devices,
/// confines every path, and caps entries and sizes; creation is deterministic so re-sealing is reproducible (P7).
/// </summary>
public static class SafeArchive
{
    /// <summary>The media type of these archives.</summary>
    public const string ContentType = "application/gzip+tar";

    /// <summary>The most entries an archive may hold.</summary>
    public const int MaxEntries = 20_000;

    /// <summary>The most bytes an archive may expand to.</summary>
    public const long MaxTotalBytes = 512L * 1024 * 1024;

    /// <summary>The largest single file.</summary>
    public const long MaxFileBytes = 64L * 1024 * 1024;

    private static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Extracts a tar.gz into <paramref name="destination"/> and returns the number of files written.</summary>
    /// <param name="archive">The archive.</param>
    /// <param name="destination">The folder to extract into.</param>
    /// <param name="overwrite">
    /// True to replace existing files, for Lab Modules and Releases in the Learner workspace. Writes never follow a
    /// link in the destination, so a link can't redirect them outside it.
    /// </param>
    public static int ExtractTarGz(ReadOnlySpan<byte> archive, string destination, bool overwrite = false) =>
        ExtractTarGz(archive, destination, new ArchiveLimits(MaxEntries, MaxTotalBytes, MaxFileBytes), overwrite);

    internal static int ExtractTarGz(ReadOnlySpan<byte> archive, string destination, ArchiveLimits limits, bool overwrite = false)
    {
        Directory.CreateDirectory(destination);
        using var compressed = new MemoryStream(archive.ToArray(), writable: false);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        var entries = 0;
        var files = 0;
        long total = 0;
        while (reader.GetNextEntry(copyData: false) is { } entry)
        {
            if (++entries > limits.Entries)
            {
                throw Refuse("it has too many entries");
            }

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    var folder = SafePath.Resolve(destination, entry.Name.TrimEnd('/'));
                    SafePath.EnsureNoLinks(destination, folder);
                    Directory.CreateDirectory(folder);
                    break;
                case TarEntryType.RegularFile or TarEntryType.V7RegularFile:
                    var path = SafePath.Resolve(destination, entry.Name);
                    SafePath.EnsureNoLinks(destination, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    total += CopyCapped(entry.DataStream, path, limits.FileBytes, limits.TotalBytes - total, overwrite);
                    files++;
                    break;
                default:
                    throw Refuse("it contains a " + entry.EntryType + " entry (only files and folders are allowed)");
            }
        }

        return files;
    }

    /// <summary>
    /// Creates a deterministic tar.gz of a folder: sorted paths, forward slashes, a fixed timestamp, no owners, and
    /// fixed modes (executables keep their execute bit on Unix).
    /// </summary>
    public static byte[] CreateTarGz(string sourceDirectory)
    {
        var root = Path.GetFullPath(sourceDirectory);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => (Full: path, Name: Path.GetRelativePath(root, path).Replace('\\', '/')))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach (var (full, name) in files)
            {
                using var data = File.OpenRead(full);
                var entry = new UstarTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = data,
                    ModificationTime = FixedTime,
                    Mode = IsExecutable(full) ? (UnixFileMode)0b111_101_101 : (UnixFileMode)0b110_100_100,
                    Uid = 0,
                    Gid = 0,
                    UserName = string.Empty,
                    GroupName = string.Empty,
                };
                writer.WriteEntry(entry);
            }
        }

        return output.ToArray();
    }

    private static long CopyCapped(Stream? source, string path, long maxFileBytes, long remainingBudget, bool overwrite)
    {
        using var target = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
        if (source is null)
        {
            return 0;
        }

        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            written += read;
            if (written > maxFileBytes)
            {
                throw Refuse("a file is larger than the per-file limit");
            }

            if (written > remainingBudget)
            {
                throw Refuse("it expands beyond the total size limit");
            }

            target.Write(buffer, 0, read);
        }

        return written;
    }

    private static bool IsExecutable(string path) =>
        !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;

    internal readonly record struct ArchiveLimits(int Entries, long TotalBytes, long FileBytes);

    private static AscentException Refuse(string reason) =>
        new("Refused to unpack a Sealed archive: " + reason + ".", "Report it with 'ascent bug'; the archive may have been tampered with.");
}
