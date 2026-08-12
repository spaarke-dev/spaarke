using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> KEEP path / email-communication-
/// intelligence-r1 task 023, FR-05/NFR-04) for the email-triage trigger wired into
/// <see cref="CommunicationEnrichmentService"/>'s new "email-triage" step. Drives the REAL
/// <see cref="CommunicationEnrichmentService.EnrichAsync"/> orchestration and doubles only the module
/// boundaries: <see cref="IGenericEntityService"/> (the persisted classification-signal read) and
/// <see cref="ICommunicationTriageAi"/> (the facade). Proves (a) a persisted AI-classify signal drives a
/// real facade invocation, and (b) a triage failure — at ANY layer, including the Dataverse read itself —
/// never fails capture/enrichment (NFR-04).
/// </summary>
public sealed class EmailTriageSeamTests
{
    private const string CommunicationEntity = "sprk_communication";

    /// <summary>The exact provenance shape <see cref="Engine.Rungs.AiClassificationRung"/> +
    /// <see cref="Engine.AssociationStatusMapper"/> produce for a fired AI-classify signal.</summary>
    private const string SamplePersistedProvenanceJson =
        """
        {"version":1,"direction":"Incoming","decision":{"status":"PendingReview","autoFiled":false,"killSwitchEnabled":true,"autoFileThreshold":0.85,"topDeterministicConfidence":0.0,"topConfidence":0.6,"aiInvolved":true,"reason":"test"},"rungsFired":["AiClassification"],"candidates":[],"signals":[{"category":"court-notice","confidence":0.6,"provenance":"ai-classify:category=court-notice:urgency=urgent:types=[sprk_matter]:actions=[calendar-deadline]:Court deadline.","obligations":["respond-by-deadline"]}]}
        """;

    private static CommunicationEnrichmentService CreateService(
        IGenericEntityService entityService,
        ICommunicationTriageAi triageAi,
        Sprk.Bff.Api.Services.Communication.Engine.CategoryRoutingGate? routingGate = null)
    {
        var enqueuer = new Mock<IPostUploadIndexingEnqueuer>(MockBehavior.Loose);
        var producer = new Mock<ICommunicationAssessedProducer>(MockBehavior.Loose);
        var config = new ConfigurationBuilder().Build();

        return new CommunicationEnrichmentService(
            EnrichmentScopeFactoryStub.Create(
                enqueuer.Object, triageAi, new NullCommunicationProposeAi(), new NullCommunicationCreateTaskAi()),
            entityService,
            config,
            producer.Object,
            new Mock<IActionSeam>(MockBehavior.Loose).Object,
            routingGate ?? TestRoutingGate.Disabled(),
            NullLogger<CommunicationEnrichmentService>.Instance);
    }

    private static NormalizedMessage Message() => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "sender@example.com",
        To = new[] { "reviewer@example.com" },
        Subject = "URGENT: Response due Friday",
        BodyText = "Please respond to the court by Friday.",
    };

    private static Entity RecordWithProvenance(string? provenanceJson)
    {
        var entity = new Entity(CommunicationEntity, Guid.NewGuid());
        if (provenanceJson is not null)
        {
            entity["sprk_associationprovenance"] = provenanceJson;
        }
        return entity;
    }

    [Fact]
    public async Task EnrichAsync_WithPersistedClassifySignal_InvokesTriageFacade()
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordWithProvenance(SamplePersistedProvenanceJson));

        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        triageAi
            .Setup(t => t.TriageAsync(It.IsAny<CommunicationTriageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationTriageResult("Court / Filing", "Two-line summary.", new[] { "Respond by Friday" }, "Urgent", "Route"));

        var sut = CreateService(entityService.Object, triageAi.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        triageAi.Verify(
            t => t.TriageAsync(
                It.Is<CommunicationTriageRequest>(r =>
                    r.Classification.Category == "court-notice"
                    && r.Classification.Urgency == "urgent"
                    && r.Classification.Obligations.Contains("respond-by-deadline")
                    && r.Subject == "URGENT: Response due Friday"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the trigger must feed AiClassificationRung's ALREADY-PRODUCED signal (reconstructed from the persisted provenance) — no second classification call");
    }

    private static readonly Guid LitigationTeamId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    /// <summary>Build the triage-trigger fixture: the provenance read fires the triage step, the triage facade
    /// returns <paramref name="category"/>, and RetrieveMultiple returns the litigation team for a `team`
    /// name query (empty for any other lookup). The captured <see cref="Mock{T}"/> lets the caller assert the
    /// persist <c>UpdateAsync</c>.</summary>
    private static Mock<IGenericEntityService> RoutingFixture(string category)
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordWithProvenance(SamplePersistedProvenanceJson));
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
                q.EntityName == "team"
                    ? new EntityCollection(new List<Entity> { new("team") { Id = LitigationTeamId } })
                    : new EntityCollection());
        return entityService;
    }

    private static Mock<ICommunicationTriageAi> TriageReturning(string category)
    {
        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        triageAi
            .Setup(t => t.TriageAsync(It.IsAny<CommunicationTriageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationTriageResult(category, "Summary.", new[] { "Respond by Friday" }, "Urgent", "Route"));
        return triageAi;
    }

    // FR-E7 (task 057) — routing ENABLED + a MAPPED category: the communication is ASSIGNED to the mapped team
    // (ownerid set on the SAME additive triage UpdateAsync — no second write path, ADR-024). Proves the full
    // slice: gate resolve → team-name lookup → ownerid set.
    [Fact]
    public async Task EnrichAsync_WhenCategoryMappedToTeam_AssignsOwneridToThatTeam()
    {
        var entityService = RoutingFixture("Court / Filing");
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" },
        });
        var sut = CreateService(entityService.Object, TriageReturning("Court / Filing").Object, gate);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        entityService.Verify(s => s.UpdateAsync(
            CommunicationEntity,
            It.IsAny<Guid>(),
            It.Is<Dictionary<string, object>>(f =>
                f.ContainsKey("ownerid")
                && ((EntityReference)f["ownerid"]).LogicalName == "team"
                && ((EntityReference)f["ownerid"]).Id == LitigationTeamId),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // FR-E7 (task 057) — routing ENABLED but the category is UNMAPPED: NO ownerid is set (the communication
    // lands in the default/unassigned view; never a forced mis-assignment). The triage fields still persist.
    [Fact]
    public async Task EnrichAsync_WhenCategoryUnmapped_LeavesOwneridUnset()
    {
        var entityService = RoutingFixture("General Correspondence");
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" }, // does NOT map "General Correspondence"
        });
        var sut = CreateService(entityService.Object, TriageReturning("General Correspondence").Object, gate);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        // The triage update still ran, but WITHOUT an ownerid.
        entityService.Verify(s => s.UpdateAsync(
            CommunicationEntity,
            It.IsAny<Guid>(),
            It.Is<Dictionary<string, object>>(f => !f.ContainsKey("ownerid")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnrichAsync_WithNoPersistedSignal_SkipsTriageWithoutCallingFacade()
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordWithProvenance(provenanceJson: null));

        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Strict);
        var sut = CreateService(entityService.Object, triageAi.Object);

        Func<Task> act = () => sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Outgoing, Message(), archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync("no persisted classification signal (e.g. outbound today, or rung 5 didn't fire) must no-op cleanly");
        triageAi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnrichAsync_WhenTriageFacadeThrows_CompletesWithoutPropagating()
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordWithProvenance(SamplePersistedProvenanceJson));

        var triageAi = new Mock<ICommunicationTriageAi>();
        triageAi
            .Setup(t => t.TriageAsync(It.IsAny<CommunicationTriageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("triage boom"));

        var sut = CreateService(entityService.Object, triageAi.Object);

        Func<Task> act = () => sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync("NFR-04: a triage failure must never fail the capture/enrichment path");
    }

    [Fact]
    public async Task EnrichAsync_WhenProvenanceReadThrows_CompletesWithoutPropagating()
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse read boom"));

        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Strict);
        var sut = CreateService(entityService.Object, triageAi.Object);

        Func<Task> act = () => sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync("NFR-04: even a failure reading the persisted signal must never fail capture/enrichment (RunStepAsync's outer guard)");
        triageAi.VerifyNoOtherCalls();
    }
}
