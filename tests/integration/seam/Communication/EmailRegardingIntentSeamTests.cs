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
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> KEEP path / email-communication-intelligence-r1
/// task 042, FR-12) for the regarding-vs-related intent step (<c>email-regarding-intent</c>) wired into
/// <see cref="CommunicationEnrichmentService"/>. Drives the REAL
/// <see cref="CommunicationEnrichmentService.EnrichAsync"/> orchestration and doubles only the module boundary
/// <see cref="IGenericEntityService"/> (summary read + <c>sprk_emailreviewlog</c> query/write + summary
/// update). Proves the FR-12 trust-critical properties of the human-readable half (the misfile SUPPRESSION is
/// unit-tested at capture in <c>IdentifierReverseLookupRungTests</c>):
/// (a) a communication that PRESENTS A NEW RECORD referencing an existing one stores a gated "create new
///     record" <c>Proposed</c> row (human-confirmed; NOTHING auto-finalizes) AND notes the cross-reference in
///     the triage summary — with NO related field / second mechanism (ADR-024 amended 2026-07-30);
/// (b) NEGATIVE — a plain file/update email (no new-record framing) produces NO regarding-intent row;
/// (c) idempotency — re-enrichment with an OPEN regarding-intent Proposed row writes no duplicate;
/// (d) NEGATIVE — a failure in the step NEVER fails the enrichment path (NFR-04).
/// </summary>
public sealed class EmailRegardingIntentSeamTests
{
    private const string CommunicationEntity = "sprk_communication";
    private const string ReviewLogEntity = "sprk_emailreviewlog";
    private const int ReviewActionProposed = 100000001;
    private const string RegardingIntentActor = "email-regarding-intent";

    private static NormalizedMessage Message(string subject, string body) => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "sender@example.com",
        To = new[] { "reviewer@example.com" },
        Subject = subject,
        BodyText = body,
    };

    private static Entity CommunicationRecord(string? existingSummary = null)
    {
        var e = new Entity(CommunicationEntity, Guid.NewGuid());
        if (existingSummary is not null)
            e["sprk_triagesummary"] = existingSummary;
        return e;
    }

    private static Mock<IGenericEntityService> CreateEntityService(
        List<Entity> capturedCreates,
        List<(string Entity, Guid Id, Dictionary<string, object> Fields)> capturedUpdates,
        IReadOnlyList<Entity>? existingReviewLogRows = null,
        string? existingSummary = null)
    {
        var svc = new Mock<IGenericEntityService>(MockBehavior.Loose);

        svc.Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommunicationRecord(existingSummary));

        svc.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName switch
            {
                ReviewLogEntity => new EntityCollection((existingReviewLogRows ?? Array.Empty<Entity>()).ToList()),
                _ => new EntityCollection(),
            });

        svc.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => capturedCreates.Add(e))
            .ReturnsAsync(Guid.NewGuid());

        svc.Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((en, id, f, _) => capturedUpdates.Add((en, id, f)))
            .Returns(Task.CompletedTask);

        return svc;
    }

    private static CommunicationEnrichmentService CreateService(IGenericEntityService entityService)
    {
        var enqueuer = new Mock<IPostUploadIndexingEnqueuer>(MockBehavior.Loose);
        var producer = new Mock<ICommunicationAssessedProducer>(MockBehavior.Loose);
        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);   // null → triage no-ops
        var proposeAi = new Mock<ICommunicationProposeAi>(MockBehavior.Loose); // null → propose no-ops
        var createTaskAi = new Mock<ICommunicationCreateTaskAi>(MockBehavior.Loose);
        createTaskAi.Setup(p => p.ExtractAsync(It.IsAny<CommunicationCreateTaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TaskCandidate>()); // Job C / attachment steps no-op
        var actionSeam = new Mock<IActionSeam>(MockBehavior.Loose);
        var config = new ConfigurationBuilder().Build();

        return new CommunicationEnrichmentService(
            enqueuer.Object, entityService, config, producer.Object,
            triageAi.Object, proposeAi.Object, createTaskAi.Object, actionSeam.Object,
            TestRoutingGate.Disabled(),
            NullLogger<CommunicationEnrichmentService>.Instance);
    }

    private static Entity OpenRegardingIntentRow(string sentinelField, string targetEntityMarker) =>
        new(ReviewLogEntity, Guid.NewGuid())
        {
            ["sprk_action"] = new OptionSetValue(ReviewActionProposed),
            ["sprk_targetfield"] = sentinelField,
            ["sprk_targetentity"] = targetEntityMarker,
            ["createdon"] = DateTime.UtcNow,
        };

    private static Entity? RegardingIntentRow(IEnumerable<Entity> creates) =>
        creates.FirstOrDefault(e => e.LogicalName == ReviewLogEntity
            && (e.GetAttributeValue<string>("sprk_actor") ?? string.Empty) == RegardingIntentActor);

    // ── (a) presents a new record referencing an existing one → gated proposal + summary note ──

    [Fact]
    public async Task EnrichAsync_PresentsNewRecordReferencingExisting_StoresGatedProposalAndNotesSummary()
    {
        var creates = new List<Entity>();
        var updates = new List<(string, Guid, Dictionary<string, object>)>();
        var entityService = CreateEntityService(creates, updates);
        var sut = CreateService(entityService.Object);

        await sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming,
            Message("New matter to open", "This is a new litigation matter related to matter LIT-123456."),
            archivedDocumentId: null, CancellationToken.None);

        var row = RegardingIntentRow(creates);
        row.Should().NotBeNull("a new-record intent stores a gated create-new-record proposal");
        ((OptionSetValue)row!["sprk_action"]).Value.Should().Be(ReviewActionProposed, "the proposal is PENDING — nothing auto-finalizes (ADR-015)");
        row["sprk_targetrecordid"].Should().Be(string.Empty, "a NEW record is proposed — there is no existing target id");

        var suggestion = JsonDocument.Parse((string)row["sprk_aisuggestion"]).RootElement;
        suggestion.GetProperty("kind").GetString().Should().Be("regarding-intent");
        suggestion.GetProperty("intent").GetString().Should().Be("new-record");
        suggestion.GetProperty("proposedEntity").GetString().Should().Be("sprk_matter");
        suggestion.GetProperty("requireConfirm").GetBoolean().Should().BeTrue();
        suggestion.GetProperty("referencedIdentifiers").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("LIT-123456");

        // The cross-reference is NOTED in the triage summary (ADR-024: not persisted as a link).
        var summaryUpdate = updates.Should().ContainSingle(u => u.Item3.ContainsKey("sprk_triagesummary")).Subject;
        ((string)summaryUpdate.Item3["sprk_triagesummary"]).Should().Contain("[Regarding intent]")
            .And.Contain("LIT-123456");
    }

    // ── (b) NEGATIVE: a plain file/update email produces NO regarding-intent row ──

    [Fact]
    public async Task EnrichAsync_PlainUpdateToExistingRecord_ProducesNoRegardingIntentRow()
    {
        var creates = new List<Entity>();
        var updates = new List<(string, Guid, Dictionary<string, object>)>();
        var entityService = CreateEntityService(creates, updates);
        var sut = CreateService(entityService.Object);

        await sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming,
            Message("Re: MAT-123", "Please update the billing contact on matter MAT-123."),
            archivedDocumentId: null, CancellationToken.None);

        RegardingIntentRow(creates).Should().BeNull("no new-record framing ⇒ FR-12 does not fire");
        updates.Should().NotContain(u => u.Item3.ContainsKey("sprk_triagesummary"), "no intent ⇒ no summary annotation");
    }

    // ── (c) idempotency: an OPEN regarding-intent proposal is not duplicated on re-enrichment ──

    [Fact]
    public async Task EnrichAsync_OpenRegardingIntentProposalExists_DoesNotDuplicate()
    {
        // Pre-seed the SAME sentinel the step will compute for this intent (trigger "matter" + referenced LIT-123456).
        var intent = NewRecordIntentDetectorProbe("New matter based on LIT-123456");
        var creates = new List<Entity>();
        var updates = new List<(string, Guid, Dictionary<string, object>)>();
        var existingOpen = OpenRegardingIntentRow(intent.SentinelField, intent.TargetEntityMarker);
        var entityService = CreateEntityService(creates, updates, existingReviewLogRows: new[] { existingOpen });
        var sut = CreateService(entityService.Object);

        await sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming,
            Message("New matter", "New matter based on LIT-123456"),
            archivedDocumentId: null, CancellationToken.None);

        RegardingIntentRow(creates).Should().BeNull("an open Proposed row already exists — append-only idempotent, no duplicate");
    }

    // ── (d) NEGATIVE: a failure in the step never fails the enrichment path (NFR-04) ──

    [Fact]
    public async Task EnrichAsync_WhenRegardingIntentWriteThrows_CompletesWithoutPropagating()
    {
        var creates = new List<Entity>();
        var updates = new List<(string, Guid, Dictionary<string, object>)>();
        var entityService = CreateEntityService(creates, updates);
        // The Proposed-row create throws — the step must swallow it and enrichment must still complete.
        entityService.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse write boom"));
        var sut = CreateService(entityService.Object);

        Func<Task> act = () => sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming,
            Message("New matter", "This is a new litigation matter related to LIT-123456"),
            archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync("NFR-04: a regarding-intent failure must never fail capture/enrichment");
    }

    /// <summary>Recomputes the step's idempotency sentinel + target-entity marker for a given envelope, using
    /// the SAME public detector the production step uses (so the pre-seeded open row matches exactly).</summary>
    private static (string SentinelField, string TargetEntityMarker) NewRecordIntentDetectorProbe(string body)
    {
        var intent = Sprk.Bff.Api.Services.Communication.Engine.NewRecordIntentDetector.Detect(null, body)!;
        var basis = intent.ProposedTypeLabel.Trim().ToLowerInvariant() + "|" +
            string.Join(",", intent.ReferencedIdentifiers.Select(v => v.Trim().ToLowerInvariant()).OrderBy(v => v, StringComparer.Ordinal));
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(basis));
        var sentinel = "__regarding_intent__:" + Convert.ToHexString(hash)[..8];
        var marker = intent.ProposedEntityHint ?? "__new_record__";
        return (sentinel, marker);
    }
}
