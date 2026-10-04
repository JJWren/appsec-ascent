using Ascent.Cli.Rendering;
using Ascent.Cli.Tests.TestSupport;
using Ascent.Core.Errors;
using Ascent.Storage;
using Spectre.Console;

namespace Ascent.Cli.Tests;

public sealed class RenderingTests
{
    [Theory]
    [InlineData(true, null, null, false, null, true)]
    [InlineData(false, "1", null, false, null, true)]
    [InlineData(false, "", null, false, null, false)]
    [InlineData(false, null, "dumb", false, null, true)]
    [InlineData(false, null, "xterm-256color", false, null, false)]
    [InlineData(false, null, null, true, null, true)]
    [InlineData(false, null, null, false, true, true)]
    [InlineData(false, null, null, false, false, false)]
    public void Plain_mode_has_five_triggers(bool flag, string? noColor, string? term, bool redirected, bool? profile, bool expected)
    {
        string? Environment(string name) => name switch
        {
            "NO_COLOR" => noColor,
            "TERM" => term,
            _ => null,
        };

        RenderMode.IsPlain(flag, Environment, redirected, profile).ShouldBe(expected);
    }

    [Fact]
    public void Plain_output_uses_words_and_aligned_columns()
    {
        using var writer = new StringWriter();
        var renderer = new PlainRenderer(writer);

        renderer.Heading("Status");
        renderer.Status(Outcome.Pass, "Flag accepted");
        renderer.Status(Outcome.Fail, "Tests failing");
        renderer.Status(Outcome.Warn, "Clock skew");
        renderer.Status(Outcome.Info, "FYI");
        renderer.Table(["Rank", "XP"], [["Developer", "0"], ["Security Champion", "120"]]);
        renderer.Line();

        var lines = writer.ToString().ReplaceLineEndings("\n").Split('\n');
        lines[0].ShouldBe("Status");
        lines[1].ShouldBe("------");
        lines[2].ShouldBe("PASS: Flag accepted");
        lines[3].ShouldBe("FAIL: Tests failing");
        lines[4].ShouldBe("WARN: Clock skew");
        lines[5].ShouldBe("INFO: FYI");
        lines[6].ShouldBe("Rank               XP");
        lines[7].ShouldBe("Developer          0");
        lines[8].ShouldBe("Security Champion  120");
        renderer.IsPlain.ShouldBeTrue();
    }

    [Fact]
    public void Untrusted_text_cannot_inject_markup_or_escape_sequences()
    {
        using var writer = new StringWriter();
        var console = Ascent.Cli.Hosting.EngineHost.PlainConsole(writer);
        var renderer = new RichRenderer(console);
        const string hostile = "[red]not markup[/] \u001b]8;;https://evil.example\u001b\\link\u001b]8;;\u001b\\";

        renderer.Heading(hostile);
        renderer.Line(hostile);
        renderer.Status(Outcome.Pass, hostile);
        renderer.Table([hostile], [[hostile]]);

        var text = writer.ToString();
        text.ShouldContain("[red]not markup[/]");
        text.ShouldNotContain("\u001b");
        text.ShouldNotContain("evil.example");
        text.ShouldContain("PASS");
        renderer.IsPlain.ShouldBeFalse();
        renderer.Console.ShouldBe(console);
    }

    [Fact]
    public void Plain_prompts_read_lines_and_retry_until_valid()
    {
        using var output = new StringWriter();
        var prompter = new PlainPrompter(new StringReader("maybe\ny\n\n7\n2\nnope\nok\n  DELETE  \n"), output);

        prompter.Confirm("Continue?").ShouldBeTrue();
        prompter.Confirm("Again?", defaultValue: true).ShouldBeTrue();
        prompter.Choose("Pick one", ["first", "second"]).ShouldBe(1);
        prompter.Ask("Word?", answer => answer == "ok" ? null : "Type ok.").ShouldBe("ok");
        prompter.ConfirmTyped("Type DELETE to confirm:", "DELETE").ShouldBeTrue();

        var text = output.ToString();
        text.ShouldContain("Please answer y or n.");
        text.ShouldContain("1) first");
        text.ShouldContain("Please enter one of the numbers shown.");
        text.ShouldContain("Type ok.");
    }

    [Fact]
    public void Plain_prompts_fail_clearly_when_input_ends()
    {
        var prompter = new PlainPrompter(new StringReader(string.Empty), TextWriter.Null);
        Should.Throw<UsageException>(() => prompter.Confirm("Continue?")).ExitCode.ShouldBe(ExitCodes.Usage);
        Should.Throw<UsageException>(() => prompter.AskSecret("Passphrase:"));
        new PlainPrompter(new StringReader("no\n"), TextWriter.Null).Confirm("Sure?").ShouldBeFalse();
        new PlainPrompter(new StringReader("delete\n"), TextWriter.Null).ConfirmTyped("Type DELETE:", "DELETE").ShouldBeFalse();
    }

    [Fact]
    public void The_profile_can_ask_for_plain_output()
    {
        using var engine = TestEngine.Empty(plain: false);
        using (var output = new StringWriter())
        using (var host = engine.CreateHost(output))
        {
            host.Renderer.IsPlain.ShouldBeFalse();
            new ProfileStore(host.Database).Write(ProfilePeek.PlainModeKey, "true");
        }

        using var later = new StringWriter();
        using var again = engine.CreateHost(later);
        again.Renderer.IsPlain.ShouldBeTrue();
        ProfilePeek.PlainMode(engine.Paths).ShouldBe(true);
    }

    [Fact]
    public void Rich_mode_uses_spectre_prompts_and_plain_mode_uses_line_prompts()
    {
        using var rich = TestEngine.Empty(plain: false);
        using var richOutput = new StringWriter();
        using var richHost = rich.CreateHost(richOutput);
        richHost.Prompter.ShouldBeOfType<SpectrePrompter>();
        richHost.OutputConsole().ShouldNotBeNull();

        using var plain = TestEngine.Empty(plain: true);
        using var plainOutput = new StringWriter();
        using var plainHost = plain.CreateHost(plainOutput);
        plainHost.Prompter.ShouldBeOfType<PlainPrompter>();
    }
}
