using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Services.Ai.Telemetry;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Sessions;

/// <summary>
/// Unit tests for <see cref="SessionPersistenceService.ListRecentSessionsAsync"/>'s FR-D7
/// projection extension (spaarkeai-assistant-enhancements-r2, DI-01).
///
/// Verifies the mapping from the Cosmos <c>RecentSessionProjection</c> shape to
/// <see cref="RecentSessionInfo"/> for the three new fields the History dropdown
/// (<c>HistoryOverlay.tsx</c>, task 037) reads: <c>Preview</c> (conversationSummary ??
/// firstMessage, single-line, bounded), <c>MessageCount</c> (pass-through of
/// <c>ARRAY_LENGTH(c.messages)</c>), and <c>TabSummary</c> (joined tab display names, empty →
/// null). Existing title/entity mapping behaviour is covered by
/// <see cref="SessionPersistenceServiceTests"/> and is not re-asserted here.
///
/// Patterns mirror <see cref="SessionPersistenceServiceTabsTests"/> — same fixture wiring,
/// same Moq + FluentAssertions stack, same Cosmos <c>GetItemQueryIterator</c> mocking approach
/// (see <see cref="MockFeedIterator{T}"/>, mirrored from <c>MemoryItemStoreTests</c>).
/// </summary>
public class SessionPersistenceServiceHistoryProjectionTests
{
    private const string TenantId = "tenant-history";
    private const string DatabaseName = "spaarke-ai";
    private const string CosmosEndpoint = "https://spaarke-cosmos-dev.documents.azure.com:443/";

    private readonly TrackingTenantCache _cache;
    private readonly Mock<CosmosClient> _cosmosClientMock;
    private readonly Mock<Container> _containerMock;
    private readonly Mock<ILogger<SessionPersistenceService>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly SessionPersistenceService _sut;

    public SessionPersistenceServiceHistoryProjectionTests()
    {
        _cache = new TrackingTenantCache();
        _cosmosClientMock = new Mock<CosmosClient>();
        _containerMock = new Mock<Container>();
        _loggerMock = new Mock<ILogger<SessionPersistenceService>>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CosmosPersistence:Endpoint"] = CosmosEndpoint,
                ["CosmosPersistence:DatabaseName"] = DatabaseName
            })
            .Build();

        _cosmosClientMock
            .Setup(c => c.GetContainer(DatabaseName, "sessions"))
            .Returns(_containerMock.Object);

        _sut = new SessionPersistenceService(
            _cache,
            _cosmosClientMock.Object,
            _configuration,
            _loggerMock.Object,
            // chat-routing-redesign-r1 task 074 — IContextEventEmitter dep added for
            // context.upload_persisted emission. ListRecentSessionsAsync does not emit; Loose mock suffices.
            new Mock<IContextEventEmitter>().Object);
    }

    // =========================================================================
    // Preview — conversationSummary ?? firstMessage, single-line, bounded, null when absent
    // =========================================================================

    [Fact]
    public async Task ListRecentSessionsAsync_PreviewPrefersConversationSummary_WhenBothPresent()
    {
        ArrangeProjection(BuildProjection(
            conversationSummary: "Reviewed the NDA redlines",
            firstMessage: "Can you review this NDA?"));

        var results = await _sut.ListRecentSessionsAsync(TenantId, TestSessionOwner.Oid, limit: 10);

        results.Should().HaveCount(1);
        results[0].Preview.Should().Be("Reviewed the NDA redlines");
    }

    [Fact]
    public async Task ListRecentSessionsAsync_PreviewFallsBackToFirstMessage_WhenConversationSummaryAbsent()
    {
        ArrangeProjection(BuildProjection(
            conversationSummary: null,
            firstMessage: "Can you review this NDA?"));

        var results = await _sut.ListRecentSessionsAsync(TenantId, TestSessionOwner.Oid, limit: 10);

        results[0].Preview.Should().Be("Can you review this NDA?");
    }

    [Fact]
    public async Task ListRecentSessionsAsync_PreviewIsNull_WhenNeitherSummaryNorFirstMessagePresent()
    {
        // Unlike BuildSessionTitle (which falls back to a "Conversation · <timestamp>" placeholder),
        // Preview has no placeholder fallback — the client omits the preview line entirely when absent.
        ArrangeProjection(BuildProjection(conversationSummary: null, firstMessage: null));

        var results = await _sut.ListRecentSessionsAsync(TenantId, TestSessionOwner.Oid, limit: 10);

        results[0].Preview.Should().BeNull();
    }

    [Fact]
    public async Task ListRecentSessionsAsync_PreviewIsSingleLineAndTruncated_WhenSourceIsLongAndMultiline()
    {
        var longMultilineSummary = "Line one of the summary.\r\nLine two continues the discussion about the matter, " +
            "covering scope, fees, and the proposed timeline for the engagement in considerable detail.";

        ArrangeProjection(BuildProjection(conversationSummary: longMultilineSummary, firstMessage: null));

        var results = await _sut.ListRecentSessionsAsync(TenantId, TestSessionOwner.Oid, limit: 10);

        var preview = results[0].Preview;
        preview.Should().NotBeNull();
        preview.Should().NotContain("\n").And.NotContain("\r");
        preview!.Length.Should().BeLessOrEqualTo(140);
        preview.Should().EndWith("…", "the source text exceeds the 140-char bound and must be marked truncated");
    }

    // =========================================================================
    // MessageCount — pass-through of ARRAY_LENGTH(c.messages)
    // =========================================================================

    [Fact]
    public async Task ListRecentSessionsAsync_ProjectsMessageCount_FromArrayLength()
    {
        ArrangeProjection(BuildProjection(messageCount: 7));

        var results = await _sut.ListRecentSessionsAsync(TenantId, TestSessionOwner.Oid, limit: 10);

        results[0].MessageCount.Should().Be(7);
    }

    [Fact]
    public async Task ListRecentSessionsAsync_MessageCountIsNull_WhenProjectionOmitsIt()
    {
        ArrangeProjection(BuildProjection(messageCount: null));

        var results = await _sut.ListRecentSessionsAsync(TenantId, TestSessionOwner.Oid, limit: 10);

        results[0].MessageCount.Should().BeNull();
    }

    // =========================================================================
    // TabSummary — joined display names, empty/null tabs → null
    // =========================================================================

    [Fact]
    public async Task ListRecentSessionsAsync_JoinsTabDisplayNames_WithMiddleDotSeparator()
    {
        ArrangeProjection(BuildProjection(tabs:
        [
            new StoredWorkspaceTab("tab-1", "email", null, "Email"),
            new StoredWorkspaceTab("tab-2", "compose", null, "Compose")
        ]));

        var results = await _sut.ListRecentSessionsAsync(TenantId, TestSessionOwner.Oid, limit: 10);

        results[0].TabSummary.Should().Be("Email · Compose");
    }

    [Fact]
    public async Task ListRecentSessionsAsync_TabSummaryIsNull_WhenTabsIsEmpty()
    {
        ArrangeProjection(BuildProjection(tabs: []));

        var results = await _sut.ListRecentSessionsAsync(TenantId, TestSessionOwner.Oid, limit: 10);

        results[0].TabSummary.Should().BeNull();
    }

    [Fact]
    public async Task ListRecentSessionsAsync_TabSummaryIsNull_WhenTabsIsAbsent()
    {
        ArrangeProjection(BuildProjection(tabs: null));

        var results = await _sut.ListRecentSessionsAsync(TenantId, TestSessionOwner.Oid, limit: 10);

        results[0].TabSummary.Should().BeNull();
    }

    // =========================================================================
    // Fixtures
    // =========================================================================

    private static SessionPersistenceService.RecentSessionProjection BuildProjection(
        string? conversationSummary = "Default summary",
        string? firstMessage = "Default first message",
        int? messageCount = 3,
        List<StoredWorkspaceTab>? tabs = null) =>
        new()
        {
            Id = "sess-1",
            SessionId = "sess-1",
            LastActivity = DateTimeOffset.UtcNow,
            ConversationSummary = conversationSummary,
            FirstMessage = firstMessage,
            Title = "Existing title",
            EntityRefs = null,
            MessageCount = messageCount,
            Tabs = tabs
        };

    /// <summary>Arranges the Cosmos container mock to return exactly one page containing <paramref name="projection"/>.</summary>
    private void ArrangeProjection(SessionPersistenceService.RecentSessionProjection projection)
    {
        _containerMock
            .Setup(c => c.GetItemQueryIterator<SessionPersistenceService.RecentSessionProjection>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(MockFeedIterator(new[] { projection }).Object);
    }

    private static Mock<FeedIterator<T>> MockFeedIterator<T>(IEnumerable<T> items)
    {
        var iterator = new Mock<FeedIterator<T>>();
        var calls = 0;
        iterator.SetupGet(i => i.HasMoreResults).Returns(() => calls == 0);

        var feedResponse = new Mock<FeedResponse<T>>();
        feedResponse.Setup(r => r.GetEnumerator()).Returns(items.GetEnumerator());

        iterator
            .Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedResponse.Object)
            .Callback(() => calls++);

        return iterator;
    }
}
