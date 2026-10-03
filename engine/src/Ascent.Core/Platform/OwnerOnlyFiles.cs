using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Ascent.Core.Platform;

/// <summary>Creates files and folders that only the current user can access (SEC-U2-03, P20).</summary>
public interface IOwnerOnlyFiles
{
    /// <summary>Creates <paramref name="path"/>, or tightens it if it exists, so only the current user can access it.</summary>
    void CreateDirectory(string path);

    /// <summary>Opens a file that only the current user can read or write.</summary>
    FileStream CreateFile(string path, FileMode mode);

    /// <summary>Tightens an existing file or folder to the current user only.</summary>
    void Restrict(string path);
}

/// <summary>Factory and helpers for <see cref="IOwnerOnlyFiles"/>.</summary>
public static class OwnerOnlyFiles
{
    /// <summary>The adapter for the running operating system.</summary>
    public static IOwnerOnlyFiles ForCurrentOs()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsOwnerOnlyFiles();
        }

        return new UnixOwnerOnlyFiles();
    }

    /// <summary>Writes text (UTF-8, no byte-order mark) to an owner-only file, replacing any existing content.</summary>
    public static void WriteAllText(this IOwnerOnlyFiles files, string path, string text)
    {
        ArgumentNullException.ThrowIfNull(files);
        files.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
    }

    /// <summary>Writes bytes to an owner-only file, replacing any existing content.</summary>
    public static void WriteAllBytes(this IOwnerOnlyFiles files, string path, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var stream = files.CreateFile(path, FileMode.Create);
        stream.Write(bytes);
    }
}

/// <summary>Unix adapter: <c>0700</c> folders and <c>0600</c> files.</summary>
[UnsupportedOSPlatform("windows")]
internal sealed class UnixOwnerOnlyFiles : IOwnerOnlyFiles
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path, DirectoryMode);
        File.SetUnixFileMode(path, DirectoryMode);
    }

    public FileStream CreateFile(string path, FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (mode is FileMode.CreateNew or FileMode.Create or FileMode.OpenOrCreate)
        {
            options.UnixCreateMode = FileMode600;
        }

        var stream = new FileStream(path, options);
        File.SetUnixFileMode(stream.SafeFileHandle, FileMode600);
        return stream;
    }

    public void Restrict(string path) => File.SetUnixFileMode(path, Directory.Exists(path) ? DirectoryMode : FileMode600);
}

/// <summary>Windows adapter: protected ACLs that grant full control to the current user only.</summary>
[SupportedOSPlatform("windows")]
[ExcludeFromCodeCoverage(Justification = "Windows-only adapter. It runs in the Windows test job, outside the Linux coverage run (P24).")]
internal sealed class WindowsOwnerOnlyFiles : IOwnerOnlyFiles
{
    public void CreateDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (directory.Exists)
        {
            directory.SetAccessControl(DirectoryAcl());
        }
        else
        {
            directory.Create(DirectoryAcl());
        }
    }

    public FileStream CreateFile(string path, FileMode mode)
    {
        var existed = File.Exists(path);
        var stream = new FileInfo(path).Create(mode, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, FileAcl());
        if (existed)
        {
            stream.SetAccessControl(FileAcl());
        }

        return stream;
    }

    public void Restrict(string path)
    {
        if (Directory.Exists(path))
        {
            new DirectoryInfo(path).SetAccessControl(DirectoryAcl());
        }
        else
        {
            new FileInfo(path).SetAccessControl(FileAcl());
        }
    }

    private static SecurityIdentifier CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new InvalidOperationException("The current Windows account has no security identifier.");
    }

    private static DirectorySecurity DirectoryAcl()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            CurrentUser(),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    private static FileSecurity FileAcl()
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(CurrentUser(), FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
