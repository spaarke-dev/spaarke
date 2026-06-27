using System.Net;
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
/// Unit tests for <see cref="SessionPersistenceService"/>.
///
/// Verifies:
/// - Happy-path dual-write: both Redis and Cosmos DB are written on <see cref="SessionPersistenceService.PersistMessageAsync"/>.
/// - Redis failure isolation: Cosmos DB is still written when Redis throws.
/// - Cosmos failure isolation: Redis is still written when Cosmos DB throws.
/// - Both failures: no exception is thrown; only Warning logs are emitted.
/// - LoadSessionAsync: Redis HIT returns cached session, no Cosmos call.
/// - LoadSessionAsync: Redis MISS falls back to Cosmos DB and re-warms Redis.
/// - DeleteSessionAsync: removes from both stores; partial failures are non-fatal.
/// - Cosmos partition key is always /tenantId (ADR-015 Tier 3).
/// </summary>
public class SessionPersistenceServiceTests
{
    private const string TenantId = "tenant-xyz";
    private const string SessionId = "session-abc";
    private const string DatabaseName = "spaarke-ai";
    private const string CosmosEndpoint = "https://spaarke-cosmos-dev.documents.azure.com:443/";

    private readonly TrackingTenantCache _cache;
    private readonly Mock<CosmosClient> _cosmosClientMock;
    private readonly Mock<Container> _containerMock;
    private readonly Mock<ILogger<SessionPersistenceService>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly SessionPersistenceService _sut;

    public SessionPersistenceServiceTests()
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
            // context.upload_persisted emission. Tests in this file do not exercise
            // UpdateUploadedFilesAsync, so a Loose mock suffices.
            new Mock<IContextEventEmitter>().Object);
    }

    // =========================================================================
    // Redis key schema
    // =========================================================================

    [Fact]
    public void BuildRedisKey_ReturnsExpectedPattern()
    {
        var key = SessionPersistenceService.BuildRedisKey("tenant-a", "sess-1");
        // FR-05 on-wire format
        key.Should().Be("tenant:tenant-a:stored-session:sess-1:v1");
    }

    // =========================================================================
    // PersistMessageAsync — happy path (dual-write)
    // =========================================================================

    [Fact]
    public async Task PersistMessageAsync_HappyPath_WritesToBothRedisAndCosmos()
    {
        // Arrange — Redis cache miss (no existing session) + successful writes
        // TrackingTenantCache: empty by default → cache miss; SetAsync succeeds.

        var cosmosWritten = false;
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.Is<PartitionKey>(pk => pk == new PartitionKey(TenantId)),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<StoredSession, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (_, _, _, _) => cosmosWritten = true)
            .ReturnsAsync(CreateFakeUpsertResponse());

        var message = BuildMessage();

        // Act
        await _sut.PersistMessageAsync(TenantId, SessionId, message);

        // Wait briefly for the fire-and-forget Cosmos task to complete
        await Task.Delay(50);

        // Assert — Redis set was called
        _cache.SetCount.Should().Be(1, "tenant cache must be written exactly once");
        _cache.LastTenantId.Should().Be(TenantId);
        _cache.LastResource.Should().Be("stored-session");
        _cache.LastId.Should().Be(SessionId);

        // Assert — Cosmos upsert was called with correct partition key
        cosmosWritten.Should().BeTrue("Cosmos DB write must happen in the happy path");
    }

    [Fact]
    public async Task PersistMessageAsync_UsesPartitionKeyTenantId_ForCosmosWrite()
    {
        // Arrange
        // TrackingTenantCache: empty by default → cache miss; SetAsync succeeds.

        PartitionKey? capturedPartitionKey = null;
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<StoredSession, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (_, pk, _, _) => capturedPartitionKey = pk)
            .ReturnsAsync(CreateFakeUpsertResponse());

        // Act
        await _sut.PersistMessageAsync(TenantId, SessionId, BuildMessage());
        await Task.Delay(50);

        // Assert — partition key must be the tenantId (ADR-015: partition by /tenantId)
        capturedPartitionKey.Should().Be(new PartitionKey(TenantId));
    }

    // =========================================================================
    // PersistMessageAsync — Redis failure isolation
    // =========================================================================

    [Fact]
    public async Task PersistMessageAsync_RedisSetThrows_CosmosIsStillWritten_NoExceptionThrown()
    {
        // Arrange — Redis get succeeds (miss), Redis set throws
        _cache.SetThrows = new InvalidOperationException("Redis unavailable");

        var cosmosWritten = false;
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<StoredSession, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (_, _, _, _) => cosmosWritten = true)
            .ReturnsAsync(CreateFakeUpsertResponse());

        // Act — must NOT throw
        var act = async () => await _sut.PersistMessageAsync(TenantId, SessionId, BuildMessage());
        await act.Should().NotThrowAsync();

        // Wait for fire-and-forget Cosmos task
        await Task.Delay(50);

        // Assert — Cosmos was still written despite Redis failure
        cosmosWritten.Should().BeTrue("Cosmos write must proceed even if Redis set fails");
    }

    [Fact]
    public async Task PersistMessageAsync_RedisGetThrows_FallsBackToEmptySession_CosmosWritten()
    {
        // Arrange — Redis GET and SET both throw (connection problem)
        _cache.GetThrows = new InvalidOperationException("Redis connection refused");
        _cache.SetThrows = new InvalidOperationException("Redis connection refused");

        var cosmosWritten = false;
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<StoredSession, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (_, _, _, _) => cosmosWritten = true)
            .ReturnsAsync(CreateFakeUpsertResponse());

        // Act — must NOT throw
        var act = async () => await _sut.PersistMessageAsync(TenantId, SessionId, BuildMessage());
        await act.Should().NotThrowAsync();

        await Task.Delay(50);

        // Assert — Cosmos still written
        cosmosWritten.Should().BeTrue();
    }

    // =========================================================================
    // PersistMessageAsync — Cosmos failure isolation
    // =========================================================================

    [Fact]
    public async Task PersistMessageAsync_CosmosThrows_RedisIsStillWritten_NoExceptionThrown()
    {
        // Arrange
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Service unavailable", HttpStatusCode.ServiceUnavailable, 0, "", 0));

        // Act — must NOT throw
        var act = async () => await _sut.PersistMessageAsync(TenantId, SessionId, BuildMessage());
        await act.Should().NotThrowAsync();

        await Task.Delay(50);

        // Assert — Redis was still written
        _cache.SetCount.Should().Be(1, "Redis must be written exactly once even if Cosmos fails");
    }

    // =========================================================================
    // PersistMessageAsync — both stores fail
    // =========================================================================

    [Fact]
    public async Task PersistMessageAsync_BothStoresFail_NoExceptionThrown_OnlyWarningsLogged()
    {
        // Arrange — Redis get miss, Redis set throws, Cosmos throws
        _cache.SetThrows = new InvalidOperationException("Redis down");

        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Cosmos down", HttpStatusCode.ServiceUnavailable, 0, "", 0));

        // Act — must NOT throw even when both stores fail
        var act = async () => await _sut.PersistMessageAsync(TenantId, SessionId, BuildMessage());
        await act.Should().NotThrowAsync("A persistence failure must never surface to the streaming caller");

        await Task.Delay(50);

        // Assert — Warning was logged for at least the Redis failure
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(SessionId)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    // =========================================================================
    // LoadSessionAsync — Redis HIT
    // =========================================================================

    [Fact]
    public async Task LoadSessionAsync_RedisHit_ReturnsSession_NoCosmosCalled()
    {
        // Arrange — seed the tenant cache directly with the FR-05 (resource, id) coordinates
        var existing = BuildStoredSession();
        await _cache.SetAsync<StoredSession>(
            TenantId, "stored-session", SessionId, 1, existing);

        // Act
        var result = await _sut.LoadSessionAsync(TenantId, SessionId);

        // Assert
        result.Should().NotBeNull();
        result!.SessionId.Should().Be(SessionId);
        result.TenantId.Should().Be(TenantId);

        // Cosmos must NOT be called on a Redis hit
        _cosmosClientMock.Verify(
            c => c.GetContainer(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "Cosmos DB must not be called when Redis returns a valid cached session");
    }

    // =========================================================================
    // LoadSessionAsync — Redis MISS, Cosmos fallback
    // =========================================================================

    [Fact]
    public async Task LoadSessionAsync_RedisMiss_LoadsFromCosmos_RewarmsRedis()
    {
        // Arrange — Redis miss; Cosmos returns the session
        var existing = BuildStoredSession();
        var cosmosMock = new Mock<ItemResponse<StoredSession>>();
        cosmosMock.SetupGet(r => r.Resource).Returns(existing);

        _containerMock
            .Setup(c => c.ReadItemAsync<StoredSession>(
                SessionId,
                new PartitionKey(TenantId),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cosmosMock.Object);

        // Act
        var result = await _sut.LoadSessionAsync(TenantId, SessionId);

        // Assert — Cosmos returned the session
        result.Should().NotBeNull();
        result!.SessionId.Should().Be(SessionId);

        // Redis was re-warmed (exactly one SetAsync to the cache after Cosmos fallback)
        _cache.SetCount.Should().Be(1, "Redis must be re-warmed after a Cosmos fallback load");
        _cache.LastResource.Should().Be("stored-session");
        _cache.LastId.Should().Be(SessionId);
    }

    [Fact]
    public async Task LoadSessionAsync_BothMiss_ReturnsNull()
    {
        // Arrange — Redis miss (empty cache), Cosmos 404

        _containerMock
            .Setup(c => c.ReadItemAsync<StoredSession>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, "", 0));

        // Act
        var result = await _sut.LoadSessionAsync(TenantId, SessionId);

        // Assert
        result.Should().BeNull();
    }

    // =========================================================================
    // DeleteSessionAsync
    // =========================================================================

    [Fact]
    public async Task DeleteSessionAsync_DeletesFromBothStores()
    {
        // Arrange
        _containerMock
            .Setup(c => c.DeleteItemAsync<StoredSession>(
                SessionId,
                new PartitionKey(TenantId),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFakeDeleteResponse());

        // Act
        await _sut.DeleteSessionAsync(TenantId, SessionId);

        // Assert — Redis remove called
        _cache.RemoveCount.Should().Be(1, "Redis remove must be called exactly once");
        _cache.LastResource.Should().Be("stored-session");
        _cache.LastId.Should().Be(SessionId);

        // Assert — Cosmos delete called with correct partition key
        _containerMock.Verify(c => c.DeleteItemAsync<StoredSession>(
            SessionId,
            new PartitionKey(TenantId),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteSessionAsync_CosmosNotFound_IsIdempotent_NoException()
    {
        // Arrange — Cosmos returns 404 (already deleted)
        _containerMock
            .Setup(c => c.DeleteItemAsync<StoredSession>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not Found", HttpStatusCode.NotFound, 0, "", 0));

        // Act — must NOT throw
        var act = async () => await _sut.DeleteSessionAsync(TenantId, SessionId);
        await act.Should().NotThrowAsync("404 from Cosmos is idempotent — session was already deleted");
    }

    [Fact]
    public async Task DeleteSessionAsync_RedisThrows_CosmosStillCalled_NoExceptionThrown()
    {
        // Arrange
        _cache.RemoveThrows = new InvalidOperationException("Redis unavailable");

        _containerMock
            .Setup(c => c.DeleteItemAsync<StoredSession>(
                SessionId,
                new PartitionKey(TenantId),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFakeDeleteResponse());

        // Act
        var act = async () => await _sut.DeleteSessionAsync(TenantId, SessionId);
        await act.Should().NotThrowAsync();

        // Assert — Cosmos delete was still attempted
        _containerMock.Verify(c => c.DeleteItemAsync<StoredSession>(
            SessionId,
            new PartitionKey(TenantId),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // Redis TTL
    // =========================================================================

    [Fact]
    public void RedisTtl_Is24Hours()
    {
        SessionPersistenceService.RedisTtl.Should().Be(TimeSpan.FromHours(24),
            "NFR-07 and ADR-009 mandate a 24-hour sliding TTL for session hot cache");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    // TrackingTenantCache handles miss-by-default + set/remove behaviors directly —
    // no helper setup methods needed.

    private static SessionMessage BuildMessage() => new()
    {
        MessageId = "msg-001",
        Role = "user",
        Content = "Hello AI",
        Timestamp = DateTimeOffset.UtcNow
    };

    private static StoredSession BuildStoredSession() => new()
    {
        Id = SessionId,
        SessionId = SessionId,
        TenantId = TenantId,
        Messages = [],
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        LastActivity = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Creates a minimal fake <see cref="ItemResponse{T}"/> for upsert.
    /// CosmosClient returns a concrete sealed type — we return a mocked abstract.
    /// </summary>
    private static ItemResponse<StoredSession> CreateFakeUpsertResponse()
    {
        var mock = new Mock<ItemResponse<StoredSession>>();
        mock.SetupGet(r => r.Resource).Returns(BuildStoredSession());
        mock.SetupGet(r => r.StatusCode).Returns(HttpStatusCode.OK);
        return mock.Object;
    }

    private static ItemResponse<StoredSession> CreateFakeDeleteResponse()
    {
        var mock = new Mock<ItemResponse<StoredSession>>();
        mock.SetupGet(r => r.StatusCode).Returns(HttpStatusCode.NoContent);
        return mock.Object;
    }
}
