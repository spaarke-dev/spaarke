// R3 Part 1 — User-Record Membership Resolution (config binding test)
// Task 012 (2026-06-21): Verifies MembershipOptions binds correctly from the
// "Membership" appsettings section shape documented in design.md Part 1.
// Uses in-memory IConfiguration so the test does not depend on the gitignored
// appsettings.Development.json file.

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

[Trait("status", "repaired")]
public class MembershipOptionsTests
{
    [Fact]
    public void DefaultValues_AreConservativeEmptyDefaults()
    {
        var options = new MembershipOptions();

        options.IncludedIdentityTables.Should().BeEmpty();
        options.GlobalFieldExclusions.Should().BeEmpty();
        options.RoleNameStrategy.Should().Be("CamelCase");
        options.MetadataCacheTtlMinutes.Should().Be(60);
        options.EntityOverrides.Should().BeEmpty();
    }

    [Fact]
    public void AddMembership_BindsOptionsFromConfiguration()
    {
        // Arrange — in-memory config mirrors appsettings.Development.json.template
        // shape (see design.md Part 1 § Configuration shape).
        var configValues = new Dictionary<string, string?>
        {
            ["Membership:IncludedIdentityTables:0:Table"] = "systemuser",
            ["Membership:IncludedIdentityTables:0:IdentityType"] = "SystemUser",
            ["Membership:IncludedIdentityTables:1:Table"] = "contact",
            ["Membership:IncludedIdentityTables:1:IdentityType"] = "Contact",
            ["Membership:IncludedIdentityTables:2:Table"] = "team",
            ["Membership:IncludedIdentityTables:2:IdentityType"] = "Team",
            ["Membership:IncludedIdentityTables:3:Table"] = "businessunit",
            ["Membership:IncludedIdentityTables:3:IdentityType"] = "BusinessUnit",
            ["Membership:IncludedIdentityTables:4:Table"] = "account",
            ["Membership:IncludedIdentityTables:4:IdentityType"] = "Account",
            ["Membership:IncludedIdentityTables:5:Table"] = "sprk_organization",
            ["Membership:IncludedIdentityTables:5:IdentityType"] = "Organization",

            ["Membership:GlobalFieldExclusions:0"] = "createdby",
            ["Membership:GlobalFieldExclusions:1"] = "modifiedby",
            ["Membership:GlobalFieldExclusions:2"] = "createdonbehalfby",
            ["Membership:GlobalFieldExclusions:3"] = "modifiedonbehalfby",

            ["Membership:RoleNameStrategy"] = "CamelCase",
            ["Membership:MetadataCacheTtlMinutes"] = "120",

            ["Membership:EntityOverrides:sprk_matter:FieldRoleOverrides:sprk_assignedlawfirm1"] = "assignedLawFirm",
            ["Membership:EntityOverrides:sprk_matter:FieldRoleOverrides:sprk_assignedlawfirm2"] = "assignedLawFirm"
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.AddMembership(config);
        using var sp = services.BuildServiceProvider();

        // Act
        var options = sp.GetRequiredService<IOptions<MembershipOptions>>().Value;

        // Assert — top-level scalars
        options.RoleNameStrategy.Should().Be("CamelCase");
        options.MetadataCacheTtlMinutes.Should().Be(120);

        // Assert — IncludedIdentityTables (6 entries per design.md)
        options.IncludedIdentityTables.Should().HaveCount(6);
        options.IncludedIdentityTables.Should().ContainEquivalentOf(
            new IdentityTableConfig { Table = "systemuser", IdentityType = "SystemUser" });
        options.IncludedIdentityTables.Should().ContainEquivalentOf(
            new IdentityTableConfig { Table = "sprk_organization", IdentityType = "Organization" });

        // Assert — GlobalFieldExclusions (4 entries per design.md)
        options.GlobalFieldExclusions.Should().BeEquivalentTo(new[]
        {
            "createdby",
            "modifiedby",
            "createdonbehalfby",
            "modifiedonbehalfby"
        });

        // Assert — EntityOverrides binding (the acceptance criterion: per-entity
        // FieldRoleOverrides resolve correctly for sprk_matter / assignedlawfirm).
        options.EntityOverrides.Should().ContainKey("sprk_matter");
        var matter = options.EntityOverrides["sprk_matter"];
        matter.FieldRoleOverrides["sprk_assignedlawfirm1"].Should().Be("assignedLawFirm");
        matter.FieldRoleOverrides["sprk_assignedlawfirm2"].Should().Be("assignedLawFirm");
        matter.ExcludedFields.Should().BeEmpty();
        matter.IncludedFields.Should().BeEmpty();
    }

    [Fact]
    public void AddMembership_ReturnsServicesForChaining()
    {
        var services = new ServiceCollection();
        IConfiguration config = new ConfigurationBuilder().Build();

        var result = services.AddMembership(config);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddMembership_WithEmptyConfig_StillResolvesDefaults()
    {
        // Confirms the "production deployments that never opt in still work" guarantee.
        var services = new ServiceCollection();
        IConfiguration config = new ConfigurationBuilder().Build();
        services.AddMembership(config);
        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<MembershipOptions>>().Value;

        options.Should().NotBeNull();
        options.IncludedIdentityTables.Should().BeEmpty();
        options.GlobalFieldExclusions.Should().BeEmpty();
        options.RoleNameStrategy.Should().Be("CamelCase");
        options.MetadataCacheTtlMinutes.Should().Be(60);
        options.EntityOverrides.Should().BeEmpty();
    }
}
