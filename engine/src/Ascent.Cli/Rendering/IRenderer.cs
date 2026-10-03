namespace Ascent.Cli.Rendering;

/// <summary>How a status line is labelled. Meaning never depends on color alone: each outcome has a word (UX-U2-01).</summary>
public enum Outcome
{
    /// <summary>Informational.</summary>
    Info,

    /// <summary>A check passed.</summary>
    Pass,

    /// <summary>A warning.</summary>
    Warn,

    /// <summary>A check failed.</summary>
    Fail,
}

/// <summary>
/// Writes Engine output (P22). Every method takes plain text and sanitizes it (P11); nothing passed in is ever
/// interpreted as markup, so untrusted text can't inject styling or terminal control sequences.
/// </summary>
public interface IRenderer
{
    /// <summary>True for plain, screen-reader-friendly output.</summary>
    bool IsPlain { get; }

    /// <summary>Writes a section heading.</summary>
    void Heading(string text);

    /// <summary>Writes a line of text.</summary>
    void Line(string text = "");

    /// <summary>Writes a line labelled with an outcome word such as PASS or FAIL.</summary>
    void Status(Outcome outcome, string text);

    /// <summary>Writes rows under headers.</summary>
    void Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows);
}

/// <summary>Words for <see cref="Outcome"/>.</summary>
public static class OutcomeWords
{
    /// <summary>The label for an outcome.</summary>
    public static string Of(Outcome outcome) => outcome switch
    {
        Outcome.Pass => "PASS",
        Outcome.Warn => "WARN",
        Outcome.Fail => "FAIL",
        _ => "INFO",
    };
}
