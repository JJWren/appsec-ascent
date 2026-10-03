using Ascent.Content.Model;

namespace Ascent.Content.Tests;

public sealed class QuestionBankRuleTests
{
    [Fact]
    public void Valid_plaintext_question_passes()
    {
        using var repo = TestRepo.Create().WriteSealed(Samples.SingleQuestionPath, Samples.SingleQuestion);
        repo.Lint().ShouldHaveNoErrors();
    }

    [Fact]
    public void Single_answer_with_three_options_fails_QBK_02()
    {
        using var repo = TestRepo.Create().WriteSealed(Samples.SingleQuestionPath,
            Samples.SingleQuestion.Replace("  - { key: D, text: Logging, rationale: Logging supports accountability. }\n", string.Empty, StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QBK-02");
    }

    [Fact]
    public void Answer_key_must_exist_QBK_02()
    {
        using var repo = TestRepo.Create().WriteSealed(Samples.SingleQuestionPath, Samples.SingleQuestion.Replace("answer: A", "answer: E", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QBK-02");
    }

    [Fact]
    public void Blank_rationale_fails_QBK_03()
    {
        using var repo = TestRepo.Create().WriteSealed(Samples.SingleQuestionPath,
            Samples.SingleQuestion.Replace("rationale: Checksums protect integrity.", "rationale: \"     \"", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QBK-03");
    }

    [Fact]
    public void Missing_attestation_fails_QBK_06()
    {
        using var repo = TestRepo.Create().WriteSealed(Samples.SingleQuestionPath,
            Samples.SingleQuestion.Replace("attestation: original-not-exam-recalled", string.Empty, StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("QBK-06");
    }

    [Fact]
    public void Lint_messages_never_include_question_text()
    {
        using var repo = TestRepo.Create().WriteSealed(Samples.SingleQuestionPath, Samples.SingleQuestion.Replace("answer: A", "answer: E", StringComparison.Ordinal));
        repo.Lint().ShouldAllBe(f => !f.Message.Contains("confidentiality", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Too_few_practice_questions_fail_when_pack_complete_QBK_01()
    {
        using var repo = Samples.FullyValid().MarkPackComplete("d1")
            .Write("sealed/questions/qb-1.1-001.bundle.json", Samples.Bundle("qb-1.1-001", "question", "practice", "1.1", "practice", examDomain: "D1"));
        repo.Lint().ShouldContainRule("QBK-01");
    }

    [Fact]
    public void Ai_topic_without_question_warns_while_in_progress_QBK_05()
    {
        using var repo = Samples.FullyValid();
        repo.Lint().ShouldContainRule("QBK-05", Severity.Warning);
    }

    [Fact]
    public void Diagnostic_pool_of_wrong_size_warns_QBK_07()
    {
        using var repo = TestRepo.Create()
            .Write("sealed/questions/qb-1.1-900.bundle.json", Samples.Bundle("qb-1.1-900", "question", "practice", "1.1", "diagnostic", examDomain: "D1"));
        repo.Lint().ShouldContainRule("QBK-07", Severity.Warning);
    }

    [Fact]
    public void Simulation_pool_fails_when_capstone_complete_QBK_08()
    {
        using var repo = TestRepo.Create().MarkPackComplete("capstone");
        repo.Lint().ShouldContainRule("QBK-08");
    }
}
