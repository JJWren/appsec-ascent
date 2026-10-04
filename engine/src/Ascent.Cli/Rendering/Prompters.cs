using System.Globalization;
using Ascent.Core.Errors;
using Ascent.Core.Interaction;
using Ascent.Core.Platform;
using Spectre.Console;

namespace Ascent.Cli.Rendering;

/// <summary>Interactive prompts through Spectre.Console. Questions and options are escaped, never treated as markup.</summary>
public sealed class SpectrePrompter : IPrompter
{
    private readonly IAnsiConsole console;

    /// <summary>Creates prompts on a Spectre console.</summary>
    public SpectrePrompter(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        this.console = console;
    }

    /// <inheritdoc />
    public bool Confirm(string question, bool defaultValue = false) =>
        console.Prompt(new ConfirmationPrompt(Escape(question)) { DefaultValue = defaultValue });

    /// <inheritdoc />
    public string Ask(string question, Func<string, string?>? validate = null)
    {
        var prompt = new TextPrompt<string>(Escape(question));
        if (validate is not null)
        {
            prompt.Validate(answer => validate(answer) is { } error ? ValidationResult.Error(Escape(error)) : ValidationResult.Success());
        }

        return console.Prompt(prompt);
    }

    /// <inheritdoc />
    public int Choose(string question, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var indexes = Enumerable.Range(0, options.Count).ToList();
        return console.Prompt(new SelectionPrompt<int>()
            .Title(Escape(question))
            .AddChoices(indexes)
            .UseConverter(index => Escape(options[index])));
    }

    /// <inheritdoc />
    public string AskSecret(string question) => console.Prompt(new TextPrompt<string>(Escape(question)).Secret());

    /// <inheritdoc />
    public bool ConfirmTyped(string question, string expected) =>
        string.Equals(console.Prompt(new TextPrompt<string>(Escape(question)).AllowEmpty()), expected, StringComparison.Ordinal);

    private static string Escape(string text) => Markup.Escape(SafeText.Sanitize(text, 2000));
}

/// <summary>
/// Line-based prompts for plain mode: numbered options and typed answers, friendly to screen readers. End of input
/// is a usage error rather than a silent default.
/// </summary>
public sealed class PlainPrompter : IPrompter
{
    private readonly TextReader input;
    private readonly TextWriter output;

    /// <summary>Creates prompts over a reader and a writer.</summary>
    public PlainPrompter(TextReader input, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        this.input = input;
        this.output = output;
    }

    /// <inheritdoc />
    public bool Confirm(string question, bool defaultValue = false)
    {
        while (true)
        {
            var answer = Read(question + (defaultValue ? " [Y/n]: " : " [y/N]: ")).Trim();
            if (answer.Length == 0)
            {
                return defaultValue;
            }

            if (answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (answer.Equals("n", StringComparison.OrdinalIgnoreCase) || answer.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            output.WriteLine("Please answer y or n.");
        }
    }

    /// <inheritdoc />
    public string Ask(string question, Func<string, string?>? validate = null)
    {
        while (true)
        {
            var answer = Read(question + " ").Trim();
            var error = validate?.Invoke(answer);
            if (error is null)
            {
                return answer;
            }

            output.WriteLine(SafeText.Sanitize(error));
        }
    }

    /// <inheritdoc />
    public int Choose(string question, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        output.WriteLine(SafeText.Sanitize(question));
        for (var i = 0; i < options.Count; i++)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {i + 1}) ") + SafeText.Sanitize(options[i]));
        }

        while (true)
        {
            var answer = Read(string.Create(CultureInfo.InvariantCulture, $"Choose 1-{options.Count}: ")).Trim();
            if (int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var choice) && choice >= 1 && choice <= options.Count)
            {
                return choice - 1;
            }

            output.WriteLine("Please enter one of the numbers shown.");
        }
    }

    /// <inheritdoc />
    public string AskSecret(string question) =>
        throw new UsageException(
            "This command needs a secret typed into an interactive terminal.",
            "Run it in a terminal without --plain and without redirected input.");

    /// <inheritdoc />
    public bool ConfirmTyped(string question, string expected) =>
        string.Equals(Read(question + " ").Trim(), expected, StringComparison.Ordinal);

    private string Read(string prompt)
    {
        output.Write(SafeText.Sanitize(prompt));
        output.Flush();
        return input.ReadLine() ?? throw new UsageException(
            "This command needs an answer, but the input ended.",
            "Run it in an interactive terminal.");
    }
}
