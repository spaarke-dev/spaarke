using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Seam.Communication;

/// <summary>
/// Seam tests for Job B APPLY (<see cref="CommunicationProposalApplyService"/>, task 031 / FR-10). Exercises the
/// security-critical apply contract end-to-end across its module boundaries (identity resolver, Dataverse generic
/// entity seam, the blessed <see cref="IActionSeam"/> write core, envelope reader) with boundary mocks only
/// (ADR-038 — no <c>Mock&lt;HttpMessageHandler&gt;</c>, no class-under-test collaborator mocking). The properties
/// under test are non-negotiable: the record write runs UNDER THE CONFIRMING USER'S impersonation (never app-only),
/// a non-allow-listed field is refused at apply time, an unresolved caller fails closed (403), an unverifiable
/// citation is refused, and every successful apply writes exactly one append-only audit row. Also covers Job B REJECT
/// (<see cref="CommunicationProposalApplyService.DismissAsync"/>, task 055b / FR-E4): a rejection writes exactly one
/// append-only Dismissed audit row, makes NO record change, does NOT re-gate on the allow-list or re-verify the
/// citation (a rejection is safe regardless of drift), fails closed on an unresolved caller (403), and is idempotent
/// against an already-resolved proposal (409).
/// </summary>
public sealed class CommunicationProposalApplySeamTests
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReviewLogId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CommunicationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TargetRecordId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RecordTypeRefId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid AuditLogId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private const string TargetEntity = "sprk_matter";
    private const string TargetField = "sprk_closingdate";
    private const string QuotedText = "the closing has been moved to August 15, 2026";
    private const string Body = "Hello counsel — please note the closing has been moved to August 15, 2026. Regards.";

    private const int ActionProposed = 100000001;
    private const int ActionApplied = 100000005;
    private const int ActionOverriden = 100000003;
    private const int ActionDismissed = 100000004;
    private const int ActorTypeHuman = 100000001;
    private const int FieldTypeDateTime = 100000004;

    private readonly Mock<ICallerSystemUserResolver> _callerResolver = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _generic = new(MockBehavior.Strict);
    private readonly Mock<IActionSeam> _actionSeam = new(MockBehavior.Strict);
    private readonly Mock<ICommunicationEnvelopeReader> _envelopeReader = new(MockBehavior.Strict);

    private string _bodyText = Body;
    private EntityCollection _allowListRows = AllowListRows(FieldTypeDateTime);
    private EntityCollection _reviewLogWalk = new(new List<Entity> { ProposedRow() });
    private bool _auditThrows;

    private CommunicationProposalApplyService BuildSut()
    {
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));

        _generic
            .Setup(g => g.RetrieveAsync("sprk_emailreviewlog", ReviewLogId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProposedRow());

        _generic
            .Setup(g => g.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_emailreviewlog"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _reviewLogWalk);

        _generic
            .Setup(g => g.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_recordtype_ref"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("sprk_recordtype_ref") { Id = RecordTypeRefId } }));

        // The allow-list query MUST carry the enabled + field-logical-name gate (so a regression that drops the
        // sprk_enabled filter fails this strict-mock match rather than silently applying to a disabled field).
        _generic
            .Setup(g => g.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_emailupdatefield"
                    && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_enabled")
                    && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_targetfieldlogicalname")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _allowListRows);

        _generic
            .Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns(() => _auditThrows
                ? Task.FromException<Guid>(new InvalidOperationException("audit store unavailable"))
                : Task.FromResult(AuditLogId));

        _actionSeam
            .Setup(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateRecordResult(true, new[] { TargetField }, null));

        _envelopeReader
            .Setup(r => r.ReconstructEnvelopeAsync(CommunicationId, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult((new NormalizedMessage { Direction = CommunicationDirection.Incoming, Subject = "Closing update", BodyText = _bodyText }, new AssociationContext())));

        return new CommunicationProposalApplyService(
            _callerResolver.Object, _generic.Object, _actionSeam.Object, _envelopeReader.Object,
            NullLogger<CommunicationProposalApplyService>.Instance);
    }

    // (a) + the impersonation-threading property + (e) exactly-one-audit-row.
    [Fact]
    public async Task ApplyAsync_WhenConfirmedAllowListedProposal_AppliesUnderCallerImpersonationAndWritesOneAuditRow()
    {
        var sut = BuildSut();
        UpdateRecordRequest? applied = null;
        _actionSeam
            .Setup(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateRecordRequest, CancellationToken>((r, _) => applied = r)
            .ReturnsAsync(new UpdateRecordResult(true, new[] { TargetField }, null));

        var result = await sut.ApplyAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        // The write ran AS the confirming user (never app-only), against the associated record + field.
        applied.Should().NotBeNull();
        applied!.ImpersonateSystemUserId.Should().Be(CallerSystemUserId);
        applied.EntityLogicalName.Should().Be(TargetEntity);
        applied.RecordId.Should().Be(TargetRecordId);
        applied.FieldMappings.Should().ContainSingle(m =>
            m.Field == TargetField && m.Type == ActionFieldType.String && m.Value == "2026-08-15");

        // Exactly one append-only Applied audit row (actor = the confirming human).
        _generic.Verify(g => g.CreateAsync(
            It.Is<Entity>(e =>
                e.LogicalName == "sprk_emailreviewlog"
                && ((OptionSetValue)e["sprk_action"]).Value == ActionApplied
                && ((OptionSetValue)e["sprk_actortype"]).Value == ActorTypeHuman
                && (string)e["sprk_actor"] == CallerSystemUserId.ToString()),
            It.IsAny<CancellationToken>()),
            Times.Once);

        result.AuditLogId.Should().Be(AuditLogId);
        result.TargetEntity.Should().Be(TargetEntity);
        result.TargetField.Should().Be(TargetField);
    }

    // (b) NEGATIVE — allow-list: a non-allow-listed / disabled field is refused at apply time even if requested.
    [Fact]
    public async Task ApplyAsync_WhenFieldNotAllowListed_Refuses403AndNeverWrites()
    {
        _allowListRows = new EntityCollection(); // no enabled allow-list row for this (entity, field)
        var sut = BuildSut();

        var act = () => sut.ApplyAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.StatusCode.Should().Be(403);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // (c) NEGATIVE — auth: an unresolved caller fails closed (403); the write NEVER runs (no app-only fallback).
    [Fact]
    public async Task ApplyAsync_WhenCallerUnresolved_Returns403AndNeverWritesAppOnly()
    {
        var sut = BuildSut();
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no oid"));

        var act = () => sut.ApplyAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.StatusCode.Should().Be(403);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // (d) NEGATIVE — citation: a proposal whose cited text no longer exists in the message is refused (NFR-06).
    [Fact]
    public async Task ApplyAsync_WhenCitedTextNoLongerExists_Refuses422AndNeverWrites()
    {
        _bodyText = "This message no longer contains the quoted sentence at all.";
        var sut = BuildSut();

        var act = () => sut.ApplyAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.StatusCode.Should().Be(422);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // NEGATIVE — double-apply guard: a proposal already closed by a later terminal (Applied) row is refused (409);
    // the record is NOT re-patched and no second audit row is written (sequential re-apply protection).
    [Fact]
    public async Task ApplyAsync_WhenProposalAlreadyResolved_Refuses409AndNeverWrites()
    {
        var terminal = new Entity("sprk_emailreviewlog") { Id = Guid.NewGuid() };
        terminal["sprk_action"] = new OptionSetValue(ActionApplied);
        terminal["sprk_targetentity"] = TargetEntity;
        terminal["sprk_targetfield"] = TargetField;
        _reviewLogWalk = new EntityCollection(new List<Entity> { ProposedRow(), terminal });
        var sut = BuildSut();

        var act = () => sut.ApplyAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.StatusCode.Should().Be(409);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // NEGATIVE — audit integrity: if the audit-row write fails AFTER the record was mutated, surface 500 (never a
    // silent mutate-without-audit). The record PATCH DID run (attributed via impersonation) and the failure is
    // logged Critical for reconciliation — asserted here by UpdateRecordAsync running exactly once before the 500.
    [Fact]
    public async Task ApplyAsync_WhenAuditRowWriteFails_Returns500AfterRecordMutated()
    {
        _auditThrows = true;
        var sut = BuildSut();

        var act = () => sut.ApplyAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.StatusCode.Should().Be(500);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // FR-E4 (task 055a) — a human OVERRIDE applies the reviewer's edited value (not the AI's), through the SAME
    // impersonated write, and records a distinct Overriden audit row carrying BOTH the AI proposal and the applied value.
    [Fact]
    public async Task ApplyAsync_WithOverrideValue_AppliesHumanEditedValueAndWritesOverridenAuditRow()
    {
        var sut = BuildSut();
        UpdateRecordRequest? applied = null;
        _actionSeam
            .Setup(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateRecordRequest, CancellationToken>((r, _) => applied = r)
            .ReturnsAsync(new UpdateRecordResult(true, new[] { TargetField }, null));

        // The reviewer overrides the AI's proposed "2026-08-15" with their own "2026-09-20".
        var result = await sut.ApplyAsync(
            ReviewLogId, new ApplyProposalRequest("2026-09-20"), new ClaimsPrincipal(), CancellationToken.None);

        // The record write carried the HUMAN value, still under the confirming user's impersonation.
        applied.Should().NotBeNull();
        applied!.ImpersonateSystemUserId.Should().Be(CallerSystemUserId);
        applied.FieldMappings.Should().ContainSingle(m =>
            m.Field == TargetField && m.Type == ActionFieldType.String && m.Value == "2026-09-20");

        // Exactly one append-only OVERRIDEN audit row (actor = the confirming human) whose stored suggestion records
        // the applied override value + the overridden flag (self-contained: AI proposed 08-15 / human applied 09-20).
        _generic.Verify(g => g.CreateAsync(
            It.Is<Entity>(e =>
                e.LogicalName == "sprk_emailreviewlog"
                && ((OptionSetValue)e["sprk_action"]).Value == ActionOverriden
                && ((OptionSetValue)e["sprk_actortype"]).Value == ActorTypeHuman
                && ((string)e["sprk_aisuggestion"]).Contains("\"appliedValue\":\"2026-09-20\"")
                && ((string)e["sprk_aisuggestion"]).Contains("\"overridden\":true")),
            It.IsAny<CancellationToken>()),
            Times.Once);
        result.AuditLogId.Should().Be(AuditLogId);
    }

    // FR-E4 (task 055a) — an override that equals the AI's proposed value is NOT an override: it applies as a plain
    // Applied row (the override path only engages when the human's value actually differs).
    [Fact]
    public async Task ApplyAsync_WithOverrideEqualToProposedValue_AppliesAsPlainAppliedRow()
    {
        var sut = BuildSut();

        await sut.ApplyAsync(
            ReviewLogId, new ApplyProposalRequest("2026-08-15"), new ClaimsPrincipal(), CancellationToken.None);

        _generic.Verify(g => g.CreateAsync(
            It.Is<Entity>(e => ((OptionSetValue)e["sprk_action"]).Value == ActionApplied),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // FR-E4 (task 055a) NEGATIVE — an override does NOT bypass the apply-time allow-list gate: a non-allow-listed
    // field is still refused (403) and nothing is written, even when a human override value is supplied.
    [Fact]
    public async Task ApplyAsync_WithOverrideValue_WhenFieldNotAllowListed_StillRefuses403AndNeverWrites()
    {
        _allowListRows = new EntityCollection(); // no enabled allow-list row for this (entity, field)
        var sut = BuildSut();

        var act = () => sut.ApplyAsync(
            ReviewLogId, new ApplyProposalRequest("2026-09-20"), new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.StatusCode.Should().Be(403);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // FR-E4 (task 055b) — REJECT: a pending proposal is terminally dismissed by writing ONE append-only Dismissed
    // audit row (actor = the rejecting human) and making NO record change (the target field is never written).
    [Fact]
    public async Task DismissAsync_WhenPendingProposal_WritesOneDismissedAuditRowAndNeverWritesRecord()
    {
        var sut = BuildSut();

        var result = await sut.DismissAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        // Exactly one append-only Dismissed audit row (actor = the rejecting human), carrying the AI suggestion forward.
        _generic.Verify(g => g.CreateAsync(
            It.Is<Entity>(e =>
                e.LogicalName == "sprk_emailreviewlog"
                && ((OptionSetValue)e["sprk_action"]).Value == ActionDismissed
                && ((OptionSetValue)e["sprk_actortype"]).Value == ActorTypeHuman
                && (string)e["sprk_actor"] == CallerSystemUserId.ToString()
                && (string)e["sprk_targetentity"] == TargetEntity
                && (string)e["sprk_targetfield"] == TargetField),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // NO record change — a rejection writes nothing to the target record.
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        result.ReviewLogId.Should().Be(ReviewLogId);
        result.AuditLogId.Should().Be(AuditLogId);
        result.TargetEntity.Should().Be(TargetEntity);
        result.TargetField.Should().Be(TargetField);
    }

    // FR-E4 (task 055b) — the correctness property that distinguishes dismiss from apply: a dismiss does NOT re-gate on
    // the allow-list OR re-verify the citation. Rejecting a proposal whose field is no longer allow-listed (and whose
    // cited text is gone) STILL succeeds — indeed drift is a reason to reject. The envelope reader + allow-list queries
    // are never even reached (asserted by the strict envelope-reader mock: were dismiss to re-verify, it would call it).
    [Fact]
    public async Task DismissAsync_WhenFieldNoLongerAllowListedAndCitationGone_StillDismisses()
    {
        _allowListRows = new EntityCollection();     // field no longer allow-listed
        _bodyText = "This message no longer contains the quoted sentence at all."; // cited text gone
        var sut = BuildSut();

        var result = await sut.DismissAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        result.AuditLogId.Should().Be(AuditLogId);
        _generic.Verify(g => g.CreateAsync(
            It.Is<Entity>(e => ((OptionSetValue)e["sprk_action"]).Value == ActionDismissed),
            It.IsAny<CancellationToken>()),
            Times.Once);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _envelopeReader.Verify(r => r.ReconstructEnvelopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // FR-E4 (task 055b) NEGATIVE — auth: an unresolved caller fails closed (403); NO audit row is written (a rejection
    // is still a recorded human decision that must carry an authenticated identity).
    [Fact]
    public async Task DismissAsync_WhenCallerUnresolved_Returns403AndNeverWrites()
    {
        var sut = BuildSut();
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no oid"));

        var act = () => sut.DismissAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.StatusCode.Should().Be(403);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // FR-E4 (task 055b) NEGATIVE — idempotency: a proposal already closed by a later terminal row is refused (409);
    // no second (Dismissed) audit row is written (the same still-open guard the apply path uses).
    [Fact]
    public async Task DismissAsync_WhenProposalAlreadyResolved_Refuses409AndNeverWrites()
    {
        var terminal = new Entity("sprk_emailreviewlog") { Id = Guid.NewGuid() };
        terminal["sprk_action"] = new OptionSetValue(ActionApplied);
        terminal["sprk_targetentity"] = TargetEntity;
        terminal["sprk_targetfield"] = TargetField;
        _reviewLogWalk = new EntityCollection(new List<Entity> { ProposedRow(), terminal });
        var sut = BuildSut();

        var act = () => sut.DismissAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.StatusCode.Should().Be(409);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // B2.2 UNDO — reverts a just-applied proposal to its stored oldValue UNDER THE CALLER'S impersonation (never
    // app-only), through the same allow-list gate + blessed write core as apply, and writes ONE append-only
    // compensating (Overriden / reverted) audit row.
    [Fact]
    public async Task UndoApplyAsync_WhenAllowListedProposal_RevertsToOldValueUnderImpersonationAndWritesOneAuditRow()
    {
        var sut = BuildSut();
        UpdateRecordRequest? reverted = null;
        _actionSeam
            .Setup(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateRecordRequest, CancellationToken>((r, _) => reverted = r)
            .ReturnsAsync(new UpdateRecordResult(true, new[] { TargetField }, null));

        var result = await sut.UndoApplyAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        // The revert wrote the stored OLD value ("2026-01-01") back, still under the caller's impersonation.
        reverted.Should().NotBeNull();
        reverted!.ImpersonateSystemUserId.Should().Be(CallerSystemUserId);
        reverted.EntityLogicalName.Should().Be(TargetEntity);
        reverted.RecordId.Should().Be(TargetRecordId);
        reverted.FieldMappings.Should().ContainSingle(m =>
            m.Field == TargetField && m.Type == ActionFieldType.String && m.Value == "2026-01-01");

        // Exactly one append-only compensating audit row (Overriden, actor = the human) marked reverted.
        _generic.Verify(g => g.CreateAsync(
            It.Is<Entity>(e =>
                e.LogicalName == "sprk_emailreviewlog"
                && ((OptionSetValue)e["sprk_action"]).Value == ActionOverriden
                && ((OptionSetValue)e["sprk_actortype"]).Value == ActorTypeHuman
                && (string)e["sprk_actor"] == CallerSystemUserId.ToString()
                && ((string)e["sprk_aisuggestion"]).Contains("\"reverted\":true")),
            It.IsAny<CancellationToken>()),
            Times.Once);

        result.AuditLogId.Should().Be(AuditLogId);
        result.TargetEntity.Should().Be(TargetEntity);
        result.TargetField.Should().Be(TargetField);
    }

    // B2.2 UNDO — the distinguishing property: undo does NOT re-gate on still-open OR re-verify the citation (those
    // gate applying an AI suggestion; a revert is safe regardless, and the proposal is already closed by its Applied
    // row). Reverting after apply (terminal Applied row present) with the cited text gone STILL succeeds; the envelope
    // reader is never even reached.
    [Fact]
    public async Task UndoApplyAsync_WhenProposalClosedAndCitationGone_StillReverts()
    {
        var terminal = new Entity("sprk_emailreviewlog") { Id = Guid.NewGuid() };
        terminal["sprk_action"] = new OptionSetValue(ActionApplied);
        terminal["sprk_targetentity"] = TargetEntity;
        terminal["sprk_targetfield"] = TargetField;
        _reviewLogWalk = new EntityCollection(new List<Entity> { ProposedRow(), terminal }); // proposal already closed
        _bodyText = "This message no longer contains the quoted sentence at all.";           // cited text gone
        var sut = BuildSut();

        var result = await sut.UndoApplyAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        result.AuditLogId.Should().Be(AuditLogId);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _envelopeReader.Verify(r => r.ReconstructEnvelopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // B2.2 UNDO NEGATIVE — auth: an unresolved caller fails closed (403); nothing is written (no app-only revert).
    [Fact]
    public async Task UndoApplyAsync_WhenCallerUnresolved_Returns403AndNeverWrites()
    {
        var sut = BuildSut();
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no oid"));

        var act = () => sut.UndoApplyAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(403);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // B2.2 UNDO NEGATIVE — allow-list: a revert may only write an enabled sprk_emailupdatefield (the same fields apply
    // may write); a non-allow-listed field is refused (403) and nothing is written.
    [Fact]
    public async Task UndoApplyAsync_WhenFieldNotAllowListed_Refuses403AndNeverWrites()
    {
        _allowListRows = new EntityCollection();
        var sut = BuildSut();

        var act = () => sut.UndoApplyAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(403);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // B2.2 UNDO NEGATIVE — audit integrity: if the compensating audit-row write fails AFTER the revert, surface 500
    // (never a silent mutate-without-audit). The revert PATCH DID run (once) before the failure.
    [Fact]
    public async Task UndoApplyAsync_WhenAuditRowWriteFails_Returns500AfterRecordReverted()
    {
        _auditThrows = true;
        var sut = BuildSut();

        var act = () => sut.UndoApplyAsync(ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(500);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Entity ProposedRow()
    {
        var row = new Entity("sprk_emailreviewlog") { Id = ReviewLogId };
        row["sprk_communication"] = new EntityReference("sprk_communication", CommunicationId);
        row["sprk_action"] = new OptionSetValue(ActionProposed);
        row["sprk_targetentity"] = TargetEntity;
        row["sprk_targetrecordid"] = TargetRecordId.ToString();
        row["sprk_targetfield"] = TargetField;
        row["sprk_confidence"] = 0.9m;
        row["sprk_aisuggestion"] = SuggestionJson();
        return row;
    }

    private static EntityCollection AllowListRows(int fieldTypeValue)
    {
        var row = new Entity("sprk_emailupdatefield") { Id = Guid.NewGuid() };
        row["sprk_fieldtype"] = new OptionSetValue(fieldTypeValue);
        return new EntityCollection(new List<Entity> { row });
    }

    private static string SuggestionJson() => JsonSerializer.Serialize(new
    {
        field = TargetField,
        fieldType = "DateTime",
        oldValue = "2026-01-01",
        newValue = "2026-08-15",
        citation = new { source = "body", locator = "body: sentence 1", quotedText = QuotedText },
        reason = "The email states the matter's closing date changed to August 15, 2026.",
        confidence = 0.9,
        requireConfirm = true,
        privilegeFlagged = false,
    });
}
