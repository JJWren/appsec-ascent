using Ascent.Core.Errors;

namespace Ascent.Core.Platform;

/// <summary>
/// Confines paths that come from manifests, archives, configuration or arguments to a root folder (SEC-U2-04, P9).
/// </summary>
public static class SafePath
{
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Resolves <paramref name="relativePath"/> under <paramref name="root"/>, or throws <see cref="UnsafePathException"/>.</summary>
    public static string Resolve(string root, string relativePath) =>
        TryResolve(root, relativePath, out var fullPath, out var reason)
            ? fullPath
            : throw new UnsafePathException(relativePath, reason);

    /// <summary>Resolves a relative path under a root. Returns false, with a reason, when it is malformed or escapes the root.</summary>
    public static bool TryResolve(string root, string relativePath, out string fullPath, out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        fullPath = string.Empty;
        reason = Check(relativePath);
        if (reason.Length > 0)
        {
            return false;
        }

        var rootFull = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Join(rootFull, relativePath.Replace('\\', '/')));
        if (!IsUnder(rootFull, candidate))
        {
            reason = "it resolves outside its folder";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="root"/>.</summary>
    public static bool IsUnder(string root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var rootFull = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(rootFull))
        {
            rootFull += Path.DirectorySeparatorChar;
        }

        var full = Path.GetFullPath(path);
        return full.Length > rootFull.Length && full.StartsWith(rootFull, PathComparison);
    }

    private static string Check(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return "it is empty";
        }

        if (relativePath.Contains('\0', StringComparison.Ordinal))
        {
            return "it contains a NUL character";
        }

        if (relativePath.Contains(':', StringComparison.Ordinal))
        {
            return "it contains ':' (drive letters and stream names are not allowed)";
        }

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith('/') || Path.IsPathRooted(relativePath))
        {
            return "it is an absolute path";
        }

        return normalized.Split('/').Any(segment => segment == "..") ? "it contains '..'" : string.Empty;
    }
}

/// <summary>A path tried to leave its folder or was malformed (P9).</summary>
public sealed class UnsafePathException : AscentException
{
    private const string Hint = "If it came from curriculum content, report it with 'ascent bug'.";

    /// <summary>Creates the error with a default message.</summary>
    public UnsafePathException()
        : this("A path was refused because it could leave its folder.")
    {
    }

    /// <summary>Creates the error with a message.</summary>
    public UnsafePathException(string message)
        : base(message, Hint)
    {
    }

    /// <summary>Creates the error with a message and a cause.</summary>
    public UnsafePathException(string message, Exception innerException)
        : base(message, Hint, ExitCodes.CheckFailed, innerException)
    {
    }

    /// <summary>Creates the error for a refused path and the reason.</summary>
    public UnsafePathException(string path, string reason)
        : base("Refused the path '" + SafeText.Sanitize(path, 200) + "': " + reason + ".", Hint)
    {
    }
}
