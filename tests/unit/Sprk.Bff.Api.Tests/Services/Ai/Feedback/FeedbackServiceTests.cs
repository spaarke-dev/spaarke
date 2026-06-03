using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Feedback;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Feedback;

/// <summary>
/// Unit tests for <see cref="FeedbackService"/> (AIPU2-036).
///
/// Coverage:
/// (a) SubmitAsync stores an entry with the correct fields and returns its id.
/// (b) SubmitAsync truncates comments longer than 500 characters before write.
/// (c) GetAggregateByPlaybookAsync returns correct thumbs-up/down counts and satisfaction rate.
/// (d) GetAggregateByCapabilityAsync returns correct thumbs-up/down counts and satisfaction rate.
/// (e) GetAggregateByPlaybookAsync returns null when no feedback exists (empty counts).
/// </summary>
[Trait("status", "repaired")]
public class FeedbackServiceTests
{
    private const string TenantId = "tenant-test-001";
    private const string UserId = "user-test-001";
    private const string SessionId = "session-abc";
    private const string PlaybookId = "playbook-xyz";
    private const string CapabilityId = "capability-summarise";
    private const string DatabaseName = "spaarke-ai";

    private readonly Mock<CosmosClient> _cosmosClientMock;
    private readonly Mock<Container> _containerMock;
    private readonly Mock<ILogger<FeedbackService>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly FeedbackService _sut;

    public FeedbackServiceTests()
    {
        _cosmosClientMock = new Mock<CosmosClient>();
        _containerMock = new Mock<Container>();
        _loggerMock = new Mock<ILogger<FeedbackService>>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CosmosPersistence:DatabaseName"] = DatabaseName
            })
            .Build();

        _cosmosClientMock
            .Setup(c => c.GetContainer(DatabaseName, "feedback"))
            .Returns(_containerMock.Object);

        _sut = new FeedbackService(
            _cosmosClientMock.Object,
            _configuration,
            _loggerMock.Object);
    }

    // =========================================================================
    // (a) SubmitAsync stores entry with correct fields
    // =========================================================================

    [Fact]
    public async Task SubmitAsync_StoresEntry_WithCorrectFields()
    {
        // Arrange
        var entry = BuildEntry(rating: FeedbackRating.ThumbsUp, comment: "Great answer!");

        FeedbackEntry? captured = null;
        _containerMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<FeedbackEntry>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FeedbackEntry, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, _, _, _) => captured = item)
            .ReturnsAsync(CreateMockResponse(entry));

        // Act
        var returnedId = await _sut.SubmitAsync(TenantId, entry);

        // Assert — Cosmos CreateItemAsync was called exactly once
        _containerMock.Verify(
            c => c.CreateItemAsync(
                It.IsAny<FeedbackEntry>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(TenantId);
        captured.UserId.Should().Be(UserId);
        captured.SessionId.Should().Be(SessionId);
        captured.TurnIndex.Should().Be(0);
        captured.Rating.Should().Be(FeedbackRating.ThumbsUp);
        captured.Comment.Should().Be("Great answer!");
        captured.PlaybookId.Should().Be(PlaybookId);

        returnedId.Should().Be(captured.Id);
    }

    // =========================================================================
    // (b) SubmitAsync truncates comments > 500 chars
    // =========================================================================

    [Fact]
    public async Task SubmitAsync_TruncatesComment_WhenExceeds500Chars()
    {
        // Arrange — comment that is 600 characters long
        var longComment = new string('x', 600);
        var entry = BuildEntry(rating: FeedbackRating.ThumbsDown, comment: longComment);

        FeedbackEntry? captured = null;
        _containerMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<FeedbackEntry>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FeedbackEntry, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, _, _, _) => captured = item)
            .ReturnsAsync(CreateMockResponse(entry));

        // Act
        await _sut.SubmitAsync(TenantId, entry);

        // Assert — stored comment is exactly 500 chars
        captured.Should().NotBeNull();
        captured!.Comment.Should().NotBeNull();
        captured.Comment!.Length.Should().Be(500);
        captured.Comment.Should().Be(new string('x', 500));
    }

    [Fact]
    public async Task SubmitAsync_DoesNotTruncate_WhenCommentExactly500Chars()
    {
        // Arrange — comment exactly 500 characters
        var exactComment = new string('y', 500);
        var entry = BuildEntry(rating: FeedbackRating.ThumbsUp, comment: exactComment);

        FeedbackEntry? captured = null;
        _containerMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<FeedbackEntry>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FeedbackEntry, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, _, _, _) => captured = item)
            .ReturnsAsync(CreateMockResponse(entry));

        // Act
        await _sut.SubmitAsync(TenantId, entry);

        // Assert — stored comment is unchanged (no truncation)
        captured!.Comment.Should().Be(exactComment);
    }

    // =========================================================================
    // (c) GetAggregateByPlaybookAsync — correct counts and satisfaction rate
    // =========================================================================

    [Fact]
    public async Task GetAggregateByPlaybookAsync_ReturnsCorrectCounts_AndSatisfactionRate()
    {
        // Arrange — 3 thumbs-up, 1 thumbs-down → satisfactionRate = 75%
        SetupCountQuery(thumbsUp: 3, thumbsDown: 1, negativeComments: ["Response was too vague."]);

        // Act
        var result = await _sut.GetAggregateByPlaybookAsync(TenantId, PlaybookId);

        // Assert
        result.Should().NotBeNull();
        result!.EntityId.Should().Be(PlaybookId);
        result.EntityType.Should().Be("playbook");
        result.TotalCount.Should().Be(4);
        result.ThumbsUpCount.Should().Be(3);
        result.ThumbsDownCount.Should().Be(1);
        result.SatisfactionRate.Should().BeApproximately(75.0, 0.01);
        result.TopNegativeComments.Should().ContainSingle()
            .Which.Should().Be("Response was too vague.");
    }

    // =========================================================================
    // (d) GetAggregateByCapabilityAsync — correct counts and satisfaction rate
    // =========================================================================

    [Fact]
    public async Task GetAggregateByCapabilityAsync_ReturnsCorrectCounts_AndSatisfactionRate()
    {
        // Arrange — 7 thumbs-up, 3 thumbs-down → satisfactionRate = 70%
        SetupCountQuery(thumbsUp: 7, thumbsDown: 3, negativeComments: []);

        // Act
        var result = await _sut.GetAggregateByCapabilityAsync(TenantId, CapabilityId);

        // Assert
        result.Should().NotBeNull();
        result!.EntityId.Should().Be(CapabilityId);
        result.EntityType.Should().Be("capability");
        result.TotalCount.Should().Be(10);
        result.ThumbsUpCount.Should().Be(7);
        result.ThumbsDownCount.Should().Be(3);
        result.SatisfactionRate.Should().BeApproximately(70.0, 0.01);
    }

    // =========================================================================
    // (e) GetAggregateByPlaybookAsync returns null when no feedback exists
    // =========================================================================

    [Fact]
    public async Task GetAggregateByPlaybookAsync_ReturnsNull_WhenNoFeedbackExists()
    {
        // Arrange — Cosmos returns 0 for both rating queries
        SetupCountQuery(thumbsUp: 0, thumbsDown: 0, negativeComments: []);

        // Act
        var result = await _sut.GetAggregateByPlaybookAsync(TenantId, PlaybookId);

        // Assert — null signals "no data" to the endpoint (maps to 404)
        result.Should().BeNull();
    }

    // =========================================================================
    // Additional edge cases
    // =========================================================================

    [Fact]
    public async Task GetAggregateByCapabilityAsync_ReturnsNull_WhenNoFeedbackExists()
    {
        SetupCountQuery(thumbsUp: 0, thumbsDown: 0, negativeComments: []);

        var result = await _sut.GetAggregateByCapabilityAsync(TenantId, CapabilityId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_StoresNullComment_WhenCommentIsNull()
    {
        // Arrange
        var entry = BuildEntry(rating: FeedbackRating.ThumbsUp, comment: null);

        FeedbackEntry? captured = null;
        _containerMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<FeedbackEntry>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FeedbackEntry, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, _, _, _) => captured = item)
            .ReturnsAsync(CreateMockResponse(entry));

        // Act
        await _sut.SubmitAsync(TenantId, entry);

        // Assert — null is preserved (no truncation applied to null)
        captured!.Comment.Should().BeNull();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static FeedbackEntry BuildEntry(FeedbackRating rating, string? comment) =>
        new()
        {
            TenantId = TenantId,
            UserId = UserId,
            SessionId = SessionId,
            TurnIndex = 0,
            Rating = rating,
            Comment = comment,
            PlaybookId = PlaybookId,
            CapabilityId = CapabilityId
        };

    /// <summary>
    /// Configures <see cref="_containerMock"/> to return the given thumbs-up count for
    /// <see cref="FeedbackRating.ThumbsUp"/>, thumbs-down count for
    /// <see cref="FeedbackRating.ThumbsDown"/>, and a list of negative comments.
    ///
    /// The FeedbackService issues GetItemQueryIterator twice (once per rating) then once
    /// more for negative comments when thumbsDown > 0. This setup chains the mock responses
    /// in the order the service calls them.
    /// </summary>
    private void SetupCountQuery(int thumbsUp, int thumbsDown, IReadOnlyList<string> negativeComments)
    {
        // thumbs-up iterator
        var thumbsUpIterator = CreateCountIterator(thumbsUp);
        // thumbs-down iterator
        var thumbsDownIterator = CreateCountIterator(thumbsDown);
        // negative comments iterator (only queried when thumbsDown > 0)
        var commentsIterator = CreateStringIterator(negativeComments);

        // The service calls GetItemQueryIterator<int> twice (thumbs-up then thumbs-down)
        // then GetItemQueryIterator<string> once for comments.
        var intCallCount = 0;
        _containerMock
            .Setup(c => c.GetItemQueryIterator<int>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string?>(),
                It.IsAny<QueryRequestOptions?>()))
            .Returns(() =>
            {
                intCallCount++;
                return intCallCount == 1 ? thumbsUpIterator : thumbsDownIterator;
            });

        _containerMock
            .Setup(c => c.GetItemQueryIterator<string>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string?>(),
                It.IsAny<QueryRequestOptions?>()))
            .Returns(commentsIterator);
    }

    private static FeedIterator<int> CreateCountIterator(int value)
    {
        var page = MockFeedResponse(new[] { value });
        var mock = new Mock<FeedIterator<int>>();
        var called = false;
        mock.SetupGet(i => i.HasMoreResults).Returns(() => !called);
        mock.Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { called = true; return page; });
        return mock.Object;
    }

    private static FeedIterator<string> CreateStringIterator(IReadOnlyList<string> values)
    {
        var page = MockFeedResponse(values.ToArray());
        var mock = new Mock<FeedIterator<string>>();
        var called = false;
        mock.SetupGet(i => i.HasMoreResults).Returns(() => !called);
        mock.Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { called = true; return page; });
        return mock.Object;
    }

    private static FeedResponse<T> MockFeedResponse<T>(T[] items)
    {
        // NOTE: Moq cannot stub extension methods (e.g. Enumerable.FirstOrDefault).
        // FeedbackService consumes FeedResponse<T> via foreach (GetEnumerator) and
        // via the FirstOrDefault() extension which itself enumerates GetEnumerator —
        // so a correctly-mocked GetEnumerator is sufficient. Resource is also set
        // for any direct accessor reads.
        var mock = new Mock<FeedResponse<T>>();
        mock.Setup(r => r.Resource).Returns(items);
        mock.Setup(r => r.GetEnumerator()).Returns(((IEnumerable<T>)items).GetEnumerator());
        return mock.Object;
    }

    private static ItemResponse<FeedbackEntry> CreateMockResponse(FeedbackEntry entry)
    {
        var mock = new Mock<ItemResponse<FeedbackEntry>>();
        mock.Setup(r => r.Resource).Returns(entry);
        return mock.Object;
    }
}
