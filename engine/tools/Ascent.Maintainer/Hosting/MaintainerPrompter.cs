using Ascent.Core.Interaction;
using Spectre.Console;

namespace Ascent.Maintainer.Hosting;

/// <summary>Interactive prompts for the maintainer, including hidden passphrase entry. Tests use a scripted prompter.</summary>
internal sealed class MaintainerPrompter(IAnsiConsole console) : IPrompter
{
    public bool Confirm(string question, bool defaultValue = false) =>
        console.Prompt(new ConfirmationPrompt(Markup.Escape(question)) { DefaultValue = defaultValue });

    public string Ask(string question, Func<string, string?>? validate = null)
    {
        var prompt = new TextPrompt<string>(Markup.Escape(question));
        if (validate is not null)
        {
            prompt.Validate(answer => validate(answer) is { } error ? ValidationResult.Error(Markup.Escape(error)) : ValidationResult.Success());
        }

        return console.Prompt(prompt);
    }

    public int Choose(string question, IReadOnlyList<string> options) =>
        console.Prompt(new SelectionPrompt<int>().Title(Markup.Escape(question)).AddChoices(Enumerable.Range(0, options.Count)).UseConverter(i => Markup.Escape(options[i])));

    public string AskSecret(string question) => console.Prompt(new TextPrompt<string>(Markup.Escape(question)).Secret());

    public bool ConfirmTyped(string question, string expected) =>
        string.Equals(console.Prompt(new TextPrompt<string>(Markup.Escape(question)).AllowEmpty()), expected, StringComparison.Ordinal);
}
