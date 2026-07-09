using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat.Gate;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Gate;

/// <summary>
/// Confirmation Policy v2 risk-tier resolution + catalog-DATA parsing (FR-A1-03 / task 032).
/// Pure domain logic (ADR-038 unit/domain KEEP category) — no mocks, no I/O.
///
/// Protects three production behaviors:
///  1. the effective tier is DATA-derived from the catalog risk profile (criterion 1 — ZERO
///     runtime model judgment: <see cref="RiskTierResolver.Resolve"/> only accepts a
///     <see cref="GateRiskProfile"/>);
///  2. an UNDER-declared row is escalated to the stricter factor floor (fail-closed);
///  3. the seed's <c>sprk_configuration.riskProfile</c> DATA parses to the declared tier
///     (the task-020 authored source is real, machine-read data).
/// </summary>
public class RiskTierResolverTests
{
    // -------------------------------------------------------------------------
    // Correctly-declared rows resolve to exactly their declared tier.
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_EmailDraft_DraftOnlyNoMutation_ResolvesTier1()
    {
        var profile = new GateRiskProfile
        {
            DeclaredTier = GateRiskTier.Tier1,
            Reversible = true,
        };

        RiskTierResolver.Resolve(profile).Should().Be(GateRiskTier.Tier1);
    }

    [Fact]
    public void Resolve_WorkspaceTab_ReversiblePrivate_ResolvesTier2a()
    {
        var profile = new GateRiskProfile { DeclaredTier = GateRiskTier.Tier2a, Reversible = true };

        RiskTierResolver.Resolve(profile).Should().Be(GateRiskTier.Tier2a);
    }

    [Fact]
    public void Resolve_SystemOfRecordCreate_ReversibleRecordOfTruth_ResolvesTier2b()
    {
        var profile = new GateRiskProfile
        {
            DeclaredTier = GateRiskTier.Tier2b,
            Reversible = true,
            RecordOfTruthImpact = true,
        };

        RiskTierResolver.Resolve(profile).Should().Be(GateRiskTier.Tier2b);
    }

    [Fact]
    public void Resolve_Delete_IrreversibleRecordOfTruth_ResolvesTier4()
    {
        var profile = new GateRiskProfile
        {
            DeclaredTier = GateRiskTier.Tier4,
            Reversible = false,
            RecordOfTruthImpact = true,
        };

        RiskTierResolver.Resolve(profile).Should().Be(GateRiskTier.Tier4);
    }

    // -------------------------------------------------------------------------
    // Fail-closed escalation: the factor floor overrides an under-declared tier.
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_UnderDeclaredExternallyVisible_EscalatesToTier4()
    {
        // An externally-visible action mistakenly authored as a low tier must NOT execute at
        // the weaker tier — the factor floor escalates it (Policy v2's exact failure mode).
        var underDeclared = new GateRiskProfile
        {
            DeclaredTier = GateRiskTier.Tier2a,
            Reversible = false,
            ExternallyVisible = true,
        };

        RiskTierResolver.Resolve(underDeclared).Should().Be(
            GateRiskTier.Tier4, "externally-visible ⇒ Tier 4 floor overrides the weaker declaration");
    }

    [Theory]
    [InlineData(true, false, false, false, false, GateRiskTier.Tier0)]   // reversible, no factor ⇒ no floor
    [InlineData(true, false, false, false, true, GateRiskTier.Tier2b)]   // record-of-truth ⇒ 2b floor
    [InlineData(true, false, true, false, false, GateRiskTier.Tier3)]    // deadline ⇒ 3 floor
    [InlineData(true, false, false, true, false, GateRiskTier.Tier3)]    // confidentiality ⇒ 3 floor
    [InlineData(false, false, false, false, false, GateRiskTier.Tier0)]  // irreversibility ALONE (no side effect) ⇒ no floor
    [InlineData(false, false, false, false, true, GateRiskTier.Tier4)]   // irreversible record-of-truth ⇒ 4 floor
    [InlineData(true, true, false, false, false, GateRiskTier.Tier4)]    // externally visible ⇒ 4 floor
    public void FactorFloor_ComputesMinimumTier_FromRiskFactors(
        bool reversible, bool externallyVisible, bool deadline, bool confidentiality,
        bool recordOfTruth, GateRiskTier expectedFloor)
    {
        var profile = new GateRiskProfile
        {
            DeclaredTier = GateRiskTier.Tier0,
            Reversible = reversible,
            ExternallyVisible = externallyVisible,
            DeadlineImpact = deadline,
            ConfidentialityImpact = confidentiality,
            RecordOfTruthImpact = recordOfTruth,
        };

        RiskTierResolver.FactorFloor(profile).Should().Be(expectedFloor);
    }

    [Fact]
    public void Resolve_DeclaredStricterThanFloor_KeepsDeclared_NeverDownRanks()
    {
        // Tier 2c (document) declared with only a record-of-truth factor (floor = 2b): the
        // resolver keeps the stricter DECLARED 2c — the floor never down-ranks.
        var profile = new GateRiskProfile
        {
            DeclaredTier = GateRiskTier.Tier2c,
            Reversible = true,
            RecordOfTruthImpact = true,
        };

        RiskTierResolver.Resolve(profile).Should().Be(GateRiskTier.Tier2c);
    }

    // -------------------------------------------------------------------------
    // Catalog DATA parsing (criterion 1 — task-020 seed source is machine-read).
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("2b", true, false, false, false, true, GateRiskTier.Tier2b)]  // create/update row
    [InlineData("4", false, false, false, false, true, GateRiskTier.Tier4)]   // delete row
    [InlineData("1", true, false, false, false, false, GateRiskTier.Tier1)]   // email draft row
    [InlineData("2a", true, false, false, false, false, GateRiskTier.Tier2a)] // workspace tab row
    public void FromConfiguration_ParsesSeedRiskProfile_ResolvesToDeclaredTier(
        string tierToken, bool reversible, bool externallyVisible, bool deadline,
        bool confidentiality, bool recordOfTruth, GateRiskTier expectedTier)
    {
        // Mirrors the sprk_configuration.riskProfile shape authored in the task-020 seed rows.
        var configJson = $$"""
        {
            "_comment": "reserved",
            "riskProfile": {
                "tier": "{{tierToken}}",
                "reversible": {{Lower(reversible)}},
                "externallyVisible": {{Lower(externallyVisible)}},
                "deadlineImpact": {{Lower(deadline)}},
                "confidentialityImpact": {{Lower(confidentiality)}},
                "recordOfTruthImpact": {{Lower(recordOfTruth)}}
            }
        }
        """;
        using var doc = JsonDocument.Parse(configJson);

        var profile = GateRiskProfile.FromConfiguration(doc.RootElement);

        profile.Should().NotBeNull("the row declares a riskProfile block");
        profile!.DeclaredTier.Should().Be(expectedTier);
        RiskTierResolver.Resolve(profile).Should().Be(expectedTier,
            "correctly-declared seed DATA resolves to exactly its declared tier");
    }

    [Fact]
    public void FromConfiguration_NoRiskProfileBlock_ReturnsNull_LegacyRowTolerance()
    {
        using var doc = JsonDocument.Parse("""{ "_comment": "no risk profile here" }""");

        GateRiskProfile.FromConfiguration(doc.RootElement).Should().BeNull(
            "rows without a declared riskProfile are treated as no-declared-risk (fail-closed to existing gate)");
    }

    [Fact]
    public void FromConfiguration_RiskProfileMissingTier_Throws_TierIsRequiredData()
    {
        using var doc = JsonDocument.Parse("""{ "riskProfile": { "reversible": true } }""");

        var act = () => GateRiskProfile.FromConfiguration(doc.RootElement);

        act.Should().Throw<FormatException>("the tier is required catalog DATA — a model-judged tier is forbidden");
    }

    private static string Lower(bool b) => b ? "true" : "false";
}
