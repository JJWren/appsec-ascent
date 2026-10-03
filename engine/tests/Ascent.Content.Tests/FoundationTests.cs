using Ascent.Content.Model;

namespace Ascent.Content.Tests;

public sealed class LoaderAndSchemaTests
{
    [Fact]
    public void Baseline_repository_has_no_findings()
    {
        using var repo = TestRepo.Create();
        repo.Lint().ShouldBeEmpty();
    }

    [Fact]
    public void Fully_valid_content_has_no_errors()
    {
        using var repo = Samples.FullyValid();
        repo.Lint().ShouldHaveNoErrors();
    }

    [Fact]
    public void Malformed_yaml_is_reported_as_SCH_02()
    {
        using var repo = TestRepo.Create().Write(Samples.DrillPath, "id: [unclosed\nprompt: x");
        repo.Lint().ShouldContainRule("SCH-02");
    }

    [Fact]
    public void Markdown_without_front_matter_is_reported_as_SCH_02()
    {
        using var repo = TestRepo.Create().Write(Samples.QuestPath, "# No front matter\n");
        repo.Lint().ShouldContainRule("SCH-02");
    }

    [Fact]
    public void Schema_violation_is_reported_as_SCH_01()
    {
        using var repo = TestRepo.Create().Write(Samples.DrillPath, Samples.Drill.Replace("estimatedMinutes: 10", "estimatedMinutes: 99", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("SCH-01");
    }

    [Fact]
    public void Unquoted_commas_in_flow_mappings_are_caught_by_the_schema()
    {
        using var repo = TestRepo.Create().Edit("curriculum/outline/exam-outline-2023-09-15.yaml",
            text => text.Replace("\"Corrupting training or retraining data to manipulate model behavior.\"", "Corrupting data, then more text", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("SCH-01");
    }
}

public sealed class OutlineRuleTests
{
    private const string OutlinePath = "curriculum/outline/exam-outline-2023-09-15.yaml";

    [Fact]
    public void Weights_not_summing_to_100_fail_OUT_01()
    {
        using var repo = TestRepo.Create().Edit(OutlinePath, t => t.Replace("weight: 12", "weight: 13", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("OUT-01");
    }

    [Fact]
    public void Missing_objective_fails_OUT_02()
    {
        using var repo = TestRepo.Create().Edit(OutlinePath, t => t.Replace("      - { id: \"1.2\", title: Security design principles }\n", string.Empty, StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("OUT-02");
    }

    [Fact]
    public void Objective_under_wrong_domain_fails_OUT_02()
    {
        using var repo = TestRepo.Create().Edit(OutlinePath, t => t.Replace("id: \"1.2\"", "id: \"2.10\"", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("OUT-02");
    }

    [Fact]
    public void Long_title_warns_OUT_03()
    {
        using var repo = TestRepo.Create().Edit(OutlinePath,
            t => t.Replace("title: Core security concepts", "title: " + new string('x', 80), StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("OUT-03", Severity.Warning);
    }

    [Fact]
    public void Domain_without_ai_topics_fails_OUT_04()
    {
        using var repo = TestRepo.Create().Edit(OutlinePath, t =>
        {
            var start = t.IndexOf("    aiGuidanceTopics:", StringComparison.Ordinal);
            var end = t.IndexOf("\n\n", start, StringComparison.Ordinal);
            return t[..start] + "    aiGuidanceTopics: []" + t[end..];
        });
        repo.Lint().ShouldContainRule("OUT-04");
    }
}

public sealed class IdRuleTests
{
    [Fact]
    public void Bad_quest_id_fails_ID_01()
    {
        using var repo = Samples.FullyValid().Edit(Samples.QuestPath, t => t.Replace("id: q-1.1", "id: quest-1.1", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("ID-01");
    }

    [Fact]
    public void File_name_must_match_id_ID_01()
    {
        using var repo = Samples.FullyValid().Delete(Samples.DrillPath).Write("curriculum/drills/drl-d1-02.yaml", Samples.Drill);
        repo.Lint().ShouldContainRule("ID-01");
    }

    [Fact]
    public void Duplicate_ids_fail_ID_02()
    {
        using var repo = Samples.FullyValid().Write("curriculum/drills/copy/drl-d1-01.yaml", Samples.Drill)
            .Write("deliverables/rubrics/rub-d3-02.yaml", Samples.Rubric);
        repo.Lint().ShouldContainRule("ID-02");
    }

    [Fact]
    public void Spoiler_word_in_path_fails_ID_03()
    {
        using var repo = Samples.FullyValid().Write("labs/lab-d5-01/sqli-notes.txt", "notes");
        repo.Lint().ShouldContainRule("ID-03");
    }

    [Fact]
    public void Exam_mark_in_file_name_fails_ID_04()
    {
        using var repo = TestRepo.Create().Write("docs/csslp-notes.md", "# Notes\n");
        repo.Lint().ShouldContainRule("ID-04");
    }

    [Fact]
    public void Exam_mark_in_package_id_fails_ID_04()
    {
        using var repo = TestRepo.Create().Write("tools/Tool.csproj", "<Project><PropertyGroup><PackageId>Csslp.Tool</PackageId></PropertyGroup></Project>");
        repo.Lint().ShouldContainRule("ID-04");
    }
}
