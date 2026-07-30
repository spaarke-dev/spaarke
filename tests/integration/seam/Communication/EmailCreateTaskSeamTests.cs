using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> KEEP path / email-communication-
/// intelligence-r1 task 040, FR-14/NFR-04/NFR-06) for the Job C CREATE-TASK step wired into
/// <see cref="CommunicationEnrichmentService"/>'s new "email-create-task" step. Drives the REAL
/// <see cref="CommunicationEnrichmentService.EnrichAsync"/> orchestration and doubles only the module
/// boundaries: <see cref="IGenericEntityService"/> (the association read + the <c>sprk_emailreviewlog</c>
/// writes), <see cref="ICommunicationCreateTaskAi"/> (the extraction facade), and <see cref="IActionSeam"/>
/// (the shipped create-task write core). Proves the trust-critical properties:
/// (a) a non-deadline-bearing candidate is created on the CORRECT associated record, cited to the source
///     communication, via the SHIPPED <see cref="IActionSeam.CreateTaskAsync"/> path;
/// (b) a deadline-bearing candidate does NOT auto-finalize — it is stored PENDING (open <c>Proposed</c> row)
///     and <see cref="IActionSeam.CreateTaskAsync"/> is NEVER called for it (NFR-06 / ADR-015);
/// (c) a candidate whose cited text is absent from the source is NOT created or stored (verify-cited-text,
///     NFR-06);
/// (d) a Job C failure NEVER fails the enrichment path (NFR-04);
/// (e) re-enrichment with an already-stored candidate is idempotent (no duplicate row / no duplicate create).
/// </summary>
public sealed class EmailCreateTaskSeamTests
{
    private const string CommunicationEntity = "sprk_communication";
    private const string MatterEntity = "sprk_matter";
    private const string ReviewLogEntity = "sprk_emailreviewlog";

    private const int ReviewActionProposed = 100000001;
    private const int ReviewActionApplied = 100000005;
    private const int ReviewActorTypeMachine = 100000000;

    private static readonly Guid MatterId = Guid.NewGuid();

    private static NormalizedMessage Message(string subject, string body) => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "sender@example.com",
        To = new[] { "reviewer@example.com" },
        Subject = subject,
        BodyText = body,
    };

    /// <summary>The communication row carrying the task-020 association (regarding matter).</summary>
    private static Entity CommunicationRecord() =>
        new(CommunicationEntity, Guid.NewGuid())
        {
            ["sprk_regardingmatter"] = new EntityReference(MatterEntity, MatterId),
        };

    /// <summary>Wires an IGenericEntityService that returns the associated communication record, an empty
    /// existing-proposal set (unless supplied), and captures every created <c>sprk_emailreviewlog</c> row.</summary>
    private static Mock<IGenericEntityService> CreateEntityService(
        List<Entity> capturedCreates,
        IReadOnlyList<Entity>? existingReviewLogRows = null)
    {
        var svc = new Mock<IGenericEntityService>(MockBehavior.Loose);

        svc.Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommunicationRecord());

        svc.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName switch
            {
                ReviewLogEntity => new EntityCollection((existingReviewLogRows ?? Array.Empty<Entity>()).ToList()),
                _ => new EntityCollection(),
            });

        svc.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => capturedCreates.Add(e))
            .ReturnsAsync(Guid.NewGuid());

        return svc;
    }

    private static CommunicationEnrichmentService CreateService(
        IGenericEntityService entityService,
        ICommunicationCreateTaskAi createTaskAi,
        IActionSeam actionSeam)
    {
        var enqueuer = new Mock<IPostUploadIndexingEnqueuer>(MockBehavior.Loose);
        var producer = new Mock<ICommunicationAssessedProducer>(MockBehavior.Loose);
        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose); // returns null → triage no-ops
        var proposeAi = new Mock<ICommunicationProposeAi>(MockBehavior.Loose); // returns null → propose no-ops
        var config = new ConfigurationBuilder().Build();

        return new CommunicationEnrichmentService(
            enqueuer.Object,
            entityService,
            config,
            producer.Object,
            triageAi.Object,
            proposeAi.Object,
            createTaskAi,
            actionSeam,
            NullLogger<CommunicationEnrichmentService>.Instance);
    }

    private static Mock<ICommunicationCreateTaskAi> CreateTaskAiReturning(params TaskCandidate[] candidates)
    {
        var mock = new Mock<ICommunicationCreateTaskAi>(MockBehavior.Loose);
        mock.Setup(p => p.ExtractAsync(It.IsAny<CommunicationCreateTaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);
        return mock;
    }

    private static Mock<IActionSeam> ActionSeamSucceeding(Guid taskId)
    {
        var mock = new Mock<IActionSeam>(MockBehavior.Loose);
        mock.Setup(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateTaskResult(true, taskId, null));
        return mock;
    }

    // ── (a) non-deadline candidate creates via the shipped path, on the right record, cited ──

    [Fact]
    public async Task EnrichAsync_NonDeadlineBearingCandidate_CreatesTaskOnAssociatedRecord_CitedToSource()
    {
        const string body = "Counsel, please circle back with the client about the invoice discrepancy.";
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(capturedCreates);
        var createdTaskId = Guid.NewGuid();
        var actionSeam = ActionSeamSucceeding(createdTaskId);

        CreateTaskRequest? capturedRequest = null;
        actionSeam.Setup(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateTaskRequest, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync(new CreateTaskResult(true, createdTaskId, null));

        var candidate = new TaskCandidate(
            Subject: "Follow up on invoice discrepancy",
            Description: "Client raised a billing discrepancy.",
            DueDate: null,
            Citation: new ProposalCitation("body", "body: sentence 1", "please circle back with the client about the invoice discrepancy"),
            Reason: "The email asks for a follow-up on the invoice discrepancy.",
            Confidence: 0.85);

        var createTaskAi = CreateTaskAiReturning(candidate);
        var communicationId = Guid.NewGuid();
        var sut = CreateService(entityService.Object, createTaskAi.Object, actionSeam.Object);

        await sut.EnrichAsync(communicationId, CommunicationDirection.Incoming, Message("Invoice question", body), archivedDocumentId: null, CancellationToken.None);

        actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Once,
            "a non-deadline-bearing candidate is created immediately via the SHIPPED create-task write core");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RegardingObjectId.Should().Be(MatterId, "the task must be created on the record the email is associated to (task 020)");
        capturedRequest.RegardingObjectType.Should().Be(MatterEntity);
        capturedRequest.Description.Should().Contain(communicationId.ToString("D"),
            "FR-14: every created item carries a citation to the source communication");
        capturedRequest.Description.Should().Contain("please circle back with the client about the invoice discrepancy",
            "the created task's description carries the verbatim citation");

        // Two audit rows: the Proposed row (uniform trail) + the Applied row (resolution).
        capturedCreates.Should().HaveCount(2, "every candidate gets a Proposed row, and a successfully-created non-deadline candidate also gets an Applied row");
        capturedCreates[0].LogicalName.Should().Be(ReviewLogEntity);
        ((OptionSetValue)capturedCreates[0]["sprk_action"]).Value.Should().Be(ReviewActionProposed);
        ((OptionSetValue)capturedCreates[1]["sprk_action"]).Value.Should().Be(ReviewActionApplied);

        var appliedSuggestion = JsonDocument.Parse((string)capturedCreates[1]["sprk_aisuggestion"]).RootElement;
        appliedSuggestion.GetProperty("createdTaskId").GetString().Should().Be(createdTaskId.ToString());
    }

    // ── (b) a deadline-bearing candidate does NOT auto-finalize (NFR-06/ADR-015) ──

    [Fact]
    public async Task EnrichAsync_DeadlineBearingCandidate_DoesNotAutoFinalize_StoresPendingForConfirm()
    {
        const string body = "Please respond to the court by Friday, August 21, 2026.";
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(capturedCreates);
        var actionSeam = ActionSeamSucceeding(Guid.NewGuid());

        var candidate = new TaskCandidate(
            Subject: "Respond to court filing",
            Description: "Court deadline for response.",
            DueDate: new DateTime(2026, 8, 21),
            Citation: new ProposalCitation("body", "body: sentence 1", "respond to the court by Friday, August 21, 2026"),
            Reason: "The court set a response deadline of August 21, 2026.",
            Confidence: 0.9);

        var createTaskAi = CreateTaskAiReturning(candidate);
        var sut = CreateService(entityService.Object, createTaskAi.Object, actionSeam.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message("Court notice", body), archivedDocumentId: null, CancellationToken.None);

        actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never,
            "NFR-06/ADR-015: a deadline-bearing candidate MUST NOT auto-finalize — the shipped create path is never invoked for it");

        capturedCreates.Should().ContainSingle("only the Proposed row is written — no terminal Applied row for a deadline-bearing candidate");
        var row = capturedCreates[0];
        ((OptionSetValue)row["sprk_action"]).Value.Should().Be(ReviewActionProposed, "the row is a PENDING Proposed row, open for human confirm");

        var suggestion = JsonDocument.Parse((string)row["sprk_aisuggestion"]).RootElement;
        suggestion.GetProperty("requireConfirm").GetBoolean().Should().BeTrue("a deadline-bearing candidate requires human confirm");
        suggestion.GetProperty("dueDate").GetString().Should().Be("2026-08-21");
    }

    // ── (c) NEGATIVE: a candidate whose cited text is absent from the source is NOT created or stored ──

    [Fact]
    public async Task EnrichAsync_CandidateWithUnlocatableCitation_IsNotCreatedOrStored()
    {
        const string body = "Counsel, please advise on strategy for the Smith matter.";
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(capturedCreates);
        var actionSeam = ActionSeamSucceeding(Guid.NewGuid());

        // The model claims a task grounded in text that does NOT appear in the body/subject — a fabricated
        // citation. verify-cited-text (NFR-06) MUST drop it rather than create/store an unverifiable task.
        var candidate = new TaskCandidate(
            Subject: "Fabricated follow-up",
            Description: "Hallucinated task.",
            DueDate: null,
            Citation: new ProposalCitation("body", "body", "the settlement was finalized at $2,000,000"),
            Reason: "Hallucinated.",
            Confidence: 0.9);

        var createTaskAi = CreateTaskAiReturning(candidate);
        var sut = CreateService(entityService.Object, createTaskAi.Object, actionSeam.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message("Strategy", body), archivedDocumentId: null, CancellationToken.None);

        capturedCreates.Should().BeEmpty("NFR-06: a candidate whose cited quotedText cannot be located in the source is DROPPED, never created or stored");
        actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── (d) NEGATIVE: a Job C failure does not fail the enrichment path (NFR-04) ──

    [Fact]
    public async Task EnrichAsync_WhenCreateTaskFacadeThrows_CompletesWithoutPropagating()
    {
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(capturedCreates);
        var actionSeam = ActionSeamSucceeding(Guid.NewGuid());

        var createTaskAi = new Mock<ICommunicationCreateTaskAi>();
        createTaskAi.Setup(p => p.ExtractAsync(It.IsAny<CommunicationCreateTaskRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("create-task boom"));

        var sut = CreateService(entityService.Object, createTaskAi.Object, actionSeam.Object);

        Func<Task> act = () => sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming, Message("Subject", "body"), archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync("NFR-04: a Job C failure must never fail the capture/enrichment path");
        capturedCreates.Should().BeEmpty("a failed extraction stores nothing");
        actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Idempotency: an OPEN Proposed row for the same candidate suppresses a duplicate ──

    [Fact]
    public async Task EnrichAsync_WhenOpenProposalAlreadyExistsForCandidate_DoesNotStoreOrCreateADuplicate()
    {
        const string subject = "Follow up on invoice discrepancy";
        const string body = "Counsel, please circle back with the client about the invoice discrepancy.";
        var capturedCreates = new List<Entity>();

        // The sentinel field is a stable hash of the (lower-cased, trimmed) subject — reproduce it exactly as
        // CommunicationEnrichmentService.BuildCreateTaskSentinelField does, so the existing row shape matches.
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(subject.Trim().ToLowerInvariant())))[..8];
        var sentinelField = $"__create_task__:{hash}";

        var openProposed = new Entity(ReviewLogEntity, Guid.NewGuid())
        {
            ["sprk_action"] = new OptionSetValue(ReviewActionProposed),
            ["sprk_targetentity"] = MatterEntity,
            ["sprk_targetfield"] = sentinelField,
            ["createdon"] = new DateTime(2026, 7, 1),
        };

        var entityService = CreateEntityService(capturedCreates, existingReviewLogRows: new[] { openProposed });
        var actionSeam = ActionSeamSucceeding(Guid.NewGuid());

        var candidate = new TaskCandidate(
            Subject: subject,
            Description: "Client raised a billing discrepancy.",
            DueDate: null,
            Citation: new ProposalCitation("body", "body", "please circle back with the client about the invoice discrepancy"),
            Reason: "Follow-up needed.",
            Confidence: 0.85);

        var createTaskAi = CreateTaskAiReturning(candidate);
        var sut = CreateService(entityService.Object, createTaskAi.Object, actionSeam.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message("Invoice question", body), archivedDocumentId: null, CancellationToken.None);

        capturedCreates.Should().BeEmpty(
            "append-only + idempotent: an OPEN Proposed row for the same candidate suppresses a duplicate on re-enrichment");
        actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
