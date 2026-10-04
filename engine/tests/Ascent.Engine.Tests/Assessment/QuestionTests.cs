using System.Text.Json;
using System.Text.Json.Nodes;
using Ascent.Assessment.Questions;
using Ascent.Assessment.Scheduling;

namespace Ascent.Engine.Tests.Assessment;

/// <summary>Parsing and grading questions (SU-03), and the FSRS scheduler (SU-04).</summary>
public sealed class QuestionTests
{
    [Fact]
    [Trait("Rule", "SU-03")]
    public void A_question_keeps_every_rationale_and_citation_and_sanitizes_text()
    {
        var question = Question.Parse(Json("single", "\"B\"", stem: "Which \u001b[31mcontrol\u001b[0m BEST fits?"));

        question.Id.ShouldBe("qb-1.1-001");
        question.ObjectiveId.ShouldBe("1.1");
        question.Type.ShouldBe("single");
        question.Pool.ShouldBe("practice");
        question.Stem.ShouldBe("Which control BEST fits?");
        question.Options.Select(o => o.Rationale).ShouldBe(["Why A.", "Why B.", "Why C.", "Why D."]);
        question.Citations.ShouldBe([new QuestionCitation("Example standard", "NIST", "https://csrc.nist.gov/")]);
        question.Answer.ShouldBe(["B"]);
        question.Instructions.ShouldBe("Choose one option (for example B).");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"id\":\"x\",\"options\":[],\"answer\":\"A\"}")]
    public void A_question_without_options_or_an_answer_is_rejected(string json) =>
        Should.Throw<JsonException>(() => Question.Parse(json));

    [Theory]
    [InlineData("b", true, "B", true)]
    [InlineData(" B ", true, "B", true)]
    [InlineData("A", true, "A", false)]
    [InlineData("E", false, "", false)]
    [InlineData("A,B", false, "", false)]
    [InlineData("", false, "", false)]
    public void Single_choice_answers_are_one_letter(string input, bool valid, string normalized, bool correct) =>
        Question.Parse(Json("single", "\"B\"")).Grade(input).ShouldBe(new GradeResult(valid, normalized, correct, valid ? null : "Answer with one option letter."));

    [Theory]
    [InlineData("c, a", true, "A,C", true)]
    [InlineData("A;C;A", true, "A,C", true)]
    [InlineData("A", true, "A", false)]
    [InlineData("A,Z", false, "", false)]
    public void Multi_select_answers_are_sets(string input, bool valid, string normalized, bool correct)
    {
        var question = Question.Parse(Json("multi", "[\"C\",\"A\"]"));
        var result = question.Grade(input);
        (result.Valid, result.Normalized, result.Correct).ShouldBe((valid, normalized, correct));
        question.Instructions.ShouldContain("all that apply");
    }

    [Theory]
    [InlineData("C,A,B,D", true, true)]
    [InlineData("c a b d", true, true)]
    [InlineData("A,B,C,D", true, false)]
    [InlineData("C,A,B", false, false)]
    [InlineData("C,A,A,D", false, false)]
    public void Ordering_answers_list_every_option_once(string input, bool valid, bool correct)
    {
        var question = Question.Parse(Json("ordering", "[\"C\",\"A\",\"B\",\"D\"]"));
        var result = question.Grade(input);
        (result.Valid, result.Correct).ShouldBe((valid, correct));
        question.Instructions.ShouldContain("in order");
    }

    [Theory]
    [InlineData("A=1, B=2, C=1, D=2", true, "A=1,B=2,C=1,D=2", true)]
    [InlineData("d=2;c=1;b=2;a=1", true, "A=1,B=2,C=1,D=2", true)]
    [InlineData("A=2,B=1,C=1,D=2", true, "A=2,B=1,C=1,D=2", false)]
    [InlineData("A=1,A=2", false, "", false)]
    [InlineData("A1,B2", false, "", false)]
    [InlineData("Z=1", false, "", false)]
    public void Matching_answers_are_pairs(string input, bool valid, string normalized, bool correct)
    {
        var question = Question.Parse(Json("matching", "[\"A=1\",\"B=2\",\"C=1\",\"D=2\"]", matching: true));
        var result = question.Grade(input);
        (result.Valid, result.Normalized, result.Correct).ShouldBe((valid, normalized, correct));
        question.Options.Select(o => o.Match).ShouldBe(["1", "2", "1", "2"]);
        question.Instructions.ShouldContain("A=2");
    }

    [Fact]
    public void Stored_answers_grade_the_same_way()
    {
        var question = Question.Parse(Json("multi", "[\"A\",\"C\"]"));

        question.IsCorrect("A,C").ShouldBeTrue();
        question.IsCorrect("A").ShouldBeFalse();
        question.IsCorrect("garbage").ShouldBeFalse();
    }

    [Fact]
    [Trait("Rule", "SU-04")]
    public void Fsrs_is_deterministic_good_pushes_a_card_out_and_again_brings_it_back()
    {
        var scheduler = new FsrsScheduler();
        var start = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

        var first = scheduler.Apply(null, "qb-1", "1.1", "D1", correct: true, start);
        var again = new FsrsScheduler().Apply(null, "qb-1", "1.1", "D1", correct: true, start);
        first.ShouldBe(again);
        first.DueUtc.ShouldBeGreaterThan(start.AddHours(12));
        first.Reps.ShouldBe(1);
        first.Lapses.ShouldBe(0);
        first.IntroducedUtc.ShouldBe(start);

        var second = scheduler.Apply(first, "qb-1", "1.1", "D1", correct: true, first.DueUtc);
        (second.DueUtc - first.DueUtc).ShouldBeGreaterThan(first.DueUtc - start);

        var forgotten = scheduler.Apply(second, "qb-1", "1.1", "D1", correct: false, second.DueUtc);
        forgotten.Lapses.ShouldBe(1);
        forgotten.Reps.ShouldBe(3);
        (forgotten.DueUtc - second.DueUtc).ShouldBeLessThan(second.DueUtc - first.DueUtc);
        FsrsScheduler.FromJson(forgotten.FsrsState).LastReview.ShouldBe(second.DueUtc);
        FsrsScheduler.DesiredRetention.ShouldBe(0.90);
    }

    [Fact]
    [Trait("Rule", "SU-04")]
    public void A_card_keeps_its_objective_introduction_and_suspension_across_reviews()
    {
        var scheduler = new FsrsScheduler();
        var start = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
        var card = scheduler.Apply(null, "qb-1", "1.1", "D1", correct: false, start) with { ObjectiveId = "1.5", Suspended = true };

        var next = scheduler.Apply(card, "qb-1", "1.1", "D1", correct: true, start.AddDays(1));

        next.ObjectiveId.ShouldBe("1.5");
        next.IntroducedUtc.ShouldBe(start);
        next.Suspended.ShouldBeTrue();
        card.Lapses.ShouldBe(0);
        Should.Throw<JsonException>(() => FsrsScheduler.FromJson("null"));
    }

    internal static string Json(string type, string answer, string stem = "Which control BEST fits?", bool matching = false) =>
        new JsonObject
        {
            ["id"] = "qb-1.1-001",
            ["objectiveId"] = "1.1",
            ["type"] = type,
            ["stem"] = stem,
            ["options"] = new JsonArray([.. new[] { "A", "B", "C", "D" }.Select(key => (JsonNode)new JsonObject
            {
                ["key"] = key.ToLowerInvariant(),
                ["text"] = "Option " + key,
                ["rationale"] = "Why " + key + ".",
                ["match"] = matching ? (key is "A" or "C" ? "1" : "2") : null,
            })]),
            ["answer"] = JsonNode.Parse(answer),
            ["citations"] = new JsonArray(new JsonObject { ["title"] = "Example standard", ["publisher"] = "NIST", ["url"] = "https://csrc.nist.gov/" }),
            ["pool"] = "practice",
        }.ToJsonString();
}
