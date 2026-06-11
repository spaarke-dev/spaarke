using System.Net;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Memory;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Unit tests for <see cref="PinnedContextRepository"/> (R6 Pillar 7 / task 065).
/// </summary>
/// <remarks>
/// <para>
/// Verifies (a) Create-then-query (by user / by matter), (b) tenant isolation,
/// (c) Delete is idempotent on missing pin, (d) Delete of one pin leaves others intact,
/// (e) content-length cap enforcement (Cosmos write path).
/// </para>
/// <para>
/// Mocks the Cosmos <see cref="Container"/> directly via the internal test constructor —
/// pattern mirrors <see cref="MatterMemoryServiceTests"/>.
/// </para>
/// </remarks>
public sealed class PinnedContextRepositoryTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string UserA = "user-alice";
    private const string MatterX = "matter-x";

    private static (PinnedContextRepository Sut, Mock<Container> ContainerMock) CreateSut()
    {
        var containerMock = new Mock<Container>();
        var loggerMock = new Mock<ILogger<PinnedContextRepository>>();
        var sut = new PinnedContextRepository(containerMock.Object, loggerMock.Object);
        return (sut, containerMock);
    }

    private static PinnedContextItem BuildPin(
        string pinId,
        string tenantId,
        string userId,
        string? matterId = null,
        PinType pinType = PinType.UserPreference,
        string? content = null,
        string? title = null)
    {
        return new PinnedContextItem
        {
            Id = PinnedContextRepository.BuildDocumentId(tenantId, pinId),
            DocumentType = PinnedContextRepository.DocumentTypeValue,
            TenantId = tenantId,
            UserId = userId,
            PinType = pinType,
            Title = title ?? "Test pin",
            Content = content ?? "Test content.",
            MatterId = matterId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };
    }

    private static Mock<FeedIterator<PinnedContextItem>> MockFeedIterator(IEnumerable<PinnedContextItem> items)
    {
        var iterator = new Mock<FeedIterator<PinnedContextItem>>();
        var calls = 0;
        iterator.SetupGet(i => i.HasMoreResults).Returns(() => calls == 0);

        var feedResponse = new Mock<FeedResponse<PinnedContextItem>>();
        feedResponse.Setup(r => r.GetEnumerator()).Returns(items.GetEnumerator());

        iterator
            .Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedResponse.Object)
            .Callback(() => calls++);
        return iterator;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Create + Get roundtrip
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateAsync_PersistsToCosmos_WithCorrectPartitionKey()
    {
        var (sut, container) = CreateSut();
        var pin = BuildPin("pin-1", TenantA, UserA);

        PartitionKey? capturedPk = null;
        PinnedContextItem? capturedItem = null;
        container
            .Setup(c => c.CreateItemAsync(
                It.IsAny<PinnedContextItem>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<PinnedContextItem, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, pk, _, _) => { capturedItem = item; capturedPk = pk; })
            .ReturnsAsync(Mock.Of<ItemResponse<PinnedContextItem>>());

        await sut.CreateAsync(pin);

        capturedItem.Should().NotBeNull();
        capturedItem!.Id.Should().StartWith(PinnedContextRepository.IdPrefix + "_",
            because: "documentType discriminator is encoded as the id prefix per CLAUDE.md §10");
        capturedPk.Should().NotBeNull();
        capturedPk.Should().Be(new PartitionKey(TenantA));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GetByMatter filter
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetByMatterAsync_ReturnsMatchingPins()
    {
        var (sut, container) = CreateSut();
        var pinForMatterX = BuildPin("pin-x", TenantA, UserA, matterId: MatterX, pinType: PinType.MatterFact);

        container
            .Setup(c => c.GetItemQueryIterator<PinnedContextItem>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string>(),
                It.IsAny<QueryRequestOptions>()))
            .Returns(MockFeedIterator(new[] { pinForMatterX }).Object);

        var result = await sut.GetByMatterAsync(TenantA, MatterX);

        result.Should().HaveCount(1);
        result[0].MatterId.Should().Be(MatterX);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GetByUser filter
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetByUserAsync_ReturnsAllPinsForUser()
    {
        var (sut, container) = CreateSut();
        var pin1 = BuildPin("p1", TenantA, UserA);
        var pin2 = BuildPin("p2", TenantA, UserA, pinType: PinType.SystemRule);

        container
            .Setup(c => c.GetItemQueryIterator<PinnedContextItem>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string>(),
                It.IsAny<QueryRequestOptions>()))
            .Returns(MockFeedIterator(new[] { pin1, pin2 }).Object);

        var result = await sut.GetByUserAsync(TenantA, UserA);

        result.Should().HaveCount(2);
        result.Select(p => p.UserId).Should().AllBe(UserA);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Tenant isolation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetByUserAsync_UsesPartitionKey_ForTenantIsolation()
    {
        var (sut, container) = CreateSut();

        QueryRequestOptions? capturedOpts = null;
        container
            .Setup(c => c.GetItemQueryIterator<PinnedContextItem>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string>(),
                It.IsAny<QueryRequestOptions>()))
            .Callback<QueryDefinition, string, QueryRequestOptions>((_, _, opts) => capturedOpts = opts)
            .Returns(MockFeedIterator(Array.Empty<PinnedContextItem>()).Object);

        await sut.GetByUserAsync(TenantB, UserA);

        capturedOpts.Should().NotBeNull();
        capturedOpts!.PartitionKey.Should().Be(new PartitionKey(TenantB),
            because: "ADR-014 + NFR-16: queries MUST be scoped to the caller's tenant partition");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Delete (idempotent + isolation)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteAsync_IsIdempotent_WhenPinAbsent()
    {
        var (sut, container) = CreateSut();
        container
            .Setup(c => c.DeleteItemAsync<PinnedContextItem>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not Found", HttpStatusCode.NotFound, 0, string.Empty, 0));

        // Act
        var act = async () => await sut.DeleteAsync(TenantA, "pin-missing");

        // Assert — no exception escapes the repository (stale-handle race protection).
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_TargetsCorrectId_LeavingOthersIntact()
    {
        var (sut, container) = CreateSut();
        string? deletedId = null;
        PartitionKey? deletedPk = null;
        container
            .Setup(c => c.DeleteItemAsync<PinnedContextItem>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PartitionKey, ItemRequestOptions?, CancellationToken>(
                (id, pk, _, _) => { deletedId = id; deletedPk = pk; })
            .ReturnsAsync(Mock.Of<ItemResponse<PinnedContextItem>>());

        await sut.DeleteAsync(TenantA, "pin-1");

        // Only this id targeted — id is deterministic and includes the partition tenant,
        // so other pins (different id) and other tenants (different partition) are
        // untouched.
        deletedId.Should().Be(PinnedContextRepository.BuildDocumentId(TenantA, "pin-1"));
        deletedPk.Should().Be(new PartitionKey(TenantA));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Content length cap enforcement
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateAsync_Rejects_WhenContentExceedsCap()
    {
        var (sut, _) = CreateSut();
        var oversized = new string('x', PinnedContextRepository.MaxContentLength + 1);
        var pin = BuildPin("pin-big", TenantA, UserA, content: oversized);

        var act = async () => await sut.CreateAsync(pin);

        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains("Content length", StringComparison.OrdinalIgnoreCase));
    }
}
