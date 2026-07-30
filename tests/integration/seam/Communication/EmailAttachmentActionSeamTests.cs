using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> KEEP path / email-communication-
/// intelligence-r1 task 041, FR-13) for the attachment-grounded action-extraction step
/// (<c>email-attachment-action</c>) wired into <see cref="CommunicationEnrichmentService"/>. Drives the REAL
/// <see cref="CommunicationEnrichmentService.EnrichAsync"/> orchestration and doubles only the module
/// boundaries: <see cref="IGenericEntityService"/> (association read + <c>sprk_emailreviewlog</c> writes),
/// <see cref="ICommunicationCreateTaskAi"/> (the REUSED extraction facade), and <see cref="IActionSeam"/>
/// (the shipped create-task write core). The extraction facade is invoked by BOTH the 040 (email-create-task)
/// step and this 041 (email-attachment-action) step, so the facade double returns candidates ONLY for the
/// attachment step — discriminated by that step's distinguishing request shape (empty <c>Subject</c> +
/// <c>BodyText</c>, attachment text supplied). Proves the FR-13 trust-critical properties:
/// (a) an action present ONLY in an attachment is extracted, machine-verified to the attachment + page, and
///     created on the associated record via the SHIPPED <see cref="IActionSeam.CreateTaskAsync"/> path;
/// (b) the deterministic cost gate: an attachment with NO action-trigger signal is never sent for LLM
///     extraction (NFR-08);
/// (c) NEGATIVE — a candidate whose cited span is not verbatim-present in the attachment is dropped (NFR-06);
/// (d) a deadline-bearing attachment candidate does NOT auto-finalize — it is stored PENDING (NFR-06/ADR-015);
/// (e) an attachment-extraction failure NEVER fails the enrichment path (NFR-04).
/// </summary>
public sealed class EmailAttachmentActionSeamTests
{
    private const string CommunicationEntity = "sprk_communication";
    private const string MatterEntity = "sprk_matter";
    private const string ReviewLogEntity = "sprk_emailreviewlog";

    private const int ReviewActionProposed = 100000001;
    private const int ReviewActionApplied = 100000005;

    private static readonly Guid MatterId = Guid.NewGuid();

    // A non-action attachment (no trigger keyword) — the cost gate must skip it.
    private const string InertAttachmentText = "Exhibit A. Corporate org chart. Figure 1 shows the reporting lines.";

    /// <summary>A message carrying one or more per-attachment extracted texts. Subject/body deliberately do
    /// NOT contain the attachment's action, so the extracted action is present ONLY in the attachment.</summary>
    private static NormalizedMessage MessageWith(params AttachmentExtractedText[] attachments) => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "sender@example.com",
        To = new[] { "reviewer@example.com" },
        Subject = "FYI - documents enclosed",
        BodyText = "Counsel, please see the enclosed documents. Regards.",
        AttachmentText = string.Join("\n", attachments.Select(a => a.FullText)),
        AttachmentTexts = attachments,
    };

    private static AttachmentExtractedText TwoPageAgreement(string fileName = "agreement.pdf") =>
        new(
            FileName: fileName,
            DocumentId: null,
            FullText: "Cover page. Master Services Agreement between the parties.\n"
                      + "Section 3. Please countersign the enclosed agreement and return it to complete execution.",
            Pages: new[]
            {
                new ExtractedPage(1, "Cover page. Master Services Agreement between the parties."),
                new ExtractedPage(2, "Section 3. Please countersign the enclosed agreement and return it to complete execution."),
            });

    private static Entity CommunicationRecord() =>
        new(CommunicationEntity, Guid.NewGuid())
        {
            ["sprk_regardingmatter"] = new EntityReference(MatterEntity, MatterId),
        };

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
        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);   // null → triage no-ops
        var proposeAi = new Mock<ICommunicationProposeAi>(MockBehavior.Loose); // null → propose no-ops
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

    /// <summary>The reused extraction facade returns candidates ONLY for the attachment step (Subject == ""),
    /// so the 040 email-create-task step (Subject != "") no-ops and this test isolates FR-13 behavior.</summary>
    private static Mock<ICommunicationCreateTaskAi> AttachmentFacadeReturning(params TaskCandidate[] candidates)
    {
        var mock = new Mock<ICommunicationCreateTaskAi>(MockBehavior.Loose);
        mock.Setup(p => p.ExtractAsync(It.IsAny<CommunicationCreateTaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TaskCandidate>());
        mock.Setup(p => p.ExtractAsync(It.Is<CommunicationCreateTaskRequest>(r => r.Subject == string.Empty && r.BodyText == string.Empty), It.IsAny<CancellationToken>()))
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

    // ── (a) action present ONLY in an attachment → created, machine-verified to attachment + page ──

    [Fact]
    public async Task EnrichAsync_ActionOnlyInAttachment_CreatesTaskCitedToAttachmentAndVerifiedPage()
    {
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(capturedCreates);
        var createdTaskId = Guid.NewGuid();
        var actionSeam = ActionSeamSucceeding(createdTaskId);

        CreateTaskRequest? capturedRequest = null;
        actionSeam.Setup(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateTaskRequest, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync(new CreateTaskResult(true, createdTaskId, null));

        // The action is stated ONLY in the attachment (page 2) — not in the subject/body — with NO deadline,
        // so it is created immediately. quotedText is a verbatim span of attachment page 2.
        var candidate = new TaskCandidate(
            Subject: "Countersign and return the Master Services Agreement",
            Description: "The enclosed agreement requires counter-signature to complete execution.",
            DueDate: null,
            Citation: new ProposalCitation("attachment", "attachment", "Please countersign the enclosed agreement and return it"),
            Reason: "The attachment asks the recipient to countersign and return the agreement.",
            Confidence: 0.88);

        var facade = AttachmentFacadeReturning(candidate);
        var communicationId = Guid.NewGuid();
        var sut = CreateService(entityService.Object, facade.Object, actionSeam.Object);

        await sut.EnrichAsync(communicationId, CommunicationDirection.Incoming, MessageWith(TwoPageAgreement()), archivedDocumentId: null, CancellationToken.None);

        actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Once,
            "a non-deadline attachment action is created immediately via the SHIPPED create-task write core (reuse, no fork)");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RegardingObjectId.Should().Be(MatterId, "the task attaches to the record the email is associated to (task 020)");
        capturedRequest.RegardingObjectType.Should().Be(MatterEntity);
        capturedRequest.Description.Should().Contain("attachment agreement.pdf p.2",
            "FR-13: the created task carries the MACHINE-VERIFIED attachment + code-derived page locator");
        capturedRequest.Description.Should().Contain("Please countersign the enclosed agreement and return it",
            "the provenance carries the verbatim cited span");

        // Two audit rows: Proposed (uniform trail) + Applied (resolution), both under the attachment-action actor.
        capturedCreates.Should().HaveCount(2);
        capturedCreates.Should().OnlyContain(e => e.LogicalName == ReviewLogEntity);
        ((OptionSetValue)capturedCreates[0]["sprk_action"]).Value.Should().Be(ReviewActionProposed);
        ((OptionSetValue)capturedCreates[1]["sprk_action"]).Value.Should().Be(ReviewActionApplied);
        capturedCreates[0]["sprk_actor"].Should().Be("email-attachment-action",
            "FR-13 rows are distinguishable from Job C rows in the audit trail");

        var proposed = JsonDocument.Parse((string)capturedCreates[0]["sprk_aisuggestion"]).RootElement;
        proposed.GetProperty("kind").GetString().Should().Be("attachment-action");
        var citation = proposed.GetProperty("citation");
        citation.GetProperty("source").GetString().Should().Be("attachment");
        citation.GetProperty("attachmentFileName").GetString().Should().Be("agreement.pdf");
        citation.GetProperty("page").GetInt32().Should().Be(2, "the page is CODE-DERIVED by locating the verbatim span (it is on page 2)");
        citation.GetProperty("locator").GetString().Should().Be("agreement.pdf p.2");
    }

    // ── (b) cost gate (NFR-08): an attachment with no action-trigger signal is never LLM-extracted ──

    [Fact]
    public async Task EnrichAsync_InertAttachment_IsNotSentForLlmExtraction()
    {
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(capturedCreates);
        var actionSeam = ActionSeamSucceeding(Guid.NewGuid());

        var facade = AttachmentFacadeReturning(); // would return no candidates even if called
        var inert = new AttachmentExtractedText("org-chart.pdf", null, InertAttachmentText,
            new[] { new ExtractedPage(1, InertAttachmentText) });

        var sut = CreateService(entityService.Object, facade.Object, actionSeam.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, MessageWith(inert), archivedDocumentId: null, CancellationToken.None);

        // The attachment-shaped extraction call (Subject == "") must NEVER fire for an unflagged attachment.
        facade.Verify(
            p => p.ExtractAsync(It.Is<CommunicationCreateTaskRequest>(r => r.Subject == string.Empty && r.BodyText == string.Empty), It.IsAny<CancellationToken>()),
            Times.Never,
            "NFR-08 cost gate: an attachment carrying no action-trigger signal is skipped — no LLM extraction pass");
        actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── (c) NEGATIVE: a candidate whose cited span is not in the attachment is dropped (NFR-06) ──

    [Fact]
    public async Task EnrichAsync_CandidateCitationNotInAttachment_IsDroppedNotCreated()
    {
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(capturedCreates);
        var actionSeam = ActionSeamSucceeding(Guid.NewGuid());

        // The model returns a candidate whose quotedText is NOT a verbatim span of the attachment (fabricated /
        // hallucinated). The machine-verified locator gate MUST drop it — nothing created or stored.
        var candidate = new TaskCandidate(
            Subject: "Fabricated attachment task",
            Description: "Hallucinated.",
            DueDate: null,
            Citation: new ProposalCitation("attachment", "attachment", "wire the settlement amount of $5,000,000 by Monday"),
            Reason: "Hallucinated.",
            Confidence: 0.9);

        var facade = AttachmentFacadeReturning(candidate);
        var sut = CreateService(entityService.Object, facade.Object, actionSeam.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, MessageWith(TwoPageAgreement()), archivedDocumentId: null, CancellationToken.None);

        capturedCreates.Should().BeEmpty("NFR-06: a candidate whose cited span is not verbatim-present in the attachment is DROPPED");
        actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── (d) a deadline-bearing attachment candidate does NOT auto-finalize (NFR-06/ADR-015) ──

    [Fact]
    public async Task EnrichAsync_DeadlineBearingAttachmentCandidate_StoresPendingNeverAutoFinalizes()
    {
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(capturedCreates);
        var actionSeam = ActionSeamSucceeding(Guid.NewGuid());

        var deadlineAttachment = new AttachmentExtractedText(
            "notice.pdf", null,
            "NOTICE OF HEARING. You must file your response no later than August 21, 2026.",
            new[] { new ExtractedPage(1, "NOTICE OF HEARING. You must file your response no later than August 21, 2026.") });

        var candidate = new TaskCandidate(
            Subject: "File response to notice of hearing",
            Description: "The attached notice sets a filing deadline.",
            DueDate: new DateTime(2026, 8, 21),
            Citation: new ProposalCitation("attachment", "attachment", "You must file your response no later than August 21, 2026"),
            Reason: "The attachment states a concrete filing deadline.",
            Confidence: 0.92);

        var facade = AttachmentFacadeReturning(candidate);
        var sut = CreateService(entityService.Object, facade.Object, actionSeam.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, MessageWith(deadlineAttachment), archivedDocumentId: null, CancellationToken.None);

        actionSeam.Verify(s => s.CreateTaskAsync(It.IsAny<CreateTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never,
            "NFR-06/ADR-015: a deadline-bearing attachment candidate MUST NOT auto-finalize");
        capturedCreates.Should().ContainSingle("only the PENDING Proposed row is written — no terminal Applied row");
        var row = capturedCreates[0];
        ((OptionSetValue)row["sprk_action"]).Value.Should().Be(ReviewActionProposed);
        var suggestion = JsonDocument.Parse((string)row["sprk_aisuggestion"]).RootElement;
        suggestion.GetProperty("requireConfirm").GetBoolean().Should().BeTrue();
        suggestion.GetProperty("dueDate").GetString().Should().Be("2026-08-21");
        suggestion.GetProperty("citation").GetProperty("page").GetInt32().Should().Be(1);
    }

    // ── (e) NEGATIVE: an attachment-extraction failure does not fail the enrichment path (NFR-04) ──

    [Fact]
    public async Task EnrichAsync_WhenAttachmentExtractionThrows_CompletesWithoutPropagating()
    {
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(capturedCreates);
        var actionSeam = ActionSeamSucceeding(Guid.NewGuid());

        var facade = new Mock<ICommunicationCreateTaskAi>(MockBehavior.Loose);
        facade.Setup(p => p.ExtractAsync(It.IsAny<CommunicationCreateTaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TaskCandidate>());
        facade.Setup(p => p.ExtractAsync(It.Is<CommunicationCreateTaskRequest>(r => r.Subject == string.Empty && r.BodyText == string.Empty), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("attachment extraction boom"));

        var sut = CreateService(entityService.Object, facade.Object, actionSeam.Object);

        Func<Task> act = () => sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming, MessageWith(TwoPageAgreement()), archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync("NFR-04: an attachment-action failure must never fail the capture/enrichment path");
        capturedCreates.Should().BeEmpty("a failed extraction stores nothing");
    }
}
