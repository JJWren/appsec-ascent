using Ascent.Core.Platform;

namespace Ascent.Cli.Rendering;

/// <summary>Plain text: no color, no ANSI, no live widgets; tables become aligned columns (UX-U2-01).</summary>
public sealed class PlainRenderer : IRenderer
{
    private readonly TextWriter writer;

    /// <summary>Creates a renderer over <paramref name="writer"/>.</summary>
    public PlainRenderer(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
    }

    /// <inheritdoc />
    public bool IsPlain => true;

    /// <inheritdoc />
    public void Heading(string text)
    {
        var safe = SafeText.Sanitize(text);
        writer.WriteLine(safe);
        writer.WriteLine(new string('-', Math.Min(Math.Max(safe.Length, 3), 80)));
    }

    /// <inheritdoc />
    public void Line(string text = "") => writer.WriteLine(SafeText.Sanitize(text));

    /// <inheritdoc />
    public void Status(Outcome outcome, string text) => writer.WriteLine(OutcomeWords.Of(outcome) + ": " + SafeText.Sanitize(text));

    /// <inheritdoc />
    public void Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        var cells = new List<string[]> { headers.Select(h => SafeText.Sanitize(h, 200)).ToArray() };
        cells.AddRange(rows.Select(row => row.Select(cell => SafeText.Sanitize(cell, 200).ReplaceLineEndings(" ")).ToArray()));
        var widths = Enumerable.Range(0, headers.Count)
            .Select(column => cells.Max(row => column < row.Length ? row[column].Length : 0))
            .ToArray();
        foreach (var row in cells)
        {
            writer.WriteLine(string.Join("  ", row.Select((cell, column) => column < widths.Length ? cell.PadRight(widths[column]) : cell)).TrimEnd());
        }
    }
}
