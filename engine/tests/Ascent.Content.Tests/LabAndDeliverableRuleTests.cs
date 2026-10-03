using Ascent.Content.Model;

namespace Ascent.Content.Tests;

public sealed class LabRuleTests
{
    [Fact]
    public void Lab_without_brief_fails_LAB_01()
    {
        using var repo = Samples.FullyValid().Delete(Samples.LabBriefPath);
        repo.Lint().ShouldContainRule("LAB-01");
    }

    [Fact]
    public void Brief_missing_a_section_fails_LAB_01()
    {
        using var repo = Samples.FullyValid().Edit(Samples.LabBriefPath, t => t.Replace("## Rules of engagement", "## Notes", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("LAB-01");
    }

    [Fact]
    public void Brief_stages_must_match_manifest_LAB_02()
    {
        using var repo = Samples.FullyValid().Edit(Samples.LabBriefPath, t => t.Replace("stages: [local]", "stages: [local, cloud]", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("LAB-02");
    }

    [Fact]
    public void Windows_only_lab_must_be_bonus_LAB_03()
    {
        using var repo = Samples.FullyValid().Edit(Samples.LabManifestPath, t => t.Replace("windowsOnly: false", "windowsOnly: true", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("LAB-03");
    }

    [Fact]
    public void Missing_sealed_bundle_fails_LAB_04()
    {
        using var repo = Samples.FullyValid().Delete("sealed/labs/lab-d5-01.fix.bundle.json");
        repo.Lint().ShouldContainRule("LAB-04");
    }

    [Fact]
    public void Wrong_tier_fails_LAB_04()
    {
        using var repo = Samples.FullyValid().Write("sealed/labs/lab-d5-01.tests.bundle.json", Samples.Bundle("lab-d5-01.tests", "lab-tests", "start"));
        repo.Lint().ShouldContainRule("LAB-04");
    }

    [Fact]
    public void Never_deploy_resource_fails_LAB_05()
    {
        using var repo = Samples.FullyValid()
            .Edit(Samples.LabManifestPath, t => t.Replace("    services: [api, sql]\n",
                "    services: [api, sql]\n  cloud:\n    bicep: infra/labs/lab-d5-01.bicep\n    estimateUsd: 0\n    freeTier: true\n    paidSideQuest: false\n    teardownWindowHours: 4\n",
                StringComparison.Ordinal))
            .Edit(Samples.LabBriefPath, t => t.Replace("stages: [local]", "stages: [local, cloud]", StringComparison.Ordinal))
            .Write("infra/labs/lab-d5-01.bicep", "resource fw 'Microsoft.Network/azureFirewalls@2024-05-01' = {\n  name: 'fw'\n}\n");
        repo.Lint().ShouldContainRule("LAB-05");
    }

    [Theory]
    [InlineData("resource b 'Microsoft.Network/bastionHosts@2024-05-01' = {\n  sku: { name: 'Developer' }\n}\n", false)]
    [InlineData("resource b 'Microsoft.Network/bastionHosts@2024-05-01' = {\n  sku: { name: 'Basic' }\n}\n", true)]
    [InlineData("resource fd 'Microsoft.Cdn/profiles@2024-02-01' = {\n  sku: { name: 'Standard_AzureFrontDoor' }\n}\n", false)]
    [InlineData("resource fd 'Microsoft.Cdn/profiles@2024-02-01' = {\n  sku: { name: 'Premium_AzureFrontDoor' }\n}\n", true)]
    public void Never_deploy_list_respects_free_variants(string bicep, bool blocked)
    {
        var findings = Ascent.Content.Rules.LabRules.ScanTemplate("x.bicep", bicep).ToList();
        (findings.Count > 0).ShouldBe(blocked);
    }
}

public sealed class DeliverableRuleTests
{
    [Fact]
    public void Required_section_without_fields_fails_DLV_01()
    {
        using var repo = Samples.FullyValid().Write(Samples.TemplatePath, """
            id: dlv-d3-01
            objectiveIds: ["3.3"]
            title: Data classification inventory
            sections:
              - key: inventory
                title: Data inventory
                required: true
                fields: []
            rubricId: rub-d3-01
            referenceRef: dlv-d3-01.reference
            portfolioEligible: false
            """);
        repo.Lint().ShouldContainRule("DLV-01");
    }

    [Fact]
    public void Rubric_weights_must_total_100_DLV_02()
    {
        using var repo = Samples.FullyValid().Edit(Samples.RubricPath, t => t.Replace("weight: 40", "weight: 30", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("DLV-02");
    }

    [Fact]
    public void Missing_reference_bundle_fails_DLV_03()
    {
        using var repo = Samples.FullyValid().Delete("sealed/references/dlv-d3-01.reference.bundle.json");
        repo.Lint().ShouldContainRule("DLV-03");
    }

    [Fact]
    public void Portfolio_template_with_personal_data_fails_DLV_04()
    {
        using var repo = Samples.FullyValid().Edit(Samples.TemplatePath, t => t.Replace("key: asset", "key: owner-email", StringComparison.Ordinal));
        repo.Lint().ShouldContainRule("DLV-04");
    }
}
