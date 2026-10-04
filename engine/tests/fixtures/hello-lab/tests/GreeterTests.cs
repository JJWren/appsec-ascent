using Xunit;

namespace HelloLab.Tests;

public sealed class GreeterTests
{
    [Fact]
    public void Visitors_are_greeted_by_name() =>
        Assert.Equal("Hello, Ada!", Hello.Greeter.Greet("Ada", Directory.CreateTempSubdirectory().FullName));

    // The exploit-regression test (LABE-02): plant a fresh Flag, then try to capture it the way the exploit does.
    [Fact]
    public void The_backdoor_no_longer_hands_out_a_freshly_planted_flag()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        var flag = "ASCENT{" + Guid.NewGuid().ToString("N") + "}";
        File.WriteAllText(Path.Combine(folder, "hello.txt"), flag);

        Assert.DoesNotContain(flag, Hello.Greeter.Greet("admin", folder), StringComparison.Ordinal);
    }
}
