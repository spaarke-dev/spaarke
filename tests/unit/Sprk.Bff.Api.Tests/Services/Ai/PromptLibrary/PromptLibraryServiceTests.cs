using System.Net;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.PromptLibrary;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PromptLibrary;

/// <summary>
/// Unit tests for <see cref="PromptLibraryService"/> (AIPU2-035).
///
/// Coverage:
/// (a) ListAsync merges personal + team templates (Org/System return empty — deferred)
/// (b) CreateAsync / UpdateAsync / DeleteAsync succeed for Personal and Team tiers
/// (c) CreateAsync throws for Org and System tiers
/// (d) UpdateAsync / DeleteAsync throw for Org / System templates (403 semantics)
/// (e) RenderBody substitutes variables correctly
/// (f) RenderBody throws ArgumentException when required variables are missing
/// (g) GetAsync returns null when template not found
/// </summary>
public class PromptLibraryServiceTests
{
    private const string TenantId = "tenant-abc";
    private const string UserId = "user-001";
    private const string TeamId = "team-xt7";
    private const string DatabaseName = "spaarke-ai";
    private const string CosmosEndpoint = "https://spaarke-cosmos-dev.documents.azure.com:443/";

    private readonly Mock<CosmosClient> _cosmosClientMock;
    private readonly Mock<Container> _containerMock;
    private readonly Mock<ILogger<PromptLibraryService>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly PromptLibraryService _sut;

    public PromptLibraryServiceTests()
    {
        _cosmosClientMock = new Mock<CosmosClient>();
        _containerMock = new Mock<Container>();
        _loggerMock = new Mock<ILogger<PromptLibraryService>>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CosmosPersistence:Endpoint"] = CosmosEndpoint,
                ["CosmosPersistence:DatabaseName"] = DatabaseName
            })
            .Build();

        _cosmosClientMock
            .Setup(c => c.GetContainer(DatabaseName, "prompts"))
            .Returns(_containerMock.Object);

        _sut = new PromptLibraryService(
            _cosmosClientMock.Object,
            _configuration,
            _loggerMock.Object);
    }

    // =========================================================================
    // (a) ListAsync — merges personal + team; Org/System empty
    // =========================================================================

    [Fact]
    public async Task ListAsync_ReturnsMergedPersonalAndTeamTemplates()
    {
        // Arrange — personal doc returned for user query
        var personalDoc = BuildDoc(UserId, PromptOwnership.Personal);
        SetupQueryIterator([personalDoc], partitionKey: UserId, ownership: PromptOwnership.Personal);

        // Team doc returned for teamId query
        var teamDoc = BuildDoc(TeamId, PromptOwnership.Team);
        SetupQueryIterator([teamDoc], partitionKey: TeamId, ownership: PromptOwnership.Team);

        // Act
        var result = await _sut.ListAsync(TenantId, UserId, [TeamId]);

        // Assert — both templates visible; Org/System are empty (deferred)
        result.Should().HaveCount(2);
        result.Should().Contain(t => t.OwnerId == UserId && t.Ownership == PromptOwnership.Personal);
        result.Should().Contain(t => t.OwnerId == TeamId && t.Ownership == PromptOwnership.Team);
    }

    [Fact]
    public async Task ListAsync_WithNoTeamIds_ReturnsOnlyPersonalTemplates()
    {
        var personalDoc = BuildDoc(UserId, PromptOwnership.Personal);
        SetupQueryIterator([personalDoc], partitionKey: UserId, ownership: PromptOwnership.Personal);

        var result = await _sut.ListAsync(TenantId, UserId, teamIds: null);

        result.Should().HaveCount(1);
        result[0].Ownership.Should().Be(PromptOwnership.Personal);
    }

    // =========================================================================
    // (b) CreateAsync — success for Personal and Team
    // =========================================================================

    [Fact]
    public async Task CreateAsync_PersonalTemplate_CreatesDocumentWithUserAsOwner()
    {
        // Arrange
        var request = new CreatePromptRequest(
            Name: "My template",
            Body: "Hello {{name}}",
            Ownership: PromptOwnership.Personal);

        _containerMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<CosmosPromptDocument>(),
                It.Is<PartitionKey>(pk => pk == new PartitionKey(UserId)),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<CosmosPromptDocument>>());

        // Act
        var template = await _sut.CreateAsync(TenantId, UserId, request);

        // Assert
        template.Should().NotBeNull();
        template.Name.Should().Be("My template");
        template.Ownership.Should().Be(PromptOwnership.Personal);
        template.OwnerId.Should().Be(UserId);
        template.TenantId.Should().Be(TenantId);
        template.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateAsync_TeamTemplate_CreatesDocumentWithTeamAsOwner()
    {
        var request = new CreatePromptRequest(
            Name: "Team template",
            Body: "Hello {{client}}",
            Ownership: PromptOwnership.Team,
            OwnerId: TeamId);

        _containerMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<CosmosPromptDocument>(),
                It.Is<PartitionKey>(pk => pk == new PartitionKey(TeamId)),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<CosmosPromptDocument>>());

        var template = await _sut.CreateAsync(TenantId, UserId, request);

        template.Ownership.Should().Be(PromptOwnership.Team);
        template.OwnerId.Should().Be(TeamId);
    }

    // =========================================================================
    // (c) CreateAsync — forbidden for Org and System tiers
    // =========================================================================

    [Theory]
    [InlineData(PromptOwnership.Organization)]
    [InlineData(PromptOwnership.System)]
    public async Task CreateAsync_ThrowsInvalidOperation_ForOrgAndSystemTiers(PromptOwnership tier)
    {
        var request = new CreatePromptRequest(
            Name: "Restricted",
            Body: "body",
            Ownership: tier);

        var act = async () => await _sut.CreateAsync(TenantId, UserId, request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{tier}'*");
    }

    // =========================================================================
    // (d) UpdateAsync / DeleteAsync — forbidden for Org/System (403 semantics)
    // =========================================================================

    [Theory]
    [InlineData(PromptOwnership.Organization)]
    [InlineData(PromptOwnership.System)]
    public async Task UpdateAsync_ThrowsInvalidOperation_WhenTemplateIsOrgOrSystem(PromptOwnership tier)
    {
        var templateId = Guid.NewGuid().ToString("D");
        var existingDoc = BuildDoc(ownerId: "org-owner", ownership: tier, id: templateId);
        SetupFindByIdQuery(existingDoc, templateId);

        var act = async () => await _sut.UpdateAsync(TenantId, templateId, new UpdatePromptRequest(Name: "new"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{tier}'*");
    }

    [Theory]
    [InlineData(PromptOwnership.Organization)]
    [InlineData(PromptOwnership.System)]
    public async Task DeleteAsync_ThrowsInvalidOperation_WhenTemplateIsOrgOrSystem(PromptOwnership tier)
    {
        var templateId = Guid.NewGuid().ToString("D");
        var existingDoc = BuildDoc(ownerId: "org-owner", ownership: tier, id: templateId);
        SetupFindByIdQuery(existingDoc, templateId);

        var act = async () => await _sut.DeleteAsync(TenantId, templateId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{tier}'*");
    }

    // =========================================================================
    // (e) RenderBody — substitutes variables correctly
    // =========================================================================

    [Fact]
    public void RenderBody_SubstitutesAllVariables()
    {
        var template = BuildTemplate(
            body: "Dear {{client.name}}, regarding matter {{matter.id}}.",
            variables:
            [
                new TemplateVariable("client.name", TemplateVariableType.EntityRef, "Client name"),
                new TemplateVariable("matter.id",   TemplateVariableType.String,    "Matter ID")
            ]);

        var vars = new Dictionary<string, string>
        {
            ["client.name"] = "Acme Corp",
            ["matter.id"] = "M-2026-001"
        };

        var rendered = PromptLibraryService.RenderBody(template, vars);

        rendered.Should().Be("Dear Acme Corp, regarding matter M-2026-001.");
    }

    [Fact]
    public void RenderBody_LeavesUnknownPlaceholdersAsIs()
    {
        var template = BuildTemplate(
            body: "Hello {{known}} and {{unknown}}.",
            variables: [new TemplateVariable("known", TemplateVariableType.String, "Known var", Required: false)]);

        var rendered = PromptLibraryService.RenderBody(
            template,
            new Dictionary<string, string> { ["known"] = "world" });

        rendered.Should().Be("Hello world and {{unknown}}.");
    }

    // =========================================================================
    // (f) RenderBody — throws when required variables are missing
    // =========================================================================

    [Fact]
    public void RenderBody_ThrowsArgumentException_WhenRequiredVariableIsMissing()
    {
        var template = BuildTemplate(
            body: "Hello {{name}} from {{place}}.",
            variables:
            [
                new TemplateVariable("name",  TemplateVariableType.String, "Name",  Required: true),
                new TemplateVariable("place", TemplateVariableType.String, "Place", Required: true)
            ]);

        // Only "name" provided — "place" is missing
        var act = () => PromptLibraryService.RenderBody(
            template,
            new Dictionary<string, string> { ["name"] = "Bob" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*place*");
    }


    // =========================================================================
    // (g) GetAsync — returns null when not found
    // =========================================================================

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenTemplateNotFound()
    {
        var templateId = Guid.NewGuid().ToString("D");
        // SetupFindByIdQuery with empty list → returns null
        SetupFindByIdQuery(null, templateId);

        var result = await _sut.GetAsync(TenantId, templateId);

        result.Should().BeNull();
    }

    // =========================================================================
    // UpdateAsync — success for Personal template
    // =========================================================================

    [Fact]
    public async Task UpdateAsync_ReplacesItemInCosmos_ForPersonalTemplate()
    {
        var templateId = Guid.NewGuid().ToString("D");
        var existingDoc = BuildDoc(ownerId: UserId, ownership: PromptOwnership.Personal, id: templateId);
        SetupFindByIdQuery(existingDoc, templateId);

        _containerMock
            .Setup(c => c.ReplaceItemAsync(
                It.IsAny<CosmosPromptDocument>(),
                templateId,
                It.Is<PartitionKey>(pk => pk == new PartitionKey(UserId)),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<CosmosPromptDocument>>());

        await _sut.UpdateAsync(TenantId, templateId, new UpdatePromptRequest(Name: "Updated Name"));

        _containerMock.Verify(
            c => c.ReplaceItemAsync(
                It.Is<CosmosPromptDocument>(d => d.Name == "Updated Name"),
                templateId,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // DeleteAsync — success for Team template
    // =========================================================================

    [Fact]
    public async Task DeleteAsync_DeletesItemFromCosmos_ForTeamTemplate()
    {
        var templateId = Guid.NewGuid().ToString("D");
        var existingDoc = BuildDoc(ownerId: TeamId, ownership: PromptOwnership.Team, id: templateId);
        SetupFindByIdQuery(existingDoc, templateId);

        _containerMock
            .Setup(c => c.DeleteItemAsync<CosmosPromptDocument>(
                templateId,
                It.Is<PartitionKey>(pk => pk == new PartitionKey(TeamId)),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<CosmosPromptDocument>>());

        await _sut.DeleteAsync(TenantId, templateId);

        _containerMock.Verify(
            c => c.DeleteItemAsync<CosmosPromptDocument>(
                templateId,
                It.Is<PartitionKey>(pk => pk == new PartitionKey(TeamId)),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private static CosmosPromptDocument BuildDoc(
        string ownerId,
        PromptOwnership ownership,
        string? id = null) => new()
        {
            Id = id ?? Guid.NewGuid().ToString("D"),
            TenantId = TenantId,
            OwnerId = ownerId,
            Ownership = ownership,
            Name = $"{ownership} Template",
            Body = "Hello {{name}}",
            Tags = [],
            Variables = [],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static PromptTemplate BuildTemplate(
        string body,
        IReadOnlyList<TemplateVariable> variables) => new(
            Id: Guid.NewGuid().ToString("D"),
            TenantId: TenantId,
            Name: "Test",
            Description: null,
            Body: body,
            Ownership: PromptOwnership.Personal,
            OwnerId: UserId,
            Tags: [],
            Variables: variables,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

    /// <summary>
    /// Configures _containerMock to return <paramref name="docs"/> for a cross-partition
    /// <c>FindByIdAsync</c> query (no PartitionKey on QueryRequestOptions).
    /// </summary>
    private void SetupFindByIdQuery(CosmosPromptDocument? doc, string templateId)
    {
        var docs = doc is null ? [] : new List<CosmosPromptDocument> { doc };
        var iteratorMock = new Mock<FeedIterator<CosmosPromptDocument>>();
        var responseMock = new Mock<FeedResponse<CosmosPromptDocument>>();

        responseMock.Setup(r => r.GetEnumerator()).Returns(docs.GetEnumerator());

        // HasMoreResults: true the first time so we read one page, then false
        var hasMoreResultsQueue = new Queue<bool>([true, false]);
        iteratorMock
            .SetupGet(i => i.HasMoreResults)
            .Returns(() => hasMoreResultsQueue.Count > 0 && hasMoreResultsQueue.Dequeue());
        iteratorMock
            .Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        _containerMock
            .Setup(c => c.GetItemQueryIterator<CosmosPromptDocument>(
                It.Is<QueryDefinition>(q => q.QueryText.Contains("c.id = @id")),
                It.IsAny<string>(),
                It.Is<QueryRequestOptions>(o => o.MaxItemCount == 1)))
            .Returns(iteratorMock.Object);
    }

    /// <summary>
    /// Configures _containerMock to return <paramref name="docs"/> for a partition-scoped
    /// <c>QueryByOwnerAsync</c> query filtered by ownerId + ownership.
    /// </summary>
    private void SetupQueryIterator(
        List<CosmosPromptDocument> docs,
        string partitionKey,
        PromptOwnership ownership)
    {
        var iteratorMock = new Mock<FeedIterator<CosmosPromptDocument>>();
        var responseMock = new Mock<FeedResponse<CosmosPromptDocument>>();

        responseMock.Setup(r => r.GetEnumerator()).Returns(docs.GetEnumerator());

        var hasMoreResultsQueue = new Queue<bool>([true, false]);
        iteratorMock
            .SetupGet(i => i.HasMoreResults)
            .Returns(() => hasMoreResultsQueue.Count > 0 && hasMoreResultsQueue.Dequeue());
        iteratorMock
            .Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        _containerMock
            .Setup(c => c.GetItemQueryIterator<CosmosPromptDocument>(
                It.Is<QueryDefinition>(q => q.QueryText.Contains("c.ownerId = @ownerId")),
                It.IsAny<string>(),
                It.Is<QueryRequestOptions>(o =>
                    o.PartitionKey == new PartitionKey(partitionKey))))
            .Returns(iteratorMock.Object);
    }
}
