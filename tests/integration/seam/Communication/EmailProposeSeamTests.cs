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
/// intelligence-r1 task 030, FR-09/FR-11/NFR-04/NFR-06) for the Job B PROPOSE step wired into
/// <see cref="CommunicationEnrichmentService"/>'s new "email-propose" step. Drives the REAL
/// <see cref="CommunicationEnrichmentService.EnrichAsync"/> orchestration and doubles only the module
/// boundaries: <see cref="IGenericEntityService"/> (the association read, the <c>sprk_recordtype_ref</c> +
/// <c>sprk_emailupdatefield</c> allow-list reads, the current-value read, the existing-proposal read, and
/// the <c>sprk_emailreviewlog</c> Proposed-row write) and <see cref="ICommunicationProposeAi"/> (the facade).
/// Proves the trust-critical properties:
/// (a) an enabled allow-list field + a cited fact yields a PENDING <c>Proposed</c> row with old→new value +
///     citation + confidence;
/// (b) a non-allow-listed / disabled field is NEVER proposed (allow-list is the sole gate);
/// (c) a candidate whose cited text is absent from the source is NOT stored (verify-cited-text, NFR-06);
/// (d) a propose failure NEVER fails the enrichment path (NFR-04);
/// (e) proposals are stored PENDING only — the target record is NOT written by this task (application is 031).
/// </summary>
public sealed class EmailProposeSeamTests
{
    private const string CommunicationEntity = "sprk_communication";
    private const string MatterEntity = "sprk_matter";
    private const string AllowListEntity = "sprk_emailupdatefield";
    private const string RecordTypeRefEntity = "sprk_recordtype_ref";
    private const string ReviewLogEntity = "sprk_emailreviewlog";

    private const int FieldTypeDateTime = 100000004;
    private const int ReviewActionProposed = 100000001;
    private const int ReviewActorTypeMachine = 100000000;

    private static readonly Guid MatterId = Guid.NewGuid();
    private static readonly Guid RecordTypeRefId = Guid.NewGuid();

    private static NormalizedMessage Message(string body) => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "sender@example.com",
        To = new[] { "reviewer@example.com" },
        Subject = "Closing moved - Smith v. Acme",
        BodyText = body,
    };

    /// <summary>The communication row carrying the task-020 association (regarding matter) — no provenance,
    /// so the sibling triage step no-ops and does not interfere with the propose assertions.</summary>
    private static Entity CommunicationRecord() =>
        new(CommunicationEntity, Guid.NewGuid())
        {
            ["sprk_regardingmatter"] = new EntityReference(MatterEntity, MatterId),
        };

    /// <summary>The target matter's current field value (the proposal's oldValue source).</summary>
    private static Entity MatterRecord(DateTime? currentClosingDate)
    {
        var matter = new Entity(MatterEntity, MatterId);
        if (currentClosingDate.HasValue)
        {
            matter["sprk_closingdate"] = currentClosingDate.Value;
        }
        return matter;
    }

    private static Entity AllowListRow(string field, int fieldType, bool requireConfirm) =>
        new(AllowListEntity, Guid.NewGuid())
        {
            ["sprk_targetfieldlogicalname"] = field,
            ["sprk_fieldtype"] = new OptionSetValue(fieldType),
            ["sprk_extractionguidance"] = "The scheduled closing date, if the email states a new one.",
            ["sprk_requireconfirm"] = requireConfirm,
        };

    /// <summary>Wires an IGenericEntityService that dispatches on entity name: the communication association
    /// read, the target-record current-value read, the record-type-ref + allow-list + existing-proposal
    /// queries, and captures every created <c>sprk_emailreviewlog</c> row.</summary>
    private static Mock<IGenericEntityService> CreateEntityService(
        IReadOnlyList<Entity> allowListRows,
        List<Entity> capturedCreates,
        DateTime? currentClosingDate = null,
        IReadOnlyList<Entity>? existingProposals = null)
    {
        var svc = new Mock<IGenericEntityService>(MockBehavior.Loose);

        svc.Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommunicationRecord());
        svc.Setup(s => s.RetrieveAsync(MatterEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MatterRecord(currentClosingDate));

        svc.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName switch
            {
                RecordTypeRefEntity => new EntityCollection(new List<Entity> { new(RecordTypeRefEntity, RecordTypeRefId) }),
                AllowListEntity => new EntityCollection(allowListRows.ToList()),
                ReviewLogEntity => new EntityCollection((existingProposals ?? Array.Empty<Entity>()).ToList()),
                _ => new EntityCollection(),
            });

        svc.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => capturedCreates.Add(e))
            .ReturnsAsync(Guid.NewGuid());

        return svc;
    }

    private static CommunicationEnrichmentService CreateService(
        IGenericEntityService entityService, ICommunicationProposeAi proposeAi)
    {
        var enqueuer = new Mock<IPostUploadIndexingEnqueuer>(MockBehavior.Loose);
        var producer = new Mock<ICommunicationAssessedProducer>(MockBehavior.Loose);
        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose); // returns null → triage no-ops
        var config = new ConfigurationBuilder().Build();

        return new CommunicationEnrichmentService(
            enqueuer.Object,
            entityService,
            config,
            producer.Object,
            triageAi.Object,
            proposeAi,
            new NullCommunicationCreateTaskAi(),
            new Mock<IActionSeam>(MockBehavior.Loose).Object,
            NullLogger<CommunicationEnrichmentService>.Instance);
    }

    private static Mock<ICommunicationProposeAi> ProposeAiReturning(params ProposedFieldCandidate[] candidates)
    {
        var mock = new Mock<ICommunicationProposeAi>(MockBehavior.Loose);
        mock.Setup(p => p.ProposeAsync(It.IsAny<CommunicationProposeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);
        return mock;
    }

    // ── (a) enabled allow-list field + cited fact → a Proposed row (old→new + citation + confidence) ──

    [Fact]
    public async Task EnrichAsync_EnabledAllowListFieldWithCitedFact_StoresPendingProposalWithOldNewAndCitation()
    {
        const string body = "Counsel, the closing has been moved to August 15, 2026. Update your calendars.";
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(
            allowListRows: new[] { AllowListRow("sprk_closingdate", FieldTypeDateTime, requireConfirm: true) },
            capturedCreates: capturedCreates,
            currentClosingDate: new DateTime(2026, 1, 1));

        var proposeAi = ProposeAiReturning(new ProposedFieldCandidate(
            Field: "sprk_closingdate",
            NewValue: "August 15, 2026",
            Citation: new ProposalCitation("body", "body: sentence 1", "the closing has been moved to August 15, 2026"),
            Reason: "The email states the matter's closing date changed to August 15, 2026.",
            Confidence: 0.9));

        var sut = CreateService(entityService.Object, proposeAi.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(body), archivedDocumentId: null, CancellationToken.None);

        capturedCreates.Should().ContainSingle("exactly one allow-listed, cited, non-no-op proposal must be stored");
        var row = capturedCreates[0];

        row.LogicalName.Should().Be(ReviewLogEntity, "the pending proposal store is sprk_emailreviewlog (Decision 1 — no new table)");
        ((OptionSetValue)row["sprk_action"]).Value.Should().Be(ReviewActionProposed, "the row is a PENDING Proposed row");
        ((OptionSetValue)row["sprk_actortype"]).Value.Should().Be(ReviewActorTypeMachine, "Job B proposes as Machine");
        ((EntityReference)row["sprk_communication"]).LogicalName.Should().Be(CommunicationEntity);
        row["sprk_targetentity"].Should().Be(MatterEntity);
        row["sprk_targetrecordid"].Should().Be(MatterId.ToString());
        row["sprk_targetfield"].Should().Be("sprk_closingdate");
        ((decimal)row["sprk_confidence"]).Should().Be(0.9m);

        var suggestion = JsonDocument.Parse((string)row["sprk_aisuggestion"]).RootElement;
        suggestion.GetProperty("oldValue").GetString().Should().Be("2026-01-01", "the current value is captured as oldValue");
        suggestion.GetProperty("newValue").GetString().Should().Be("2026-08-15", "the raw value is coerced to ISO-8601 per the DateTime field type");
        suggestion.GetProperty("citation").GetProperty("quotedText").GetString()
            .Should().Be("the closing has been moved to August 15, 2026");
        suggestion.GetProperty("confidence").GetDouble().Should().Be(0.9);
        suggestion.GetProperty("requireConfirm").GetBoolean().Should().BeTrue("sprk_requireconfirm is true in P1 — every proposal is human-confirmed at 031");
    }

    // ── (b) NEGATIVE: a non-allow-listed / disabled field is NEVER proposed ──

    [Fact]
    public async Task EnrichAsync_CandidateForNonAllowListedField_IsNeverProposed()
    {
        const string body = "Please update the secret internal field to CLASSIFIED per my note.";
        var capturedCreates = new List<Entity>();
        // Allow-list enables ONLY sprk_closingdate; the disabled/absent sprk_secretfield row is not returned
        // by the enabled query (the query filters sprk_enabled = true), mirroring the operator-owned table.
        var entityService = CreateEntityService(
            allowListRows: new[] { AllowListRow("sprk_closingdate", FieldTypeDateTime, requireConfirm: true) },
            capturedCreates: capturedCreates);

        var proposeAi = ProposeAiReturning(new ProposedFieldCandidate(
            Field: "sprk_secretfield",
            NewValue: "CLASSIFIED",
            Citation: new ProposalCitation("body", "body", "update the secret internal field to CLASSIFIED"),
            Reason: "Reviewer asked to set the secret field.",
            Confidence: 0.95));

        var sut = CreateService(entityService.Object, proposeAi.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(body), archivedDocumentId: null, CancellationToken.None);

        capturedCreates.Should().BeEmpty(
            "the allow-list is the SOLE gate — a field with no enabled sprk_emailupdatefield row is NEVER proposed, even when the model returns it and its citation verifies");
    }

    // ── (c) NEGATIVE: a candidate whose cited text is absent from the source is NOT stored (NFR-06) ──

    [Fact]
    public async Task EnrichAsync_CandidateWithUnlocatableCitation_IsNotStored()
    {
        const string body = "Counsel, please advise on strategy for the Smith matter.";
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(
            allowListRows: new[] { AllowListRow("sprk_closingdate", FieldTypeDateTime, requireConfirm: true) },
            capturedCreates: capturedCreates,
            currentClosingDate: new DateTime(2026, 1, 1));

        // The model claims a value with a quotedText that does NOT appear in the body/subject — a fabricated
        // citation. verify-cited-text (NFR-06) MUST drop it rather than store an unverifiable proposal.
        var proposeAi = ProposeAiReturning(new ProposedFieldCandidate(
            Field: "sprk_closingdate",
            NewValue: "August 15, 2026",
            Citation: new ProposalCitation("body", "body", "the closing has been moved to August 15, 2026"),
            Reason: "Hallucinated closing date.",
            Confidence: 0.9));

        var sut = CreateService(entityService.Object, proposeAi.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(body), archivedDocumentId: null, CancellationToken.None);

        capturedCreates.Should().BeEmpty(
            "NFR-06: a proposal whose cited quotedText cannot be located in the source communication/attachment is DROPPED, never stored");
    }

    // ── (d) NEGATIVE: a propose failure does not fail the enrichment path (NFR-04) ──

    [Fact]
    public async Task EnrichAsync_WhenProposeFacadeThrows_CompletesWithoutPropagating()
    {
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(
            allowListRows: new[] { AllowListRow("sprk_closingdate", FieldTypeDateTime, requireConfirm: true) },
            capturedCreates: capturedCreates);

        var proposeAi = new Mock<ICommunicationProposeAi>();
        proposeAi.Setup(p => p.ProposeAsync(It.IsAny<CommunicationProposeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("propose boom"));

        var sut = CreateService(entityService.Object, proposeAi.Object);

        Func<Task> act = () => sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming, Message("body"), archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync("NFR-04: a propose failure must never fail the capture/enrichment path");
        capturedCreates.Should().BeEmpty("a failed propose stores nothing");
    }

    // ── (e) proposals are stored PENDING only — the target record is NOT written by this task ──

    [Fact]
    public async Task EnrichAsync_StoresProposalPendingOnly_NeverWritesTheTargetRecord()
    {
        const string body = "Counsel, the closing has been moved to August 15, 2026.";
        var capturedCreates = new List<Entity>();
        var entityService = CreateEntityService(
            allowListRows: new[] { AllowListRow("sprk_closingdate", FieldTypeDateTime, requireConfirm: true) },
            capturedCreates: capturedCreates,
            currentClosingDate: new DateTime(2026, 1, 1));

        var proposeAi = ProposeAiReturning(new ProposedFieldCandidate(
            Field: "sprk_closingdate",
            NewValue: "August 15, 2026",
            Citation: new ProposalCitation("body", "body", "the closing has been moved to August 15, 2026"),
            Reason: "Closing date changed.",
            Confidence: 0.9));

        var sut = CreateService(entityService.Object, proposeAi.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(body), archivedDocumentId: null, CancellationToken.None);

        capturedCreates.Should().ContainSingle("the proposal is stored (as a pending Proposed row)");
        entityService.Verify(
            s => s.UpdateAsync(MatterEntity, It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "application is task 031 — the propose step MUST NOT write the target record");
    }

    // ── Idempotency: an OPEN Proposed row suppresses a duplicate on re-enrichment ──

    [Fact]
    public async Task EnrichAsync_WhenOpenProposalAlreadyExists_DoesNotStoreADuplicate()
    {
        const string body = "Counsel, the closing has been moved to August 15, 2026.";
        var capturedCreates = new List<Entity>();
        var openProposed = new Entity(ReviewLogEntity, Guid.NewGuid())
        {
            ["sprk_action"] = new OptionSetValue(ReviewActionProposed),
            ["sprk_targetentity"] = MatterEntity,
            ["sprk_targetfield"] = "sprk_closingdate",
            ["createdon"] = new DateTime(2026, 7, 1),
        };
        var entityService = CreateEntityService(
            allowListRows: new[] { AllowListRow("sprk_closingdate", FieldTypeDateTime, requireConfirm: true) },
            capturedCreates: capturedCreates,
            currentClosingDate: new DateTime(2026, 1, 1),
            existingProposals: new[] { openProposed });

        var proposeAi = ProposeAiReturning(new ProposedFieldCandidate(
            Field: "sprk_closingdate",
            NewValue: "August 15, 2026",
            Citation: new ProposalCitation("body", "body", "the closing has been moved to August 15, 2026"),
            Reason: "Closing date changed.",
            Confidence: 0.9));

        var sut = CreateService(entityService.Object, proposeAi.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(body), archivedDocumentId: null, CancellationToken.None);

        capturedCreates.Should().BeEmpty(
            "append-only + idempotent: an OPEN Proposed row for the same (communication, entity, field) suppresses a duplicate");
    }
}
