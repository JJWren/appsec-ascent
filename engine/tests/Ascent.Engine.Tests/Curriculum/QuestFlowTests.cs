using Ascent.Core.Curriculum;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Profile;
using Ascent.Core.Progress;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Storage;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Curriculum;

/// <summary>The profile, settings, the Quest flow, Teach-backs and the exam-date prompt (E1-04, E1-07, E2-01, E2-04, EXM-01).</summary>
public sealed class QuestFlowTests
{
    [Fact]
    public void The_profile_is_created_once_with_a_random_local_id()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var service = new ProfileService(new ProfileStore(game.Database), game.Clock, game.Random);

        var first = service.EnsureProfile();
        var second = service.EnsureProfile();

        first.LearnerId.ShouldNotBeNull();
        first.LearnerId!.Length.ShouldBe(32);
        second.LearnerId.ShouldBe(first.LearnerId);
        first.Values[ProfileKeys.CreatedUtc].ShouldBe("2026-10-05T09:00:00.000Z");
        first.RulesAccepted.ShouldBeFalse();

        service.AcceptRules();
        service.Load().RulesAccepted.ShouldBeTrue();
        game.Facts.RulesAccepted.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ProfileKeys.WeeklyHours, "8", true)]
    [InlineData(ProfileKeys.WeeklyHours, "0", false)]
    [InlineData(ProfileKeys.WeeklyHours, "41", false)]
    [InlineData(ProfileKeys.WeeklyHours, "-3", false)]
    [InlineData(ProfileKeys.TimeZone, "UTC", true)]
    [InlineData(ProfileKeys.TimeZone, "Mars/Olympus_Mons", false)]
    [InlineData(ProfileKeys.PlainMode, "true", true)]
    [InlineData(ProfileKeys.PlainMode, "yes", false)]
    [InlineData(ProfileKeys.GitHubUser, "JJWren", true)]
    [InlineData(ProfileKeys.GitHubUser, "-bad-", false)]
    [InlineData(ProfileKeys.AiEndpoint, "http://localhost:11434/v1", true)]
    [InlineData(ProfileKeys.AiEndpoint, "ftp://localhost/", false)]
    [InlineData(ProfileKeys.AiEndpoint, "http://example.com/v1", false)]
    [InlineData(ProfileKeys.AiModel, "llama3.2", true)]
    [InlineData(ProfileKeys.ExamDate, "2027-02-15", true)]
    [InlineData(ProfileKeys.ExamDate, "15/02/2027", false)]
    [InlineData(ProfileKeys.TeachBackFolder, "notes/teachbacks", true)]
    [InlineData(ProfileKeys.TeachBackFolder, " ", false)]
    public void Settings_are_validated_before_they_are_stored(string key, string value, bool valid)
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var service = new ProfileService(new ProfileStore(game.Database), game.Clock, game.Random);

        if (valid)
        {
            service.Set(key, value);
            service.Load().Values[key].ShouldBe(value.Trim());
            service.Unset(key);
            service.Load().Values.ShouldNotContainKey(key);
        }
        else
        {
            Should.Throw<UsageException>(() => service.Set(key, value)).NextStep!.ShouldContain("ascent config");
            service.Load().Values.ShouldNotContainKey(key);
        }
    }

    [Fact]
    public void Unknown_settings_are_refused_and_profile_values_are_typed()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var service = new ProfileService(new ProfileStore(game.Database), game.Clock, game.Random);

        Should.Throw<UsageException>(() => service.Set("learnerId", "mine")).Message.ShouldContain("no setting called 'learnerId'");
        Should.Throw<UsageException>(() => service.Unset("nope"));

        service.Set(ProfileKeys.WeeklyHours, " 12 ");
        service.Set(ProfileKeys.AiEndpoint, "http://127.0.0.1:11434/v1");
        service.Set(ProfileKeys.ExamDate, "2027-02-15");
        service.ConfirmRemoteEndpoint(new Uri("https://models.example.com/v1/chat"));
        var profile = service.Load();
        profile.WeeklyHours.ShouldBe(12);
        profile.AiEndpoint.ShouldBe(new Uri("http://127.0.0.1:11434/v1"));
        profile.ExamDate.ShouldBe(new DateOnly(2027, 2, 15));
        profile.Values[ProfileKeys.AiRemoteConfirmed].ShouldBe("https://models.example.com");
        profile.TimeZone.ShouldBeNull();
        profile.GitHubUser.ShouldBeNull();
        profile.AiModel.ShouldBeNull();
        profile.TeachBackFolder.ShouldBeNull();
    }

    [Fact]
    [Trait("Rule", "EXM-01")]
    public void The_exam_prompt_appears_once_the_season_is_complete_until_a_date_is_set()
    {
        var today = new DateOnly(2027, 1, 10);
        var empty = new LearnerProfile(new Dictionary<string, string>());

        ExamPrompt.ShouldPrompt(seasonComplete: false, empty, today).ShouldBeFalse();
        ExamPrompt.ShouldPrompt(seasonComplete: true, empty, today).ShouldBeTrue();

        var snoozed = new LearnerProfile(new Dictionary<string, string> { [ProfileKeys.ExamPromptSnoozedUntil] = "2027-01-11" });
        ExamPrompt.ShouldPrompt(true, snoozed, today).ShouldBeFalse();
        ExamPrompt.ShouldPrompt(true, snoozed, today.AddDays(1)).ShouldBeTrue();

        var booked = new LearnerProfile(new Dictionary<string, string> { [ProfileKeys.ExamDate] = "2027-02-15" });
        ExamPrompt.ShouldPrompt(true, booked, today).ShouldBeFalse();

        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var service = new ProfileService(new ProfileStore(game.Database), game.Clock, game.Random);
        service.SnoozeExamPrompt(today);
        service.Load().ExamPromptSnoozedUntil.ShouldBe(today.AddDays(1));
    }

    [Fact]
    public void Quests_run_in_exam_order_with_objectives_sorted_numerically()
    {
        using var fixture = CurriculumFixture.Create()
            .Quest("q-cap-1", "CAP-1", "CAP")
            .Quest("q-2.10", "2.10", "D2")
            .Quest("q-2.9", "2.9", "D2")
            .Quest("q-1.2", "1.2", "D1")
            .Quest("q-ori-1", "ORI-1", "ORI");
        using var game = new GameHarness(fixture);

        game.Catalog.Quests.Select(q => q.Id).ShouldBe(["q-ori-1", "q-1.2", "q-2.9", "q-2.10", "q-cap-1"]);
        game.Catalog.BossDomains.ShouldBe(["D1", "D2"]);
        CurriculumCatalog.ObjectiveKey("4.10").CompareTo(CurriculumCatalog.ObjectiveKey("4.9")).ShouldBePositive();
        CurriculumCatalog.DomainRank("ZZ").ShouldBe(10);

        var quests = game.Quests;
        quests.Next()!.Id.ShouldBe("q-ori-1");
        Should.Throw<UsageException>(() => quests.Find("q-9.9")).NextStep!.ShouldContain("ascent next");
    }

    [Fact]
    [Trait("Rule", "XP-01")]
    public void A_lesson_completes_with_its_teach_back_and_the_quest_completes_with_its_activities()
    {
        using var fixture = CurriculumFixture.Create()
            .Quest("q-1.1", "1.1", "D1", drills: ["drl-d1-01"])
            .Drill("drl-d1-01", "1.1")
            .Quest("q-1.2", "1.2", "D1");
        using var game = new GameHarness(fixture);
        var quests = game.Quests;
        var teachBacks = new TeachBackService(game.TeachBackStore, Path.Join(fixture.Root, "journal"), game.Clock);

        quests.Open("q-1.1").Status.ShouldBe(QuestStatus.InProgress);
        Should.Throw<AscentException>(() => quests.CompleteLesson("q-1.1")).NextStep.ShouldBe("Run 'ascent teachback q-1.1'.");

        teachBacks.Save("q-1.1", "Confidentiality keeps data from people who shouldn't see it.", forLab: false);
        var view = quests.CompleteLesson("q-1.1");
        view.LessonDone.ShouldBeTrue();
        view.Status.ShouldBe(QuestStatus.InProgress);
        view.Activities.ShouldBe([new ActivityState("drill", "drl-d1-01", false, false)]);
        quests.ObjectivesWithLessonDone().ShouldBe(["1.1"]);

        // The drill is done (Milestone E awards it); the Quest then completes, and Lesson XP was paid once.
        game.Ledger.Award(XpKind.Drill, "drl-d1-01");
        quests.TryComplete(quests.Find("q-1.1")).Status.ShouldBe(QuestStatus.Complete);
        quests.CompleteLesson("q-1.1").Status.ShouldBe(QuestStatus.Complete);
        game.Ledger.Events.Count(e => e.Kind == XpKind.Lesson).ShouldBe(1);

        quests.Next()!.Id.ShouldBe("q-1.2");
        teachBacks.Save("q-1.2", "Least privilege.", forLab: false);
        quests.CompleteLesson("q-1.2").Status.ShouldBe(QuestStatus.Complete);
        quests.Next().ShouldBeNull();
        quests.DomainsComplete().ShouldBe(["D1"]);
        quests.CapstoneComplete.ShouldBeFalse();
    }

    [Fact]
    [Trait("Rule", "LABE-05")]
    public void Bonus_labs_never_block_a_quest()
    {
        using var fixture = CurriculumFixture.Create()
            .Quest("q-7.1", "7.1", "D7", labs: ["lab-d7-01", "lab-d7-90"])
            .Lab("lab-d7-01", "7.1")
            .Lab("lab-d7-90", "7.1", bonus: true, windowsOnly: true);
        using var game = new GameHarness(fixture);
        new TeachBackService(game.TeachBackStore, Path.Join(fixture.Root, "journal"), game.Clock).Save("q-7.1", "Secure deployment.", forLab: false);

        game.Quests.CompleteLesson("q-7.1").Status.ShouldBe(QuestStatus.InProgress);
        SetLabStage(game, "lab-d7-01", LabStage.Explained);

        var view = game.Quests.TryComplete(game.Quests.Find("q-7.1"));
        view.Status.ShouldBe(QuestStatus.Complete);
        view.Activities.Single(a => a.Id == "lab-d7-90").ShouldBe(new ActivityState("lab", "lab-d7-90", false, true));
    }

    [Fact]
    public void Capstone_completion_needs_every_capstone_quest()
    {
        using var fixture = CurriculumFixture.Create().Quest("q-cap-1", "CAP-1", "CAP").Quest("q-cap-2", "CAP-2", "CAP");
        using var game = new GameHarness(fixture);
        var teachBacks = new TeachBackService(game.TeachBackStore, Path.Join(fixture.Root, "journal"), game.Clock);

        teachBacks.Save("q-cap-1", "Assessed the whole system.", forLab: false);
        game.Quests.CompleteLesson("q-cap-1");
        game.Quests.CapstoneComplete.ShouldBeFalse();

        teachBacks.Save("q-cap-2", "Reported the findings.", forLab: false);
        game.Quests.CompleteLesson("q-cap-2");
        game.Quests.CapstoneComplete.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(150, false, true)]
    [InlineData(151, false, false)]
    [InlineData(29, true, false)]
    [InlineData(30, true, true)]
    public void Teach_backs_have_word_limits(int words, bool forLab, bool accepted)
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var folder = Path.Join(fixture.Root, "journal");
        var service = new TeachBackService(game.TeachBackStore, folder, game.Clock);
        var text = string.Join(' ', Enumerable.Repeat("word", words));

        if (accepted)
        {
            var path = service.Save("q-1.1", text, forLab);
            path.ShouldBe(Path.Join(folder, "q-1.1.md"));
            var saved = File.ReadAllText(path);
            saved.ShouldStartWith("# Teach-back: q-1.1\n\n_Saved 2026-10-05 09:00 UTC, ");
            saved.ShouldEndWith(text + "\n");
            game.TeachBackStore.Exists("q-1.1").ShouldBeTrue();
        }
        else
        {
            Should.Throw<UsageException>(() => service.Save("q-1.1", text, forLab));
            game.TeachBackStore.Exists("q-1.1").ShouldBeFalse();
        }
    }

    [Fact]
    public void Teach_backs_stay_inside_their_folder_and_are_sanitized()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var folder = Path.Join(fixture.Root, "journal");
        var service = new TeachBackService(game.TeachBackStore, folder, game.Clock);

        Should.Throw<UnsafePathException>(() => service.Save("../escape", "Nope.", forLab: false));
        var path = service.Save("q-1.1", "Bell\u0007 and \u001b[31mred\u001b[0m text.", forLab: false);
        File.ReadAllText(path).ShouldNotContain("\u001b");

        TeachBackService.CountWords("  two\twords \n").ShouldBe(2);
        var profile = new LearnerProfile(new Dictionary<string, string> { [ProfileKeys.TeachBackFolder] = "notes" });
        TeachBackService.FolderFor(profile, fixture.Paths).ShouldBe(Path.GetFullPath(Path.Join(fixture.Root, "notes")));
        TeachBackService.FolderFor(new LearnerProfile(new Dictionary<string, string>()), fixture.Paths).ShouldBe(fixture.Paths.DefaultJournal);
    }

    internal static void SetLabStage(GameHarness game, string labId, LabStage stage)
    {
        using var command = game.Database.Command();
        command.CommandText = "INSERT INTO lab_state (lab_id, state) VALUES ($lab, $state) ON CONFLICT (lab_id) DO UPDATE SET state = excluded.state;";
        command.With("$lab", labId).With("$state", stage.ToString()).ExecuteNonQuery();
    }
}
