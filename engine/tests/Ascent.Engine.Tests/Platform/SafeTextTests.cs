using Ascent.Core.Platform;

namespace Ascent.Engine.Tests.Platform;

public sealed class SafeTextTests
{
    [Theory]
    [InlineData("\u001b[31mred\u001b[0m", "red")]
    [InlineData("\u001b[38;2;255;0;0mtrue color\u001b[m", "true color")]
    [InlineData("\u001b]8;;https://evil.example\u001b\\click\u001b]8;;\u001b\\", "click")]
    [InlineData("\u001b]0;new window title\u0007visible", "visible")]
    [InlineData("\u001bPdevice control\u001b\\after", "after")]
    [InlineData("\u001bcreset", "reset")]
    [InlineData("\u001b(Bcharset", "charset")]
    [InlineData("\u009b31mC1 CSI", "C1 CSI")]
    [InlineData("\u009d0;title\u009cC1 OSC", "C1 OSC")]
    [InlineData("bell\u0007 and null\0 and backspace\b", "bell and null and backspace")]
    [InlineData("left‮right⁦isolate⁩", "leftrightisolate")]
    [InlineData("tabs\tand\nnewlines", "tabs\tand\nnewlines")]
    [InlineData("windows\r\nline\rendings", "windows\nline\nendings")]
    [InlineData("café ✓ emoji \U0001F600", "café ✓ emoji \U0001F600")]
    public void Strips_control_sequences_and_keeps_text(string input, string expected) =>
        SafeText.Sanitize(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_input_gives_empty_output(string? input) => SafeText.Sanitize(input).ShouldBe(string.Empty);

    [Fact]
    public void Unterminated_sequences_swallow_only_the_sequence()
    {
        SafeText.Sanitize("ok\u001b").ShouldBe("ok");
        SafeText.Sanitize("ok\u001b[12;").ShouldBe("ok");
        SafeText.Sanitize("ok\u001b]never ends").ShouldBe("ok");
    }

    [Fact]
    public void Long_text_is_truncated_with_a_marker()
    {
        var result = SafeText.Sanitize(new string('x', 50), maxLength: 10);
        result.ShouldBe(new string('x', 10) + SafeText.TruncationMarker);
    }

    [Fact]
    public void Truncation_never_splits_a_surrogate_pair()
    {
        var result = SafeText.Sanitize("abc\U0001F600def", maxLength: 4);
        result.ShouldBe("abc" + SafeText.TruncationMarker);
    }

    [Fact]
    public void Maximum_length_must_be_positive() =>
        Should.Throw<ArgumentOutOfRangeException>(() => SafeText.Sanitize("x", 0));

    [Fact]
    public void Bidi_controls_are_recognized()
    {
        SafeText.IsBidiControl('‪').ShouldBeTrue();
        SafeText.IsBidiControl('⁩').ShouldBeTrue();
        SafeText.IsBidiControl('a').ShouldBeFalse();
    }
}
