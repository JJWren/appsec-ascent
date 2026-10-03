namespace Ascent.Content.Model;

/// <summary>How serious a finding is. Errors fail CI; warnings never do.</summary>
public enum Severity
{
    /// <summary>Reported, but does not fail the build.</summary>
    Warning,

    /// <summary>Fails the build.</summary>
    Error,
}

/// <summary>
/// A single lint result. Messages identify items by ID and path only and never include Sealed content.
/// </summary>
public sealed record Finding(string RuleId, Severity Severity, string Path, int? Line, string Message, string? Hint = null)
{
    /// <summary>Creates a finding.</summary>
    public static Finding Of(string ruleId, Severity severity, string path, string message, int? line = null, string? hint = null) =>
        new(ruleId, severity, path, line, message, hint);
}
