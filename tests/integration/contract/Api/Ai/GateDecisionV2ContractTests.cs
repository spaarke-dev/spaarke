using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// GateDecision v2 contract test (spaarke-ai-architecture-redesign-r2 task 012 · FR-A0-04 ·
/// design D-F1). Locks the Phase-A0 gate-outcome shape the Policy v2 engine (FR-A1-03 /
/// task 032) produces and Compose r2 consumes (FR-05 / FR-28).
///
/// KEEP-path integration/contract test (ADR-038 §2 path #5). Self-contained — pure DTO
/// round-trip + reference producer/consumer; NO <c>Mock&lt;HttpMessageHandler&gt;</c>, NO
/// DI-registration assertions, no WebApplicationFactory (the contract is transport-free).
///
/// It asserts, in one place:
///   1. the versioned tolerant-reader shape (unknown extra field ignored),
///   2. the carried fields (tier/origin/completeness/overlays/confirmation-state),
///   3. the documented overlay precedence matches the Policy v2 note,
///   4. producer → serialize → deserialize → consumer round-trip,
///   5. a CONFIRMED decision renders NO second ask (structural — ADR-040),
///   6. NEGATIVE: a runtime-LLM-judged tier/origin is rejected (ADR-039),
///   7. the OPTIONAL parent-association affordance (matter/project/invoice/work-assignment/none).
/// </summary>
public class GateDecisionV2ContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ---------------------------------------------------------------------
    // 1. Versioned, tolerant-reader shape
    // ---------------------------------------------------------------------

    [Fact]
    public void GateDecision_HasVersionField_DefaultingToCurrentSchemaVersion()
    {
        var decision = new GateDecisionV2
        {
            Tier = GateRiskTier.Tier2b,
            Origin = GateRequestOrigin.Explicit,
            Completeness = GateArgCompleteness.Complete,
            Outcome = GateOutcome.ExecuteWithUndo,
        };

        decision.SchemaVersion.Should().Be(GateDecisionV2.CurrentSchemaVersion);
        GateDecisionV2.CurrentSchemaVersion.Should().Be(2, "this is the v2 contract");
    }

    [Fact]
    public void GateDecision_Deserialize_IgnoresUnknownExtraField_TolerantReader()
    {
        // A forward-compatible payload from a NEWER producer carrying an extra field the
        // reader does not know. Tolerant-reader rule: unknown fields are ignored, not fatal.
        var json = """
        {
            "schemaVersion": 2,
            "tier": "Tier2b",
            "origin": "Explicit",
            "completeness": "Complete",
            "overlays": { "injectionSuspect": false, "safetyPerimeterDegraded": false, "incompleteArgs": false, "decisive": null },
            "confirmationState": "Pending",
            "outcome": "ConfirmDialog",
            "tierProvenance": "CatalogDeclared",
            "someFutureFieldR3": { "nested": true },
            "anotherUnknownScalar": 42
        }
        """;

        var act = () => JsonSerializer.Deserialize<GateDecisionV2>(json, JsonOptions);

        act.Should().NotThrow("unknown extra fields must be ignored (additive-only tolerant reader)");
        var decision = act();
        decision!.Tier.Should().Be(GateRiskTier.Tier2b);
        decision.Outcome.Should().Be(GateOutcome.ConfirmDialog);
        decision.ConfirmationState.Should().Be(GateConfirmationState.Pending);
    }

    // ---------------------------------------------------------------------
    // 2 + 3 + 4. Carried fields + producer/consumer round-trip
    // ---------------------------------------------------------------------

    [Fact]
    public void Producer_ToJson_ToConsumer_RoundTrips_CarryingAllFields()
    {
        // Producer emits from catalog DATA (Tier 2b, explicit, complete — the worked
        // trace "create a follow-up task due Friday, assign it to me").
        var produced = GateDecisionProjector.Project(
            tier: GateRiskTier.Tier2b,
            origin: GateRequestOrigin.Explicit,
            completeness: GateArgCompleteness.Complete);

        produced.Tier.Should().Be(GateRiskTier.Tier2b);
        produced.Origin.Should().Be(GateRequestOrigin.Explicit);
        produced.Completeness.Should().Be(GateArgCompleteness.Complete);
        produced.Outcome.Should().Be(GateOutcome.ExecuteWithUndo, "explicit+complete Tier 2b executes with an Undo chip, no dialog");
        produced.TierProvenance.Should().Be(GateRiskProvenance.CatalogDeclared);

        // Serialize → deserialize (tolerant) → consumer acts on the shape.
        var json = JsonSerializer.Serialize(produced, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<GateDecisionV2>(json, JsonOptions)!;

        roundTripped.Should().BeEquivalentTo(produced, "the contract round-trips losslessly through JSON");
        GateDecisionConsumer.RequiresUserPrompt(roundTripped).Should().BeFalse();
        GateDecisionConsumer.Resolve(roundTripped).Should().Be(GateRenderAction.AutoExecute);
    }

    [Theory]
    // origin+tier base table (policy-v2 note §1)
    [InlineData(GateRiskTier.Tier0, GateRequestOrigin.Inferred, GateOutcome.Execute)]
    [InlineData(GateRiskTier.Tier1, GateRequestOrigin.Inferred, GateOutcome.Execute)]
    [InlineData(GateRiskTier.Tier2a, GateRequestOrigin.Explicit, GateOutcome.ExecuteWithUndo)]
    [InlineData(GateRiskTier.Tier2a, GateRequestOrigin.Inferred, GateOutcome.ConfirmDialog)]
    [InlineData(GateRiskTier.Tier2b, GateRequestOrigin.Explicit, GateOutcome.ExecuteWithUndo)]
    [InlineData(GateRiskTier.Tier2b, GateRequestOrigin.Inferred, GateOutcome.ConfirmDialog)]
    [InlineData(GateRiskTier.Tier2c, GateRequestOrigin.Explicit, GateOutcome.ConfirmDialog)]
    [InlineData(GateRiskTier.Tier3, GateRequestOrigin.Explicit, GateOutcome.ConfirmDialog)]
    [InlineData(GateRiskTier.Tier4, GateRequestOrigin.Explicit, GateOutcome.ConfirmDialog)]
    public void Producer_ResolvesOutcome_PerPolicyV2TierTable(
        GateRiskTier tier, GateRequestOrigin origin, GateOutcome expected)
    {
        var decision = GateDecisionProjector.Project(tier, origin, GateArgCompleteness.Complete);

        decision.Outcome.Should().Be(expected);
    }

    [Fact]
    public void Producer_OverlayPrecedence_InjectionSuspect_WinsOverExplicitLowTier()
    {
        // E-6 / overlay 1: dispatchUncertain on an otherwise-explicit complete request ⇒ dialog.
        var decision = GateDecisionProjector.Project(
            tier: GateRiskTier.Tier2b,
            origin: GateRequestOrigin.Explicit,
            completeness: GateArgCompleteness.Complete,
            injectionSuspect: true);

        decision.Overlays.Decisive.Should().Be(GateOverlay.InjectionSuspect);
        decision.Outcome.Should().Be(GateOutcome.ConfirmDialog, "injection-suspect always wins ⇒ dialog");
    }

    [Fact]
    public void Producer_OverlayPrecedence_InjectionBeatsSafetyBeatsIncompleteArgs()
    {
        // All three overlays active — precedence: injection > safety > incomplete-args.
        var allThree = GateDecisionProjector.Project(
            GateRiskTier.Tier2b, GateRequestOrigin.Explicit, GateArgCompleteness.Incomplete,
            injectionSuspect: true, safetyPerimeterDegraded: true);
        allThree.Overlays.Decisive.Should().Be(GateOverlay.InjectionSuspect);

        // Safety + incomplete only — safety wins.
        var safetyAndIncomplete = GateDecisionProjector.Project(
            GateRiskTier.Tier2b, GateRequestOrigin.Explicit, GateArgCompleteness.Incomplete,
            safetyPerimeterDegraded: true);
        safetyAndIncomplete.Overlays.Decisive.Should().Be(GateOverlay.SafetyPerimeterDegraded);

        // Incomplete only.
        var incompleteOnly = GateDecisionProjector.Project(
            GateRiskTier.Tier2b, GateRequestOrigin.Explicit, GateArgCompleteness.Incomplete);
        incompleteOnly.Overlays.Decisive.Should().Be(GateOverlay.IncompleteArgs);
        incompleteOnly.Outcome.Should().Be(GateOutcome.Elicit, "incomplete args ⇒ ONE elicitation turn");
    }

    [Fact]
    public void Producer_SafetyPerimeterDegraded_LeavesReadsFailOpen()
    {
        // Overlay 2: reads (Tier 0) stay fail-open; gated writes (Tier >= 2) degrade to confirm.
        var read = GateDecisionProjector.Project(
            GateRiskTier.Tier0, GateRequestOrigin.Inferred, GateArgCompleteness.Complete,
            safetyPerimeterDegraded: true);
        read.Outcome.Should().Be(GateOutcome.Execute, "reads stay fail-open under shield degradation (D-F0(b))");

        var write = GateDecisionProjector.Project(
            GateRiskTier.Tier2b, GateRequestOrigin.Explicit, GateArgCompleteness.Complete,
            safetyPerimeterDegraded: true);
        write.Outcome.Should().Be(GateOutcome.ConfirmDialog, "gated writes degrade to confirm-required");
    }

    // ---------------------------------------------------------------------
    // 5. Confirmed ⇒ NO second ask (structural — ADR-040)
    // ---------------------------------------------------------------------

    [Fact]
    public void ConfirmedDecision_RendersNoSecondAsk_EvenWhenOutcomeIsConfirmDialog()
    {
        // The engine originally resolved a dialog, but the Gate ledger now records CONFIRMED.
        // Structural invariant: a confirmed gate is NEVER re-asked, regardless of Outcome.
        var confirmed = new GateDecisionV2
        {
            Tier = GateRiskTier.Tier3,
            Origin = GateRequestOrigin.Explicit,
            Completeness = GateArgCompleteness.Complete,
            Outcome = GateOutcome.ConfirmDialog,           // would prompt...
            ConfirmationState = GateConfirmationState.Confirmed, // ...but stored state says already confirmed
            GateId = "confirmation-abc123",
        };

        GateDecisionConsumer.RequiresUserPrompt(confirmed).Should().BeFalse(
            "a confirmed gate cannot trigger a second ask — confirmation-state is ledger-backed (ADR-040)");
        GateDecisionConsumer.Resolve(confirmed).Should().Be(GateRenderAction.AutoExecute);
    }

    [Fact]
    public void Producer_FromConfirmedLedgerStatus_ProjectsConfirmedState_NoPrompt()
    {
        // Producer reads the ADR-040 ledger vocabulary ("confirmed") — the PendingPlanManager
        // GateStatusConfirmed constant value — and projects a non-prompting decision.
        var decision = GateDecisionProjector.Project(
            GateRiskTier.Tier2b, GateRequestOrigin.Explicit, GateArgCompleteness.Complete,
            ledgerGateStatus: "confirmed", gateId: "confirmation-xyz");

        decision.ConfirmationState.Should().Be(GateConfirmationState.Confirmed);
        GateDecisionConsumer.RequiresUserPrompt(decision).Should().BeFalse();
    }

    [Theory]
    [InlineData("confirmed", GateConfirmationState.Confirmed)]
    [InlineData("confirmed-unexecutable", GateConfirmationState.Confirmed)]
    [InlineData("dispatch-failed", GateConfirmationState.Confirmed)]
    [InlineData("rejected", GateConfirmationState.Rejected)]
    [InlineData("expired", GateConfirmationState.Expired)]
    [InlineData("superseded", GateConfirmationState.Superseded)]
    [InlineData("pending", GateConfirmationState.Pending)]
    [InlineData(null, GateConfirmationState.None)]
    public void MapLedgerStatus_MapsAdr040Vocabulary_ToConfirmationState(
        string? ledgerStatus, GateConfirmationState expected)
    {
        GateDecisionProjector.MapLedgerStatus(ledgerStatus).Should().Be(expected);
    }

    [Theory]
    [InlineData(GateConfirmationState.Rejected)]
    [InlineData(GateConfirmationState.Expired)]
    [InlineData(GateConfirmationState.Superseded)]
    public void TerminalNonConfirmedStates_NeverReprompt(GateConfirmationState terminal)
    {
        var decision = new GateDecisionV2
        {
            Tier = GateRiskTier.Tier2b,
            Origin = GateRequestOrigin.Explicit,
            Completeness = GateArgCompleteness.Complete,
            Outcome = GateOutcome.ConfirmDialog,
            ConfirmationState = terminal,
        };

        GateDecisionConsumer.RequiresUserPrompt(decision).Should().BeFalse(
            "a terminal gate state is not re-asked");
        GateDecisionConsumer.Resolve(decision).Should().Be(GateRenderAction.HonestBlock);
    }

    // ---------------------------------------------------------------------
    // 6. NEGATIVE — runtime-LLM-judged risk is rejected (ADR-039)
    // ---------------------------------------------------------------------

    [Fact]
    public void RuntimeModelJudgedProvenance_IsRejected_ByValidate()
    {
        // A decision whose tier/origin came from a runtime model risk judgment is the
        // second-intent mechanism ADR-039 bans. Validate MUST reject it.
        var modelJudged = new GateDecisionV2
        {
            Tier = GateRiskTier.Tier2b,
            Origin = GateRequestOrigin.Explicit,
            Completeness = GateArgCompleteness.Complete,
            Outcome = GateOutcome.ExecuteWithUndo,
            TierProvenance = GateRiskProvenance.RuntimeModelJudged,
        };

        modelJudged.IsCatalogGrounded.Should().BeFalse();

        var act = () => modelJudged.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ADR-039*", "risk is catalog-declared DATA, never a runtime LLM judgment");
    }

    [Fact]
    public void RuntimeModelJudgedProvenance_NeverRendersAsLiveAction()
    {
        var modelJudged = new GateDecisionV2
        {
            Tier = GateRiskTier.Tier2b,
            Origin = GateRequestOrigin.Explicit,
            Completeness = GateArgCompleteness.Complete,
            Outcome = GateOutcome.ExecuteWithUndo,
            TierProvenance = GateRiskProvenance.RuntimeModelJudged,
        };

        // Fail-closed at the consumer too: a non-catalog-grounded decision blocks, never executes.
        GateDecisionConsumer.RequiresUserPrompt(modelJudged).Should().BeFalse();
        GateDecisionConsumer.Resolve(modelJudged).Should().Be(GateRenderAction.HonestBlock);
    }

    [Fact]
    public void Projector_CannotExpressModelJudgedProvenance_AlwaysCatalogDeclared()
    {
        // The producer's signature only accepts catalog enums — model risk-judgment is
        // not even expressible, so every projected decision is catalog-grounded.
        var decision = GateDecisionProjector.Project(
            GateRiskTier.Tier4, GateRequestOrigin.Inferred, GateArgCompleteness.Complete);

        decision.TierProvenance.Should().Be(GateRiskProvenance.CatalogDeclared);
        decision.IsCatalogGrounded.Should().BeTrue();
    }

    // ---------------------------------------------------------------------
    // 7. OPTIONAL parent-association affordance
    // ---------------------------------------------------------------------

    [Fact]
    public void GateDecision_CarriesOptionalAssociationAffordance_AllFourTargetsPlusNone()
    {
        var affordance = new GateAssociationAffordance
        {
            AllowedTargets = new[]
            {
                GateAssociationTargetType.None,
                GateAssociationTargetType.Matter,
                GateAssociationTargetType.Project,
                GateAssociationTargetType.Invoice,
                GateAssociationTargetType.WorkAssignment,
            },
            Selected = GateAssociationTargetType.Matter,
            SelectedRecordId = "matter-0001",
            Required = false,
        };

        var decision = GateDecisionProjector.Project(
            GateRiskTier.Tier2b, GateRequestOrigin.Explicit, GateArgCompleteness.Complete,
            association: affordance);

        decision.Association.Should().NotBeNull();
        decision.Association!.AllowedTargets.Should().Contain(new[]
        {
            GateAssociationTargetType.Matter,
            GateAssociationTargetType.Project,
            GateAssociationTargetType.Invoice,
            GateAssociationTargetType.WorkAssignment,
            GateAssociationTargetType.None,
        }, "the one gate dialog hosts the associate-to picker without a bespoke banner");
        decision.Association.HasSelection.Should().BeTrue();

        // Survives the JSON round-trip (Compose r2 consumes it via PublicContracts).
        var json = JsonSerializer.Serialize(decision, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<GateDecisionV2>(json, JsonOptions)!;
        roundTripped.Association!.Selected.Should().Be(GateAssociationTargetType.Matter);
        roundTripped.Association.SelectedRecordId.Should().Be("matter-0001");
    }

    [Fact]
    public void AssociationAffordance_IsOptional_NullWhenNotOffered()
    {
        var decision = GateDecisionProjector.Project(
            GateRiskTier.Tier2b, GateRequestOrigin.Explicit, GateArgCompleteness.Complete);

        decision.Association.Should().BeNull("the affordance is opt-in; actions without it carry null");
    }

    [Fact]
    public void AssociationAffordance_NoneSelection_HasNoConcreteSelection()
    {
        var affordance = new GateAssociationAffordance
        {
            AllowedTargets = new[] { GateAssociationTargetType.None, GateAssociationTargetType.Matter },
            Selected = GateAssociationTargetType.None,
        };

        affordance.HasSelection.Should().BeFalse();
    }
}
