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
/// Seam tests for Job C APPLY (<see cref="CommunicationCreateTaskApplyService"/>, task 034 / FR-D5). Exercises the
/// security-critical create-task apply contract end-to-end across its module boundaries (caller resolver, Dataverse
/// generic entity seam, the blessed <see cref="IActionSeam"/> write core, envelope reader) with boundary mocks only
/// (ADR-038 — no <c>Mock&lt;HttpMessageHandler&gt;</c>, no class-under-test collaborator mocking). The properties
/// under test are non-negotiable: the task is CREATED via the facade and its FR-E5 fields are PATCHed UNDER THE
/// CONFIRMING USER'S impersonation (Path B, ADR-013), an unresolved caller fails closed (403), a non-create-task or
/// unverifiable-citation or already-resolved proposal is refused, and every successful apply writes exactly one
/// append-only audit row.
/// </summary>
public sealed class CreateTaskApplySeamTests
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReviewLogId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CommunicationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RegardingRecordId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CreatedTaskId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid AuditLogId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid AssignedToUserId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private const string TargetEntity = "sprk_matter";
    private const string SentinelField = "__create_task__:abcd1234";
    private const string QuotedText = "file the discovery responses by September 1";
    private const string Body = "Counsel — please file the discovery responses by September 1. Thanks.";

    private const int ActionProposed = 100000001;
    private const int ActionApplied = 100000005;
    private const int ActorTypeHuman = 100000001;
    private const int EventStatusOpen = 1;

    private readonly Mock<ICallerSystemUserResolver> _callerResolver = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _generic = new(MockBehavior.Strict);
    private readonly Mock<IActionSeam> _actionSeam = new(MockBehavior.Strict);
    private readonly Mock<ICommunicationEnvelopeReader> _envelopeReader = new(MockBehavior.Strict);

    private string _bodyText = Body;
    private string _targetField = SentinelField;
    private EntityCollection _reviewLogWalk = new(new List<Entity> { ProposedRow(SentinelField) });
    private bool _auditThrows;
    private CreateTaskResult _createResult = new(true, CreatedTaskId, null);

    private static ApplyCreateTaskRequest DefaultRequest() => new()
    {
        BaseDate = new DateOnly(2026, 8, 1),
        FinalDueDate = new DateOnly(2026, 9, 15),
        Status = EventStatusOpen,
        AssignedTo = AssignedToUserId,
    };

    private CommunicationCreateTaskApplyService BuildSut()
    {
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));

        _generic
            .Setup(g => g.RetrieveAsync("sprk_emailreviewlog", ReviewLogId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ProposedRow(_targetField));

        _generic
            .Setup(g => g.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_emailreviewlog"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _reviewLogWalk);

        _generic
            .Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns(() => _auditThrows
                ? Task.FromException<Guid>(new InvalidOperationException("audit store unavailable"))
                : Task.FromResult(AuditLogId));

        _actionSeam
            .Setup(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _createResult);

        _actionSeam
            .Setup(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateRecordResult(true, new[] { "sprk_basedate", "sprk_finalduedate", "sprk_eventstatus" }, null));

        _envelopeReader
            .Setup(r => r.ReconstructEnvelopeAsync(CommunicationId, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult((new NormalizedMessage { Direction = CommunicationDirection.Incoming, Subject = "Discovery", BodyText = _bodyText }, new AssociationContext())));

        return new CommunicationCreateTaskApplyService(
            _callerResolver.Object, _generic.Object, _actionSeam.Object, _envelopeReader.Object,
            NullLogger<CommunicationCreateTaskApplyService>.Instance);
    }

    // (success) — the task is CREATED via the facade; its FR-E5 fields are PATCHed UNDER the confirming user's
    // impersonation (Path B); exactly one append-only Applied audit row is written (actor = the confirming human).
    [Fact]
    public async Task ApplyAsync_WhenConfirmedCreateTaskProposal_CreatesTaskThenPatchesUnderImpersonationAndWritesOneAuditRow()
    {
        var sut = BuildSut();
        CreateTaskRequest? created = null;
        UpdateRecordRequest? patched = null;
        _actionSeam
            .Setup(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateTaskRequest, CancellationToken>((r, _) => created = r)
            .ReturnsAsync(new CreateTaskResult(true, CreatedTaskId, null));
        _actionSeam
            .Setup(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateRecordRequest, CancellationToken>((r, _) => patched = r)
            .ReturnsAsync(new UpdateRecordResult(true, new[] { "sprk_basedate", "sprk_finalduedate", "sprk_eventstatus" }, null));

        var result = await sut.ApplyAsync(ReviewLogId, DefaultRequest(), new ClaimsPrincipal(), CancellationToken.None);

        // The create went through the blessed write core with the extracted subject + the confirmed association +
        // the human-supplied owner. (App-only create is intentional — the facade exposes no impersonated create and
        // ADR-013 forbids widening it; the confirming user rides on ownerid + the PATCH + the audit row.)
        created.Should().NotBeNull();
        created!.Subject.Should().Be("Follow up on discovery");
        created.RegardingObjectType.Should().Be(TargetEntity);
        created.RegardingObjectId.Should().Be(RegardingRecordId);
        created.OwnerId.Should().Be(AssignedToUserId);
        created.DueDate.Should().NotBeNull("the extracted deadline is carried to the created task");

        // The FR-E5 fields PATCH ran AS the confirming user (never app-only), against the newly-created sprk_event.
        patched.Should().NotBeNull();
        patched!.ImpersonateSystemUserId.Should().Be(CallerSystemUserId);
        patched.EntityLogicalName.Should().Be("sprk_event");
        patched.RecordId.Should().Be(CreatedTaskId);
        patched.FieldMappings.Should().Contain(m => m.Field == "sprk_basedate")
            .And.Contain(m => m.Field == "sprk_finalduedate")
            .And.Contain(m => m.Field == "sprk_eventstatus");

        // Exactly one append-only Applied audit row (actor = the confirming human), keyed by the sentinel so the
        // create-task proposal is closed.
        _generic.Verify(g => g.CreateAsync(
            It.Is<Entity>(e =>
                e.LogicalName == "sprk_emailreviewlog"
                && ((OptionSetValue)e["sprk_action"]).Value == ActionApplied
                && ((OptionSetValue)e["sprk_actortype"]).Value == ActorTypeHuman
                && (string)e["sprk_actor"] == CallerSystemUserId.ToString()
                && (string)e["sprk_targetfield"] == SentinelField),
            It.IsAny<CancellationToken>()),
            Times.Once);

        result.CreatedTaskId.Should().Be(CreatedTaskId);
        result.AuditLogId.Should().Be(AuditLogId);
        result.RegardingEntity.Should().Be(TargetEntity);
        result.RegardingRecordId.Should().Be(RegardingRecordId);
    }

    // No FR-E5 overrides supplied → the task is still created, but NO impersonated PATCH is issued (nothing to set),
    // and exactly one audit row is written.
    [Fact]
    public async Task ApplyAsync_WhenNoFieldOverrides_CreatesTaskWithoutPatchAndWritesOneAuditRow()
    {
        var sut = BuildSut();

        var result = await sut.ApplyAsync(ReviewLogId, request: null, new ClaimsPrincipal(), CancellationToken.None);

        _actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _actionSeam.Verify(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
        result.CreatedTaskId.Should().Be(CreatedTaskId);
    }

    // NEGATIVE — auth: an unresolved caller fails closed (403); nothing is ever created/patched/audited.
    [Fact]
    public async Task ApplyAsync_WhenCallerUnresolved_Returns403AndNeverWrites()
    {
        var sut = BuildSut();
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no oid"));

        var act = () => sut.ApplyAsync(ReviewLogId, DefaultRequest(), new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(403);
        _actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // NEGATIVE — wrong endpoint: a Job B field-update proposal (real targetfield, not the __create_task__ sentinel)
    // POSTed here is refused (422); nothing is created.
    [Fact]
    public async Task ApplyAsync_WhenProposalIsNotCreateTask_Refuses422AndNeverCreates()
    {
        _targetField = "sprk_closingdate"; // a Job B field-update row, not a create-task sentinel
        var sut = BuildSut();

        var act = () => sut.ApplyAsync(ReviewLogId, DefaultRequest(), new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(422);
        _actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // NEGATIVE — citation: a proposal whose cited text no longer exists in the message is refused (NFR-06); no task.
    [Fact]
    public async Task ApplyAsync_WhenCitedTextNoLongerExists_Refuses422AndNeverCreates()
    {
        _bodyText = "This message no longer contains the quoted sentence at all.";
        var sut = BuildSut();

        var act = () => sut.ApplyAsync(ReviewLogId, DefaultRequest(), new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(422);
        _actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // NEGATIVE — double-apply guard: a proposal already closed by a later terminal (Applied) row is refused (409);
    // no task is created and no second audit row is written (sequential re-apply protection).
    [Fact]
    public async Task ApplyAsync_WhenProposalAlreadyResolved_Refuses409AndNeverCreates()
    {
        var terminal = new Entity("sprk_emailreviewlog") { Id = Guid.NewGuid() };
        terminal["sprk_action"] = new OptionSetValue(ActionApplied);
        terminal["sprk_targetentity"] = TargetEntity;
        terminal["sprk_targetfield"] = SentinelField;
        _reviewLogWalk = new EntityCollection(new List<Entity> { ProposedRow(SentinelField), terminal });
        var sut = BuildSut();

        var act = () => sut.ApplyAsync(ReviewLogId, DefaultRequest(), new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(409);
        _actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // NEGATIVE — degraded create: if the blessed write core reports a failed/degraded create, refuse (422) and write
    // NO audit row (nothing was mutated).
    [Fact]
    public async Task ApplyAsync_WhenCreateTaskDegraded_Refuses422AndWritesNoAuditRow()
    {
        _createResult = new CreateTaskResult(true, Guid.Empty, "Dataverse rejected the create");
        var sut = BuildSut();

        var act = () => sut.ApplyAsync(ReviewLogId, DefaultRequest(), new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(422);
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // NEGATIVE — deadline integrity (ADR-015): if the FR-E5 field PATCH fails AFTER the task was created, the failure
    // is surfaced loudly (422) — never a silent dropped deadline field — but the mutation IS audited (create + the
    // patch failure both recorded in the ONE Applied audit row before the 422).
    [Fact]
    public async Task ApplyAsync_WhenFieldPatchFails_Returns422ButStillWritesAuditRow()
    {
        var sut = BuildSut();
        _actionSeam
            .Setup(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateRecordResult(false, Array.Empty<string>(), "sprk_eventstatus: value '99' is not a valid option."));

        var act = () => sut.ApplyAsync(ReviewLogId, DefaultRequest(), new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(422);
        _actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Once, "the task was created before the field PATCH");
        _generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once, "the create is audited even when the follow-on PATCH fails (no mutate-without-audit)");
    }

    // NEGATIVE — audit integrity: if the audit-row write fails AFTER the task was created, surface 500 (never a silent
    // mutate-without-audit). The create DID run — asserted by CreateTaskAsync running exactly once before the 500.
    [Fact]
    public async Task ApplyAsync_WhenAuditRowWriteFails_Returns500AfterTaskCreated()
    {
        _auditThrows = true;
        var sut = BuildSut();

        var act = () => sut.ApplyAsync(ReviewLogId, DefaultRequest(), new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(500);
        _actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Entity ProposedRow(string targetField)
    {
        var row = new Entity("sprk_emailreviewlog") { Id = ReviewLogId };
        row["sprk_communication"] = new EntityReference("sprk_communication", CommunicationId);
        row["sprk_action"] = new OptionSetValue(ActionProposed);
        row["sprk_targetentity"] = TargetEntity;
        row["sprk_targetrecordid"] = RegardingRecordId.ToString();
        row["sprk_targetfield"] = targetField;
        row["sprk_confidence"] = 0.85m;
        row["sprk_aisuggestion"] = SuggestionJson();
        return row;
    }

    private static string SuggestionJson() => JsonSerializer.Serialize(new
    {
        kind = "create-task",
        subject = "Follow up on discovery",
        description = "Prepare and file discovery responses",
        dueDate = "2026-09-01",
        regardingObjectType = TargetEntity,
        regardingObjectId = RegardingRecordId,
        citation = new { source = "body", locator = "body: sentence 1", quotedText = QuotedText },
        reason = "the email asks counsel to file discovery",
        confidence = 0.85,
        requireConfirm = true,
    });
}
