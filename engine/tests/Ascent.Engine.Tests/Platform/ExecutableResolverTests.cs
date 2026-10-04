using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;

namespace Ascent.Engine.Tests.Platform;

public sealed class ExecutableResolverTests
{
    [Fact]
    public void Finds_a_tool_in_a_fully_qualified_path_entry()
    {
        using var temp = new TempDirectory();
        var bin = temp.Combine("bin");
        var tool = PlantTool(bin, "git");

        var resolver = new ExecutableResolver(Environment(path: bin), null);

        // Windows builds the name from PATHEXT (".EXE"), and its file system ignores case.
        resolver.Resolve(ExternalTool.Git).ShouldBe(tool, StringCompareShould.IgnoreCase);
    }

    [Fact]
    public void Never_finds_a_tool_planted_in_the_working_directory()
    {
        using var temp = new TempDirectory();
        PlantTool(temp.Path, "git");
        PlantTool(temp.Combine("relative", "bin"), "git");

        // ".", an empty entry and relative entries all depend on the current directory, so the resolver skips them.
        // (The current directory is never consulted, so this holds wherever the test runs.) A relative path to the
        // planted tool is added only when one exists: across drives on Windows, GetRelativePath returns an absolute path.
        var relativeToTemp = Path.GetRelativePath(Directory.GetCurrentDirectory(), temp.Path);
        string[] entries = Path.IsPathRooted(relativeToTemp) ? [".", string.Empty, "relative/bin"] : [".", string.Empty, "relative/bin", relativeToTemp];
        var resolver = new ExecutableResolver(Environment(path: string.Join(Path.PathSeparator, entries)), null);

        resolver.Resolve(ExternalTool.Git).ShouldBeNull();
    }

    [Fact]
    public void An_empty_path_finds_nothing_except_the_running_dotnet_host()
    {
        var resolver = new ExecutableResolver(_ => null, null);
        resolver.Resolve(ExternalTool.Git).ShouldBeNull();
        resolver.Resolve(ExternalTool.Docker).ShouldBeNull();
    }

    [Fact]
    public void Dotnet_falls_back_to_the_host_path_set_by_the_sdk()
    {
        using var temp = new TempDirectory();
        var host = PlantTool(temp.Combine("sdk"), "dotnet");
        var resolver = new ExecutableResolver(name => name == "DOTNET_HOST_PATH" ? host : null, null);

        resolver.Resolve(ExternalTool.Dotnet).ShouldBe(host);
    }

    [Fact]
    public void Overrides_win()
    {
        var resolver = new ExecutableResolver(_ => null, new Dictionary<ExternalTool, string> { [ExternalTool.Az] = "/fixed/az" });
        resolver.Resolve(ExternalTool.Az).ShouldBe("/fixed/az");
    }

    [Fact]
    public void A_file_without_execute_permission_is_skipped_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var bin = temp.Combine("bin");
        Directory.CreateDirectory(bin);
        var path = Path.Join(bin, "git");
        File.WriteAllText(path, "#!/bin/sh\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        new ExecutableResolver(Environment(path: bin), null).Resolve(ExternalTool.Git).ShouldBeNull();
    }

    private static Func<string, string?> Environment(string path) =>
        name => name switch
        {
            "PATH" => path,
            "PATHEXT" => ".EXE;.CMD",
            _ => null,
        };

    private static string PlantTool(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Join(folder, OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.WriteAllText(path, "#!/bin/sh\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
