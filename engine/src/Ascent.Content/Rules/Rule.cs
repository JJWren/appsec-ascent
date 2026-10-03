using Ascent.Content.Loading;
using Ascent.Content.Model;

namespace Ascent.Content.Rules;

/// <summary>A lint rule identified by its business-rule ID (for example QST-02).</summary>
public interface IContentRule
{
    /// <summary>Business-rule ID.</summary>
    string Id { get; }

    /// <summary>One-line description.</summary>
    string Title { get; }

    /// <summary>Evaluates the rule against the loaded content.</summary>
    IEnumerable<Finding> Evaluate(RuleContext context);
}

/// <summary>A rule implemented by a delegate.</summary>
public sealed class Rule(string id, string title, Func<RuleContext, IEnumerable<Finding>> evaluate) : IContentRule
{
    /// <inheritdoc />
    public string Id { get; } = id;

    /// <inheritdoc />
    public string Title { get; } = title;

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(RuleContext context) => evaluate(context);
}

/// <summary>What a rule can see: the content index plus helpers for pack-exit severity.</summary>
public sealed class RuleContext(ContentIndex index)
{
    /// <summary>The loaded content.</summary>
    public ContentIndex Index { get; } = index;

    /// <summary>Error once the pack is marked complete; a warning while it is still being built.</summary>
    public Severity AtPackExit(string packKey) => Index.Season.IsPackComplete(packKey) ? Severity.Error : Severity.Warning;

    /// <summary>
    /// Pack-exit checks stay silent until a pack has any Quest (coverage already reports empty packs),
    /// unless the pack is marked complete.
    /// </summary>
    public bool PackStarted(string packKey) =>
        Index.Season.IsPackComplete(packKey) || ContentFacts.QuestsInPack(Index, packKey).Any();
}
