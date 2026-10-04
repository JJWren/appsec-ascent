using Ascent.Core.Platform;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Ascent.Cli.Rendering;

/// <summary>Spectre.Console output. Text always goes through <see cref="Text"/> or <see cref="Markup.Escape"/> (P11).</summary>
public sealed class RichRenderer : IRenderer
{
    /// <summary>Creates a renderer over a Spectre console.</summary>
    public RichRenderer(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        Console = console;
    }

    /// <summary>The underlying console.</summary>
    public IAnsiConsole Console { get; }

    /// <inheritdoc />
    public bool IsPlain => false;

    /// <inheritdoc />
    public void Heading(string text) =>
        Console.Write(new Rule(Markup.Escape(SafeText.Sanitize(text, 200))) { Justification = Justify.Left });

    /// <inheritdoc />
    public void Line(string text = "") => Console.Write(new Text(SafeText.Sanitize(text) + Environment.NewLine));

    /// <inheritdoc />
    public void Status(Outcome outcome, string text)
    {
        Console.Write(new Text(OutcomeWords.Of(outcome), StyleFor(outcome)));
        Console.Write(new Text(" " + SafeText.Sanitize(text) + Environment.NewLine));
    }

    /// <inheritdoc />
    public void Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        var table = new Table().Border(TableBorder.Rounded);
        foreach (var header in headers)
        {
            table.AddColumn(new TableColumn(new Text(SafeText.Sanitize(header, 200), new Style(decoration: Decoration.Bold))));
        }

        foreach (var row in rows)
        {
            table.AddRow(row.Select(cell => (IRenderable)new Text(SafeText.Sanitize(cell, 2000))).ToArray());
        }

        Console.Write(table);
    }

    /// <summary>Writes a Spectre renderable, such as a formatted parse error.</summary>
    public void Write(IRenderable renderable) => Console.Write(renderable);

    private static Style StyleFor(Outcome outcome) => outcome switch
    {
        Outcome.Pass => new Style(Color.Green, decoration: Decoration.Bold),
        Outcome.Warn => new Style(Color.Yellow, decoration: Decoration.Bold),
        Outcome.Fail => new Style(Color.Red, decoration: Decoration.Bold),
        _ => new Style(Color.Grey, decoration: Decoration.Bold),
    };
}
