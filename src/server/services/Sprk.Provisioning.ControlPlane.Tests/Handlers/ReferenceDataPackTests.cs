// -----------------------------------------------------------------------------
// ReferenceDataPackTests.cs
//
// Data-setup plan D4: the `referenceDataPack` intake key and the packs behind it
// must agree. IntakeParameterCatalog.AllowedReferenceDataPacks is what POST
// /api/runs and H12b accept; ReferenceDataSet.PackTableNames is what the seeder
// can load. A value accepted but not loadable would fail H12b on every run that
// used it; a pack that cannot be selected is dead data. Each embedded file must
// also exist (the csproj wildcard embeds whatever is on disk).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ReferenceDataPackTests
{
    [Fact]
    public void D4_EveryAcceptedPack_ExceptNone_IsAPackTheSeederCanLoad_AndTheReverse()
    {
        var accepted = IntakeParameterCatalog.AllowedReferenceDataPacks
            .Where(p => p != IntakeParameterCatalog.NoReferenceDataPack);

        accepted.Should().BeEquivalentTo(ReferenceDataSet.PackTableNames.Keys,
            "the intake key accepts exactly the packs that ship in scripts/seed-data/reference/packs/");
        foreach (var pack in ReferenceDataSet.PackTableNames.Keys)
        {
            ReferenceDataSet.Pack(pack).Should().HaveCount(ReferenceDataSet.PackTableNames[pack].Count, $"every embedded file of '{pack}' loads");
        }
    }

    [Fact]
    public void D4_EveryPackFileOnDisk_IsListedByThePackRegistry_SoItIsNeitherEmbeddedDeadNorMissing()
    {
        var root = FindRepoRoot();
        foreach (var (pack, tables) in ReferenceDataSet.PackTableNames)
        {
            var onDisk = Directory.EnumerateFiles(Path.Combine(root, "scripts", "seed-data", "reference", "packs", pack), "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Order(StringComparer.Ordinal);

            onDisk.Should().Equal(tables.Order(StringComparer.Ordinal),
                $"a pack file must be listed in ReferenceDataSet.PackTableNames['{pack}'] (and the reverse), or H12b would ignore it");
        }

        Directory.EnumerateDirectories(Path.Combine(root, "scripts", "seed-data", "reference", "packs"))
            .Select(Path.GetFileName)
            .Should().BeEquivalentTo(ReferenceDataSet.PackTableNames.Keys, "a pack folder no intake value selects is dead data");
    }

    [Fact]
    public void D4_ResolveReferenceDataPack_DefaultsToLegalStandard_AndReturnsTheStoredValueAsIs()
    {
        IntakeParameterCatalog.ResolveReferenceDataPack(new Dictionary<string, string>())
            .Should().Be(IntakeParameterCatalog.LegalStandardReferenceDataPack);
        IntakeParameterCatalog.ResolveReferenceDataPack(new Dictionary<string, string> { ["referenceDataPack"] = "  " })
            .Should().Be(IntakeParameterCatalog.LegalStandardReferenceDataPack, "blank means absent, as for solutionPackageType");
        IntakeParameterCatalog.ResolveReferenceDataPack(new Dictionary<string, string> { ["referenceDataPack"] = "none" })
            .Should().Be("none");
        IntakeParameterCatalog.ResolveReferenceDataPack(new Dictionary<string, string> { ["referenceDataPack"] = "bogus" })
            .Should().Be("bogus", "the caller checks the allowed set");
        IntakeParameterCatalog.IsKnown("referenceDataPack").Should().BeTrue();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "scripts", "seed-data", "reference")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root (scripts/seed-data/reference) not found above the test binary.");
    }
}
