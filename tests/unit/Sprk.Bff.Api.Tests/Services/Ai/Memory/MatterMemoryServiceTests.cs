using System.Net;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Unit tests for <see cref="MatterMemoryService"/>.
///
/// Cosmos DB interactions are verified by asserting that the correct methods are called on
/// <see cref="Container"/> (mocked). The service's internal logic — prompt fragment rendering,
/// truncation, ETag threading — is tested via the internal static helper methods directly
/// and indirectly through the mock-based integration tests.
///
/// Tests (per acceptance criteria in AIPU2-034):
/// (a) GetMemoryAsync returns null for an unknown matter.
/// (b) SaveFactAsync persists structured data and increments Version.
/// (c) ToSystemPromptFragmentAsync output is within the 200–500 token target for a typical payload.
/// (d) Version increments on each SaveFactAsync call (optimistic concurrency counter).
/// (e) ClearMemoryAsync deletes the document; idempotent when not found.
/// (f) BuildPromptFragment groups facts under correct section headings.
/// (g) BuildPromptFragment excludes low-confidence unconfirmed facts.
/// (h) BuildDocumentId format matches expected pattern.
/// </summary>
public class MatterMemoryServiceTests
{
    private const string TenantId = "tenant-test-001";
    private const string MatterId = "matter-acme-v-corp";
    private const string DatabaseName = "spaarke-ai";

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Creates a MatterMemoryService wired to a mock CosmosClient.
    /// The mock Container is returned separately for assertion setup.
    /// </summary>
    private static (MatterMemoryService Service, Mock<Container> ContainerMock) CreateSut()
    {
        var containerMock = new Mock<Container>();
        var clientMock = new Mock<CosmosClient>();
        clientMock
            .Setup(c => c.GetContainer(DatabaseName, "memory"))
            .Returns(containerMock.Object);

        var loggerMock = new Mock<ILogger<MatterMemoryService>>();
        var sut = new MatterMemoryService(clientMock.Object, DatabaseName, loggerMock.Object);
        return (sut, containerMock);
    }

    /// <summary>
    /// Creates a mock <see cref="ItemResponse{T}"/> that returns the given resource and ETag.
    /// </summary>
    private static Mock<ItemResponse<MatterMemory>> MockReadResponse(MatterMemory memory, string etag = "\"etag-v1\"")
    {
        var responseMock = new Mock<ItemResponse<MatterMemory>>();
        responseMock.Setup(r => r.Resource).Returns(memory);
        responseMock.Setup(r => r.ETag).Returns(etag);
        return responseMock;
    }

    /// <summary>
    /// Creates a mock <see cref="ItemResponse{T}"/> for upsert (no Resource needed).
    /// </summary>
    private static Mock<ItemResponse<MatterMemory>> MockUpsertResponse()
    {
        var responseMock = new Mock<ItemResponse<MatterMemory>>();
        return responseMock;
    }

    /// <summary>
    /// Builds a <see cref="CosmosException"/> simulating a 404 Not Found.
    /// </summary>
    private static CosmosException NotFoundCosmosException()
        => new("Not Found", HttpStatusCode.NotFound, 0, string.Empty, 0);

    // =========================================================================
    // (a) GetMemoryAsync — null for unknown matter
    // =========================================================================

    [Fact]
    public async Task GetMemoryAsync_ReturnsNull_WhenMatterNotFound()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        containerMock
            .Setup(c => c.ReadItemAsync<MatterMemory>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFoundCosmosException());

        // Act
        var result = await sut.GetMemoryAsync(TenantId, MatterId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMemoryAsync_ReturnsMemory_WhenDocumentExists()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        var existingMemory = new MatterMemory
        {
            Id = MatterMemoryService.BuildDocumentId(TenantId, MatterId),
            TenantId = TenantId,
            MatterId = MatterId,
            Version = 3,
        };
        containerMock
            .Setup(c => c.ReadItemAsync<MatterMemory>(
                existingMemory.Id,
                new PartitionKey(TenantId),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockReadResponse(existingMemory, "\"etag-abc\"").Object);

        // Act
        var result = await sut.GetMemoryAsync(TenantId, MatterId);

        // Assert
        result.Should().NotBeNull();
        result!.TenantId.Should().Be(TenantId);
        result.MatterId.Should().Be(MatterId);
        result.Version.Should().Be(3);
        result.ETag.Should().Be("\"etag-abc\"");
    }

    // =========================================================================
    // (b) SaveFactAsync — persists structured data
    // =========================================================================

    [Fact]
    public async Task SaveFactAsync_CreatesNewDocument_WhenNoExistingMemory()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();

        // GetMemoryAsync (called internally) → not found
        containerMock
            .Setup(c => c.ReadItemAsync<MatterMemory>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFoundCosmosException());

        MatterMemory? capturedItem = null;
        containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<MatterMemory>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<MatterMemory, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, _, _, _) => capturedItem = item)
            .ReturnsAsync(MockUpsertResponse().Object);

        var fact = new MemoryFact
        {
            Type = MemoryFactType.Party,
            Key = "Plaintiff",
            Value = "Company X",
            Source = "user",
            ConfirmedByUser = true,
        };

        // Act
        await sut.SaveFactAsync(TenantId, MatterId, fact);

        // Assert
        capturedItem.Should().NotBeNull();
        capturedItem!.TenantId.Should().Be(TenantId);
        capturedItem.MatterId.Should().Be(MatterId);
        capturedItem.Facts.Should().HaveCount(1);
        capturedItem.Facts[0].Key.Should().Be("Plaintiff");
        capturedItem.Facts[0].Type.Should().Be(MemoryFactType.Party);
        capturedItem.Version.Should().Be(1);
    }

    // =========================================================================
    // (d) Version increments on each SaveFactAsync (optimistic concurrency counter)
    // =========================================================================

    [Fact]
    public async Task SaveFactAsync_IncrementsVersion_OnEachWrite()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        var existingMemory = new MatterMemory
        {
            Id = MatterMemoryService.BuildDocumentId(TenantId, MatterId),
            TenantId = TenantId,
            MatterId = MatterId,
            Version = 5,
            ETag = "\"etag-v5\"",
        };

        containerMock
            .Setup(c => c.ReadItemAsync<MatterMemory>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockReadResponse(existingMemory, "\"etag-v5\"").Object);

        MatterMemory? capturedItem = null;
        ItemRequestOptions? capturedOptions = null;
        containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<MatterMemory>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<MatterMemory, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, _, options, _) => { capturedItem = item; capturedOptions = options; })
            .ReturnsAsync(MockUpsertResponse().Object);

        var fact = new MemoryFact
        {
            Type = MemoryFactType.KeyFact,
            Key = "Contract Value",
            Value = "$2.4M",
            Source = "user",
            ConfirmedByUser = true,
        };

        // Act
        await sut.SaveFactAsync(TenantId, MatterId, fact);

        // Assert: Version incremented
        capturedItem!.Version.Should().Be(6);

        // Assert: ETag was threaded through (optimistic concurrency)
        capturedOptions.Should().NotBeNull();
        capturedOptions!.IfMatchEtag.Should().Be("\"etag-v5\"");
    }

    // =========================================================================
    // (e) ClearMemoryAsync — deletes document; idempotent on 404
    // =========================================================================

    [Fact]
    public async Task ClearMemoryAsync_DeletesDocument_WhenExists()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        containerMock
            .Setup(c => c.DeleteItemAsync<MatterMemory>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockUpsertResponse().Object);

        // Act
        var act = () => sut.ClearMemoryAsync(TenantId, MatterId);

        // Assert: no exception thrown, delete was called
        await act.Should().NotThrowAsync();
        containerMock.Verify(
            c => c.DeleteItemAsync<MatterMemory>(
                MatterMemoryService.BuildDocumentId(TenantId, MatterId),
                new PartitionKey(TenantId),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ClearMemoryAsync_IsIdempotent_WhenDocumentNotFound()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        containerMock
            .Setup(c => c.DeleteItemAsync<MatterMemory>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFoundCosmosException());

        // Act
        var act = () => sut.ClearMemoryAsync(TenantId, MatterId);

        // Assert: 404 is swallowed — idempotent delete
        await act.Should().NotThrowAsync();
    }

    // =========================================================================
    // (c) ToSystemPromptFragmentAsync — 200–500 tokens for a typical payload
    // =========================================================================

    [Fact]
    public async Task ToSystemPromptFragmentAsync_ReturnsEmpty_WhenNoMemory()
    {
        // Arrange
        var (sut, containerMock) = CreateSut();
        containerMock
            .Setup(c => c.ReadItemAsync<MatterMemory>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFoundCosmosException());

        // Act
        var result = await sut.ToSystemPromptFragmentAsync(TenantId, MatterId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildPromptFragment_TypicalPayload_IsWithinTokenBudget()
    {
        // Arrange — a realistic matter with 3 parties, 5 dates, 2 analyses, 5 facts.
        var facts = new List<MemoryFact>
        {
            new() { Type = MemoryFactType.Party, Key = "Plaintiff", Value = "Company X", ConfirmedByUser = true },
            new() { Type = MemoryFactType.Party, Key = "Defendant", Value = "Company Y", ConfirmedByUser = true },
            new() { Type = MemoryFactType.Party, Key = "Plaintiff Counsel", Value = "Smith & Partners LLP", ConfirmedByUser = true },

            new() { Type = MemoryFactType.KeyDate, Key = "Markman Hearing", Value = "July 15, 2026", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Trial Start", Value = "January 12, 2027", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Summary Judgment Deadline", Value = "October 1, 2026", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Fact Discovery Close", Value = "August 31, 2026", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Expert Disclosure", Value = "September 15, 2026", ConfirmedByUser = true },

            new() { Type = MemoryFactType.PriorAnalysis, Key = "Session 2026-05-12", Value = "3 weak claims identified; claim 4 upheld", ConfirmedByUser = true },
            new() { Type = MemoryFactType.PriorAnalysis, Key = "Session 2026-05-14", Value = "Prior art search complete; 2 references found", ConfirmedByUser = true },

            new() { Type = MemoryFactType.KeyFact, Key = "Contract Value", Value = "$2.4M", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyFact, Key = "Contract Term", Value = "3 years", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyFact, Key = "Governing Law", Value = "California", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyFact, Key = "Jurisdiction", Value = "NDCA", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyFact, Key = "Patent Count", Value = "4 asserted patents", ConfirmedByUser = true },
        };

        // Act
        var fragment = MatterMemoryService.BuildPromptFragment(facts);

        // Assert: fragment is non-empty and contains expected section headings
        fragment.Should().NotBeEmpty();
        fragment.Should().Contain("### Matter Context (from prior sessions)");
        fragment.Should().Contain("**Parties**:");
        fragment.Should().Contain("**Key Dates**:");
        fragment.Should().Contain("**Prior Analyses**:");
        fragment.Should().Contain("**Key Facts**:");

        // Token estimate: chars / 1.3 chars-per-token (same as service).
        // Target: 200–500 tokens.
        var estimatedTokens = fragment.Length / 1.3;
        estimatedTokens.Should().BeGreaterThan(200,
            because: "a typical matter with 15 facts should produce at least 200 estimated tokens");
        estimatedTokens.Should().BeLessThanOrEqualTo(500,
            because: "the fragment must stay within the 500-token budget for system prompt injection");
    }

    [Fact]
    public void BuildPromptFragment_IncludesAllFacts_WhenUnderBudget()
    {
        // Arrange — small payload guaranteed to fit within 500 tokens.
        var facts = new List<MemoryFact>
        {
            new() { Type = MemoryFactType.Party, Key = "Plaintiff", Value = "Acme Corp", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Trial", Value = "March 1, 2027", ConfirmedByUser = true },
        };

        // Act
        var fragment = MatterMemoryService.BuildPromptFragment(facts);

        // Assert: both facts appear verbatim
        fragment.Should().Contain("Acme Corp");
        fragment.Should().Contain("March 1, 2027");
    }

    // =========================================================================
    // (f) BuildPromptFragment — groups facts under correct headings
    // =========================================================================

    [Fact]
    public void BuildPromptFragment_GroupsFactsByType_UnderCorrectHeadings()
    {
        // Arrange
        var facts = new List<MemoryFact>
        {
            new() { Type = MemoryFactType.Party, Key = "Plaintiff", Value = "Acme", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyDate, Key = "Hearing", Value = "June 1", ConfirmedByUser = true },
            new() { Type = MemoryFactType.PriorAnalysis, Key = "Session A", Value = "2 claims weak", ConfirmedByUser = true },
            new() { Type = MemoryFactType.KeyFact, Key = "Jurisdiction", Value = "SDNY", ConfirmedByUser = true },
        };

        // Act
        var fragment = MatterMemoryService.BuildPromptFragment(facts);

        // Assert: each section heading appears exactly once in correct order
        var partiesIndex = fragment.IndexOf("**Parties**:", StringComparison.Ordinal);
        var datesIndex = fragment.IndexOf("**Key Dates**:", StringComparison.Ordinal);
        var analysesIndex = fragment.IndexOf("**Prior Analyses**:", StringComparison.Ordinal);
        var factsIndex = fragment.IndexOf("**Key Facts**:", StringComparison.Ordinal);

        partiesIndex.Should().BeGreaterThan(-1);
        datesIndex.Should().BeGreaterThan(partiesIndex);
        analysesIndex.Should().BeGreaterThan(datesIndex);
        factsIndex.Should().BeGreaterThan(analysesIndex);

        // Content under correct headings
        fragment.Should().Contain("Plaintiff — Acme");
        fragment.Should().Contain("Hearing — June 1");
        fragment.Should().Contain("Session A — 2 claims weak");
        fragment.Should().Contain("Jurisdiction — SDNY");
    }

    // =========================================================================
    // (g) BuildPromptFragment — excludes low-confidence unconfirmed facts
    // =========================================================================

    [Fact]
    public void BuildPromptFragment_ExcludesLowConfidenceUnconfirmedFacts()
    {
        // Arrange
        var facts = new List<MemoryFact>
        {
            // High confidence, unconfirmed — INCLUDED (above 0.7 threshold)
            new() { Type = MemoryFactType.KeyFact, Key = "Contract Value", Value = "$2.4M", ConfirmedByUser = false, Confidence = 0.9 },
            // Low confidence, unconfirmed — EXCLUDED
            new() { Type = MemoryFactType.KeyFact, Key = "Secret Fact", Value = "should not appear", ConfirmedByUser = false, Confidence = 0.5 },
            // Confirmed by user — INCLUDED regardless of confidence
            new() { Type = MemoryFactType.Party, Key = "Plaintiff", Value = "Company X", ConfirmedByUser = true, Confidence = 0.3 },
        };

        // Act
        var fragment = MatterMemoryService.BuildPromptFragment(facts);

        // Assert
        fragment.Should().Contain("Contract Value — $2.4M");
        fragment.Should().Contain("Plaintiff — Company X");
        fragment.Should().NotContain("should not appear");
    }

    [Fact]
    public void BuildPromptFragment_ReturnsEmpty_WhenAllFactsBelowThreshold()
    {
        // Arrange: all facts are unconfirmed and below threshold
        var facts = new List<MemoryFact>
        {
            new() { Type = MemoryFactType.KeyFact, Key = "Guess", Value = "not sure", ConfirmedByUser = false, Confidence = 0.2 },
        };

        // Act
        var fragment = MatterMemoryService.BuildPromptFragment(facts);

        // Assert
        fragment.Should().BeEmpty();
    }

    // =========================================================================
    // (h) BuildDocumentId format
    // =========================================================================

    [Fact]
    public void BuildDocumentId_ReturnsExpectedFormat()
    {
        var id = MatterMemoryService.BuildDocumentId("tenant-123", "matter-xyz");
        id.Should().Be("tenant-123_matter-xyz");
    }

    // =========================================================================
    // Argument validation
    // =========================================================================

    [Theory]
    [InlineData(null, "matter-1")]
    [InlineData("", "matter-1")]
    [InlineData("tenant-1", null)]
    [InlineData("tenant-1", "")]
    public async Task GetMemoryAsync_ThrowsArgumentException_WhenArgumentsInvalid(
        string? tenantId, string? matterId)
    {
        var (sut, _) = CreateSut();
        var act = () => sut.GetMemoryAsync(tenantId!, matterId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null, "matter-1")]
    [InlineData("", "matter-1")]
    [InlineData("tenant-1", null)]
    [InlineData("tenant-1", "")]
    public async Task ClearMemoryAsync_ThrowsArgumentException_WhenArgumentsInvalid(
        string? tenantId, string? matterId)
    {
        var (sut, _) = CreateSut();
        var act = () => sut.ClearMemoryAsync(tenantId!, matterId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
