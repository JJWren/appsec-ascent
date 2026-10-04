using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;

namespace Ascent.Engine.Tests.Platform;

public sealed class SafePathTests
{
    [Theory]
    [InlineData("labs/lab-d5-01/module.tar.gz")]
    [InlineData("a/./b.txt")]
    [InlineData(@"windows\style\path.txt")]
    [InlineData("deep/er/still/fine.md")]
    public void Relative_paths_inside_the_root_resolve(string relative)
    {
        using var root = new TempDirectory();
        var full = SafePath.Resolve(root.Path, relative);
        SafePath.IsUnder(root.Path, full).ShouldBeTrue();
    }

    [Theory]
    [InlineData("../escape.txt", "'..'")]
    [InlineData("a/../../escape.txt", "'..'")]
    [InlineData(@"a\..\..\escape.txt", "'..'")]
    [InlineData("/etc/passwd", "absolute")]
    [InlineData(@"\\server\share\x", "absolute")]
    [InlineData("C:/Windows/win.ini", "':'")]
    [InlineData("file.txt:hidden-stream", "':'")]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("nul\0byte", "NUL")]
    public void Malformed_or_escaping_paths_are_refused(string relative, string reasonFragment)
    {
        using var root = new TempDirectory();
        SafePath.TryResolve(root.Path, relative, out var full, out var reason).ShouldBeFalse();
        full.ShouldBeEmpty();
        reason.ShouldContain(reasonFragment);

        var error = Should.Throw<UnsafePathException>(() => SafePath.Resolve(root.Path, relative));
        error.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        error.NextStep.ShouldNotBeNull();
    }

    [Fact]
    public void A_sibling_folder_with_a_common_prefix_is_not_inside()
    {
        using var root = new TempDirectory();
        var sibling = root.Path + "-other";
        SafePath.IsUnder(root.Path, Path.Join(sibling, "x.txt")).ShouldBeFalse();
        SafePath.IsUnder(root.Path, root.Path).ShouldBeFalse();
        SafePath.IsUnder(root.Path + Path.DirectorySeparatorChar, Path.Join(root.Path, "x")).ShouldBeTrue();
    }

    [Fact]
    public void Refused_paths_are_sanitized_in_the_message()
    {
        var error = new UnsafePathException("../\u001b[31mred", "it contains '..'");
        error.Message.ShouldNotContain("\u001b");
        error.Message.ShouldContain("it contains '..'");
    }

    [Fact]
    public void Standard_constructors_carry_a_next_step()
    {
        new UnsafePathException().NextStep.ShouldNotBeNull();
        new UnsafePathException("m").Message.ShouldBe("m");
        new UnsafePathException("m", new InvalidOperationException()).InnerException.ShouldBeOfType<InvalidOperationException>();
    }
}
