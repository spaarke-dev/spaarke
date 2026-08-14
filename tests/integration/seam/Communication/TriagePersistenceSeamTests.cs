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
/// intelligence-r1 task 025, FR-07) for the triage-output PERSISTENCE step wired into
/// <see cref="CommunicationEnrichmentService"/>'s "email-triage" step (immediately after the produced
/// <see cref="CommunicationTriageResult"/>). Drives the REAL <see cref="CommunicationEnrichmentService.EnrichAsync"/>
/// orchestration and doubles only the module boundaries: <see cref="IGenericEntityService"/> (the persisted
/// classification-signal read, the category-lookup query, and the <c>sprk_communication</c> update) and
/// <see cref="ICommunicationTriageAi"/> (the facade). Proves: (a) all six triage fields persist with the
/// as-built option-set integers + lean-JSON obligations on the SINGULAR <c>sprk_triageobligation</c>, (b)
/// the persisted <c>sprk_riconfidence</c> matches the <c>communication_assessed</c> signal's confidence
/// (same <see cref="RiConfidenceScorer"/> formula, same inputs), (c) a known seeded category resolves to its
/// taxonomy row GUID while an unknown category leaves the lookup unset (ADR-024 — never free text, never
/// fabricated), and (d) a persistence-layer failure degrades non-fatally (NFR-04) without ever failing
/// capture/enrichment.
/// </summary>
public sealed class TriagePersistenceSeamTests
{
    private const string CommunicationEntity = "sprk_communication";
    private const string TriageCategoryEntity = "sprk_triagecategory";

    private static readonly Guid CourtFilingCategoryId = Guid.NewGuid();

    /// <summary>Rung-0 provenance (TopDeterministicConfidence 0.8) carrying a persisted AI-classify signal,
    /// so BOTH the email-triage step and the assessment-emission step read the SAME document.</summary>
    private const string SamplePersistedProvenanceJson =
        """
        {"version":1,"direction":"Incoming","decision":{"status":"Resolved","autoFiled":true,"killSwitchEnabled":true,"autoFileThreshold":0.85,"topDeterministicConfidence":0.8,"topConfidence":0.8,"aiInvolved":false,"reason":"test"},"rungsFired":["ExplicitReference"],"candidates":[],"signals":[{"category":"court-notice","confidence":0.6,"provenance":"ai-classify:category=court-notice:urgency=urgent:types=[sprk_matter]:actions=[calendar-deadline]:Court deadline.","obligations":["respond-by-deadline"]}]}
        """;

    private static Entity RecordWithProvenance(string? provenanceJson)
    {
        var entity = new Entity(CommunicationEntity, Guid.NewGuid());
        if (provenanceJson is not null)
        {
            entity["sprk_associationprovenance"] = provenanceJson;
        }
        return entity;
    }

    /// <summary>Captures every published <c>communication_assessed</c> signal.</summary>
    private sealed class CapturingAssessedProducer : ICommunicationAssessedProducer
    {
        public readonly List<CommunicationAssessedSignal> Published = new();

        public Task PublishAsync(CommunicationAssessedSignal signal, CancellationToken ct = default)
        {
            Published.Add(signal);
            return Task.CompletedTask;
        }
    }

    private static NormalizedMessage Message() => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "sender@example.com",
        To = new[] { "reviewer@example.com" },
        Subject = "URGENT: Response due Friday",
        BodyText = "Please respond to the court by Friday.",
    };

    private static CommunicationEnrichmentService CreateService(
        IGenericEntityService entityService,
        ICommunicationTriageAi triageAi,
        ICommunicationAssessedProducer? producer = null)
    {
        var enqueuer = new Mock<IPostUploadIndexingEnqueuer>(MockBehavior.Loose);
        var assessedProducer = producer ?? new Mock<ICommunicationAssessedProducer>(MockBehavior.Loose).Object;
        var config = new ConfigurationBuilder().Build();

        return new CommunicationEnrichmentService(
            EnrichmentScopeFactoryStub.Create(
                enqueuer.Object, triageAi, new NullCommunicationProposeAi(), new NullCommunicationCreateTaskAi()),
            entityService,
            config,
            assessedProducer,
            new Mock<IActionSeam>(MockBehavior.Loose).Object,
            TestRoutingGate.Disabled(),
            NullLogger<CommunicationEnrichmentService>.Instance);
    }

    /// <summary>Category-lookup query double: resolves ONLY "Court / Filing" to <see cref="CourtFilingCategoryId"/>;
    /// any other name (e.g. an unseeded category) yields an empty result — mirrors the real
    /// <c>sprk_triagecategory</c> taxonomy query behavior without a live Dataverse call.</summary>
    private static EntityCollection ResolveCategoryLookup(QueryExpression query)
    {
        query.EntityName.Should().Be(TriageCategoryEntity, "category resolution must query the 013 taxonomy table");

        var condition = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_name");
        var categoryName = condition?.Values.FirstOrDefault() as string;

        var collection = new EntityCollection();
        if (string.Equals(categoryName, "Court / Filing", StringComparison.Ordinal))
        {
            collection.Entities.Add(new Entity(TriageCategoryEntity, CourtFilingCategoryId));
        }
        return collection;
    }

    private static Mock<IGenericEntityService> CreateEntityServiceMock(
        string? provenanceJson,
        Dictionary<string, object>?[] capturedUpdates,
        bool throwOnUpdate = false,
        bool throwOnCategoryLookup = false)
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);

        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordWithProvenance(provenanceJson));

        if (throwOnCategoryLookup)
        {
            entityService
                .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("category lookup boom"));
        }
        else
        {
            entityService
                .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) => ResolveCategoryLookup(q));
        }

        if (throwOnUpdate)
        {
            entityService
                .Setup(s => s.UpdateAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("dataverse update boom"));
        }
        else
        {
            entityService
                .Setup(s => s.UpdateAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) => capturedUpdates[0] = fields)
                .Returns(Task.CompletedTask);
        }

        return entityService;
    }

    // ── (a) all six fields persist with as-built option-set integers + lean-JSON obligations ──────

    [Fact]
    public async Task EnrichAsync_AfterTriage_PersistsAllSixTriageFieldsWithAsBuiltValues()
    {
        var capturedUpdates = new Dictionary<string, object>?[1];
        var entityService = CreateEntityServiceMock(SamplePersistedProvenanceJson, capturedUpdates);

        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        triageAi
            .Setup(t => t.TriageAsync(It.IsAny<CommunicationTriageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationTriageResult(
                "Court / Filing", "Two-line summary.", new[] { "Respond by Friday" }, "Urgent", "Route"));

        var producer = new CapturingAssessedProducer();
        var sut = CreateService(entityService.Object, triageAi.Object, producer);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        var fields = capturedUpdates[0];
        fields.Should().NotBeNull("the email-triage step must persist the produced result via IGenericEntityService.UpdateAsync");

        fields!["sprk_triagecategory"].Should().BeOfType<EntityReference>()
            .Which.Id.Should().Be(CourtFilingCategoryId, "the category NAME must resolve to the 013 taxonomy row's GUID, not free text");
        ((EntityReference)fields["sprk_triagecategory"]).LogicalName.Should().Be(TriageCategoryEntity);

        fields["sprk_triagepriority"].Should().BeOfType<OptionSetValue>()
            .Which.Value.Should().Be(100000000, "Urgent is the as-built sprk_triagepriority integer");

        fields["sprk_triagesummary"].Should().Be("Two-line summary.");

        fields["sprk_triageobligation"].Should().Be(
            JsonSerializer.Serialize(new[] { "Respond by Friday" }),
            "obligations persist as lean JSON on the SINGULAR sprk_triageobligation column (D-06)");

        fields["sprk_reviewoutcome"].Should().BeOfType<OptionSetValue>()
            .Which.Value.Should().Be(100000002, "Route is the as-built sprk_reviewoutcome integer");

        fields["sprk_riconfidence"].Should().BeOfType<double>()
            .Which.Should().BeApproximately(0.8, 1e-9, "Urgent (weight 1.0) x TopDeterministicConfidence 0.8");

        // The persisted RI-confidence must match the communication_assessed signal's confidence exactly —
        // both read the SAME persisted provenance + the SAME triage Priority (task 024's formula, task
        // 025's independent-but-identical recomputation).
        producer.Published.Should().ContainSingle();
        producer.Published[0].Confidence.Should().Be((double)fields["sprk_riconfidence"],
            "025's persisted sprk_riconfidence must match 024's emitted communication_assessed signal exactly");
    }

    // ── (b) category resolution: known seeded category resolves; unknown leaves the lookup unset ──

    [Fact]
    public async Task EnrichAsync_WithKnownSeededCategory_ResolvesToTaxonomyRowGuid()
    {
        var capturedUpdates = new Dictionary<string, object>?[1];
        var entityService = CreateEntityServiceMock(SamplePersistedProvenanceJson, capturedUpdates);

        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        triageAi
            .Setup(t => t.TriageAsync(It.IsAny<CommunicationTriageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationTriageResult(
                "Court / Filing", "Summary.", Array.Empty<string>(), "Medium", "Pending"));

        var sut = CreateService(entityService.Object, triageAi.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        capturedUpdates[0]!["sprk_triagecategory"].Should().BeOfType<EntityReference>()
            .Which.Id.Should().Be(CourtFilingCategoryId);
    }

    [Fact]
    public async Task EnrichAsync_WithUnknownCategory_LeavesLookupUnsetWithoutFabricatingARow()
    {
        var capturedUpdates = new Dictionary<string, object>?[1];
        var entityService = CreateEntityServiceMock(SamplePersistedProvenanceJson, capturedUpdates);

        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        triageAi
            .Setup(t => t.TriageAsync(It.IsAny<CommunicationTriageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationTriageResult(
                "Marketing Blast (unseeded)", "Summary.", Array.Empty<string>(), "Low", "Dismiss"));

        var sut = CreateService(entityService.Object, triageAi.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        var fields = capturedUpdates[0];
        fields.Should().NotBeNull("the update must still proceed for the other five fields");
        fields!.Should().NotContainKey("sprk_triagecategory",
            "an unresolved category must leave the lookup unset — never a fabricated taxonomy row, never free text");

        // The other fields still persist — an unresolved category degrades gracefully, not wholesale.
        fields!["sprk_triagepriority"].Should().BeOfType<OptionSetValue>().Which.Value.Should().Be(100000003);
        fields["sprk_reviewoutcome"].Should().BeOfType<OptionSetValue>().Which.Value.Should().Be(100000003);
    }

    // ── (c) NFR-04: a persist/resolve failure degrades non-fatally, never fails capture/enrichment ──

    [Fact]
    public async Task EnrichAsync_WhenDataverseUpdateThrows_CompletesWithoutPropagating()
    {
        var capturedUpdates = new Dictionary<string, object>?[1];
        var entityService = CreateEntityServiceMock(SamplePersistedProvenanceJson, capturedUpdates, throwOnUpdate: true);

        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        triageAi
            .Setup(t => t.TriageAsync(It.IsAny<CommunicationTriageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationTriageResult(
                "Court / Filing", "Summary.", new[] { "Respond by Friday" }, "Urgent", "Route"));

        var producer = new CapturingAssessedProducer();
        var sut = CreateService(entityService.Object, triageAi.Object, producer);

        Func<Task> act = () => sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync(
            "NFR-04: a triage-persistence write failure must never fail the capture/enrichment path");

        // Downstream steps (assessment-event) still run — the triage result itself is still used for the
        // communication_assessed signal even though its persistence failed.
        producer.Published.Should().ContainSingle(
            "a persistence failure must not prevent the already-produced triage result from feeding the RI-confidence signal");
    }

    [Fact]
    public async Task EnrichAsync_WhenCategoryLookupQueryThrows_CompletesWithoutPropagating_AndNeverCallsUpdate()
    {
        var capturedUpdates = new Dictionary<string, object>?[1];
        var entityService = CreateEntityServiceMock(SamplePersistedProvenanceJson, capturedUpdates, throwOnCategoryLookup: true);

        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        triageAi
            .Setup(t => t.TriageAsync(It.IsAny<CommunicationTriageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationTriageResult(
                "Court / Filing", "Summary.", new[] { "Respond by Friday" }, "Urgent", "Route"));

        var sut = CreateService(entityService.Object, triageAi.Object);

        Func<Task> act = () => sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync(
            "NFR-04: a category-resolution query failure must never fail the capture/enrichment path");

        entityService.Verify(
            s => s.UpdateAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the entire persistence method is wrapped in one try/catch — a failure upstream of the update call must skip the update, not partially apply it");
    }
}
