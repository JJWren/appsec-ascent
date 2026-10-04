using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;

namespace Ascent.Engine.Tests.Platform;

public sealed class BatchArgumentGuardTests
{
    [Theory]
    [InlineData(@"C:\tools\az.cmd", true)]
    [InlineData(@"C:\tools\legacy.BAT", true)]
    [InlineData("/usr/bin/az", false)]
    [InlineData(@"C:\tools\git.exe", false)]
    public void Recognizes_batch_files(string path, bool expected) => BatchArgumentGuard.IsBatchFile(path).ShouldBe(expected);

    [Theory]
    [InlineData("rg-ascent-lab-d5-01")]
    [InlineData(@"C:\Users\joshu\source\repos\CSSLP\infra\lab.bicep")]
    [InlineData("expires-on=2026-10-03T12:00:00Z")]
    [InlineData("a value with spaces")]
    public void Safe_arguments_pass(string argument)
    {
        BatchArgumentGuard.FindUnsafeArgument([argument]).ShouldBe(-1);
        Should.NotThrow(() => BatchArgumentGuard.Validate("az.cmd", [argument]));
    }

    [Theory]
    [InlineData("name\" & calc")]
    [InlineData("%PATH%")]
    [InlineData("!delayed!")]
    [InlineData("caret^")]
    [InlineData("a|b")]
    [InlineData("<in")]
    [InlineData(">out")]
    [InlineData("(paren)")]
    [InlineData("line\nbreak")]
    public void Unsafe_arguments_are_refused_for_batch_files_only(string argument)
    {
        BatchArgumentGuard.FindUnsafeArgument(["ok", argument]).ShouldBe(1);

        var error = Should.Throw<AscentException>(() => BatchArgumentGuard.Validate(@"C:\tools\az.cmd", ["ok", argument]));
        error.Message.ShouldContain("argument 2");
        error.Message.ShouldNotContain(argument);

        Should.NotThrow(() => BatchArgumentGuard.Validate("/usr/bin/az", ["ok", argument]));
    }

    [Fact]
    public async Task The_runner_refuses_before_starting_a_batch_file()
    {
        using var temp = new TempDirectory();
        var batch = temp.WriteFile("tool.cmd", "@echo off\r\necho should not run\r\n");
        var runner = new ProcessRunner(new ExecutableResolver(_ => null, new Dictionary<ExternalTool, string> { [ExternalTool.Az] = batch }));

        await Should.ThrowAsync<AscentException>(() => runner.RunAsync(
            new ToolCommand(ExternalTool.Az, ["group", "delete", "--name", "x & calc"], temp.Path, TimeSpan.FromSeconds(30)),
            TestContext.Current.CancellationToken));
    }
}
