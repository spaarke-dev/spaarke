using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ladder tests for the confidence→status mapper (R4 FR-11) — the highest-consequence decision in the
/// Association Engine. Each test names a concrete ladder band, reinforcement rule, AI-never-auto-file
/// guarantee, conflict outcome, or provenance-shape contract that would break in production if deleted.
/// </summary>
public class AssociationStatusMapperTests
{
    private const string MatterField = "sprk_regardingmatter";
    private const string PersonField = "sprk_regardingperson";

    private static RungMatch Match(
        string field, Guid targetId, double confidence, RungKind rung,
        string entity = "sprk_matter", string provenance = "test")
        => new()
        {
            RegardingFieldName = field,
            Target = new EntityReference(entity, targetId),
            Confidence = confidence,
            Provenance = provenance,
            Rung = rung,
        };

    // ── Ladder bands ─────────────────────────────────────────────────────────────

    [Fact]
    public void Decide_DeterministicAtOrAboveThreshold_KillSwitchOn_ResolvesAndAutoFiles()
    {
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(enabled: true, threshold: 0.85);

        var decision = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.ExplicitReference) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.AutoFiled.Should().BeTrue();
        decision.RegardingWrites.Should().ContainKey(MatterField);
        decision.RegardingWrites[MatterField].Id.Should().Be(matterId);
    }

    [Fact]
    public void Decide_FallbackContactMatchOnly_AtOrAboveThreshold_DoesNotAutoFile_ButWritesTheContact()
    {
        // A contact/participant match is a fallback identity signal — not what the email is about. Even at
        // high deterministic confidence it must NOT auto-file to Resolved; it lands Suggested (review for the
        // substantive matter/project/invoice) while the contact is still written (a correct association).
        var contactId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(enabled: true, threshold: 0.85);

        var decision = mapper.Decide(
            new[] { Match(PersonField, contactId, 0.95, RungKind.ParticipantCorrelation, entity: "contact") },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.RegardingWrites.Should().ContainKey(PersonField); // fallback still written (filed)
        decision.RegardingWrites[PersonField].Id.Should().Be(contactId);
    }

    [Fact]
    public void Decide_SubstantiveMatterPlusFallbackContact_AutoFilesOnTheMatter()
    {
        // A substantive matter match ≥ threshold auto-files Resolved; the co-occurring contact fallback is
        // written too. (The contact no longer suppresses — nor is required for — the substantive auto-file.)
        //
        // C-1 DECOUPLING (021): the contact fallback here is identified via rung 2 (ParticipantCorrelation),
        // which is no longer auto-file-eligible for the STATUS decision — but it IS still in the deterministic
        // WRITE set (rung 0–3), so it is persisted alongside the auto-filed matter exactly as the shipped
        // design specifies ("fallback matches are still WRITTEN"). r5's review surface displays it. This
        // proves the C-1 narrowing changed only the auto-file STATUS, not the Resolved-branch write set.
        var matterId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(enabled: true, threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.90, RungKind.ExplicitReference),
                Match(PersonField, contactId, 0.95, RungKind.ParticipantCorrelation, entity: "contact"),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.AutoFiled.Should().BeTrue();
        decision.RegardingWrites.Should().ContainKey(MatterField);
        decision.RegardingWrites.Should().ContainKey(PersonField);
    }

    // ── RecordNameMatch (rung 3.5): deterministic-but-surface-only (email-r4 UAT 2026-07-17) ─────────────

    [Fact]
    public void Decide_RecordNameMatchOnly_SurfacesSuggested_NeverAutoFiles_AndWritesCandidate()
    {
        // A deterministic name match on a SUBSTANTIVE target (matter) at high confidence must NOT auto-file —
        // owner spec: surface for review, the user picks the primary. The candidate is still written so the
        // review UI can one-click confirm it.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(enabled: true, threshold: 0.85);

        var decision = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.RecordNameMatch) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.RegardingWrites.Should().ContainKey(MatterField);
        decision.RegardingWrites[MatterField].Id.Should().Be(matterId);
        // It is a deterministic exact match, NOT an AI rung — must not be mislabeled as AI-involved.
        decision.Provenance.Decision.AiInvolved.Should().BeFalse();
    }

    [Fact]
    public void Decide_RecordNameMatch_MatterAndProject_BothWritten_ForReview()
    {
        // Both a matter and a project are named in the email → surface BOTH (different fields, no conflict),
        // Suggested, so the reviewer chooses the primary.
        var matterId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(enabled: true, threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.90, RungKind.RecordNameMatch),
                Match("sprk_regardingproject", projectId, 0.90, RungKind.RecordNameMatch, entity: "sprk_project"),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.RegardingWrites.Should().ContainKey(MatterField);
        decision.RegardingWrites.Should().ContainKey("sprk_regardingproject");
    }

    [Fact]
    public void Decide_SameDeterministicMatch_KillSwitchOff_DowngradesToSuggested()
    {
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(enabled: false, threshold: 0.85);

        var decision = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.ExplicitReference) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        // The proposed target is still written so the review UI can surface + one-click confirm it.
        decision.RegardingWrites.Should().ContainKey(MatterField);
        decision.Provenance.Decision.Reason.Should().Contain("kill-switch");
    }

    [Fact]
    public void Decide_MidConfidence_LandsSuggested()
    {
        var mapper = AssociationTestSupport.Mapper();

        var decision = mapper.Decide(
            new[] { Match(PersonField, Guid.NewGuid(), 0.70, RungKind.ParticipantCorrelation, entity: "contact") },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.RegardingWrites.Should().ContainKey(PersonField);
    }

    [Fact]
    public void Decide_BelowSuggestFloor_LandsPendingReviewWithNoWrites()
    {
        var mapper = AssociationTestSupport.Mapper();

        var decision = mapper.Decide(
            new[] { Match(MatterField, Guid.NewGuid(), 0.40, RungKind.ParticipantCorrelation) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.PendingReview);
        decision.RegardingWrites.Should().BeEmpty();
    }

    [Fact]
    public void Decide_NoMatches_LandsPendingReview()
    {
        var mapper = AssociationTestSupport.Mapper();

        var decision = mapper.Decide(
            Array.Empty<RungMatch>(), CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.PendingReview);
        decision.RegardingWrites.Should().BeEmpty();
    }

    // ── AI never auto-files ──────────────────────────────────────────────────────

    [Fact]
    public void Decide_AiRungHighConfidenceAlone_NeverAutoFiles_LandsSuggested()
    {
        var mapper = AssociationTestSupport.Mapper();

        var decision = mapper.Decide(
            new[] { Match(MatterField, Guid.NewGuid(), 0.95, RungKind.AiClassification) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.Provenance.Decision.AiInvolved.Should().BeTrue();
    }

    [Fact]
    public void Decide_DeterministicPlusAiOnSameTarget_DeterministicBelowThreshold_DoesNotAutoFile()
    {
        // det 0.70 + AI 0.80 on the SAME matter → full reinforced ~0.94 (≥ threshold) but deterministic
        // alone is 0.70 (< threshold). AI must NOT be able to push the target to auto-file.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.70, RungKind.ParticipantCorrelation),
                Match(MatterField, matterId, 0.80, RungKind.SemanticMatch),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.Provenance.Decision.TopConfidence.Should().BeGreaterThanOrEqualTo(0.85);
        decision.Provenance.Decision.TopDeterministicConfidence.Should().BeLessThan(0.85);
    }

    [Fact]
    public void Decide_DeterministicAboveThreshold_WithAiAgreeing_StillAutoFiles()
    {
        // det 0.90 (≥ threshold) with an AI rung also agreeing must still auto-file — AI agreement never
        // BLOCKS a deterministic target that already clears the bar.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.90, RungKind.ThreadContinuity),
                Match(MatterField, matterId, 0.80, RungKind.SemanticMatch),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.AutoFiled.Should().BeTrue();
    }

    // ── Signal reinforcement (owner priority (b)) ────────────────────────────────

    [Fact]
    public void Decide_TwoIndependentDeterministicRungsAgree_ReinforceOverThreshold_AutoFiles()
    {
        // C-1 (only rung 0 + rung 1 are auto-file-eligible): a rung-0 bare-numeric-band match (0.65,
        // ExplicitReference) reinforcing with a rung-1 thread-inheritance match (0.70, ThreadContinuity) on
        // the SAME field noisy-ORs to 1-(0.30)(0.35) = 0.895 ⇒ auto-file. Both contributing rungs are
        // auto-file-eligible post-narrowing, so reinforcement across them still auto-files.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.70, RungKind.ThreadContinuity),
                Match(MatterField, matterId, 0.65, RungKind.ExplicitReference),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.AutoFiled.Should().BeTrue();
        decision.Provenance.Decision.TopDeterministicConfidence.Should().BeApproximately(0.895, 0.01);
    }

    // ── C-1 auto-file narrowing (rung 0 + rung 1 only; rung 2/3 → Suggested) ─────

    [Fact]
    public void Decide_Rung1ThreadContinuitySubstantiveMatch_AtOrAboveThreshold_AutoFiles()
    {
        // Rung 1 (thread inheritance) is one of the two C-1 auto-file-eligible rungs.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.92, RungKind.ThreadContinuity) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.AutoFiled.Should().BeTrue();
        decision.RegardingWrites[MatterField].Id.Should().Be(matterId);
    }

    [Fact]
    public void Decide_ParticipantCorrelationSubstantiveMatch_AtOrAboveThreshold_LandsSuggested_NotResolved()
    {
        // C-1 narrowing: a sender/participant-only match (rung 2), even at high confidence on a SUBSTANTIVE
        // field (a membership-derived matter, not a fallback contact/org/account), is no longer
        // auto-file-eligible by default — it still MATCHES but resolves to Suggested for human confirmation.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.ParticipantCorrelation) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.RegardingWrites.Should().ContainKey(MatterField); // still written for review
        decision.RegardingWrites[MatterField].Id.Should().Be(matterId);
    }

    [Fact]
    public void Decide_StructuralDetectorSubstantiveMatch_AtOrAboveThreshold_LandsSuggested_NotResolved()
    {
        // C-1 narrowing: a structural-detector match (rung 3), even at high confidence on a substantive
        // field, no longer auto-file-eligible by default — MATCHES but resolves to Suggested.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.StructuralDetector) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.RegardingWrites.Should().ContainKey(MatterField); // still written for review
        decision.RegardingWrites[MatterField].Id.Should().Be(matterId);
    }

    [Fact]
    public void Decide_BareNumericExplicitReferenceWithoutReinforcement_LandsSuggested()
    {
        // Task 020's IdentifierReverseLookupRung emits bare-numeric matches at 0.65 confidence (below the
        // 0.85 threshold) — a rung-0-class match with no same-field reinforcement is not strong enough alone
        // to auto-file; it lands Suggested via the ordinary threshold mechanism (no separate flag needed).
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.65, RungKind.ExplicitReference) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.RegardingWrites.Should().ContainKey(MatterField);
    }

    [Fact]
    public void Decide_KillSwitchRung2And3AutoFileEnabledLegacyMode_ParticipantCorrelationAutoFiles()
    {
        // The C-1 narrowing is kill-switch-governed (ADR-018): toggling Rung2And3AutoFileEnabled = true
        // restores the legacy pre-C-1 behavior, proving the narrowing is revertible without a redeploy.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85, rung2And3AutoFileEnabled: true);

        var decision = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.ParticipantCorrelation) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.AutoFiled.Should().BeTrue();
        decision.RegardingWrites[MatterField].Id.Should().Be(matterId);
    }

    [Fact]
    public void Decide_SameRungKindTwice_DoesNotReinforce_TakesMaxOnly()
    {
        // Two matches from the SAME rung kind on the same target must NOT noisy-OR (that would let a rung
        // inflate its own confidence). Max = 0.70 (< threshold) ⇒ Suggested, not Resolved. Uses rung 1
        // (ThreadContinuity, C-1 auto-file-eligible) so the deterministic confidence is non-zero and the
        // max-not-noisy-OR assertion remains meaningful post-C-1-narrowing.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.70, RungKind.ThreadContinuity),
                Match(MatterField, matterId, 0.70, RungKind.ThreadContinuity),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.Provenance.Decision.TopDeterministicConfidence.Should().BeApproximately(0.70, 0.001);
    }

    // ── Conflict → Ambiguous (owner priority (c)) ────────────────────────────────

    [Fact]
    public void Decide_TwoHighConfidenceTargetsOnSameField_LandsAmbiguousWithNoWrites()
    {
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, Guid.NewGuid(), 0.90, RungKind.ThreadContinuity),
                Match(MatterField, Guid.NewGuid(), 0.90, RungKind.ExplicitReference),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Ambiguous);
        decision.AutoFiled.Should().BeFalse();
        decision.RegardingWrites.Should().BeEmpty();
    }

    [Fact]
    public void Decide_ConflictOnOneField_StillWritesCleanSiblingField()
    {
        // Duplicate same-named projects (legitimate in production) conflict on the project field, but a single
        // exact-name matter on the matter field is unambiguous — it must still be written for review. A
        // conflict on one field must not suppress a clean association on another (email-r4 UAT 2026-07-17).
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.90, RungKind.RecordNameMatch),
                Match("sprk_regardingproject", Guid.NewGuid(), 0.90, RungKind.RecordNameMatch, entity: "sprk_project"),
                Match("sprk_regardingproject", Guid.NewGuid(), 0.90, RungKind.RecordNameMatch, entity: "sprk_project"),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Ambiguous);
        decision.RegardingWrites.Should().ContainKey(MatterField);          // clean field written
        decision.RegardingWrites[MatterField].Id.Should().Be(matterId);
        decision.RegardingWrites.Should().NotContainKey("sprk_regardingproject"); // conflicting field withheld
    }

    [Fact]
    public void Decide_TwoTargetsSameField_OnlyOneHighConfidence_ClearWinnerNoConflict()
    {
        // Winner 0.90, runner-up 0.60 (< threshold) ⇒ not a conflict; the clear winner auto-files.
        var winnerId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, winnerId, 0.90, RungKind.ThreadContinuity),
                Match(MatterField, Guid.NewGuid(), 0.60, RungKind.ParticipantCorrelation),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.RegardingWrites[MatterField].Id.Should().Be(winnerId);
    }

    // ── Complementary fields ─────────────────────────────────────────────────────

    [Fact]
    public void Decide_ComplementaryFields_WritesBothWhenResolved()
    {
        // A rung-1 matter auto-files (Resolved) and its co-occurring rung-2 (ParticipantCorrelation) contact
        // field is written alongside it — proving the deterministic WRITE set is the full rung 0–3 set,
        // decoupled from the C-1 auto-file-STATUS narrowing (rung 0+1).
        var matterId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.90, RungKind.ThreadContinuity),
                Match(PersonField, personId, 0.70, RungKind.ParticipantCorrelation, entity: "contact"),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.RegardingWrites.Should().ContainKey(MatterField);
        decision.RegardingWrites.Should().ContainKey(PersonField);
    }

    // ── Per-tenant kill-switch ───────────────────────────────────────────────────

    [Fact]
    public void Decide_PerTenantKillSwitchOff_DowngradesToSuggested()
    {
        var matterId = Guid.NewGuid();
        var mapper = new AssociationStatusMapper(
            AssociationTestSupport.Gate(
                enabled: true, threshold: 0.85,
                tenants: new Dictionary<string, Configuration.AutoFileTenantOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tenant-b"] = new Configuration.AutoFileTenantOverride { Enabled = false },
                }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AssociationStatusMapper>.Instance);

        var global = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.ExplicitReference) },
            CommunicationDirection.Incoming, tenantKey: null);
        var tenantB = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.ExplicitReference) },
            CommunicationDirection.Incoming, tenantKey: "tenant-b");

        global.Status.Should().Be(AssociationStatusCodes.Resolved);
        tenantB.Status.Should().Be(AssociationStatusCodes.Suggested);
    }

    // ── Direction symmetry ───────────────────────────────────────────────────────

    [Fact]
    public void Decide_InboundAndOutbound_ProduceIdenticalStatusAndWrites()
    {
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper();
        var matches = new[] { Match(MatterField, matterId, 0.90, RungKind.ExplicitReference) };

        var inbound = mapper.Decide(matches, CommunicationDirection.Incoming, tenantKey: null);
        var outbound = mapper.Decide(matches, CommunicationDirection.Outgoing, tenantKey: null);

        inbound.Status.Should().Be(outbound.Status);
        inbound.AutoFiled.Should().Be(outbound.AutoFiled);
        inbound.RegardingWrites.Keys.Should().BeEquivalentTo(outbound.RegardingWrites.Keys);
        inbound.Provenance.Direction.Should().Be("Incoming");
        outbound.Provenance.Direction.Should().Be("Outgoing");
    }

    // ── Provenance shape ─────────────────────────────────────────────────────────

    [Fact]
    public void Decide_WritesProvenance_WithRungsFiredContributorsAndDecision()
    {
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.70, RungKind.ParticipantCorrelation, provenance: "sender:membership"),
                Match(MatterField, matterId, 0.65, RungKind.ExplicitReference, provenance: "subject:token"),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        var json = JsonSerializer.Serialize(decision.Provenance,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        json.Should().Contain("\"rungsFired\"");
        json.Should().Contain("\"candidates\"");
        json.Should().Contain("\"contributors\"");
        json.Should().Contain("\"autoFiled\"");

        decision.Provenance.RungsFired.Should().Contain("ParticipantCorrelation");
        decision.Provenance.RungsFired.Should().Contain("ExplicitReference");
        decision.Provenance.Candidates.Should().ContainSingle();
        decision.Provenance.Candidates[0].Contributors.Should().HaveCount(2);
        decision.Provenance.Candidates[0].Written.Should().BeTrue();
    }

    [Fact]
    public void Decide_TwoContactsNamedOnRegardingPerson_SurfacesBothAsSuggestedCandidates()
    {
        // R4 UAT-R2-B1: an email whose body names two existing contacts (e.g. "Eyal Iffergan and Sara
        // Chen") emits two Suggested-band sprk_regardingperson matches. Both must surface in the review
        // trace so the reviewer sees + picks among them (owner: "surface all matches, user picks primary;
        // never auto-dedup") — NOT collapsed to the single highest-confidence winner. Only the winner is
        // written (single-value lookup); neither auto-files (contact is a fallback field).
        var eyalId = Guid.NewGuid();
        var saraId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(enabled: true, threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(PersonField, eyalId, 0.72, RungKind.ContactNameMatch, entity: "contact", provenance: "body:fullname"),
                Match(PersonField, saraId, 0.68, RungKind.ContactNameMatch, entity: "contact", provenance: "body:fullname"),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();

        var personCandidates = decision.Provenance.Candidates.Where(c => c.Field == PersonField).ToList();
        personCandidates.Should().HaveCount(2, "both named contacts must surface for review");
        personCandidates.Select(c => c.TargetId).Should()
            .BeEquivalentTo(new[] { eyalId.ToString("D"), saraId.ToString("D") });
        personCandidates.Count(c => c.Written).Should().Be(1, "only the single-value lookup winner is written");
    }

    [Fact]
    public void Decide_RecordsMetadataOnlySignals_RegardlessOfAssociationOutcome()
    {
        // A structural detector metadata-only match (no target) is recorded as a signal even when a
        // separate rung resolves the association (carry-forward #3: always-run detector pass).
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper();

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.90, RungKind.ThreadContinuity),
                new RungMatch
                {
                    RegardingFieldName = null,
                    Target = null,
                    Confidence = 0.80,
                    Provenance = "structural:invoice-number",
                    Rung = RungKind.StructuralDetector,
                    Category = "invoice",
                    Obligations = new[] { "payment-review" },
                },
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.Provenance.Signals.Should().ContainSingle(s => s.Category == "invoice");
        decision.Provenance.Signals[0].Obligations.Should().Contain("payment-review");
    }

    // ── NoisyOr unit ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new[] { 0.70, 0.65 }, 0.895)]
    [InlineData(new[] { 1.0, 0.80 }, 1.0)]
    [InlineData(new[] { 0.50 }, 0.50)]
    [InlineData(new double[] { }, 0.0)]
    public void NoisyOr_CombinesIndependentConfidences_Bounded(double[] inputs, double expected)
        => AssociationStatusMapper.NoisyOr(inputs).Should().BeApproximately(expected, 0.001);
}
