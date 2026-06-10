using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for the multi-container-multi-index-r1 shared resolver. Covers the
/// 3-step chain (doc → parent → BU) with two fall-through tiers per Phase G (task 102):
///   • Lookup path (sprk_ai_search_index → sprk_aisearchindex.sprk_searchindexname) wins;
///   • Legacy text column (sprk_searchindexname on the source entity) is the
///     migration-safety fallback (logs PhaseG.TextFallback).
/// Mocks <see cref="IGenericEntityService.RetrieveMultipleAsync(FetchExpression, CancellationToken)"/>
/// because the resolver now issues a SINGLE FetchXml fetch per step (with link-entity outer join).
/// </summary>
public sealed class SearchIndexNameResolverTests
{
    private const string AliasedAttr = "idx.sprk_searchindexname";
    private const string LegacyText = "sprk_searchindexname";

    private readonly Mock<IGenericEntityService> _entityServiceMock = new();
    private readonly Mock<ILogger<SearchIndexNameResolver>> _loggerMock = new();

    private SearchIndexNameResolver CreateResolver() =>
        new(_entityServiceMock.Object, _loggerMock.Object);

    /// <summary>
    /// Builds an Entity that mimics the FetchXml result for a source-record fetch with
    /// the sprk_aisearchindex link-entity outer-join. Set <paramref name="linkedIndexName"/>
    /// to a value when the lookup resolves; leave null when the lookup is empty.
    /// Set <paramref name="legacyTextName"/> to simulate the legacy text-column fallback.
    /// </summary>
    private static Entity FetchedRow(
        string entityName,
        Guid id,
        string? linkedIndexName = null,
        string? legacyTextName = null,
        EntityReference? owningBu = null)
    {
        var entity = new Entity(entityName, id);
        if (linkedIndexName is not null)
        {
            entity[AliasedAttr] = new AliasedValue(
                "sprk_aisearchindex", "sprk_searchindexname", linkedIndexName);
        }
        if (legacyTextName is not null)
        {
            entity[LegacyText] = legacyTextName;
        }
        if (owningBu is not null)
        {
            entity["owningbusinessunit"] = owningBu;
        }
        return entity;
    }

    private static EntityCollection SingleRow(Entity? row)
    {
        var coll = new EntityCollection();
        if (row is not null)
        {
            coll.Entities.Add(row);
        }
        return coll;
    }

    /// <summary>
    /// Returns true when the FetchXml string contains an attribute filter binding
    /// the supplied id (used to verify which entity-step the mock is matching).
    /// FetchExpression equality is reference-based; we match on payload content.
    /// </summary>
    private static bool FetchMatches(FetchExpression fe, string entityName, Guid id) =>
        fe?.Query is not null
        && fe.Query.Contains($"<entity name='{entityName}'")
        && fe.Query.Contains(id.ToString("D"));

    [Fact]
    public async Task ResolveAsync_DocumentLevelLookupValue_WinsOverParentAndBu()
    {
        // Arrange — sprk_document's link-entity outer-join projects an index name → chain stops.
        var docId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "sprk_document", docId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleRow(FetchedRow("sprk_document", docId, linkedIndexName: "spaarke-file-index")));

        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(
            docId.ToString(),
            "sprk_matter",
            parentId.ToString(),
            CancellationToken.None);

        // Assert
        result.Should().Be("spaarke-file-index");

        // Parent + BU lookups MUST NOT be invoked (short-circuit at step 1)
        _entityServiceMock.Verify(
            x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "sprk_matter", parentId)),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_DocumentTextFallback_ReturnsTextValueAndLogsWarning()
    {
        // Arrange — sprk_document has no lookup but has the legacy text column populated.
        // Resolver must (a) return that value, (b) emit the PhaseG.TextFallback warning.
        var docId = Guid.NewGuid();

        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "sprk_document", docId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleRow(FetchedRow("sprk_document", docId, legacyTextName: "spaarke-knowledge-index-v2")));

        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(docId.ToString(), null, null, CancellationToken.None);

        // Assert
        result.Should().Be("spaarke-knowledge-index-v2");

        // Verify the warning marker was logged at least once with the expected template.
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("PhaseG.TextFallback")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ResolveAsync_DocumentValueEmpty_FallsThroughToParentLookup()
    {
        // Arrange — document found but neither lookup nor text set → walk to parent.
        var docId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "sprk_document", docId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleRow(FetchedRow("sprk_document", docId)));

        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "sprk_matter", parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleRow(FetchedRow("sprk_matter", parentId, linkedIndexName: "spaarke-knowledge-index-v2")));

        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(
            docId.ToString(),
            "sprk_matter",
            parentId.ToString(),
            CancellationToken.None);

        // Assert
        result.Should().Be("spaarke-knowledge-index-v2");
    }

    [Fact]
    public async Task ResolveAsync_NoDocumentOrParentValue_FallsThroughToOwningBu()
    {
        // Arrange — chain reaches step 3 (parent's owning BU). Parent fetch returns
        // BU reference but no index name. BU fetch returns the BU's index.
        var parentId = Guid.NewGuid();
        var buId = Guid.NewGuid();
        var buRef = new EntityReference("businessunit", buId);

        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "sprk_matter", parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleRow(FetchedRow("sprk_matter", parentId, owningBu: buRef)));

        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "businessunit", buId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleRow(FetchedRow("businessunit", buId, linkedIndexName: "spaarke-bu-index")));

        var resolver = CreateResolver();

        // Act — no documentId provided; parent has only BU; BU resolves.
        var result = await resolver.ResolveAsync(
            documentId: null,
            "sprk_matter",
            parentId.ToString(),
            CancellationToken.None);

        // Assert
        result.Should().Be("spaarke-bu-index");
    }

    [Fact]
    public async Task ResolveAsync_NoValueAnywhere_ReturnsNull()
    {
        // Arrange — every step returns nothing.
        var parentId = Guid.NewGuid();
        var buId = Guid.NewGuid();
        var buRef = new EntityReference("businessunit", buId);

        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "sprk_matter", parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleRow(FetchedRow("sprk_matter", parentId, owningBu: buRef)));

        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "businessunit", buId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleRow(FetchedRow("businessunit", buId)));

        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(
            documentId: null,
            "sprk_matter",
            parentId.ToString(),
            CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_NoInputs_ReturnsNullWithoutCallingDataverse()
    {
        // Arrange
        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(
            documentId: null,
            parentEntityType: null,
            parentEntityId: null,
            CancellationToken.None);

        // Assert
        result.Should().BeNull();
        _entityServiceMock.Verify(
            x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_DocumentLookupThrows_FallsThroughToParent()
    {
        // Arrange — sprk_document fetch throws (transient Dataverse failure). Resolver MUST
        // continue the chain so a single-step failure does not poison the entire result.
        var docId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "sprk_document", docId)),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse transient failure"));

        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "sprk_matter", parentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleRow(FetchedRow("sprk_matter", parentId, linkedIndexName: "spaarke-file-index")));

        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(
            docId.ToString(),
            "sprk_matter",
            parentId.ToString(),
            CancellationToken.None);

        // Assert — recovered via parent lookup
        result.Should().Be("spaarke-file-index");
    }

    [Fact]
    public async Task ResolveAsync_MalformedGuids_ReturnsNull()
    {
        // Arrange — non-GUID strings short-circuit silently.
        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(
            documentId: "not-a-guid",
            parentEntityType: "sprk_matter",
            parentEntityId: "also-not-a-guid",
            CancellationToken.None);

        // Assert
        result.Should().BeNull();
        _entityServiceMock.Verify(
            x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_LookupWinsOverTextFallback_NoWarning()
    {
        // Arrange — when BOTH lookup and text are set on the same row, lookup wins (no warning).
        var docId = Guid.NewGuid();

        _entityServiceMock
            .Setup(x => x.RetrieveMultipleAsync(
                It.Is<FetchExpression>(fe => FetchMatches(fe, "sprk_document", docId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleRow(FetchedRow(
                "sprk_document",
                docId,
                linkedIndexName: "spaarke-knowledge-index-v2",
                legacyTextName: "spaarke-file-index")));

        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAsync(docId.ToString(), null, null, CancellationToken.None);

        // Assert
        result.Should().Be("spaarke-knowledge-index-v2", "lookup is the source of truth; text is migration fallback only");

        // No PhaseG.TextFallback warning when lookup wins
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("PhaseG.TextFallback")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
