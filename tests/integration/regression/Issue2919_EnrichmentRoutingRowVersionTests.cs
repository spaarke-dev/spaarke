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
using Sprk.Bff.Api.Tests.Seam.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// Same flaw as #2919, in the email-triage category→team routing (unified-access-control-r2 task 146 r2 / b2). The Dataverse
/// ServiceClient returns a row's version on <see cref="Entity.RowVersion"/> for a <c>ColumnSet(true)</c> read, not as the
/// <c>versionnumber</c> attribute. <see cref="CommunicationEnrichmentService"/> read only the attribute when it read the
/// communication's filing, so on a live environment every row looked unreadable and no communication was ever routed (it failed
/// closed, but the feature was silently dead). These rows carry the version the way the ServiceClient returns it. Setup mirrors
/// <see cref="EmailTriageSeamTests"/>.
/// </summary>
public sealed class Issue2919_EnrichmentRoutingRowVersionTests
{
    private const string CommunicationEntity = "sprk_communication";
    private static readonly Guid LitigationTeamId = Guid.Parse("29190000-0000-4000-8000-0000000000aa");

    /// <summary>A persisted AI-classify signal, so the email-triage step runs (the same shape as <see cref="EmailTriageSeamTests"/>).</summary>
    private const string PersistedProvenanceJson =
        """
        {"version":1,"direction":"Incoming","decision":{"status":"PendingReview","autoFiled":false,"killSwitchEnabled":true,"autoFileThreshold":0.85,"topDeterministicConfidence":0.0,"topConfidence":0.6,"aiInvolved":true,"reason":"test"},"rungsFired":["AiClassification"],"candidates":[],"signals":[{"category":"court-notice","confidence":0.6,"provenance":"ai-classify:category=court-notice:urgency=urgent:types=[sprk_matter]:actions=[calendar-deadline]:Court deadline.","obligations":["respond-by-deadline"]}]}
        """;

    [Fact]
    public async Task Issue2919_VersionOnlyOnRowVersion_AnUnfiledCommunicationIsRouted_ConditionalOnThatVersion()
    {
        var routed = RoutedWrites();
        var communicationId = Guid.NewGuid();

        await Enrich(communicationId, id => new Entity(CommunicationEntity, id) { ["sprk_name"] = "Email: unfiled", RowVersion = "12345" }, routed);

        routed.Verify(f => f.UpdateRecordFieldsIfUnchangedAsync(
            CommunicationEntity,
            communicationId,
            It.Is<Dictionary<string, object?>>(fields =>
                fields.Count == 1 && (string?)fields["ownerid@odata.bind"] == $"/teams({LitigationTeamId:D})"),
            12345L,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Issue2919_NoVersionOnEitherSource_TheCommunicationIsNotRouted()
    {
        var routed = RoutedWrites();

        await Enrich(Guid.NewGuid(), id => new Entity(CommunicationEntity, id) { ["sprk_name"] = "Email: unfiled", RowVersion = null }, routed);

        routed.Verify(f => f.UpdateRecordFieldsIfUnchangedAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Issue2919_VersionOnTheAttribute_StillRoutes_AndTheAttributeWinsOverRowVersion()
    {
        var routed = RoutedWrites();
        var communicationId = Guid.NewGuid();

        await Enrich(
            communicationId,
            id => new Entity(CommunicationEntity, id) { ["sprk_name"] = "Email: unfiled", ["versionnumber"] = 7L, RowVersion = "9" },
            routed);

        routed.Verify(f => f.UpdateRecordFieldsIfUnchangedAsync(
            CommunicationEntity, communicationId, It.IsAny<Dictionary<string, object?>>(), 7L,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Issue2919_VersionOnlyOnRowVersion_AFiledCommunicationIsStillNotRouted()
    {
        var routed = RoutedWrites();

        await Enrich(
            Guid.NewGuid(),
            id => new Entity(CommunicationEntity, id)
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid()),
                RowVersion = "12345",
            },
            routed);

        routed.Verify(f => f.UpdateRecordFieldsIfUnchangedAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Runs the real enrichment with routing ON for "Court / Filing", the triage facade returning that category, and
    /// <paramref name="communicationRow"/> as the row the filing read returns.</summary>
    private static async Task Enrich(Guid communicationId, Func<Guid, Entity> communicationRow, Mock<IFieldMappingDataverseService> routed)
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(CommunicationEntity, communicationId) { ["sprk_associationprovenance"] = PersistedProvenanceJson });
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName switch
            {
                "team" => new EntityCollection(new List<Entity> { new("team") { Id = LitigationTeamId } }),
                CommunicationEntity => new EntityCollection(new List<Entity> { communicationRow((Guid)q.Criteria.Conditions[0].Values[0]) }),
                _ => new EntityCollection(),
            });

        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        triageAi
            .Setup(t => t.TriageAsync(It.IsAny<CommunicationTriageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationTriageResult("Court / Filing", "Summary.", new[] { "Respond by Friday" }, "Urgent", "Route"));

        var gate = TestRoutingGate.From(new CategoryRoutingOptions
        {
            Enabled = true,
            CategoryToTeam = { ["Court / Filing"] = "Litigation Team" },
        });

        var sut = new CommunicationEnrichmentService(
            EnrichmentScopeFactoryStub.Create(
                new Mock<IPostUploadIndexingEnqueuer>(MockBehavior.Loose).Object,
                triageAi.Object,
                new NullCommunicationProposeAi(),
                new NullCommunicationCreateTaskAi()),
            entityService.Object,
            new ConfigurationBuilder().Build(),
            new Mock<ICommunicationAssessedProducer>(MockBehavior.Loose).Object,
            new Mock<IActionSeam>(MockBehavior.Loose).Object,
            gate,
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            routed.Object,
            NullLogger<CommunicationEnrichmentService>.Instance);

        var message = new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            From = "sender@example.com",
            To = new[] { "reviewer@example.com" },
            Subject = "URGENT: Response due Friday",
            BodyText = "Please respond to the court by Friday.",
        };

        await sut.EnrichAsync(communicationId, CommunicationDirection.Incoming, message, archivedDocumentId: null, CancellationToken.None);
    }

    /// <summary>The Web API seam the routed owner write goes through (conditional, If-Match). Strict: only that write may reach it.</summary>
    private static Mock<IFieldMappingDataverseService> RoutedWrites()
    {
        var fieldMapping = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        fieldMapping
            .Setup(f => f.UpdateRecordFieldsIfUnchangedAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return fieldMapping;
    }
}
