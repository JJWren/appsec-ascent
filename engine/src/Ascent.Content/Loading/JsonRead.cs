using System.Text.Json.Nodes;

namespace Ascent.Content.Loading;

/// <summary>Null-tolerant readers for JSON nodes produced from YAML or bundle JSON.</summary>
public static class JsonRead
{
    /// <summary>Reads a string property.</summary>
    public static string? Str(JsonNode? node, string property) =>
        node is JsonObject obj && obj[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>Reads a boolean property.</summary>
    public static bool? Bool(JsonNode? node, string property) =>
        node is JsonObject obj && obj[property] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    /// <summary>Reads a numeric property as a double.</summary>
    public static double? Num(JsonNode? node, string property)
    {
        if (node is not JsonObject obj || obj[property] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var whole))
        {
            return whole;
        }

        if (value.TryGetValue<int>(out var small))
        {
            return small;
        }

        return value.TryGetValue<double>(out var real) ? real : null;
    }

    /// <summary>Reads an integer property.</summary>
    public static int? WholeNumber(JsonNode? node, string property)
    {
        var number = Num(node, property);
        return number is { } n && Math.Abs(n % 1) < double.Epsilon ? (int)n : null;
    }

    /// <summary>Reads an object property.</summary>
    public static JsonObject? Obj(JsonNode? node, string property) => node is JsonObject obj ? obj[property] as JsonObject : null;

    /// <summary>Reads an array property.</summary>
    public static JsonArray? Arr(JsonNode? node, string property) => node is JsonObject obj ? obj[property] as JsonArray : null;

    /// <summary>The string elements of an array (non-strings are skipped).</summary>
    public static IReadOnlyList<string> Strings(JsonArray? array) =>
        array is null
            ? []
            : [.. array.OfType<JsonValue>().Select(item => item.TryGetValue<string>(out var text) ? text : null).OfType<string>()];
}
