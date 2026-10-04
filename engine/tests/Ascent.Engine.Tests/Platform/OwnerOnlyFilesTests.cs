using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;

namespace Ascent.Engine.Tests.Platform;

public sealed class OwnerOnlyFilesTests
{
    private readonly IOwnerOnlyFiles files = OwnerOnlyFiles.ForCurrentOs();

    [Fact]
    public void Created_folders_and_files_are_owner_only()
    {
        using var temp = new TempDirectory();
        var folder = temp.Combine("secret");
        files.CreateDirectory(folder);
        var file = Path.Join(folder, "data.bin");
        files.WriteAllBytes(file, [1, 2, 3]);

        File.ReadAllBytes(file).ShouldBe(new byte[] { 1, 2, 3 });
        AssertOwnerOnly(folder, isDirectory: true);
        AssertOwnerOnly(file, isDirectory: false);
    }

    [Fact]
    public void Existing_items_are_tightened()
    {
        using var temp = new TempDirectory();
        var folder = temp.Combine("existing");
        Directory.CreateDirectory(folder);
        var file = temp.WriteFile("existing/loose.txt", "x");

        files.CreateDirectory(folder);
        files.Restrict(file);
        files.WriteAllText(Path.Join(folder, "replaced.txt"), "first");
        files.WriteAllText(Path.Join(folder, "replaced.txt"), "second");

        File.ReadAllText(Path.Join(folder, "replaced.txt")).ShouldBe("second");
        AssertOwnerOnly(folder, isDirectory: true);
        AssertOwnerOnly(file, isDirectory: false);
        AssertOwnerOnly(Path.Join(folder, "replaced.txt"), isDirectory: false);
    }

    [Fact]
    public void Opening_an_existing_file_keeps_it_owner_only()
    {
        using var temp = new TempDirectory();
        var file = temp.WriteFile("open.txt", "content");
        using (var stream = files.CreateFile(file, FileMode.Open))
        {
            stream.Length.ShouldBe(7);
        }

        AssertOwnerOnly(file, isDirectory: false);
    }

    private static void AssertOwnerOnly(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            AssertWindowsAcl(path, isDirectory);
            return;
        }

        var expected = isDirectory
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.GetUnixFileMode(path).ShouldBe(expected);
    }

    [SupportedOSPlatform("windows")]
    private static void AssertWindowsAcl(string path, bool isDirectory)
    {
        FileSystemSecurity security = isDirectory
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();
        security.AreAccessRulesProtected.ShouldBeTrue();
        using var identity = WindowsIdentity.GetCurrent();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();
        rules.ShouldNotBeEmpty();
        rules.ShouldAllBe(rule => rule.IdentityReference.Equals(identity.User) && rule.AccessControlType == AccessControlType.Allow);
    }
}
