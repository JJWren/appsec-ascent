using System.Text;

namespace Ascent.Core.Platform;

/// <summary>
/// Makes untrusted text safe to show in a terminal (P11). It removes escape sequences, control characters and
/// bidirectional overrides, normalizes line endings, and bounds the length.
/// </summary>
public static class SafeText
{
    /// <summary>The default maximum length, in characters.</summary>
    public const int DefaultMaxLength = 20_000;

    /// <summary>Appended when text is cut short.</summary>
    public const string TruncationMarker = "…[truncated]";

    private const char Escape = '\u001b';
    private const char Bell = '\u0007';
    private const char StringTerminator = '\u009c';

    /// <summary>Returns <paramref name="text"/> without terminal control sequences, keeping newlines and tabs.</summary>
    public static string Sanitize(string? text, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        var builder = new StringBuilder(Math.Min(text.Length, maxLength) + TruncationMarker.Length);
        var i = 0;
        while (i < text.Length && builder.Length < maxLength)
        {
            var c = text[i];
            if (c == Escape)
            {
                i = SkipEscapeSequence(text, i + 1);
            }
            else if (c == '\r')
            {
                builder.Append('\n');
                i += i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            }
            else if (c is '\n' or '\t')
            {
                builder.Append(c);
                i++;
            }
            else if (c is >= '\u0080' and <= '\u009f')
            {
                i = SkipC1Control(text, i);
            }
            else if (char.IsControl(c) || IsBidiControl(c))
            {
                i++;
            }
            else
            {
                builder.Append(c);
                i++;
            }
        }

        if (i < text.Length)
        {
            if (builder.Length > 0 && char.IsHighSurrogate(builder[^1]))
            {
                builder.Length--;
            }

            builder.Append(TruncationMarker);
        }

        return builder.ToString();
    }

    /// <summary>True for the Unicode embedding, override and isolate controls that can reorder displayed text.</summary>
    public static bool IsBidiControl(char c) => c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩';

    // `start` is the index just after ESC. Returns the index after the whole sequence.
    private static int SkipEscapeSequence(string text, int start)
    {
        if (start >= text.Length)
        {
            return start;
        }

        return text[start] switch
        {
            '[' => SkipControlSequence(text, start + 1),
            ']' or 'P' or 'X' or '^' or '_' => SkipControlString(text, start + 1),
            _ => SkipShortSequence(text, start),
        };
    }

    // 8-bit C1 controls: CSI (0x9B) and the string introducers carry payloads; the rest are single characters.
    private static int SkipC1Control(string text, int index) => text[index] switch
    {
        '\u009b' => SkipControlSequence(text, index + 1),
        '\u0090' or '\u0098' or '\u009d' or '\u009e' or '\u009f' => SkipControlString(text, index + 1),
        _ => index + 1,
    };

    // CSI: parameter and intermediate bytes, then one final byte in 0x40–0x7E.
    private static int SkipControlSequence(string text, int index)
    {
        while (index < text.Length && text[index] is < '@' or > '~')
        {
            index++;
        }

        return Math.Min(index + 1, text.Length);
    }

    // OSC, DCS, SOS, PM and APC: everything up to BEL, ESC '\' or the 8-bit string terminator.
    private static int SkipControlString(string text, int index)
    {
        while (index < text.Length)
        {
            var c = text[index];
            if (c is Bell or StringTerminator)
            {
                return index + 1;
            }

            if (c == Escape && index + 1 < text.Length && text[index + 1] == '\\')
            {
                return index + 2;
            }

            index++;
        }

        return index;
    }

    // ESC, optional intermediate bytes (0x20–0x2F), then one final byte.
    private static int SkipShortSequence(string text, int index)
    {
        while (index < text.Length && text[index] is >= ' ' and <= '/')
        {
            index++;
        }

        return Math.Min(index + 1, text.Length);
    }
}
