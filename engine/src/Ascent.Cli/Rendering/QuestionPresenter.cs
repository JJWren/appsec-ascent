using System.Globalization;
using Ascent.Assessment.Questions;
using Ascent.Core.Interaction;

namespace Ascent.Cli.Rendering;

/// <summary>Shows a question, collects a valid answer, and shows feedback with every rationale and citation (SU-03).</summary>
internal static class QuestionPresenter
{
    /// <summary>Asks a question until the answer is well-formed; returns the normalized grade.</summary>
    public static GradeResult Ask(IRenderer renderer, IPrompter prompter, Question question, string heading)
    {
        renderer.Heading(heading);
        renderer.Line(question.Stem);
        foreach (var option in question.Options)
        {
            renderer.Line("  " + option.Key + ") " + option.Text);
        }

        if (question.Type == "matching")
        {
            // Only the targets are shown, never which option they belong to.
            renderer.Line("Targets: " + string.Join(", ", question.Options.Select(o => o.Match).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
        }

        renderer.Line(question.Instructions);
        var answer = prompter.Ask("Your answer:", input => question.Grade(input) is { Valid: false } invalid ? invalid.Error : null);
        return question.Grade(answer);
    }

    /// <summary>Shows whether the answer was right, then every option's rationale and the citations.</summary>
    public static void Feedback(IRenderer renderer, Question question, bool correct)
    {
        renderer.Status(
            correct ? Outcome.Pass : Outcome.Fail,
            correct ? "Correct." : "Not quite. The answer is " + string.Join(",", question.Answer) + ".");
        foreach (var option in question.Options)
        {
            renderer.Line("  " + option.Key + ") " + option.Rationale);
        }

        foreach (var citation in question.Citations)
        {
            renderer.Line("  Source: " + citation.Title + ", " + citation.Publisher + (citation.Url is { } url ? " (" + url + ")" : string.Empty));
        }
    }

    /// <summary>The end-of-attempt review: each stem, the Learner's answer, then the feedback (BF-02, SIM-04).</summary>
    public static void Review(IRenderer renderer, IEnumerable<(Question Question, string? Answer, bool Correct)> items)
    {
        renderer.Heading("Review");
        var number = 0;
        foreach (var (question, answer, correct) in items)
        {
            renderer.Line(string.Create(CultureInfo.InvariantCulture, $"{++number}. ") + question.Stem);
            renderer.Line("Your answer: " + (answer ?? "(none)"));
            Feedback(renderer, question, correct);
        }
    }

    /// <summary>Formats remaining time as <c>mm:ss left</c>.</summary>
    public static string TimeLeft(TimeSpan remaining)
    {
        var clamped = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        return string.Create(CultureInfo.InvariantCulture, $"{(int)clamped.TotalMinutes:00}:{clamped.Seconds:00} left");
    }
}
