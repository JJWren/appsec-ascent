using Ascent.Content.Loading;
using Ascent.Content.Model;
using Ascent.Content.Rules;

namespace Ascent.Content.Tests;

/// <summary>
/// Builds a throwaway repository for a test: a compliant baseline copied from the real repository
/// (schemas, outline, season, policy documents), plus whatever files the test adds, changes or deletes.
/// </summary>
internal sealed class TestRepo : IDisposable
{
    private static readonly string[] BaselineFiles =
    [
        "curriculum/outline/exam-outline-2023-09-15.yaml",
        "curriculum/season.yaml",
        "security/exceptions.yaml",
        "README.md",
        "LICENSE",
        "LICENSE-CONTENT",
        "CONTRIBUTING.md",
        "SECURITY.md",
        "CONTEXT.md",
        ".gitignore",
        ".github/ISSUE_TEMPLATE/content-bug.yml",
    ];

    private TestRepo(string root) => Root = root;

    public string Root { get; }

    public string? SealedRoot { get; private set; }

    /// <summary>The real repository root (the folder containing AppSecAscent.slnx).</summary>
    public static string RealRoot { get; } = FindRealRoot();

    public static TestRepo Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "ascent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var repo = new TestRepo(root);

        foreach (var file in Directory.EnumerateFiles(Path.Combine(RealRoot, "schemas"), "*.schema.json"))
        {
            repo.Write("schemas/" + Path.GetFileName(file), File.ReadAllText(file));
        }

        foreach (var file in BaselineFiles)
        {
            repo.Write(file, File.ReadAllText(Path.Combine(RealRoot, file)));
        }

        // The baseline README links to folders that a test repository does not contain.
        repo.Write("README.md", "# Test\nCSSLP® is a registered certification mark of ISC2, Inc. This project is not affiliated with ISC2.\n");
        return repo;
    }

    public TestRepo Write(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        return this;
    }

    public TestRepo WriteSealed(string relativePath, string content)
    {
        SealedRoot ??= Path.Combine(Root, "..", Path.GetFileName(Root) + "-sealed");
        var full = Path.Combine(SealedRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        return this;
    }

    public TestRepo Delete(string relativePath)
    {
        File.Delete(Path.Combine(Root, relativePath));
        return this;
    }

    public TestRepo Edit(string relativePath, Func<string, string> change) =>
        Write(relativePath, change(File.ReadAllText(Path.Combine(Root, relativePath))));

    public TestRepo MarkPackComplete(string packKey) =>
        Edit("curriculum/season.yaml", text => text.Replace("  " + packKey + ": draft", "  " + packKey + ": complete", StringComparison.Ordinal));

    public ContentIndex Load() => ContentLoader.Load(Root, SealedRoot);

    public IReadOnlyList<Finding> Lint() => new RuleEngine().Run(Load());

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        if (SealedRoot is not null && Directory.Exists(SealedRoot))
        {
            Directory.Delete(SealedRoot, recursive: true);
        }
    }

    private static string FindRealRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AppSecAscent.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root (AppSecAscent.slnx).");
    }
}

/// <summary>Assertion helpers over findings.</summary>
internal static class FindingAssertions
{
    public static void ShouldContainRule(this IReadOnlyList<Finding> findings, string ruleId, Severity severity = Severity.Error) =>
        findings.ShouldContain(f => f.RuleId == ruleId && f.Severity == severity,
            "Expected " + ruleId + " (" + severity + ") but got: " + Describe(findings));

    public static void ShouldNotContainRule(this IReadOnlyList<Finding> findings, string ruleId) =>
        findings.ShouldNotContain(f => f.RuleId == ruleId, "Did not expect " + ruleId + " but got: " + Describe(findings));

    public static void ShouldHaveNoErrors(this IReadOnlyList<Finding> findings) =>
        findings.Where(f => f.Severity == Severity.Error).ShouldBeEmpty("Unexpected errors: " + Describe(findings));

    private static string Describe(IReadOnlyList<Finding> findings) =>
        findings.Count == 0 ? "(none)" : string.Join("; ", findings.Select(f => f.RuleId + " " + f.Path + " " + f.Message));
}
