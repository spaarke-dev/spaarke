// R3 Part 1 — Task 030: MembershipFieldDiscoveryService unit tests
//
// Verifies the metadata-driven Lookup-field discovery contract from
// spec.md FR-1A.1 through FR-1A.4 + FR-1A.7, and design.md Part 1 §
// "Discovery algorithm":
//
//   - Happy path (sprk_matter): AC-1A.1 expected fields all discovered with
//     correct roles + identity types — owner (SystemUser), owningteam (Team),
//     owningbusinessunit (BusinessUnit), sprk_assignedattorney1/2 (Contact),
//     sprk_assignedparalegal1/2 (Contact), sprk_assignedlawfirm1/2 (Organization
//     per Q4 fix), sprk_assignedtointernal, sprk_assignedtoexternal.
//   - System-field exclusion: createdby/modifiedby etc. land in ExcludedFields
//     with reason="global-exclusion".
//   - Non-identity-target lookups (sprk_chartdefinition → some-other-table)
//     land in IgnoredFields with reason="target-table-not-in-identity-list".
//   - CamelCase role-name strategy: sprk_AssignedAttorney1 → "assignedAttorney".
//   - Q4 fix: sprk_assignedlawfirm1 target=sprk_organization →
//     identityType="Organization" (NOT Contact, as design.md's report example
//     originally showed before owner clarification).
//   - Per-entity FieldRoleOverrides: sprk_assignedlawfirm1 → role "assignedLawFirm"
//     with source="override".
//   - Per-entity ExcludedFields: drops fields with reason="per-entity-exclusion".
//   - Per-entity IncludedFields: force-includes globally-excluded fields with
//     source="override".
//   - Cache hit: second call within TTL skips metadata fetch (subclass verifies
//     FetchLookupAttributesAsync called exactly once).
//   - Input guards: null/empty/whitespace entityLogicalName throws
//     ArgumentException; entityLogicalName normalized to lowercase for cache key.
//
// Test strategy: subclass MembershipFieldDiscoveryService and override the
// protected virtual FetchLookupAttributesAsync seam with canned LookupAttributeRow
// data. Avoids the need to stand up a ServiceClient mock or wrap RetrieveEntityRequest.
// Mirrors the canonical sub-classing pattern used elsewhere in the codebase for
// SDK-dependent services.

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

[Trait("status", "repaired")]
public class MembershipFieldDiscoveryServiceTests
{
    private const string MatterEntity = "sprk_matter";

    // ────────────────────────────────────────────────────────────────────
    // Happy path — AC-1A.1: discovery for sprk_matter
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_SprkMatter_DiscoversExpectedFieldsPerAC_1A_1()
    {
        // Arrange — full sprk_matter lookup roster as documented in AC-1A.1
        // + the Q4-clarified sprk_assignedlawfirm1/2 → sprk_organization wiring
        // + a few system fields that must be globally excluded
        // + one non-identity-target lookup that must be ignored.
        var lookups = new[]
        {
            // System / standard membership lookups
            Lookup("ownerid",                  "systemuser"),
            Lookup("owningteam",               "team"),
            Lookup("owningbusinessunit",       "businessunit"),
            // System touch-history (globally excluded)
            Lookup("createdby",                "systemuser"),
            Lookup("modifiedby",               "systemuser"),
            Lookup("createdonbehalfby",        "systemuser"),
            Lookup("modifiedonbehalfby",       "systemuser"),
            // Spaarke assignment fields — Contact-targeted
            Lookup("sprk_assignedattorney1",   "contact"),
            Lookup("sprk_assignedattorney2",   "contact"),
            Lookup("sprk_assignedparalegal1",  "contact"),
            Lookup("sprk_assignedparalegal2",  "contact"),
            // Q4 fix: law-firm lookups target sprk_organization, NOT contact
            Lookup("sprk_assignedlawfirm1",    "sprk_organization"),
            Lookup("sprk_assignedlawfirm2",    "sprk_organization"),
            // Generic internal/external assignment
            Lookup("sprk_assignedtointernal",  "systemuser"),
            Lookup("sprk_assignedtoexternal",  "contact"),
            // Lookup whose target is NOT in the identity-table list — must
            // surface in IgnoredFields, not DiscoveredFields.
            Lookup("sprk_chartdefinition",     "sprk_chartdefinition"),
        };

        var sut = new TestableMembershipFieldDiscoveryService(
            BuildOptions(),
            cannedLookups: lookups);

        // Act
        var result = await sut.DiscoverAsync(MatterEntity, CancellationToken.None);

        // Assert — exact set of discovered fields
        result.EntityType.Should().Be(MatterEntity);
        result.DiscoveredFields.Select(d => d.Field).Should().BeEquivalentTo(new[]
        {
            "ownerid",
            "owningteam",
            "owningbusinessunit",
            "sprk_assignedattorney1",
            "sprk_assignedattorney2",
            "sprk_assignedparalegal1",
            "sprk_assignedparalegal2",
            "sprk_assignedlawfirm1",
            "sprk_assignedlawfirm2",
            "sprk_assignedtointernal",
            "sprk_assignedtoexternal",
        });

        // Identity types correctly derived from target tables
        DescriptorFor(result, "ownerid").IdentityType.Should().Be("SystemUser");
        DescriptorFor(result, "owningteam").IdentityType.Should().Be("Team");
        DescriptorFor(result, "owningbusinessunit").IdentityType.Should().Be("BusinessUnit");
        DescriptorFor(result, "sprk_assignedattorney1").IdentityType.Should().Be("Contact");
        DescriptorFor(result, "sprk_assignedparalegal1").IdentityType.Should().Be("Contact");
        DescriptorFor(result, "sprk_assignedtointernal").IdentityType.Should().Be("SystemUser");
        DescriptorFor(result, "sprk_assignedtoexternal").IdentityType.Should().Be("Contact");

        // Globally-excluded system fields surface in ExcludedFields, NOT DiscoveredFields
        result.ExcludedFields.Select(e => e.Field).Should().BeEquivalentTo(new[]
        {
            "createdby", "modifiedby", "createdonbehalfby", "modifiedonbehalfby",
        });
        result.ExcludedFields.Should().OnlyContain(e => e.Reason == "global-exclusion");

        // Non-identity-target lookup ignored with target name preserved
        result.IgnoredFields.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new IgnoredField(
                "sprk_chartdefinition",
                "target-table-not-in-identity-list",
                "sprk_chartdefinition"));
    }

    // ────────────────────────────────────────────────────────────────────
    // Q4 fix — sprk_assignedlawfirm1/2 → Organization (the critical bug guard)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_AssignedLawFirmFields_ResolveToOrganizationIdentityTypePerQ4()
    {
        // Per spec.md Q4 owner clarification (2026-06-20): the Lookup target
        // for sprk_assignedlawfirm1/2 is sprk_organization, NOT contact as
        // the design.md discovery-report example originally showed. This test
        // is the regression guard for that fix.
        var lookups = new[]
        {
            Lookup("sprk_assignedlawfirm1", "sprk_organization"),
            Lookup("sprk_assignedlawfirm2", "sprk_organization"),
        };

        var sut = new TestableMembershipFieldDiscoveryService(BuildOptions(), lookups);

        var result = await sut.DiscoverAsync(MatterEntity, CancellationToken.None);

        var lf1 = DescriptorFor(result, "sprk_assignedlawfirm1");
        var lf2 = DescriptorFor(result, "sprk_assignedlawfirm2");

        lf1.IdentityType.Should().Be("Organization");
        lf1.TargetTable.Should().Be("sprk_organization");
        lf2.IdentityType.Should().Be("Organization");
        lf2.TargetTable.Should().Be("sprk_organization");

        // No per-entity override configured here → role auto-derived; the
        // override test below pins the "disambiguate to assignedLawFirm" case.
    }

    // ────────────────────────────────────────────────────────────────────
    // CamelCase role-name strategy — design.md Part 1 § Discovery step 5
    // ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_assignedattorney1", "assignedattorney")]
    [InlineData("sprk_assignedattorney2", "assignedattorney")]
    [InlineData("sprk_assignedparalegal1", "assignedparalegal")]
    [InlineData("ownerid", "ownerid")] // no sprk_ prefix, no trailing digits
    [InlineData("owningteam", "owningteam")]
    public async Task DiscoverAsync_RoleNameStrategy_StripsSprkPrefixAndTrailingDigits(
        string fieldLogicalName,
        string expectedRole)
    {
        var sut = new TestableMembershipFieldDiscoveryService(
            BuildOptions(),
            new[] { Lookup(fieldLogicalName, fieldLogicalName.Contains("attorney") || fieldLogicalName.Contains("paralegal") ? "contact" : (fieldLogicalName == "owningteam" ? "team" : "systemuser")) });

        var result = await sut.DiscoverAsync(MatterEntity, CancellationToken.None);

        var descriptor = DescriptorFor(result, fieldLogicalName);
        descriptor.Role.Should().Be(expectedRole);
        descriptor.Source.Should().Be("auto");
    }

    [Fact]
    public void DeriveRoleNameCamelCase_SchemaNameWithCapitals_LowercasesFirstChar()
    {
        // Direct test of the static helper — verifies the design.md example
        // "sprk_AssignedAttorney1 → assignedAttorney" works when a SchemaName-
        // style value flows through (vs the canonical lowercase LogicalName).
        var role = MembershipFieldDiscoveryService.DeriveRoleNameCamelCase("sprk_AssignedAttorney1");
        role.Should().Be("assignedAttorney");
    }

    // ────────────────────────────────────────────────────────────────────
    // Per-entity overrides
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_FieldRoleOverride_AppliesOverrideAndMarksSourceAsOverride()
    {
        // Arrange — operator configures both lawfirm lookups to share the
        // "assignedLawFirm" role (the canonical disambiguation example from
        // design.md Part 1 § Configuration shape).
        var options = BuildOptions();
        options.EntityOverrides[MatterEntity] = new EntityOverride
        {
            FieldRoleOverrides =
            {
                ["sprk_assignedlawfirm1"] = "assignedLawFirm",
                ["sprk_assignedlawfirm2"] = "assignedLawFirm",
            }
        };

        var lookups = new[]
        {
            Lookup("sprk_assignedlawfirm1", "sprk_organization"),
            Lookup("sprk_assignedlawfirm2", "sprk_organization"),
        };

        var sut = new TestableMembershipFieldDiscoveryService(options, lookups);

        // Act
        var result = await sut.DiscoverAsync(MatterEntity, CancellationToken.None);

        // Assert
        var lf1 = DescriptorFor(result, "sprk_assignedlawfirm1");
        var lf2 = DescriptorFor(result, "sprk_assignedlawfirm2");

        lf1.Role.Should().Be("assignedLawFirm");
        lf1.Source.Should().Be("override");
        lf1.IdentityType.Should().Be("Organization"); // still Organization, not changed by override

        lf2.Role.Should().Be("assignedLawFirm");
        lf2.Source.Should().Be("override");
    }

    [Fact]
    public async Task DiscoverAsync_PerEntityExcludedField_DropsWithReasonPerEntityExclusion()
    {
        var options = BuildOptions();
        options.EntityOverrides[MatterEntity] = new EntityOverride
        {
            ExcludedFields = { "sprk_assignedtoexternal" }
        };

        var lookups = new[]
        {
            Lookup("sprk_assignedattorney1",  "contact"),
            Lookup("sprk_assignedtoexternal", "contact"),
        };

        var sut = new TestableMembershipFieldDiscoveryService(options, lookups);

        var result = await sut.DiscoverAsync(MatterEntity, CancellationToken.None);

        result.DiscoveredFields.Select(d => d.Field).Should()
            .BeEquivalentTo(new[] { "sprk_assignedattorney1" });
        result.ExcludedFields.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new IgnoredField(
                "sprk_assignedtoexternal",
                "per-entity-exclusion"));
    }

    [Fact]
    public async Task DiscoverAsync_PerEntityIncludedFields_ForceIncludesGloballyExcludedFieldAsOverride()
    {
        // Edge case: operator wants the createdby Lookup (normally globally
        // excluded as touch-history) to count as membership for one specific
        // entity. The discovery service must override the global exclusion and
        // mark the descriptor's source as "override".
        var options = BuildOptions();
        options.EntityOverrides[MatterEntity] = new EntityOverride
        {
            IncludedFields = { "createdby" }
        };

        var lookups = new[]
        {
            Lookup("createdby", "systemuser"),
        };

        var sut = new TestableMembershipFieldDiscoveryService(options, lookups);

        var result = await sut.DiscoverAsync(MatterEntity, CancellationToken.None);

        result.DiscoveredFields.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new MembershipDescriptor(
                Field: "createdby",
                Role: "createdby", // CamelCase strategy applied, no sprk_ prefix to strip
                IdentityType: "SystemUser",
                TargetTable: "systemuser",
                Source: "override"));
        result.ExcludedFields.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────
    // Cache hit (ADR-009: TTL from MembershipOptions.MetadataCacheTtlMinutes)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_SecondCallWithinTtl_HitsCacheAndSkipsMetadataFetch()
    {
        var lookups = new[]
        {
            Lookup("ownerid",                "systemuser"),
            Lookup("sprk_assignedattorney1", "contact"),
        };

        var cache = new FakeDistributedCache();
        var sut = new TestableMembershipFieldDiscoveryService(
            BuildOptions(),
            lookups,
            cache: cache);

        // Act — call twice
        var first = await sut.DiscoverAsync(MatterEntity, CancellationToken.None);
        var second = await sut.DiscoverAsync(MatterEntity, CancellationToken.None);

        // Assert — same descriptor set; metadata fetched ONCE (first call only)
        second.DiscoveredFields.Select(d => d.Field).Should()
            .BeEquivalentTo(first.DiscoveredFields.Select(d => d.Field));

        sut.FetchInvocationCount.Should().Be(1,
            "second DiscoverAsync call should hit cache, not metadata");

        cache.SetCallCount.Should().Be(1,
            "cache should be populated exactly once across both calls");
        cache.GetCallCount.Should().BeGreaterThanOrEqualTo(2,
            "cache should be read on both calls (miss then hit)");
    }

    [Fact]
    public async Task DiscoverAsync_NormalizesEntityNameToLowerCase()
    {
        var lookups = new[] { Lookup("ownerid", "systemuser") };
        var sut = new TestableMembershipFieldDiscoveryService(BuildOptions(), lookups);

        // Mixed-case input
        var result = await sut.DiscoverAsync("Sprk_Matter", CancellationToken.None);

        result.EntityType.Should().Be("sprk_matter");
        sut.LastFetchedEntityName.Should().Be("sprk_matter");
    }

    // ────────────────────────────────────────────────────────────────────
    // Input guards
    // ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DiscoverAsync_NullEmptyOrWhitespaceEntityName_ThrowsArgumentException(string? entityName)
    {
        var sut = new TestableMembershipFieldDiscoveryService(
            BuildOptions(),
            Array.Empty<MembershipFieldDiscoveryService.LookupAttributeRow>());

        Func<Task> act = () => sut.DiscoverAsync(entityName!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("entityLogicalName");
    }

    [Fact]
    public async Task DiscoverAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var sut = new TestableMembershipFieldDiscoveryService(
            BuildOptions(),
            Array.Empty<MembershipFieldDiscoveryService.LookupAttributeRow>());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => sut.DiscoverAsync(MatterEntity, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ────────────────────────────────────────────────────────────────────
    // R7 W12 task 130 — regression: zero-config defaults still discover
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_WithPostConfiguredDefaultMembershipOptions_DiscoversMatterAssignmentFields()
    {
        // R7 W12 task 130 (2026-06-30): regression test for the
        // "0-memberships-for-every-user in deployed environments" bug. The
        // "Membership" appsettings section is NOT present in
        // appsettings.template.json, appsettings.Production.json.template, OR in
        // the Bicep appSettings used to deploy to spaarkedev1. Before this fix,
        // MembershipOptions defaulted to empty IncludedIdentityTables, which
        // caused EVERY membership-bearing lookup on sprk_matter (incl. ownerid,
        // sprk_assignedattorney1, sprk_assignedparalegal1) to be classified as
        // "target-table-not-in-identity-list" (silent no-op). Downstream,
        // MembershipResolverService.ResolveAsync would return Count=0 for every
        // user. The fix adds a MembershipOptionsDefaults post-configure step
        // (registered in MembershipModule.AddMembership) that seeds the
        // canonical identity tables + audit exclusions when the bound list is
        // empty.
        //
        // This test pins the contract by applying the same post-configure step
        // the production DI pipeline applies, then verifies that the canonical
        // sprk_matter assignment fields are discovered. The post-configure is
        // simulated directly here (rather than going through ServiceCollection)
        // to keep this test focused on the discovery contract; the DI wiring
        // itself is covered by MembershipOptionsTests.AddMembership_*.

        var lookups = new[]
        {
            Lookup("ownerid",                  "systemuser"),
            Lookup("owningteam",               "team"),
            Lookup("owningbusinessunit",       "businessunit"),
            Lookup("createdby",                "systemuser"),    // globally excluded
            Lookup("sprk_assignedattorney1",   "contact"),
            Lookup("sprk_assignedparalegal1",  "contact"),
            Lookup("sprk_assignedlawfirm1",    "sprk_organization"),
        };

        // Apply the production post-configure step on raw default options —
        // this is what AddMembership() does for any environment that does NOT
        // bind a "Membership" appsettings section.
        var defaultOptions = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(name: null, options: defaultOptions);

        var sut = new TestableMembershipFieldDiscoveryService(defaultOptions, lookups);

        var result = await sut.DiscoverAsync(MatterEntity, CancellationToken.None);

        // The bug-was-here assertion: after the post-configure seeds defaults,
        // BOTH the systemuser lookups (ownerid, owningteam, owningbusinessunit)
        // AND the contact / organization lookups (sprk_assignedattorney1,
        // sprk_assignedparalegal1, sprk_assignedlawfirm1) MUST be discovered.
        // Pre-fix, all 6 would be classified IgnoredField with reason
        // "target-table-not-in-identity-list".
        result.DiscoveredFields.Select(d => d.Field).Should().BeEquivalentTo(new[]
        {
            "ownerid", "owningteam", "owningbusinessunit",
            "sprk_assignedattorney1", "sprk_assignedparalegal1", "sprk_assignedlawfirm1",
        }, because: "MembershipOptionsDefaults seeds the 6 canonical identity tables so " +
                    "membership-bearing lookups on sprk_matter are discovered even when " +
                    "no 'Membership' section is bound from appsettings");

        // Audit fields land in ExcludedFields (default GlobalFieldExclusions).
        result.ExcludedFields.Select(e => e.Field).Should().Contain("createdby",
            because: "MembershipOptionsDefaults seeds the standard audit-field exclusions " +
                     "(createdby, modifiedby, createdonbehalfby, modifiedonbehalfby)");

        // Spot-check the identity-type derivation works through the seeded config.
        DescriptorFor(result, "sprk_assignedattorney1").IdentityType.Should().Be("Contact");
        DescriptorFor(result, "sprk_assignedlawfirm1").IdentityType.Should().Be("Organization");
        DescriptorFor(result, "ownerid").IdentityType.Should().Be("SystemUser");
    }

    // ────────────────────────────────────────────────────────────────────
    // R7 W12 T130 follow-up (2026-06-30) — polymorphic Owner + Customer
    // fallback synthesis. Pins the ProjectLookupAttributeRows contract that
    // fixes the "sprk_matter resolved rows=0 for a user who owns 44 matters"
    // production symptom. See rationale block in MembershipFieldDiscoveryService
    // for evidence + root-cause analysis.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProjectLookupAttributeRows_OwnerAttributeAsBaseAttributeMetadata_SynthesizesSystemUserAndTeamTargets()
    {
        // Simulates the modern-SDK deserialization behavior for polymorphic
        // Owner attributes: `ownerid` comes back as base `AttributeMetadata`
        // with `AttributeType=Owner` because OwnerAttributeMetadata is not a
        // registered [KnownType] on AttributeMetadata. Without the fallback
        // synthesis, `.OfType<LookupAttributeMetadata>()` would drop it.
        var ownerAttr = CreateBaseAttributeMetadata(Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Owner);
        ownerAttr.LogicalName = "ownerid";

        var regularLookup = new Microsoft.Xrm.Sdk.Metadata.LookupAttributeMetadata();
        regularLookup.LogicalName = "sprk_someregularlookup";

        var attributes = new Microsoft.Xrm.Sdk.Metadata.AttributeMetadata[]
        {
            ownerAttr,
            regularLookup,
        };

        var rows = MembershipFieldDiscoveryService.ProjectLookupAttributeRows(attributes);

        rows.Should().HaveCount(2);

        var ownerRow = rows.Single(r => r.LogicalName == "ownerid");
        ownerRow.Targets.Should().BeEquivalentTo(new[] { "systemuser", "team" },
            because: "polymorphic Owner attributes always target systemuser + team in Dataverse");

        var regularRow = rows.Single(r => r.LogicalName == "sprk_someregularlookup");
        regularRow.Targets.Should().BeEmpty(
            because: "the regular Lookup has no Targets configured in this synthetic test");
    }

    [Fact]
    public void ProjectLookupAttributeRows_CustomerAttributeAsBaseAttributeMetadata_SynthesizesAccountAndContactTargets()
    {
        // Same rationale as the Owner case, but for polymorphic Customer
        // attributes (targets account + contact — the standard Dataverse
        // Customer type binding).
        var customerAttr = CreateBaseAttributeMetadata(Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Customer);
        customerAttr.LogicalName = "customerid";

        var attributes = new[] { customerAttr };

        var rows = MembershipFieldDiscoveryService.ProjectLookupAttributeRows(attributes);

        var customerRow = rows.Should().ContainSingle().Subject;
        customerRow.LogicalName.Should().Be("customerid");
        customerRow.Targets.Should().BeEquivalentTo(new[] { "account", "contact" });
    }

    [Fact]
    public void ProjectLookupAttributeRows_OwnerAttributeAlreadyCapturedByPrimaryPath_NotDoubleCounted()
    {
        // Belt-and-suspenders: if a future SDK version DOES deserialize an
        // Owner attribute as LookupAttributeMetadata (with Targets already
        // populated), the primary-pass row must win and the fallback pass
        // must NOT append a duplicate. The `seen` set is what guards this.
        var ownerAsLookup = new Microsoft.Xrm.Sdk.Metadata.LookupAttributeMetadata();
        ownerAsLookup.LogicalName = "ownerid";
        // Simulate the SDK populating Targets from the server response — the
        // Targets property is publicly settable via the DataContract surface.
        typeof(Microsoft.Xrm.Sdk.Metadata.LookupAttributeMetadata)
            .GetProperty("Targets")!
            .SetValue(ownerAsLookup, new[] { "systemuser", "team" });

        var attributes = new Microsoft.Xrm.Sdk.Metadata.AttributeMetadata[]
        {
            ownerAsLookup,
        };

        var rows = MembershipFieldDiscoveryService.ProjectLookupAttributeRows(attributes);

        rows.Should().ContainSingle(r => r.LogicalName == "ownerid",
            because: "the primary LookupAttributeMetadata pass captured it — " +
                     "the Owner fallback pass must not double-count");
        rows.Single().Targets.Should().BeEquivalentTo(new[] { "systemuser", "team" });
    }

    [Fact]
    public void ProjectLookupAttributeRows_NonLookupNonOwnerNonCustomerAttribute_Ignored()
    {
        // Baseline: a String attribute (or anything that isn't Lookup/Owner/
        // Customer) is not membership-bearing and must not appear.
        var stringAttr = new Microsoft.Xrm.Sdk.Metadata.StringAttributeMetadata();
        stringAttr.LogicalName = "sprk_name";

        var intAttr = CreateBaseAttributeMetadata(Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Integer);
        intAttr.LogicalName = "sprk_count";

        var attributes = new Microsoft.Xrm.Sdk.Metadata.AttributeMetadata[]
        {
            stringAttr,
            intAttr,
        };

        var rows = MembershipFieldDiscoveryService.ProjectLookupAttributeRows(attributes);

        rows.Should().BeEmpty();
    }

    [Fact]
    public void ProjectLookupAttributeRows_AttributeWithEmptyLogicalName_Skipped()
    {
        var noNameOwner = CreateBaseAttributeMetadata(Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Owner);
        // LogicalName intentionally left null

        var attributes = new[] { noNameOwner };

        var rows = MembershipFieldDiscoveryService.ProjectLookupAttributeRows(attributes);

        rows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    private static MembershipOptions BuildOptions() => new()
    {
        IncludedIdentityTables = new List<IdentityTableConfig>
        {
            new() { Table = "systemuser",         IdentityType = "SystemUser" },
            new() { Table = "contact",            IdentityType = "Contact" },
            new() { Table = "team",               IdentityType = "Team" },
            new() { Table = "businessunit",       IdentityType = "BusinessUnit" },
            new() { Table = "account",            IdentityType = "Account" },
            new() { Table = "sprk_organization",  IdentityType = "Organization" },
        },
        GlobalFieldExclusions = new List<string>
        {
            "createdby", "modifiedby", "createdonbehalfby", "modifiedonbehalfby"
        },
        RoleNameStrategy = "CamelCase",
        MetadataCacheTtlMinutes = 60,
    };

    private static MembershipFieldDiscoveryService.LookupAttributeRow Lookup(
        string logicalName,
        params string[] targets)
        => new(logicalName, targets);

    /// <summary>
    /// Creates a bare <see cref="Microsoft.Xrm.Sdk.Metadata.AttributeMetadata"/>
    /// with the requested <paramref name="typeCode"/> via reflection — the
    /// `AttributeMetadata(AttributeTypeCode)` constructor is `protected` in
    /// the SDK, but reflection can still invoke it. This mirrors the way the
    /// SDK's DataContract deserializer materializes attributes that don't
    /// match a registered `[KnownType]` (i.e., what happens for
    /// `@odata.type=OwnerAttributeMetadata` responses in modern Dataverse).
    /// </summary>
    private static Microsoft.Xrm.Sdk.Metadata.AttributeMetadata CreateBaseAttributeMetadata(
        Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode typeCode)
    {
        var ctor = typeof(Microsoft.Xrm.Sdk.Metadata.AttributeMetadata)
            .GetConstructor(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode) },
                modifiers: null)
            ?? throw new InvalidOperationException(
                "AttributeMetadata(AttributeTypeCode) constructor not found — SDK version drift?");
        return (Microsoft.Xrm.Sdk.Metadata.AttributeMetadata)ctor.Invoke(new object[] { typeCode });
    }

    private static MembershipDescriptor DescriptorFor(DiscoveryResult result, string field)
        => result.DiscoveredFields.SingleOrDefault(d => d.Field == field)
            ?? throw new InvalidOperationException(
                $"Expected DiscoveredFields to contain '{field}', but it did not. " +
                $"Actual fields: [{string.Join(", ", result.DiscoveredFields.Select(d => d.Field))}]");

    /// <summary>
    /// Test subclass — overrides the protected virtual metadata-fetch seam to
    /// return canned <see cref="MembershipFieldDiscoveryService.LookupAttributeRow"/>
    /// instances without touching Dataverse. Tracks invocation count + last
    /// requested entity name for cache-hit + normalization assertions.
    /// </summary>
    private sealed class TestableMembershipFieldDiscoveryService : MembershipFieldDiscoveryService
    {
        private readonly IReadOnlyList<LookupAttributeRow> _cannedLookups;
        private int _fetchInvocationCount;
        public string? LastFetchedEntityName { get; private set; }

        public int FetchInvocationCount => _fetchInvocationCount;

        public TestableMembershipFieldDiscoveryService(
            MembershipOptions options,
            IReadOnlyList<LookupAttributeRow> cannedLookups,
            ITenantCache? cache = null)
            : base(
                new Mock<IDataverseService>(MockBehavior.Loose).Object,
                cache ?? new FakeDistributedCache(),
                Options.Create(options),
                NullLogger<MembershipFieldDiscoveryService>.Instance)
        {
            _cannedLookups = cannedLookups;
        }

        protected override Task<IReadOnlyList<LookupAttributeRow>> FetchLookupAttributesAsync(
            string entityLogicalName,
            CancellationToken ct)
        {
            Interlocked.Increment(ref _fetchInvocationCount);
            LastFetchedEntityName = entityLogicalName;
            return Task.FromResult(_cannedLookups);
        }
    }

    /// <summary>
    /// Tiny in-memory <see cref="ITenantCache"/> for unit-test isolation
    /// (mirrors the FakeDistributedCache in IdentityNormalizationServiceTests).
    /// Tracks Get/Set counts so cache-hit assertions can verify behavior without
    /// a Redis dependency.
    /// </summary>
    private sealed class FakeDistributedCache : ITenantCache
    {
        private readonly ConcurrentDictionary<string, object?> _store = new(StringComparer.Ordinal);
        private int _getCount;
        private int _setCount;

        public int GetCallCount => _getCount;
        public int SetCallCount => _setCount;

        private static string BuildKey(string tenantId, string resource, string id, int version)
            => $"tenant:{tenantId}:{resource}:{id}:v{version}";

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
        {
            Interlocked.Increment(ref _getCount);
            var key = BuildKey(tenantId, resource, id, version);
            return Task.FromResult(_store.TryGetValue(key, out var v) ? (T?)v : default);
        }

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            Interlocked.Increment(ref _setCount);
            var key = BuildKey(tenantId, resource, id, version);
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
        {
            var key = BuildKey(tenantId, resource, id, version);
            _store.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            var existing = await GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);
            if (existing is not null)
            {
                return existing;
            }
            var produced = await factory(ct);
            if (produced is not null)
            {
                await SetAsync(tenantId, resource, id, version, produced, ttl, cacheInstance, ct);
            }
            return produced!;
        }
    }
}
