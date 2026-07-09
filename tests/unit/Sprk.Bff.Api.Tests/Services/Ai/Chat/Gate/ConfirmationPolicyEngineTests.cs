using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Gate;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Gate;

/// <summary>
/// Confirmation Policy v2 gate ENGINE (FR-A1-03 / task 032) — the deterministic producer of
/// <see cref="GateDecisionV2"/> from (risk tier × origin × completeness × overlays × ledger).
/// Pure domain logic (ADR-038 unit/domain KEEP category).
///
/// Reproduces the policy-v2 note §6 worked traces (incl. the negative rows), the strict overlay
/// precedence, the structural no-second-ask (ADR-040), and the Tier 2c association picker —
/// mapping 1:1 to the task-032 acceptance criteria.
/// </summary>
public class ConfirmationPolicyEngineTests
{
    // Catalog-declared profiles (mirror the task-020 seed rows).
    private static readonly GateRiskProfile Create2b = new()
    {
        DeclaredTier = GateRiskTier.Tier2b, Reversible = true, RecordOfTruthImpact = true,
    };
    private static readonly GateRiskProfile EmailSend4 = new()
    {
        DeclaredTier = GateRiskTier.Tier4, Reversible = false, ExternallyVisible = true,
    };
    private static readonly GateRiskProfile Deadline3 = new()
    {
        DeclaredTier = GateRiskTier.Tier3, Reversible = true, DeadlineImpact = true,
    };
    private static readonly GateRiskProfile DocVersion2c = new()
    {
        DeclaredTier = GateRiskTier.Tier2c, Reversible = true, RecordOfTruthImpact = true,
    };
    private static readonly GateRiskProfile Read0 = new() { DeclaredTier = GateRiskTier.Tier0 };

    private static GateOriginRequest ExplicitUtterance() => new()
    {
        Source = GateRequestSource.UserUtterance, UtteranceEnumeratesCapability = true,
    };

    // =========================================================================
    // Criterion 6 — worked traces reproduce.
    // =========================================================================

    [Fact]
    public void Evaluate_CreateFollowUpTask_2bExplicitComplete_AutoExecutesWithUndo_NoDialog()
    {
        // "create a follow-up task due Friday, assign it to me" — 2b, explicit, complete.
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Create2b,
            Origin = ExplicitUtterance(),
            ArgsComplete = true,
        });

        decision.Tier.Should().Be(GateRiskTier.Tier2b);
        decision.Origin.Should().Be(GateRequestOrigin.Explicit);
        decision.Outcome.Should().Be(GateOutcome.ExecuteWithUndo);
        GateDecisionConsumer.RequiresUserPrompt(decision).Should().BeFalse("explicit+complete 2b executes, no dialog");
        decision.TierProvenance.Should().Be(GateRiskProvenance.CatalogDeclared, "risk is catalog DATA (criterion 1)");
    }

    [Fact]
    public void Evaluate_TwoProposalsThenGoAhead_E1Fails_ConfirmsOnce()
    {
        // model proposes two options → user "go ahead" → E-1 fails ⇒ inferred ⇒ ONE dialog.
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Create2b,
            Origin = new GateOriginRequest
            {
                Source = GateRequestSource.UserUtterance,
                IsBareAffirmation = true,
                PrecedingProposal = new GateProposalContext { ConcreteActionCount = 2, ArgsComplete = true },
            },
            ArgsComplete = true,
        });

        decision.Origin.Should().Be(GateRequestOrigin.Inferred);
        decision.Outcome.Should().Be(GateOutcome.ConfirmDialog);
    }

    [Fact]
    public void Evaluate_SingleCompleteProposalThenGoAhead_E1Passes_ExecutesWithUndo()
    {
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Create2b,
            Origin = new GateOriginRequest
            {
                Source = GateRequestSource.UserUtterance,
                IsBareAffirmation = true,
                PrecedingProposal = new GateProposalContext { ConcreteActionCount = 1, ArgsComplete = true },
            },
            ArgsComplete = true,
        });

        decision.Origin.Should().Be(GateRequestOrigin.Explicit);
        decision.Outcome.Should().Be(GateOutcome.ExecuteWithUndo, "one complete proposal + affirmation ⇒ no re-ask");
    }

    [Fact]
    public void Evaluate_EmailSend_Tier4_AlwaysDialogs_EvenExplicitComplete()
    {
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = EmailSend4,
            Origin = ExplicitUtterance(),
            ArgsComplete = true,
        });

        decision.Tier.Should().Be(GateRiskTier.Tier4);
        decision.Outcome.Should().Be(GateOutcome.ConfirmDialog, "Tier 4 always dialogs regardless of origin");
    }

    [Fact]
    public void Evaluate_Deadline_Tier3_AlwaysDialogs()
    {
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Deadline3,
            Origin = ExplicitUtterance(),
            ArgsComplete = true,
        });

        decision.Outcome.Should().Be(GateOutcome.ConfirmDialog);
    }

    [Fact]
    public void Evaluate_PromptShieldTimeout_DegradesWriteToConfirm_LeavesReadsFailOpen()
    {
        // Overlay 2: a degraded safety perimeter degrades gated WRITES to confirm-required...
        var write = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Create2b,
            Origin = ExplicitUtterance(),
            ArgsComplete = true,
            SafetyPerimeterDegraded = true,
        });
        write.Overlays.Decisive.Should().Be(GateOverlay.SafetyPerimeterDegraded);
        write.Outcome.Should().Be(GateOutcome.ConfirmDialog);

        // ...while READS stay fail-open (D-F0(b)).
        var read = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Read0,
            Origin = new GateOriginRequest { Source = GateRequestSource.UserUtterance },
            ArgsComplete = true,
            SafetyPerimeterDegraded = true,
        });
        read.Outcome.Should().Be(GateOutcome.Execute, "reads stay fail-open under shield degradation");
    }

    // =========================================================================
    // Criterion 7 (negatives) — doc-origin + dispatchUncertain.
    // =========================================================================

    [Fact]
    public void Evaluate_UploadedDocumentText_CreateTaskToWireFunds_ClassifiesInferred_ConfirmsAtMinimum()
    {
        // E-3: an uploaded document's text is doc-origin, never a user utterance.
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Create2b,
            Origin = new GateOriginRequest
            {
                Source = GateRequestSource.DocumentContent,
                UtteranceEnumeratesCapability = true, // the doc TEXT names the action — must be ignored
            },
            ArgsComplete = true,
        });

        decision.Origin.Should().Be(GateRequestOrigin.Inferred, "document-derived content is never a user utterance (E-3)");
        decision.Outcome.Should().Be(GateOutcome.ConfirmDialog, "confirms at minimum");
    }

    [Fact]
    public void Evaluate_BareGoAhead_WithDispatchUncertain_DialogsWithSuspicion_EvenExplicitComplete()
    {
        // E-6 / overlay 1: dispatchUncertain wins over an otherwise-explicit, complete request.
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Create2b,
            Origin = new GateOriginRequest
            {
                Source = GateRequestSource.UserUtterance,
                IsBareAffirmation = true,
                PrecedingProposal = new GateProposalContext { ConcreteActionCount = 1, ArgsComplete = true },
            },
            ArgsComplete = true,
            DispatchUncertain = true,
        });

        decision.Origin.Should().Be(GateRequestOrigin.Explicit, "the origin itself is explicit...");
        decision.Overlays.Decisive.Should().Be(GateOverlay.InjectionSuspect, "...but injection-suspect overlay fires first");
        decision.Outcome.Should().Be(GateOutcome.ConfirmDialog);
    }

    // =========================================================================
    // Criterion 2 — strict overlay precedence (injection > safety > incomplete-args).
    // =========================================================================

    [Fact]
    public void Evaluate_AllOverlaysActive_InjectionSuspectWins()
    {
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Create2b,
            Origin = ExplicitUtterance(),
            ArgsComplete = false,           // overlay 3
            SafetyPerimeterDegraded = true, // overlay 2
            DispatchUncertain = true,       // overlay 1
        });

        decision.Overlays.Decisive.Should().Be(GateOverlay.InjectionSuspect);
    }

    [Fact]
    public void Evaluate_IncompleteArgs_Tier2b_ElicitsOnce()
    {
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Create2b,
            Origin = ExplicitUtterance(),
            ArgsComplete = false,
        });

        decision.Overlays.Decisive.Should().Be(GateOverlay.IncompleteArgs);
        decision.Outcome.Should().Be(GateOutcome.Elicit);
    }

    // =========================================================================
    // Criterion 5 — confirmation state is a ledger property; no second ask.
    // =========================================================================

    [Fact]
    public void Evaluate_WithConfirmedGateInLedger_RendersNoSecondAsk_Structural()
    {
        const string gateId = "confirmation-abc123";
        // The ledger records this gate as pending → confirmed (append-only, ADR-040).
        var ledger = new List<SessionGate>
        {
            new() { GateId = gateId, Kind = "confirmation", Status = "pending", Turn = 3 },
            new() { GateId = gateId, Kind = "confirmation", Status = "confirmed", Turn = 3 },
        };

        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = EmailSend4,   // even a Tier 4 that WOULD dialog...
            Origin = ExplicitUtterance(),
            ArgsComplete = true,
            Ledger = ledger,
            GateId = gateId,
        });

        decision.ConfirmationState.Should().Be(GateConfirmationState.Confirmed);
        GateDecisionConsumer.RequiresUserPrompt(decision).Should().BeFalse(
            "a confirmed gate is NEVER re-asked — confirmation state is ledger-backed (R3-1 loop impossible)");
    }

    [Fact]
    public void Evaluate_WithPendingGateInLedger_StillPrompts()
    {
        const string gateId = "confirmation-pending-1";
        var ledger = new List<SessionGate>
        {
            new() { GateId = gateId, Kind = "confirmation", Status = "pending", Turn = 1 },
        };

        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Create2b,
            Origin = new GateOriginRequest { Source = GateRequestSource.ModelInitiated },
            ArgsComplete = true,
            Ledger = ledger,
            GateId = gateId,
        });

        decision.ConfirmationState.Should().Be(GateConfirmationState.Pending);
        GateDecisionConsumer.RequiresUserPrompt(decision).Should().BeTrue();
    }

    // =========================================================================
    // Criterion 7 (association) — Tier 2c hosts the optional parent-association picker.
    // =========================================================================

    [Fact]
    public void Evaluate_Tier2cDocumentCreate_HostsOptionalAssociationPicker_InTheOneDialog()
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
            Selected = GateAssociationTargetType.None,
            Required = false, // optional — the user MAY associate; the container is resolved deterministically, never prompted
        };

        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = DocVersion2c,
            Origin = ExplicitUtterance(),
            ArgsComplete = true,
            Association = affordance,
        });

        decision.Tier.Should().Be(GateRiskTier.Tier2c);
        decision.Outcome.Should().Be(GateOutcome.ConfirmDialog, "Tier 2c previews/confirms in r2");
        decision.Association.Should().NotBeNull("the ONE dialog hosts the associate-to picker — no bespoke banner");
        decision.Association!.AllowedTargets.Should().Contain(new[]
        {
            GateAssociationTargetType.Matter,
            GateAssociationTargetType.Project,
            GateAssociationTargetType.Invoice,
            GateAssociationTargetType.WorkAssignment,
            GateAssociationTargetType.None,
        });
        decision.Association.Required.Should().BeFalse("association is optional — 'none' is a valid choice");
    }

    [Fact]
    public void Evaluate_NonTier2c_DropsAssociationPicker_NeverInventsAffordance()
    {
        var affordance = new GateAssociationAffordance
        {
            AllowedTargets = new[] { GateAssociationTargetType.None, GateAssociationTargetType.Matter },
            Selected = GateAssociationTargetType.None,
        };

        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = Create2b, // Tier 2b — not a document create/version
            Origin = ExplicitUtterance(),
            ArgsComplete = true,
            Association = affordance,
        });

        decision.Association.Should().BeNull(
            "the association picker rides ONLY the Tier 2c document dialog — the engine never invents it elsewhere");
    }
}
