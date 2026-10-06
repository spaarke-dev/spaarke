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
        Sprk.Bff.Api.Services.Communication.Engine.CategoryRoutingGate? routingGate = null,
        Mock<IFieldMappingDataverseService>? fieldMapping = null)
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
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            (fieldMapping ?? new Mock<IFieldMappingDataverseService>(MockBehavior.Loose)).Object,
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

    /// <summary>The communication's versionnumber when its filing is read (task 146 b2).</summary>
    private const long RowVersion = 7;

    /// <summary>The Web API seam the routed owner write goes through (conditional, If-Match). Strict: only the
    /// conditional write may reach it.</summary>
    private static Mock<IFieldMappingDataverseService> RoutedWrites(Exception? concurrentChange = null)
    {
        var fieldMapping = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        var setup = fieldMapping.Setup(f => f.UpdateRecordFieldsIfUnchangedAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()));
        if (concurrentChange is not null)
            setup.ThrowsAsync(concurrentChange);
        else
            setup.Returns(Task.CompletedTask);
        return fieldMapping;
    }

    private static void VerifyNotRouted(Mock<IFieldMappingDataverseService> fieldMapping) =>
        fieldMapping.Verify(f => f.UpdateRecordFieldsIfUnchangedAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);

    /// <summary>Build the triage-trigger fixture: the provenance read fires the triage step, the triage facade
    /// returns <paramref name="category"/>, and RetrieveMultiple returns the litigation team for a `team`
    /// name query, the communication row itself for the routing gate's filing read (task 146 r2 — UNFILED unless
    /// <paramref name="communicationRow"/> says otherwise; <c>null</c> = the row cannot be read), and nothing for any
    /// other lookup. The captured <see cref="Mock{T}"/> lets the caller assert the persist <c>UpdateAsync</c>.</summary>
    private static Mock<IGenericEntityService> RoutingFixture(string category, Func<Guid, Entity?>? communicationRow = null)
    {
        communicationRow ??= id => new Entity(CommunicationEntity, id)
        {
            ["sprk_name"] = "Email: unfiled",
            ["versionnumber"] = RowVersion, // task 146 b2: the routed owner write is conditional on it
        };
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordWithProvenance(SamplePersistedProvenanceJson));
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName switch
            {
                "team" => new EntityCollection(new List<Entity> { new("team") { Id = LitigationTeamId } }),
                CommunicationEntity when communicationRow((Guid)q.Criteria.Conditions[0].Values[0]) is { } row
                    => new EntityCollection(new List<Entity> { row }),
                _ => new EntityCollection(),
            });
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

    // FR-E7 (task 057) — routing ENABLED + a MAPPED category: an UNFILED communication is ASSIGNED to the mapped team.
    // Proves the full slice: gate resolve → team-name lookup → owner written. Task 146 b2 (verifier LOW "race"): the
    // owner is its OWN write, CONDITIONAL on the version the filing was read at — never folded into the triage update.
    [Fact]
    public async Task EnrichAsync_WhenCategoryMappedToTeam_AssignsTheOwner_ConditionalOnTheVersionTheFilingWasReadAt()
    {
        var entityService = RoutingFixture("Court / Filing");
        var routed = RoutedWrites();
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" },
        });
        var sut = CreateService(entityService.Object, TriageReturning("Court / Filing").Object, gate, routed);
        var communicationId = Guid.NewGuid();

        await sut.EnrichAsync(communicationId, CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        routed.Verify(f => f.UpdateRecordFieldsIfUnchangedAsync(
            CommunicationEntity,
            communicationId,
            It.Is<Dictionary<string, object?>>(fields =>
                fields.Count == 1
                && (string?)fields["ownerid@odata.bind"] == $"/teams({LitigationTeamId:D})"),
            RowVersion,
            It.IsAny<CancellationToken>()),
            Times.Once);
        entityService.Verify(s => s.UpdateAsync(
            CommunicationEntity,
            It.IsAny<Guid>(),
            It.Is<Dictionary<string, object>>(f => !f.ContainsKey("ownerid") && f.ContainsKey("sprk_triagesummary")),
            It.IsAny<CancellationToken>()),
            Times.Once, "the triage fields are written on their own; the owner never rides an unconditional write");
    }

    // Task 146 b2 (verifier LOW "race"): the email was filed (its version moved) between the filing read and the routed
    // write — the conditional write is refused (412) and the email keeps the owner its filing gave it. Nothing escapes.
    [Fact]
    public async Task EnrichAsync_WhenTheCommunicationChangesBetweenTheFilingReadAndTheRoutedWrite_IsNotRouted()
    {
        var entityService = RoutingFixture("Court / Filing");
        var routed = RoutedWrites(new System.Data.DBConcurrencyException("412 Precondition Failed"));
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" },
        });
        var sut = CreateService(entityService.Object, TriageReturning("Court / Filing").Object, gate, routed);

        var act = () => sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync("routing is additive (NFR-04)");
        routed.Verify(f => f.UpdateRecordFieldsIfUnchangedAsync(
            CommunicationEntity, It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), RowVersion,
            It.IsAny<CancellationToken>()), Times.Once);
        entityService.Verify(s => s.UpdateAsync(
            CommunicationEntity, It.IsAny<Guid>(),
            It.Is<Dictionary<string, object>>(f => !f.ContainsKey("ownerid") && f.ContainsKey("sprk_triagesummary")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Fail closed: a row whose version cannot be read cannot be written conditionally — it is not routed.
    [Fact]
    public async Task EnrichAsync_WhenTheCommunicationsVersionCannotBeRead_IsNotRouted()
    {
        var entityService = RoutingFixture("Court / Filing", id => new Entity(CommunicationEntity, id) { ["sprk_name"] = "Email: unfiled" });
        var routed = RoutedWrites();
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" },
        });
        var sut = CreateService(entityService.Object, TriageReturning("Court / Filing").Object, gate, routed);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        VerifyNotRouted(routed);
    }

    // FR-E7 (task 057) — routing ENABLED but the category is UNMAPPED: NO ownerid is set (the communication
    // lands in the default/unassigned view; never a forced mis-assignment). The triage fields still persist.
    [Fact]
    public async Task EnrichAsync_WhenCategoryUnmapped_LeavesOwneridUnset()
    {
        var entityService = RoutingFixture("General Correspondence");
        var routed = RoutedWrites();
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" }, // does NOT map "General Correspondence"
        });
        var sut = CreateService(entityService.Object, TriageReturning("General Correspondence").Object, gate, routed);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        VerifyNotRouted(routed);

        // The triage update still ran, but WITHOUT an ownerid.
        entityService.Verify(s => s.UpdateAsync(
            CommunicationEntity,
            It.IsAny<Guid>(),
            It.Is<Dictionary<string, object>>(f => !f.ContainsKey("ownerid")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Task 146 r2 (verifier item 1): a communication FILED under a record is a child of it — its owner is the
    // record-ownership resolver's (the named Secure team for a secure matter). Category routing, which finds a team by
    // NAME in any business unit, must not override it. The triage fields still persist.
    [Fact]
    public async Task EnrichAsync_WhenTheCommunicationIsFiledToASecureMatter_NeverRoutesItsOwner()
    {
        var secureMatter = Guid.NewGuid();
        var entityService = RoutingFixture("Court / Filing", id => new Entity(CommunicationEntity, id)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", secureMatter),
            ["ownerid"] = new EntityReference("team", Guid.NewGuid()), // the Secure team the resolver chose at create
            ["versionnumber"] = RowVersion,
        });
        var routed = RoutedWrites();
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" },
        });
        var sut = CreateService(entityService.Object, TriageReturning("Court / Filing").Object, gate, routed);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        VerifyNotRouted(routed);

        entityService.Verify(s => s.UpdateAsync(
            CommunicationEntity,
            It.IsAny<Guid>(),
            It.Is<Dictionary<string, object>>(f => !f.ContainsKey("ownerid") && f.ContainsKey("sprk_triagesummary")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Fail closed: when the communication's filing cannot be read, it is not routed.
    [Fact]
    public async Task EnrichAsync_WhenTheCommunicationsFilingCannotBeRead_NeverRoutesItsOwner()
    {
        var entityService = RoutingFixture("Court / Filing", _ => null);
        var routed = RoutedWrites();
        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" },
        });
        var sut = CreateService(entityService.Object, TriageReturning("Court / Filing").Object, gate, routed);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        VerifyNotRouted(routed);

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
