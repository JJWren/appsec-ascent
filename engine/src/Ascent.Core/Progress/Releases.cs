namespace Ascent.Core.Progress;

/// <summary>
/// The Release ladder (ADR 0006, REL-01): Release 0 is the public Throughline System; Releases 1–8 belong to Domains
/// D1–D8, and Release 9 to the Capstone. Each Lab runs on its Domain's Release.
/// </summary>
public static class ReleaseLadder
{
    /// <summary>The Capstone's Release.</summary>
    public const int Capstone = 9;

    /// <summary>The Domain a Release belongs to: <c>D1</c>–<c>D8</c>, or <c>CAP</c>; Release 0 belongs to Orientation.</summary>
    public static string DomainOf(int release) => release switch
    {
        0 => "ORI",
        Capstone => "CAP",
        >= 1 and <= 8 => "D" + (char)('0' + release),
        _ => throw new ArgumentOutOfRangeException(nameof(release), release, "Releases run from 0 to 9."),
    };

    /// <summary>The Release number of a Domain, or null for anything else.</summary>
    public static int? NumberOf(string examDomain) => examDomain switch
    {
        "ORI" => 0,
        "CAP" => Capstone,
        ['D', var digit] when digit is >= '1' and <= '8' => digit - '0',
        _ => null,
    };

    /// <summary>The workspace's current Release: the highest unlocked, or 0.</summary>
    public static int Current(IEnumerable<ReleaseRecord> unlocked)
    {
        ArgumentNullException.ThrowIfNull(unlocked);
        return unlocked.Select(r => NumberOf(r.ExamDomain) ?? 0).DefaultIfEmpty(0).Max();
    }
}
