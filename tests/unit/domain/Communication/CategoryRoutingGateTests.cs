using FluentAssertions;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Communication;

/// <summary>
/// Pure-resolution tests (ADR-038 §2 path #6 — mapping logic, no I/O) for the FR-E7 (task 057)
/// <see cref="Sprk.Bff.Api.Services.Communication.Engine.CategoryRoutingGate"/>: it maps a triage-category
/// name to an owning-team name per the ADR-018 <see cref="CategoryRoutingOptions"/>, honoring the global
/// on-off + map and any per-tenant override, and returns <c>null</c> (no assignment) for a disabled/blank/
/// unmapped input. The gate builds over an in-memory options monitor (config source, not a mocked
/// collaborator).
/// </summary>
public sealed class CategoryRoutingGateTests
{
    [Fact]
    public void ResolveTeamName_WhenEnabledAndMapped_ReturnsTeam()
    {
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" },
        });

        gate.ResolveTeamName("Court / Filing").Should().Be("Litigation Team");
    }

    [Fact]
    public void ResolveTeamName_WhenDisabled_ReturnsNull()
    {
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = false, // opt-in default
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" },
        });

        gate.ResolveTeamName("Court / Filing").Should().BeNull();
    }

    [Theory]
    [InlineData("General Correspondence")] // enabled but not in the map
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ResolveTeamName_WhenUnmappedOrBlank_ReturnsNull(string? category)
    {
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" },
        });

        gate.ResolveTeamName(category).Should().BeNull();
    }

    [Fact]
    public void ResolveTeamName_WhenTenantOverridesMap_UsesTheTenantMapping()
    {
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Global Litigation" },
            Tenants =
            {
                ["tenant-b"] = new CategoryRoutingTenantOverride
                {
                    CategoryToTeam = new() { ["Court / Filing"] = "Tenant B Litigation" },
                },
            },
        });

        gate.ResolveTeamName("Court / Filing").Should().Be("Global Litigation");              // global
        gate.ResolveTeamName("Court / Filing", "tenant-b").Should().Be("Tenant B Litigation"); // per-tenant
    }

    [Fact]
    public void ResolveTeamName_WhenTenantDisablesRouting_ReturnsNull()
    {
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" },
            Tenants = { ["tenant-off"] = new CategoryRoutingTenantOverride { Enabled = false } },
        });

        gate.ResolveTeamName("Court / Filing", "tenant-off").Should().BeNull();
    }
}
