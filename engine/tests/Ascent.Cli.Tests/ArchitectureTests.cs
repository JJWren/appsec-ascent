using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Versioning;
using Ascent.Cli.Hosting;

namespace Ascent.Cli.Tests;

/// <summary>Enforces the allowed project references and the coverage-exclusion rule (P29, P24).</summary>
public sealed class ArchitectureTests
{
    // Direct references each Engine assembly may have to other Engine assemblies (logical-components.md).
    private static readonly Dictionary<string, string[]> AllowedReferences = new(StringComparer.Ordinal)
    {
        ["Ascent.Content"] = [],
        ["Ascent.Core"] = ["Ascent.Content"],
        ["Ascent.Storage"] = ["Ascent.Core", "Ascent.Content"],
        ["Ascent.Sealing"] = ["Ascent.Core", "Ascent.Content"],
        ["Ascent.Assessment"] = ["Ascent.Sealing", "Ascent.Core", "Ascent.Content"],
        ["Ascent.Labs"] = ["Ascent.Sealing", "Ascent.Core", "Ascent.Content"],
        ["Ascent.Deliverables"] = ["Ascent.Sealing", "Ascent.Core", "Ascent.Content"],
        ["Ascent.Integrations"] = ["Ascent.Core", "Ascent.Content"],
        ["ascent"] = ["Ascent.Content", "Ascent.Core", "Ascent.Storage", "Ascent.Sealing", "Ascent.Assessment", "Ascent.Labs", "Ascent.Deliverables", "Ascent.Integrations"],
    };

    private static readonly string[] InfrastructurePackagesForbiddenInCore = ["Microsoft.Data.Sqlite", "SQLitePCLRaw", "Spectre."];

    [Fact]
    public void Engine_assemblies_reference_only_what_they_are_allowed_to()
    {
        foreach (var assembly in EngineAssemblies())
        {
            var name = assembly.GetName().Name!;
            AllowedReferences.ShouldContainKey(name, "Add the new assembly to the allowed-reference table.");
            var engineReferences = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(IsEngineAssembly)
                .ToList();
            engineReferences.ShouldBeSubsetOf(AllowedReferences[name], name + " references an assembly it must not.");
        }
    }

    [Fact]
    public void No_learner_assembly_references_the_maintainer_tool() =>
        EngineAssemblies().ShouldAllBe(assembly => assembly.GetReferencedAssemblies().All(reference => reference.Name != "ascent-maint"));

    [Fact]
    public void Core_stays_free_of_infrastructure_packages()
    {
        var references = typeof(Ascent.Core.EnginePaths).Assembly.GetReferencedAssemblies().Select(r => r.Name!).ToList();
        references.ShouldNotContain(name => InfrastructurePackagesForbiddenInCore.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void Coverage_exclusions_are_only_for_platform_specific_types()
    {
        // Coverlet injects its own excluded tracker types while measuring; they aren't Engine code.
        var excluded = EngineAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttribute<ExcludeFromCodeCoverageAttribute>() is not null)
            .Where(type => type.Namespace?.StartsWith("Coverlet.", StringComparison.Ordinal) != true)
            .ToList();

        excluded.ShouldNotBeEmpty("The Windows owner-only adapter is the expected first entry.");

        excluded.ShouldAllBe(type => type.GetCustomAttributes<SupportedOSPlatformAttribute>().Any());
    }

    private static List<Assembly> EngineAssemblies()
    {
        // Loading the CLI's references brings in every Engine assembly it uses.
        var cli = typeof(EngineApp).Assembly;
        var assemblies = new List<Assembly> { cli };
        foreach (var reference in cli.GetReferencedAssemblies().Where(r => IsEngineAssembly(r.Name!)))
        {
            assemblies.Add(Assembly.Load(reference));
        }

        return assemblies;
    }

    private static bool IsEngineAssembly(string name) =>
        name.StartsWith("Ascent.", StringComparison.Ordinal) || name is "ascent" or "ascent-maint";
}
