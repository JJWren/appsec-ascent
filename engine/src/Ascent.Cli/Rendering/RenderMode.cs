namespace Ascent.Cli.Rendering;

/// <summary>Decides between rich and plain output (P22).</summary>
public static class RenderMode
{
    /// <summary>
    /// Plain output applies when <c>--plain</c> is given, <c>NO_COLOR</c> is set and not empty, <c>TERM=dumb</c>,
    /// output is redirected, or the Learner's profile asks for it.
    /// </summary>
    public static bool IsPlain(bool plainFlag, Func<string, string?> environment, bool outputRedirected, bool? profilePlain)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return plainFlag
            || !string.IsNullOrEmpty(environment("NO_COLOR"))
            || string.Equals(environment("TERM"), "dumb", StringComparison.Ordinal)
            || outputRedirected
            || profilePlain == true;
    }
}
