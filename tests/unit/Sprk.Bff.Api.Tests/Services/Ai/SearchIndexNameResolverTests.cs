using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for the multi-container-multi-index-r1 indexer-routing-fix (Tier 3) shared
/// resolver. Covers the 3-step chain:
///   (1) sprk_document.sprk_searchindexname wins (highest precedence);
///   (2) parent record's sprk_searchindexname wins next;
///   (3) parent's owning BU's sprk_searchindexname wins last;
///   (4) returns null when no value is found (caller falls through to tenant default).
/// </summary>
public sealed class SearchIndexNameResolverTests
{
    private readonly Mock<IGenericEntityService> _entityServiceMock = new();
    private readonly Mock<ILogger<SearchIndexNameResolver>> _loggerMock = new();

    private SearchIndexNameResolver CreateResolver() =>
        new(_entityServiceMock.Object, _loggerMock.Object);

    private static Entity EntityWith(string entityName, Guid id, Dictionary<string, object?> attributes)
    {
        var entity = new Entity(entityName, id);
        foreach (var (k, v) in attributes)
        {
            if (v is not null)
            {
                entity[k] = v;
            }
        }
        return entity;
    }

    [Fact]
    public async Task ResolveAsync_DocumentLevelValue_WinsOverParentAndBu()
    {
        // Arrange — sprk_document has an explicit value; chain must STOP at step 1.
        var docId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        _entityServiceMock
            .Setup(x => x.RetrieveAsync(
                "sprk_document",
                docId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntityWith("sprk_document", docId, new()
            {
                ["sprk_searchindexname"] = "spaarke-file-index"
            }));

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
            x => x.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _entityServiceMock.Verify(
            x => x.RetrieveAsync("businessunit", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_DocumentValueEmpty_FallsThroughToParent()
    {
        // Arrange — document found but sprk_searchindexname empty → check parent.
        var docId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        _entityServiceMock
            .Setup(x => x.RetrieveAsync("sprk_document", docId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntityWith("sprk_document", docId, new()
            {
                ["sprk_searchindexname"] = ""
            }));

        _entityServiceMock
            .Setup(x => x.RetrieveAsync("sprk_matter", parentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntityWith("sprk_matter", parentId, new()
            {
                ["sprk_searchindexname"] = "spaarke-knowledge-index-v2"
            }));

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
        // Arrange — chain reaches step 3 (parent's owning BU).
        var parentId = Guid.NewGuid();
        var buId = Guid.NewGuid();

        _entityServiceMock
            .Setup(x => x.RetrieveAsync("sprk_matter", parentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntityWith("sprk_matter", parentId, new()
            {
                ["owningbusinessunit"] = new EntityReference("businessunit", buId)
            }));

        _entityServiceMock
            .Setup(x => x.RetrieveAsync("businessunit", buId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntityWith("businessunit", buId, new()
            {
                ["sprk_searchindexname"] = "spaarke-bu-index"
            }));

        var resolver = CreateResolver();

        // Act — no documentId provided, parent has no explicit value but owning BU does
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

        _entityServiceMock
            .Setup(x => x.RetrieveAsync("sprk_matter", parentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntityWith("sprk_matter", parentId, new()
            {
                ["owningbusinessunit"] = new EntityReference("businessunit", buId)
            }));

        _entityServiceMock
            .Setup(x => x.RetrieveAsync("businessunit", buId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntityWith("businessunit", buId, new()));

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
            x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_DocumentLookupThrows_FallsThroughToParent()
    {
        // Arrange — sprk_document lookup throws (transient Dataverse failure). Resolver MUST
        // continue the chain so a single-step failure does not poison the entire result.
        var docId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        _entityServiceMock
            .Setup(x => x.RetrieveAsync("sprk_document", docId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse transient failure"));

        _entityServiceMock
            .Setup(x => x.RetrieveAsync("sprk_matter", parentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntityWith("sprk_matter", parentId, new()
            {
                ["sprk_searchindexname"] = "spaarke-file-index"
            }));

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
            x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
