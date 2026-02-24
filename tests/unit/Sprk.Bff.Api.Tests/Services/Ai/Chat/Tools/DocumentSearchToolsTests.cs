using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Tools;

/// <summary>
/// Unit tests for <see cref="DocumentSearchTools"/>.
///
/// Verifies:
/// - Tool methods have [Description] attributes (required for AIFunctionFactory.Create)
/// - RagService.SearchAsync is called with the correct tenantId (ADR-014 compliance)
/// - Correct RagSearchOptions are built for SearchDocuments vs SearchDiscovery
/// - Results are formatted correctly and returned
/// - Edge cases: empty results, parameter validation
/// </summary>
public class DocumentSearchToolsTests
{
    private const string TestTenantId = "tenant-abc";
    private const string TestQuery = "contract renewal terms";

    // === [Description] attribute tests ===

    [Fact]
    public void SearchDocumentsAsync_HasDescriptionAttribute_OnQueryParameter()
    {
        // Arrange
        var method = typeof(DocumentSearchTools).GetMethod(nameof(DocumentSearchTools.SearchDocumentsAsync));
        method.Should().NotBeNull();

        var queryParam = method!.GetParameters().First(p => p.Name == "query");

        // Act
        var description = queryParam.GetCustomAttribute<DescriptionAttribute>();

        // Assert
        description.Should().NotBeNull("AIFunctionFactory.Create requires [Description] on key parameters");
        description!.Description.Should().Be("Search query for document knowledge");
    }

    [Fact]
    public void SearchDiscoveryAsync_HasDescriptionAttribute_OnQueryParameter()
    {
        // Arrange
        var method = typeof(DocumentSearchTools).GetMethod(nameof(DocumentSearchTools.SearchDiscoveryAsync));
        method.Should().NotBeNull();

        var queryParam = method!.GetParameters().First(p => p.Name == "query");

        // Act
        var description = queryParam.GetCustomAttribute<DescriptionAttribute>();

        // Assert
        description.Should().NotBeNull("AIFunctionFactory.Create requires [Description] on key parameters");
        description!.Description.Should().Be("Broad discovery search across all documents");
    }

    // === SearchDocumentsAsync service call tests ===

    [Fact]
    public async Task SearchDocumentsAsync_CallsRagService_WithCorrectTenantId()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object);

        // Act
        await sut.SearchDocumentsAsync(TestQuery, TestTenantId);

        // Assert — tenantId must be passed through to RagService (ADR-014)
        ragServiceMock.Verify(
            r => r.SearchAsync(
                TestQuery,
                It.Is<RagSearchOptions>(o => o.TenantId == TestTenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchDocumentsAsync_UsesDefaultTopK_WhenNotSpecified()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object);

        // Act
        await sut.SearchDocumentsAsync(TestQuery, TestTenantId);

        // Assert — default topK is 5
        ragServiceMock.Verify(
            r => r.SearchAsync(
                It.IsAny<string>(),
                It.Is<RagSearchOptions>(o => o.TopK == 5),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchDocumentsAsync_PassesTopK_ToRagService()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object);

        // Act
        await sut.SearchDocumentsAsync(TestQuery, TestTenantId, topK: 10);

        // Assert
        ragServiceMock.Verify(
            r => r.SearchAsync(
                It.IsAny<string>(),
                It.Is<RagSearchOptions>(o => o.TopK == 10),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchDocumentsAsync_ReturnsNoResultsMessage_WhenRagReturnsEmpty()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object);

        // Act
        var result = await sut.SearchDocumentsAsync(TestQuery, TestTenantId);

        // Assert
        result.Should().Contain("No relevant documents found");
    }

    [Fact]
    public async Task SearchDocumentsAsync_FormatsResults_WhenRagReturnsMatches()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponseWithResult("Contract Agreement.pdf", "This contract stipulates renewal terms."));

        var sut = new DocumentSearchTools(ragServiceMock.Object);

        // Act
        var result = await sut.SearchDocumentsAsync(TestQuery, TestTenantId);

        // Assert
        result.Should().Contain("Contract Agreement.pdf");
        result.Should().Contain("This contract stipulates renewal terms.");
        result.Should().Contain("Found 1 relevant document");
    }

    [Fact]
    public async Task SearchDocumentsAsync_ThrowsArgumentException_WhenQueryIsEmpty()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        var sut = new DocumentSearchTools(ragServiceMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SearchDocumentsAsync(string.Empty, TestTenantId));
    }

    [Fact]
    public async Task SearchDocumentsAsync_ThrowsArgumentException_WhenTenantIdIsEmpty()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        var sut = new DocumentSearchTools(ragServiceMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SearchDocumentsAsync(TestQuery, string.Empty));
    }

    // === SearchDiscoveryAsync service call tests ===

    [Fact]
    public async Task SearchDiscoveryAsync_CallsRagService_WithCorrectTenantId()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object);

        // Act
        await sut.SearchDiscoveryAsync(TestQuery, TestTenantId);

        // Assert — tenantId must be passed through to RagService (ADR-014)
        ragServiceMock.Verify(
            r => r.SearchAsync(
                TestQuery,
                It.Is<RagSearchOptions>(o => o.TenantId == TestTenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchDiscoveryAsync_UsesDefaultTopK10_WhenNotSpecified()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object);

        // Act
        await sut.SearchDiscoveryAsync(TestQuery, TestTenantId);

        // Assert — discovery default is 10 (broader search)
        ragServiceMock.Verify(
            r => r.SearchAsync(
                It.IsAny<string>(),
                It.Is<RagSearchOptions>(o => o.TopK == 10),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchDiscoveryAsync_UsesLowerMinScore_ForBroaderDiscovery()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object);

        // Act
        await sut.SearchDiscoveryAsync(TestQuery, TestTenantId);

        // Assert — discovery uses lower threshold (0.5) vs knowledge search (0.7)
        ragServiceMock.Verify(
            r => r.SearchAsync(
                It.IsAny<string>(),
                It.Is<RagSearchOptions>(o => o.MinScore < 0.7f),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchDiscoveryAsync_ReturnsDiscoveryMessage_WhenResultsFound()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponseWithResult("Policy Document.pdf", "Employee handbook policy text."));

        var sut = new DocumentSearchTools(ragServiceMock.Object);

        // Act
        var result = await sut.SearchDiscoveryAsync(TestQuery, TestTenantId);

        // Assert
        result.Should().Contain("Discovery search found 1 document");
        result.Should().Contain("Policy Document.pdf");
    }

    // === Knowledge scope tests ===

    [Fact]
    public async Task SearchDocumentsAsync_PassesKnowledgeSourceIds_WhenScopeProvided()
    {
        // Arrange
        var knowledgeSourceIds = new List<string> { "knw-001", "knw-002" };
        var scope = new ChatKnowledgeScope(knowledgeSourceIds, null, null, null);

        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object, scope);

        // Act
        await sut.SearchDocumentsAsync(TestQuery, TestTenantId);

        // Assert — knowledge source IDs from scope should be passed to search
        ragServiceMock.Verify(
            r => r.SearchAsync(
                TestQuery,
                It.Is<RagSearchOptions>(o =>
                    o.KnowledgeSourceIds != null &&
                    o.KnowledgeSourceIds.Count == 2 &&
                    o.KnowledgeSourceIds.Contains("knw-001") &&
                    o.KnowledgeSourceIds.Contains("knw-002")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchDocumentsAsync_DoesNotSetKnowledgeSourceIds_WhenNoScope()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object); // No scope

        // Act
        await sut.SearchDocumentsAsync(TestQuery, TestTenantId);

        // Assert — no knowledge source filtering when scope is null
        ragServiceMock.Verify(
            r => r.SearchAsync(
                TestQuery,
                It.Is<RagSearchOptions>(o => o.KnowledgeSourceIds == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchDiscoveryAsync_DoesNotSetKnowledgeSourceIds_EvenWhenScopeProvided()
    {
        // Arrange — SearchDiscovery is intentionally tenant-wide (no knowledge scoping)
        var knowledgeSourceIds = new List<string> { "knw-001" };
        var scope = new ChatKnowledgeScope(knowledgeSourceIds, null, null, null);

        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object, scope);

        // Act
        await sut.SearchDiscoveryAsync(TestQuery, TestTenantId);

        // Assert — discovery search must remain tenant-wide
        ragServiceMock.Verify(
            r => r.SearchAsync(
                TestQuery,
                It.Is<RagSearchOptions>(o => o.KnowledgeSourceIds == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // === Entity scope tests ===

    [Fact]
    public async Task SearchDiscoveryAsync_WithEntityScope_PassesEntityFieldsToRagSearchOptions()
    {
        // Arrange
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: new List<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: null,
            ParentEntityType: "matter",
            ParentEntityId: "entity-123");

        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object, scope);

        // Act
        await sut.SearchDiscoveryAsync(TestQuery, TestTenantId);

        // Assert — entity scope fields must be passed through to RagSearchOptions
        ragServiceMock.Verify(
            r => r.SearchAsync(
                TestQuery,
                It.Is<RagSearchOptions>(o =>
                    o.ParentEntityType == "matter" &&
                    o.ParentEntityId == "entity-123"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchDiscoveryAsync_WithoutEntityScope_NullEntityFields()
    {
        // Arrange
        var ragServiceMock = new Mock<IRagService>();
        ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyResponse(TestQuery));

        var sut = new DocumentSearchTools(ragServiceMock.Object); // No scope

        // Act
        await sut.SearchDiscoveryAsync(TestQuery, TestTenantId);

        // Assert — without scope, entity fields should be null
        ragServiceMock.Verify(
            r => r.SearchAsync(
                TestQuery,
                It.Is<RagSearchOptions>(o =>
                    o.ParentEntityType == null &&
                    o.ParentEntityId == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // === Constructor validation ===

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenRagServiceIsNull()
    {
        // Act & Assert
        var action = () => new DocumentSearchTools(null!);
        action.Should().Throw<ArgumentNullException>().WithParameterName("ragService");
    }

    // === Private helpers ===

    private static RagSearchResponse CreateEmptyResponse(string query)
        => new RagSearchResponse
        {
            Query = query,
            Results = [],
            TotalCount = 0
        };

    private static RagSearchResponse CreateResponseWithResult(string documentName, string content)
        => new RagSearchResponse
        {
            Query = TestQuery,
            Results =
            [
                new RagSearchResult
                {
                    Id = "chunk-001",
                    DocumentId = Guid.NewGuid().ToString(),
                    DocumentName = documentName,
                    Content = content,
                    Score = 0.85,
                    ChunkIndex = 0,
                    ChunkCount = 1
                }
            ],
            TotalCount = 1
        };
}
