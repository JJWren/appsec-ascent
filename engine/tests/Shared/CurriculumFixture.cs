using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Ascent.Core;
using Ascent.Core.Domain;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Crypto;

namespace Ascent.Tests.Shared;

/// <summary>
/// A throwaway repository holding a small, valid Curriculum: the real schemas, outline and season file, plus the
/// Quests, Labs, drills, Deliverables and Question Bank items a test adds. Questions are sealed and signed with a test
/// key at test time (D6), so the Engine opens them through the real verify-then-decrypt pipeline.
/// </summary>
internal sealed class CurriculumFixture : IDisposable
{
    /// <summary>The real outline's version.</summary>
    public const string OutlineVersion = "2023-09-15";

    private static readonly DateTimeOffset Created = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private CurriculumFixture(string root)
    {
        Root = root;
        ShieldKey = [.. Enumerable.Range(0, BundleCipher.KeySize).Select(i => (byte)(255 - i))];
        Keys = new KeyHierarchy(ShieldKey);
    }

    /// <summary>The repository root.</summary>
    public string Root { get; }

    /// <summary>The Engine's paths for this repository.</summary>
    public EnginePaths Paths => new(Root);

    /// <summary>The test signing key; the Engine trusts its public half through the composition root (P8).</summary>
    public ECDsa SigningKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The public half of <see cref="SigningKey"/>.</summary>
    public string PublicKeyPem => SigningKey.ExportSubjectPublicKeyInfoPem();

    /// <summary>The fixture's shield key, written to <c>sealed/shield.json</c>.</summary>
    public byte[] ShieldKey { get; }

    /// <summary>Keys derived from <see cref="ShieldKey"/>.</summary>
    public KeyHierarchy Keys { get; }

    /// <summary>The real repository root (the folder containing AppSecAscent.slnx).</summary>
    public static string RealRoot { get; } = FindRealRoot();

    /// <summary>A repository with the real schemas, outline and season file, and a test shield key, but no content.</summary>
    public static CurriculumFixture Create()
    {
        var fixture = new CurriculumFixture(Path.Join(Path.GetTempPath(), "ascent-fixtures", Guid.NewGuid().ToString("N")));
        foreach (var file in Directory.EnumerateFiles(Path.Join(RealRoot, "schemas"), "*.schema.json"))
        {
            fixture.Write("schemas/" + Path.GetFileName(file), File.ReadAllText(file));
        }

        fixture.Write("curriculum/outline/exam-outline-" + OutlineVersion + ".yaml", File.ReadAllText(Path.Join(RealRoot, "curriculum", "outline", "exam-outline-" + OutlineVersion + ".yaml")));
        fixture.Season(complete: false);
        fixture.Write(ShieldKeyFile.RelativePath, new JsonObject { ["formatVersion"] = 1, ["shieldKey"] = Convert.ToBase64String(fixture.ShieldKey) }.ToJsonString());
        return fixture;
    }

    /// <summary>The real outline.</summary>
    public static Ascent.Content.Model.Outline RealOutline => LazyOutline.Value;

    private static Lazy<Ascent.Content.Model.Outline> LazyOutline { get; } = new(() =>
    {
        using var fixture = Create();
        return Ascent.Content.Loading.ContentLoader.Load(fixture.Root).Outline ?? throw new InvalidOperationException("No outline.");
    });

    /// <summary>The Objective IDs of a Domain in the real outline, in order.</summary>
    public static IReadOnlyList<string> ObjectivesOf(string examDomain) => [.. RealOutline.FindDomain(examDomain)!.Objectives.Select(o => o.Id)];

    /// <summary>Writes <c>curriculum/season.yaml</c>, complete or in progress.</summary>
    public CurriculumFixture Season(bool complete) => Write(
        "curriculum/season.yaml",
        "season: 1\noutlineVersion: \"" + OutlineVersion + "\"\nstatus: " + (complete ? "complete" : "in-progress") + "\npacks:\n"
        + string.Concat(new[] { "orientation", "d1", "d2", "d3", "d4", "d5", "d6", "d7", "d8", "capstone" }.Select(p => "  " + p + ": draft\n")));

    /// <summary>Adds a Quest.</summary>
    public CurriculumFixture Quest(
        string id,
        string objectiveId,
        string examDomain,
        int minutes = 30,
        IReadOnlyList<string>? labs = null,
        IReadOnlyList<string>? drills = null,
        IReadOnlyList<string>? deliverables = null,
        string body = "Lesson text.")
    {
        var folder = examDomain switch
        {
            "ORI" => "orientation",
            "CAP" => "capstone",
            _ => examDomain.ToLowerInvariant(),
        };
        static string List(IReadOnlyList<string>? items) => "[" + string.Join(", ", items ?? []) + "]";
        return Write(
            "curriculum/" + folder + "/" + id + ".md",
            "---\n"
            + "id: " + id + "\n"
            + "objectiveId: \"" + objectiveId + "\"\n"
            + "outlineVersion: \"" + OutlineVersion + "\"\n"
            + "examDomain: " + examDomain + "\n"
            + "release: 1\n"
            + "title: Quest " + id + "\n"
            + "estimatedMinutes: " + minutes.ToString(CultureInfo.InvariantCulture) + "\n"
            + "status: draft\n"
            + "citations:\n  - { id: c1, title: Example standard, publisher: NIST, kind: standard, role: primary }\n"
            + "aiLens: null\n"
            + "activities:\n  labs: " + List(labs) + "\n  drills: " + List(drills) + "\n  deliverables: " + List(deliverables) + "\n"
            + "---\n# Quest " + id + "\n\n" + body + "\n");
    }

    /// <summary>
    /// Adds a Lab manifest. Its Release defaults to 0, the workspace's first, so tests can start it; <paramref name="cloud"/>
    /// is the YAML of a <c>stages.cloud</c> block, indented under <c>stages:</c>.
    /// </summary>
    public CurriculumFixture Lab(
        string id,
        string objectiveId,
        bool bonus = false,
        bool windowsOnly = false,
        int red = 25,
        int blue = 40,
        int explain = 10,
        int release = 0,
        string? cloud = null) => Write(
        "labs/" + id + "/lab.yaml",
        "id: " + id + "\n"
        + "objectiveId: \"" + objectiveId + "\"\n"
        + "outlineVersion: \"" + OutlineVersion + "\"\n"
        + "release: " + release.ToString(CultureInfo.InvariantCulture) + "\n"
        + "brief: BRIEF.md\n"
        + "module:\n  sealedRef: " + id + ".module\n"
        + "stages:\n  local:\n    services: [api]\n" + (cloud ?? string.Empty)
        + "sealed:\n  tests: " + id + ".tests\n  fix: " + id + ".fix\n  plant: " + id + ".plant\n"
        + "xp:\n  red: " + red.ToString(CultureInfo.InvariantCulture) + "\n  blue: " + blue.ToString(CultureInfo.InvariantCulture) + "\n  explain: " + explain.ToString(CultureInfo.InvariantCulture) + "\n"
        + "windowsOnly: " + (windowsOnly ? "true" : "false") + "\n"
        + "bonus: " + (bonus ? "true" : "false") + "\n");

    /// <summary>Adds a drill.</summary>
    public CurriculumFixture Drill(string id, string objectiveId) => Write(
        "curriculum/drills/" + id + ".yaml",
        "id: " + id + "\nobjectiveId: \"" + objectiveId + "\"\nprompt: Classify each control.\nestimatedMinutes: 10\nanswerRef: " + id + ".answer\n");

    /// <summary>Adds a Deliverable template.</summary>
    public CurriculumFixture Deliverable(string id, string objectiveId) => Write(
        "deliverables/templates/" + id + ".yaml",
        "id: " + id + "\nobjectiveIds: [\"" + objectiveId + "\"]\ntitle: Deliverable " + id + "\n"
        + "sections:\n  - key: main\n    title: Main\n    required: true\n    fields:\n      - key: summary\n        type: text\n        required: true\n"
        + "rubricId: rub-" + id + "\nreferenceRef: " + id + ".reference\nportfolioEligible: true\n");

    /// <summary>Adds a rubric with criteria of the given weights, each with levels 0, 2 and 4.</summary>
    public CurriculumFixture Rubric(string id, int passThreshold, params (string Key, int Weight)[] criteria) => Write(
        "deliverables/rubrics/" + id + ".yaml",
        "id: " + id + "\npassThreshold: " + passThreshold.ToString(CultureInfo.InvariantCulture) + "\ncriteria:\n"
        + string.Concat(criteria.Select(c => "  - key: " + c.Key + "\n    description: Judges " + c.Key + " well.\n    weight: " + c.Weight.ToString(CultureInfo.InvariantCulture)
            + "\n    levels:\n      - { score: 0, descriptor: Missing }\n      - { score: 2, descriptor: Partly there }\n      - { score: 4, descriptor: Complete }\n")));

    /// <summary>Adds an outline mapping.</summary>
    public CurriculumFixture Mapping(string from, string to, params (string? From, string[] To, string Kind)[] entries) => Write(
        "curriculum/outline/mappings/" + from + "-to-" + to + ".yaml",
        "from: \"" + from + "\"\nto: \"" + to + "\"\nentries:\n"
        + string.Concat(entries.Select(e => "  - { from: " + (e.From is null ? "null" : "\"" + e.From + "\"") + ", to: [" + string.Join(", ", e.To.Select(t => "\"" + t + "\"")) + "], kind: " + e.Kind + " }\n")));

    /// <summary>
    /// Seals and signs a single-choice question whose answer is A, unless told otherwise. Its options' rationales read
    /// "Why A." and so on, and its stem is "Stem of &lt;itemId&gt;?".
    /// </summary>
    public CurriculumFixture Question(string itemId, string objectiveId, string pool = "practice", string type = "single", JsonNode? answer = null, string? examDomain = null)
    {
        var options = new JsonArray();
        foreach (var key in new[] { "A", "B", "C", "D" })
        {
            var option = new JsonObject { ["key"] = key, ["text"] = "Option " + key, ["rationale"] = "Why " + key + "." };
            if (type == "matching")
            {
                option["match"] = key is "A" or "C" ? "1" : "2";
            }

            options.Add(option);
        }

        var question = new JsonObject
        {
            ["id"] = itemId,
            ["objectiveId"] = objectiveId,
            ["outlineVersion"] = OutlineVersion,
            ["type"] = type,
            ["stem"] = "Stem of " + itemId + "?",
            ["options"] = options,
            ["answer"] = answer ?? JsonValue.Create("A"),
            ["citations"] = new JsonArray(new JsonObject { ["title"] = "Example standard", ["publisher"] = "NIST", ["url"] = "https://csrc.nist.gov/" }),
            ["difficulty"] = 2,
            ["aiLens"] = false,
            ["pool"] = pool,
            ["attestation"] = "original-not-exam-recalled",
        };
        return Seal(itemId, "question", pool == "simulation" ? SealTier.Simulation : SealTier.Practice, Encoding.UTF8.GetBytes(question.ToJsonString()), objectiveId, examDomain ?? "D" + objectiveId[0], pool);
    }

    /// <summary>Seals and signs a folder as a tar.gz item, such as a Lab Module or a Release.</summary>
    public CurriculumFixture SealFolder(string itemId, string itemType, SealTier tier, string folder) =>
        Seal(itemId, itemType, tier, Ascent.Sealing.Unsealing.SafeArchive.CreateTarGz(folder), contentType: Ascent.Sealing.Unsealing.SafeArchive.ContentType);

    /// <summary>Seals and signs any item.</summary>
    public CurriculumFixture Seal(string itemId, string itemType, SealTier tier, byte[] plaintext, string? objectiveId = null, string? examDomain = null, string? pool = null, string contentType = "application/json")
    {
        var header = BundleHeader.Create(itemId, itemType, tier, contentType, RandomNumberGenerator.GetBytes(12), Created, objectiveId, examDomain, pool);
        var (ciphertext, tag) = BundleCipher.Encrypt(Keys.ItemKey(tier, itemId), header.Nonce, plaintext, header.CanonicalBytes());
        var bundle = new SealedBundle(header, ciphertext, tag, []);
        bundle = bundle.WithSignature(SigningKey.SignData(bundle.SigningInput(), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        var path = Path.Join(Root, "sealed", "items", itemId + SealedBundle.FileSuffix);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bundle.Write(path);
        return this;
    }

    /// <summary>Writes a file, normalizing line endings.</summary>
    public CurriculumFixture Write(string relativePath, string content)
    {
        var full = Path.Join(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        return this;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SigningKey.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A handle is still open; the OS cleans temp eventually.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string FindRealRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, EnginePaths.SolutionFile)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
