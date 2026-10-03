using Ascent.Content.Model;

namespace Ascent.Content.Tests;

public sealed class QuestRuleTests
{
    private static TestRepo WithQuest(Func<string, string> change) =>
        Samples.FullyValid().Edit(Samples.QuestPath, change);

    [Fact]
    public void Duplicate_quests_for_an_objective_fail_QST_01()
    {
        using var repo = Samples.FullyValid().Write("curriculum/d1-concepts/q-1.1-copy/q-1.1.md", Samples.Quest);
        repo.Lint().ShouldContainRule("QST-01");
    }

    [Fact]
    public void Missing_quests_warn_while_pack_is_in_progress_QST_01()
    {
        using var repo = Samples.FullyValid();
        repo.Lint().ShouldContainRule("QST-01", Severity.Warning);
    }

    [Fact]
    public void Missing_quests_fail_once_pack_is_complete_QST_01()
    {
        using var repo = Samples.FullyValid().MarkPackComplete("d1");
        repo.Lint().ShouldContainRule("QST-01");
    }

    [Fact]
    public void Empty_pack_is_silent_until_started()
    {
        using var repo = TestRepo.Create();
        repo.Lint().ShouldNotContainRule("QST-01");
    }

    [Fact]
    public void Section_without_citation_fails_QST_02()
    {
        using var repo = WithQuest(t => t.Replace("authorized parties.[^c1]", "authorized parties.", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QST-02");
    }

    [Fact]
    public void Unknown_citation_marker_fails_QST_02()
    {
        using var repo = WithQuest(t => t.Replace("authorized parties.[^c1]", "authorized parties.[^c9]", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QST-02");
    }

    [Fact]
    public void Activities_section_needs_no_citation()
    {
        using var repo = Samples.FullyValid();
        repo.Lint().ShouldNotContainRule("QST-02");
    }

    [Fact]
    public void Primary_citation_without_url_fails_QST_03()
    {
        using var repo = WithQuest(t => t.Replace("    url: https://csrc.nist.gov/\n", string.Empty, StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QST-03");
    }

    [Fact]
    public void Book_as_primary_fails_QST_03()
    {
        using var repo = WithQuest(t => t.Replace("kind: standard", "kind: book", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QST-03");
    }

    [Fact]
    public void Quest_without_activities_fails_QST_04()
    {
        using var repo = WithQuest(t => t.Replace("drills: [drl-d1-01]", "drills: []", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QST-04");
    }

    [Fact]
    public void Unresolved_activity_fails_QST_04()
    {
        using var repo = WithQuest(t => t.Replace("labs: []", "labs: [lab-d1-09]", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QST-04");
    }

    [Fact]
    public void Unknown_ai_topic_fails_QST_05()
    {
        using var repo = WithQuest(t => t.Replace("topics: [data-poisoning]", "topics: [made-up-topic]", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QST-05");
    }

    [Fact]
    public void Uncovered_ai_topics_fail_when_pack_complete_QST_05()
    {
        using var repo = Samples.FullyValid().MarkPackComplete("d1");
        repo.Lint().ShouldContainRule("QST-05");
    }

    [Fact]
    public void Unrealistic_minutes_warn_QST_06()
    {
        using var repo = WithQuest(t => t.Replace("estimatedMinutes: 45", "estimatedMinutes: 5", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QST-06", Severity.Warning);
    }

    [Fact]
    public void Wrong_release_fails_QST_07()
    {
        using var repo = WithQuest(t => t.Replace("release: 1", "release: 2", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QST-07");
    }

    [Fact]
    public void Wrong_folder_fails_QST_07()
    {
        using var repo = Samples.FullyValid().Delete(Samples.QuestPath).Write("curriculum/d2-lifecycle/q-1.1.md", Samples.Quest);
        repo.Lint().ShouldContainRule("QST-07");
    }
}
