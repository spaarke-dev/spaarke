using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="ChatSessionManager"/>.
///
/// Verifies:
/// - <see cref="ChatSessionManager.CreateSessionAsync"/> persists to Dataverse and warms Redis.
/// - <see cref="ChatSessionManager.GetSessionAsync"/> returns cached session on Redis hit (no Dataverse call).
/// - <see cref="ChatSessionManager.GetSessionAsync"/> falls back to Dataverse on Redis miss.
/// - Cache key pattern: "chat:session:{tenantId}:{sessionId}" (ADR-014).
/// - Sliding TTL: 24 hours (NFR-07, ADR-009).
/// - <see cref="ChatSessionManager.DeleteSessionAsync"/> removes from Redis and archives in Dataverse.
///
/// Cosmos write-through integration (decision D-06):
/// - Write-through: Redis write always precedes Cosmos upsert (fire-and-forget).
/// - Redis HIT: Cosmos is NOT consulted on warm-cache reads.
/// - Redis MISS: Cosmos fallback re-populates Redis before Dataverse fallback.
/// - Cosmos write failure: Redis write still succeeds; error is logged only.
/// </summary>
public class ChatSessionManagerTests
{
    private const string TenantId = "tenant-abc";
    private const string DocumentId = "doc-001";
    private static readonly Guid PlaybookId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");

    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<IChatDataverseRepository> _repoMock;
    private readonly Mock<ILogger<ChatSessionManager>> _loggerMock;
    private readonly Mock<ISessionPersistenceService> _persistenceMock;

    /// <summary>SUT without Cosmos (backward-compatible mode).</summary>
    private readonly ChatSessionManager _sut;

    /// <summary>SUT with Cosmos persistence wired in (D-06 write-through mode).</summary>
    private readonly ChatSessionManager _sutWithCosmos;

    public ChatSessionManagerTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _repoMock = new Mock<IChatDataverseRepository>();
        _loggerMock = new Mock<ILogger<ChatSessionManager>>();
        _persistenceMock = new Mock<ISessionPersistenceService>();

        _sut = new ChatSessionManager(
            _cacheMock.Object,
            _repoMock.Object,
            _loggerMock.Object);

        _sutWithCosmos = new ChatSessionManager(
            _cacheMock.Object,
            _repoMock.Object,
            _loggerMock.Object,
            _persistenceMock.Object);
    }

    // =========================================================================
    // CreateSessionAsync
    // =========================================================================

    [Fact]
    public async Task CreateSessionAsync_ReturnsChatSession_WithExpectedProperties()
    {
        // Arrange
        SetupCacheSetSuccess();
        _repoMock.Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        // Act
        var session = await _sut.CreateSessionAsync(TenantId, DocumentId, PlaybookId);

        // Assert
        session.Should().NotBeNull();
        session.TenantId.Should().Be(TenantId);
        session.DocumentId.Should().Be(DocumentId);
        session.PlaybookId.Should().Be(PlaybookId);
        session.SessionId.Should().NotBeNullOrEmpty();
        session.Messages.Should().BeEmpty();
        session.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        session.LastActivity.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateSessionAsync_PersistsSessionToDataverse()
    {
        // Arrange
        SetupCacheSetSuccess();
        ChatSession? capturedSession = null;
        _repoMock.Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
                 .Callback<ChatSession, CancellationToken>((s, _) => capturedSession = s)
                 .Returns(Task.CompletedTask);

        // Act
        var session = await _sut.CreateSessionAsync(TenantId, DocumentId, PlaybookId);

        // Assert — Dataverse persist was called with the same session
        _repoMock.Verify(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()), Times.Once);
        capturedSession.Should().NotBeNull();
        capturedSession!.SessionId.Should().Be(session.SessionId);
        capturedSession.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task CreateSessionAsync_WarmsRedisCache_After_DataversePersist()
    {
        // Arrange
        _repoMock.Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        byte[]? capturedBytes = null;
        DistributedCacheEntryOptions? capturedOptions = null;

        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, bytes, opts, _) =>
                {
                    capturedBytes = bytes;
                    capturedOptions = opts;
                })
            .Returns(Task.CompletedTask);

        // Act
        var session = await _sut.CreateSessionAsync(TenantId, DocumentId, PlaybookId);

        // Assert — Redis Set was called once
        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify 24-hour sliding TTL (NFR-07, ADR-009)
        capturedOptions.Should().NotBeNull();
        capturedOptions!.SlidingExpiration.Should().Be(ChatSessionManager.SessionCacheTtl);
        capturedOptions.SlidingExpiration.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task CreateSessionAsync_CacheKey_FollowsPattern_TenantScopedSessionId()
    {
        // Arrange
        _repoMock.Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        string? capturedKey = null;
        _cacheMock
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (key, _, _, _) => capturedKey = key)
            .Returns(Task.CompletedTask);

        // Act
        var session = await _sut.CreateSessionAsync(TenantId, DocumentId, PlaybookId);

        // Assert — key format: "chat:session:{tenantId}:{sessionId}" (ADR-014)
        capturedKey.Should().NotBeNull();
        capturedKey.Should().StartWith($"chat:session:{TenantId}:");
        capturedKey.Should().EndWith(session.SessionId);
    }

    // =========================================================================
    // GetSessionAsync — Redis HIT
    // =========================================================================

    [Fact]
    public async Task GetSessionAsync_ReturnsCachedSession_OnRedisHit_WithoutCallingDataverse()
    {
        // Arrange
        var existingSession = CreateTestSession("session-xyz");
        var cachedBytes = JsonSerializer.SerializeToUtf8Bytes(existingSession);
        var cacheKey = ChatSessionManager.BuildCacheKey(TenantId, "session-xyz");

        _cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBytes);
        _cacheMock
            .Setup(c => c.RefreshAsync(cacheKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.GetSessionAsync(TenantId, "session-xyz");

        // Assert
        result.Should().NotBeNull();
        result!.SessionId.Should().Be("session-xyz");
        result.TenantId.Should().Be(TenantId);

        // Dataverse MUST NOT be called on a Redis hit
        _repoMock.Verify(r => r.GetSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSessionAsync_RefressesSlidingTtl_OnRedisHit()
    {
        // Arrange
        var existingSession = CreateTestSession("session-ttl");
        var cachedBytes = JsonSerializer.SerializeToUtf8Bytes(existingSession);
        var cacheKey = ChatSessionManager.BuildCacheKey(TenantId, "session-ttl");

        _cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBytes);
        _cacheMock
            .Setup(c => c.RefreshAsync(cacheKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.GetSessionAsync(TenantId, "session-ttl");

        // Assert — sliding TTL is refreshed via RefreshAsync
        _cacheMock.Verify(c => c.RefreshAsync(cacheKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // GetSessionAsync — Redis MISS (Dataverse fallback)
    // =========================================================================

    [Fact]
    public async Task GetSessionAsync_FallsBackToDataverse_OnRedisMiss()
    {
        // Arrange — cache returns null (miss)
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var dvSession = CreateTestSession("session-cold");
        _repoMock
            .Setup(r => r.GetSessionAsync(TenantId, "session-cold", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dvSession);

        SetupCacheSetSuccess();

        // Act
        var result = await _sut.GetSessionAsync(TenantId, "session-cold");

        // Assert — Dataverse was called
        _repoMock.Verify(r => r.GetSessionAsync(TenantId, "session-cold", It.IsAny<CancellationToken>()), Times.Once);
        result.Should().NotBeNull();
        result!.SessionId.Should().Be("session-cold");
    }

    [Fact]
    public async Task GetSessionAsync_ReWarmsCache_AfterDataverseFallback()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var dvSession = CreateTestSession("session-rewarm");
        _repoMock
            .Setup(r => r.GetSessionAsync(TenantId, "session-rewarm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dvSession);

        SetupCacheSetSuccess();

        // Act
        await _sut.GetSessionAsync(TenantId, "session-rewarm");

        // Assert — cache was set (re-warmed) after Dataverse fallback
        _cacheMock.Verify(c => c.SetAsync(
            It.Is<string>(k => k.Contains("session-rewarm")),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsNull_WhenSessionNotFoundInCacheOrDataverse()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _repoMock
            .Setup(r => r.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        // Act
        var result = await _sut.GetSessionAsync(TenantId, "session-nonexistent");

        // Assert
        result.Should().BeNull();
    }

    // =========================================================================
    // DeleteSessionAsync
    // =========================================================================

    [Fact]
    public async Task DeleteSessionAsync_RemovesFromRedis()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repoMock
            .Setup(r => r.ArchiveSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.DeleteSessionAsync(TenantId, "session-delete");

        // Assert
        _cacheMock.Verify(c => c.RemoveAsync(
            It.Is<string>(k => k == ChatSessionManager.BuildCacheKey(TenantId, "session-delete")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteSessionAsync_ArchivesSessionInDataverse()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repoMock
            .Setup(r => r.ArchiveSessionAsync(TenantId, "session-archive", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.DeleteSessionAsync(TenantId, "session-archive");

        // Assert
        _repoMock.Verify(r => r.ArchiveSessionAsync(TenantId, "session-archive", It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // R5 task 007 (D1-07) — Session-files cleanup signal integration
    // =========================================================================

    /// <summary>
    /// R5 task 007 / D1-07 (spec NFR-02 "Aggressive cleanup on session-end"):
    /// <see cref="ChatSessionManager.DeleteSessionAsync"/> must raise a single
    /// fire-and-forget signal to the cleanup hosted service at the end of its
    /// existing logic.
    /// </summary>
    [Fact]
    public async Task DeleteSessionAsync_FiresCleanupSignal_ExactlyOnce_WithTenantAndSessionIds()
    {
        // Arrange
        var cleanupSignalMock = new Mock<Sprk.Bff.Api.Services.Ai.Chat.ISessionFilesCleanupSignal>();
        var sut = new ChatSessionManager(
            _cacheMock.Object,
            _repoMock.Object,
            _loggerMock.Object,
            persistence: null,
            cleanupSignal: cleanupSignalMock.Object);

        _cacheMock
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repoMock
            .Setup(r => r.ArchiveSessionAsync(TenantId, "session-r5-signal", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await sut.DeleteSessionAsync(TenantId, "session-r5-signal");

        // Assert — signal invoked exactly once with the expected tenant + session.
        cleanupSignalMock.Verify(
            s => s.SignalSessionEnded(TenantId, "session-r5-signal"),
            Times.Once,
            "DeleteSessionAsync must raise the cleanup signal at the end of the existing logic " +
            "(spec NFR-02 aggressive cleanup-on-session-end contract)");
    }

    /// <summary>
    /// R5 task 007 / D1-07 back-compat: <see cref="ChatSessionManager.DeleteSessionAsync"/>
    /// must succeed when the cleanup signal is absent (null) — back-compat for
    /// callers that don't register the service (compound-AI-OFF code path,
    /// pre-R5 unit tests).
    /// </summary>
    [Fact]
    public async Task DeleteSessionAsync_SucceedsWhenCleanupSignalIsNull_BackCompat()
    {
        // Arrange — SUT uses the original 4-param constructor; cleanup signal defaults to null.
        _cacheMock
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repoMock
            .Setup(r => r.ArchiveSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act + Assert — no exception, no dependency on the cleanup signal.
        var act = async () => await _sut.DeleteSessionAsync(TenantId, "session-no-cleanup-signal");
        await act.Should().NotThrowAsync(
            "the cleanup-signal injection is nullable + the call is fire-and-forget — " +
            "DeleteSessionAsync must continue to work when the signal is not registered");

        // Cache + Dataverse paths still fire normally.
        _cacheMock.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.ArchiveSessionAsync(TenantId, "session-no-cleanup-signal", It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Cache key and TTL constants
    // =========================================================================

    [Fact]
    public void BuildCacheKey_ProducesExpectedPattern()
    {
        // Act
        var key = ChatSessionManager.BuildCacheKey("my-tenant", "my-session");

        // Assert
        key.Should().Be("chat:session:my-tenant:my-session");
    }

    [Fact]
    public void SessionCacheTtl_Is24Hours()
    {
        ChatSessionManager.SessionCacheTtl.Should().Be(TimeSpan.FromHours(24));
    }

    // =========================================================================
    // Cosmos write-through integration tests (decision D-06)
    // =========================================================================

    /// <summary>
    /// Write-through: GetSessionAsync on a warm Redis cache must NOT call Cosmos.
    /// Cosmos is consulted only on Redis misses (ADR-009 Redis-first).
    /// </summary>
    [Fact]
    public async Task GetSessionAsync_WarmRedisHit_DoesNotCallCosmos()
    {
        // Arrange
        var existingSession = CreateTestSession("session-warm");
        var cachedBytes = JsonSerializer.SerializeToUtf8Bytes(existingSession);
        var cacheKey = ChatSessionManager.BuildCacheKey(TenantId, "session-warm");

        _cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBytes);
        _cacheMock
            .Setup(c => c.RefreshAsync(cacheKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sutWithCosmos.GetSessionAsync(TenantId, "session-warm");

        // Assert — result returned from Redis, Cosmos NOT called
        result.Should().NotBeNull();
        result!.SessionId.Should().Be("session-warm");
        _persistenceMock.Verify(
            p => p.LoadSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Cosmos must not be consulted on a Redis hit (ADR-009 Redis-first)");
    }

    /// <summary>
    /// Redis miss + Cosmos hit: Cosmos fallback re-populates Redis so subsequent requests hit the hot path.
    /// Dataverse must NOT be called when Cosmos holds the session.
    /// </summary>
    [Fact]
    public async Task GetSessionAsync_RedisMiss_CosmosFallback_RePopulatesRedisAndSkipsDataverse()
    {
        // Arrange — Redis returns null (cache miss)
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Cosmos holds the session
        var storedSession = new StoredSession
        {
            Id = "session-cold-cosmos",
            SessionId = "session-cold-cosmos",
            TenantId = TenantId,
            PlaybookId = PlaybookId,
            Messages = [],
            WidgetStates = [],
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            LastActivity = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        _persistenceMock
            .Setup(p => p.LoadSessionAsync(TenantId, "session-cold-cosmos", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedSession);

        SetupCacheSetSuccess();

        // Act
        var result = await _sutWithCosmos.GetSessionAsync(TenantId, "session-cold-cosmos");

        // Assert — session returned from Cosmos
        result.Should().NotBeNull();
        result!.SessionId.Should().Be("session-cold-cosmos");
        result.TenantId.Should().Be(TenantId);

        // Redis must be re-warmed
        _cacheMock.Verify(c => c.SetAsync(
            It.Is<string>(k => k.Contains("session-cold-cosmos")),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "Redis must be re-warmed from Cosmos so subsequent reads hit the hot path");

        // Dataverse must NOT be called — Cosmos was sufficient
        _repoMock.Verify(r => r.GetSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Dataverse must not be called when Cosmos already has the session");
    }

    /// <summary>
    /// Cosmos write failure: Redis write succeeds and the session is returned normally.
    /// The Cosmos failure must never propagate to the caller (D-06 non-fatal policy).
    /// </summary>
    [Fact]
    public async Task CreateSessionAsync_CosmosWriteFailure_RedisWriteSucceeds_NoExceptionThrown()
    {
        // Arrange — Dataverse succeeds
        _repoMock
            .Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SetupCacheSetSuccess();

        // Cosmos persistence throws (e.g., transient network error)
        _persistenceMock
            .Setup(p => p.PersistSessionAsync(It.IsAny<StoredSession>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cosmos DB unavailable"));

        // Act — must not throw despite Cosmos failure
        var act = async () => await _sutWithCosmos.CreateSessionAsync(TenantId, DocumentId, PlaybookId);
        await act.Should().NotThrowAsync(
            "Cosmos write failure must not surface to the caller (D-06 non-fatal policy)");

        // Assert — Redis was still written
        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "Redis write must succeed even when Cosmos is unavailable");
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private static ChatSession CreateTestSession(string sessionId)
        => new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: DocumentId,
            PlaybookId: PlaybookId,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: []);

    private void SetupCacheSetSuccess()
    {
        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }
}
