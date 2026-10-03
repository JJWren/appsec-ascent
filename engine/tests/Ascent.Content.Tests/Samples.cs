namespace Ascent.Content.Tests;

/// <summary>Valid sample content; tests mutate these to trigger individual rules.</summary>
internal static class Samples
{
    public const string QuestPath = "curriculum/d1-concepts/q-1.1.md";

    public const string Quest = """
        ---
        id: q-1.1
        objectiveId: "1.1"
        outlineVersion: "2023-09-15"
        examDomain: D1
        release: 1
        title: Core security concepts
        estimatedMinutes: 45
        status: draft
        citations:
          - id: c1
            title: Example security standard
            publisher: NIST
            url: https://csrc.nist.gov/
            accessed: "2026-10-03"
            kind: standard
            role: primary
        aiLens:
          anchor: ai-lens
          topics: [data-poisoning]
        activities:
          labs: []
          drills: [drl-d1-01]
          deliverables: []
        deepDive: null
        ---
        # Core security concepts

        ## Confidentiality
        Confidentiality limits disclosure to authorized parties.[^c1]

        ## AI Lens
        Poisoned training data undermines integrity.[^c1]

        ## Activities
        - drl-d1-01
        """;

    public const string DrillPath = "curriculum/drills/drl-d1-01.yaml";

    public const string Drill = """
        id: drl-d1-01
        objectiveId: "1.1"
        prompt: Classify each control by the security property it protects.
        estimatedMinutes: 10
        answerRef: drl-d1-01.answer
        """;

    public const string LabManifestPath = "labs/lab-d5-01/lab.yaml";

    public const string LabManifest = """
        id: lab-d5-01
        objectiveId: "5.1"
        outlineVersion: "2023-09-15"
        release: 5
        brief: BRIEF.md
        module:
          sealedRef: lab-d5-01.module
        stages:
          local:
            services: [api, sql]
        sealed:
          tests: lab-d5-01.tests
          fix: lab-d5-01.fix
          plant: lab-d5-01.plant
        xp:
          red: 25
          blue: 40
          explain: 10
        windowsOnly: false
        bonus: false
        """;

    public const string LabBriefPath = "labs/lab-d5-01/BRIEF.md";

    public const string LabBrief = """
        ---
        labId: lab-d5-01
        objectiveId: "5.1"
        stages: [local]
        estimatedCost: "$0"
        ---
        # Lab: appointment search

        ## Scenario
        Patients search for open appointments.

        ## Acceptance criteria
        - The security tests pass.

        ## Stages
        Local only.

        ## Rules of engagement
        Only attack your own local Throughline System.
        """;

    public const string TemplatePath = "deliverables/templates/dlv-d3-01.yaml";

    public const string Template = """
        id: dlv-d3-01
        objectiveIds: ["3.3"]
        title: Data classification inventory
        sections:
          - key: inventory
            title: Data inventory
            required: true
            fields:
              - key: asset
                type: text
                required: true
              - key: classification
                type: enum
                required: true
                options: [public, internal, confidential, restricted]
        rubricId: rub-d3-01
        referenceRef: dlv-d3-01.reference
        portfolioEligible: true
        """;

    public const string RubricPath = "deliverables/rubrics/rub-d3-01.yaml";

    public const string Rubric = """
        id: rub-d3-01
        passThreshold: 70
        criteria:
          - key: completeness
            description: Every data store is inventoried.
            weight: 60
            levels:
              - { score: 0, descriptor: Missing }
              - { score: 2, descriptor: Complete }
          - key: accuracy
            description: Classifications match the policy.
            weight: 40
            levels:
              - { score: 0, descriptor: Wrong }
              - { score: 2, descriptor: Correct }
        """;

    public const string SingleQuestionPath = "questions/1.1/qb-1.1-001.yaml";

    public const string SingleQuestion = """
        id: qb-1.1-001
        objectiveId: "1.1"
        outlineVersion: "2023-09-15"
        type: single
        stem: Which control BEST protects confidentiality of records at rest?
        options:
          - { key: A, text: Encryption, rationale: Encryption protects confidentiality at rest. }
          - { key: B, text: Checksums, rationale: Checksums protect integrity. }
          - { key: C, text: Replication, rationale: Replication protects availability. }
          - { key: D, text: Logging, rationale: Logging supports accountability. }
        answer: A
        citations:
          - { title: Example standard, publisher: NIST }
        difficulty: 2
        aiLens: false
        pool: practice
        attestation: original-not-exam-recalled
        """;

    public static string Bundle(string itemId, string itemType, string tier, string? objectiveId = null, string? pool = null, string? aiTopic = null, string? examDomain = null)
    {
        var optional = (objectiveId is null ? string.Empty : ",\n    \"objectiveId\": \"" + objectiveId + "\"")
            + (pool is null ? string.Empty : ",\n    \"pool\": \"" + pool + "\"")
            + (aiTopic is null ? string.Empty : ",\n    \"aiTopic\": \"" + aiTopic + "\"")
            + (examDomain is null ? string.Empty : ",\n    \"examDomain\": \"" + examDomain + "\"");
        return "{\n  \"header\": {\n    \"formatVersion\": 1,\n    \"itemId\": \"" + itemId + "\",\n    \"itemType\": \"" + itemType
            + "\",\n    \"tier\": \"" + tier + "\",\n    \"contentType\": \"text/plain\",\n    \"kdfInfo\": \"" + itemId
            + "\",\n    \"nonce\": \"AAAAAAAAAAAAAAAA\",\n    \"createdUtc\": \"2026-10-03T00:00:00Z\"" + optional
            + "\n  },\n  \"ciphertext\": \"AA==\",\n  \"tag\": \"AA==\",\n  \"signature\": \"AA==\"\n}\n";
    }

    /// <summary>A repository with one valid Quest, Drill, Lab and Deliverable (with their Sealed bundles).</summary>
    public static TestRepo FullyValid()
    {
        var repo = TestRepo.Create()
            .Write(QuestPath, Quest)
            .Write(DrillPath, Drill)
            .Write(LabManifestPath, LabManifest)
            .Write(LabBriefPath, LabBrief)
            .Write(TemplatePath, Template)
            .Write(RubricPath, Rubric)
            .Write("sealed/references/dlv-d3-01.reference.bundle.json", Bundle("dlv-d3-01.reference", "reference", "submitted"));

        foreach (var (part, type, tier) in new[] { ("module", "lab-module", "start"), ("plant", "lab-plant", "start"), ("tests", "lab-tests", "earned"), ("fix", "lab-fix", "earned") })
        {
            repo.Write("sealed/labs/lab-d5-01." + part + ".bundle.json", Bundle("lab-d5-01." + part, type, tier));
        }

        return repo;
    }
}
