using System.Globalization;
using System.Text;

namespace Ascent.Sealing.Bundles;

/// <summary>
/// Canonical header bytes (P1): a JSON object with keys sorted by ordinal comparison, no whitespace, and only
/// <c>"</c>, <c>\</c> and control characters escaped (RFC 8785 style), encoded as UTF-8.
/// </summary>
public static class HeaderCanonicalizer
{
    /// <summary>Canonicalizes string fields plus the integer <c>formatVersion</c>.</summary>
    public static byte[] Canonicalize(IReadOnlyDictionary<string, string> fields, int formatVersion)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var keys = fields.Keys.Append("formatVersion").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var builder = new StringBuilder("{");
        var first = true;
        foreach (var key in keys)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            AppendString(builder, key);
            builder.Append(':');
            if (key == "formatVersion")
            {
                builder.Append(formatVersion.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                AppendString(builder, fields[key]);
            }
        }

        builder.Append('}');
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetBytes(builder.ToString());
    }

    private static void AppendString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
