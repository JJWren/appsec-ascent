using Ascent.Content.Loading;
using Ascent.Content.Model;

namespace Ascent.Content.Rules;

/// <summary>Runs lint rules over a content index and returns deterministically ordered findings.</summary>
public sealed class RuleEngine
{
    /// <summary>Creates an engine with the given rules, or the default rule set.</summary>
    public RuleEngine(IEnumerable<IContentRule>? rules = null) => Rules = [.. rules ?? DefaultRules];

    /// <summary>Every rule shipped with the framework.</summary>
    public static IReadOnlyList<IContentRule> DefaultRules { get; } =
    [
        .. OutlineRules.All,
        .. IdRules.All,
        .. QuestRules.All,
        .. QuestionBankRules.All,
        .. LabRules.All,
        .. DeliverableRules.All,
        .. LegalRules.All,
        .. BundleRules.All,
        .. LinkRules.All,
        .. RepoRules.All,
    ];

    /// <summary>The rules this engine runs.</summary>
    public IReadOnlyList<IContentRule> Rules { get; }

    /// <summary>Load findings plus rule findings, de-duplicated and sorted by path, line, rule and message.</summary>
    public IReadOnlyList<Finding> Run(ContentIndex index)
    {
        var context = new RuleContext(index);
        return
        [
            .. index.LoadFindings
                .Concat(Rules.SelectMany(rule => rule.Evaluate(context)))
                .Distinct()
                .OrderBy(f => f.Path, StringComparer.Ordinal)
                .ThenBy(f => f.Line ?? 0)
                .ThenBy(f => f.RuleId, StringComparer.Ordinal)
                .ThenBy(f => f.Message, StringComparer.Ordinal),
        ];
    }
}
