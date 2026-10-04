namespace Ascent.Content.Loading;

/// <summary>Where each kind of content lives, and which schema validates it.</summary>
public static class RepoLayout
{
    /// <summary>Directories never scanned (build output, VCS data, private records, learner state).</summary>
    public static readonly IReadOnlySet<string> ExcludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "aidlc-docs", "node_modules", ".ascent", "journal", "my-work", "TestResults", ".vs", ".idea", "artifacts",
    };

    /// <summary>Schema file used for each kind of document.</summary>
    public static string SchemaFor(DocumentKind kind) => kind switch
    {
        DocumentKind.Outline => "exam-outline.schema.json",
        DocumentKind.OutlineMapping => "outline-mapping.schema.json",
        DocumentKind.Season => "season.schema.json",
        DocumentKind.Quest => "quest.schema.json",
        DocumentKind.Drill => "drill.schema.json",
        DocumentKind.LabManifest => "lab-manifest.schema.json",
        DocumentKind.LabBrief => "lab-brief.schema.json",
        DocumentKind.DeliverableTemplate => "deliverable.schema.json",
        DocumentKind.Rubric => "rubric.schema.json",
        DocumentKind.Bundle => "sealed-bundle.schema.json",
        DocumentKind.ExceptionRegister => "exceptions.schema.json",
        DocumentKind.SealedQuestion => "question.schema.json",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown document kind."),
    };

    /// <summary>Classifies a repository-relative path (forward slashes), or returns null for other files.</summary>
    public static DocumentKind? Classify(string path)
    {
        var segments = path.Split('/');
        var fileName = segments[^1];

        if (path.StartsWith("curriculum/outline/mappings/", StringComparison.Ordinal) && IsYaml(fileName) && segments.Length == 4)
        {
            return DocumentKind.OutlineMapping;
        }

        if (path.StartsWith("curriculum/outline/", StringComparison.Ordinal) && IsYaml(fileName) && segments.Length == 3)
        {
            return DocumentKind.Outline;
        }

        if (path == "curriculum/season.yaml")
        {
            return DocumentKind.Season;
        }

        if (path.StartsWith("curriculum/drills/", StringComparison.Ordinal) && IsYaml(fileName) && segments.Length == 3)
        {
            return DocumentKind.Drill;
        }

        if (path.StartsWith("curriculum/", StringComparison.Ordinal) && fileName.StartsWith("q-", StringComparison.Ordinal)
            && fileName.EndsWith(".md", StringComparison.Ordinal))
        {
            return DocumentKind.Quest;
        }

        if (path.StartsWith("labs/", StringComparison.Ordinal) && segments.Length == 3)
        {
            if (fileName == "lab.yaml")
            {
                return DocumentKind.LabManifest;
            }

            if (fileName == "BRIEF.md")
            {
                return DocumentKind.LabBrief;
            }
        }

        if (path.StartsWith("deliverables/templates/", StringComparison.Ordinal) && IsYaml(fileName) && segments.Length == 3)
        {
            return DocumentKind.DeliverableTemplate;
        }

        if (path.StartsWith("deliverables/rubrics/", StringComparison.Ordinal) && IsYaml(fileName) && segments.Length == 3)
        {
            return DocumentKind.Rubric;
        }

        if (path.StartsWith("sealed/", StringComparison.Ordinal) && fileName.EndsWith(".bundle.json", StringComparison.Ordinal))
        {
            return DocumentKind.Bundle;
        }

        return path == "security/exceptions.yaml" ? DocumentKind.ExceptionRegister : null;
    }

    /// <summary>Pack key for a Quest directory such as <c>d4-design</c>, <c>orientation</c> or <c>capstone</c>.</summary>
    public static string? PackForDirectory(string directoryName)
    {
        if (directoryName is "orientation" or "capstone")
        {
            return directoryName;
        }

        return directoryName.Length >= 2 && directoryName[0] == 'd' && directoryName[1] is >= '1' and <= '8'
            && (directoryName.Length == 2 || directoryName[2] == '-')
            ? directoryName[..2]
            : null;
    }

    private static bool IsYaml(string fileName) =>
        fileName.EndsWith(".yaml", StringComparison.Ordinal) || fileName.EndsWith(".yml", StringComparison.Ordinal);
}
