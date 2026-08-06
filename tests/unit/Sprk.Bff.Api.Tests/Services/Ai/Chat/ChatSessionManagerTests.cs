using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
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
/// - Cache wrapper invariants per spaarke-redis-cache-remediation-r1 FR-05:
///     tenantId + resource "session" + sessionId + version 1 produces the on-wire key
///     <c>spaarke:tenant:{tenantId}:session:{sessionId}:v1</c> when prefixed by InstanceName.
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
    private const string CacheResource = ChatSessionManager.CacheResource;
    private const int CacheVersion = ChatSessionManager.CacheVersion;
    private static readonly Guid PlaybookId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");

    private readonly Mock<ITenantCache> _cacheMock;
    private readonly Mock<IChatDataverseRepository> _repoMock;
    private readonly Mock<ILogger<ChatSessionManager>> _loggerMock;
    private readonly Mock<ISessionPersistenceService> _persistenceMock;

    /// <summary>SUT without Cosmos (backward-compatible mode).</summary>
    private readonly ChatSessionManager _sut;

    /// <summary>SUT with Cosmos persistence wired in (D-06 write-through mode).</summary>
    private readonly ChatSessionManager _sutWithCosmos;

    public ChatSessionManagerTests()
    {
        _cacheMock = new Mock<ITenantCache>();
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
    public async Task CreateSessionAsync_WarmsRedisCache_With24hSlidingTtl()
    {
        // Arrange
        _repoMock.Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        TimeSpan capturedSliding = TimeSpan.Zero;
        _cacheMock
            .Setup(c => c.SetSlidingAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<ChatSession>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, ChatSession, TimeSpan, string, CancellationToken>(
                (_, _, _, _, _, sliding, _, _) => capturedSliding = sliding)
            .Returns(Task.CompletedTask);

        // Act
        var session = await _sut.CreateSessionAsync(TenantId, DocumentId, PlaybookId);

        // Assert — wrapper invoked with 24h sliding TTL (NFR-07, ADR-009)
        _cacheMock.Verify(c => c.SetSlidingAsync(
            TenantId,
            CacheResource,
            session.SessionId,
            CacheVersion,
            It.IsAny<ChatSession>(),
            ChatSessionManager.SessionCacheTtl,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        capturedSliding.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task CreateSessionAsync_CacheCall_UsesSessionResource_ForFR14SmokeTest()
    {
        // Arrange — FR-14 / Phase 3 smoke test requires resource = "session"
        // so the on-wire key matches spaarke:tenant:{tenantId}:session:{id}:v1.
        _repoMock.Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        string? capturedTenant = null;
        string? capturedResource = null;
        string? capturedId = null;
        int capturedVersion = 0;

        _cacheMock
            .Setup(c => c.SetSlidingAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<ChatSession>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, ChatSession, TimeSpan, string, CancellationToken>(
                (tenant, resource, id, version, _, _, _, _) =>
                {
                    capturedTenant = tenant;
                    capturedResource = resource;
                    capturedId = id;
                    capturedVersion = version;
                })
            .Returns(Task.CompletedTask);

        // Act
        var session = await _sut.CreateSessionAsync(TenantId, DocumentId, PlaybookId);

        // Assert — resource MUST be "session" (FR-14 smoke-test contract)
        capturedTenant.Should().Be(TenantId);
        capturedResource.Should().Be("session");
        capturedId.Should().Be(session.SessionId);
        capturedVersion.Should().Be(1);
    }

    // =========================================================================
    // GetSessionAsync — Redis HIT
    // =========================================================================

    [Fact]
    public async Task GetSessionAsync_ReturnsCachedSession_OnRedisHit_WithoutCallingDataverse()
    {
        // Arrange
        var existingSession = CreateTestSession("session-xyz");

        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                TenantId, CacheResource, "session-xyz", CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSession);
        _cacheMock
            .Setup(c => c.RefreshAsync(
                TenantId, CacheResource, "session-xyz", CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
    public async Task GetSessionAsync_RefreshesSlidingTtl_OnRedisHit()
    {
        // Arrange
        var existingSession = CreateTestSession("session-ttl");

        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                TenantId, CacheResource, "session-ttl", CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSession);
        _cacheMock
            .Setup(c => c.RefreshAsync(
                TenantId, CacheResource, "session-ttl", CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.GetSessionAsync(TenantId, "session-ttl");

        // Assert — sliding TTL is refreshed via wrapper RefreshAsync
        _cacheMock.Verify(c => c.RefreshAsync(
            TenantId, CacheResource, "session-ttl", CacheVersion,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // GetSessionAsync — Redis MISS (Dataverse fallback)
    // =========================================================================

    [Fact]
    public async Task GetSessionAsync_FallsBackToDataverse_OnRedisMiss()
    {
        // Arrange — cache returns default (miss)
        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

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
            .Setup(c => c.GetAsync<ChatSession>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        var dvSession = CreateTestSession("session-rewarm");
        _repoMock
            .Setup(r => r.GetSessionAsync(TenantId, "session-rewarm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dvSession);

        SetupCacheSetSuccess();

        // Act
        await _sut.GetSessionAsync(TenantId, "session-rewarm");

        // Assert — cache was set (re-warmed) after Dataverse fallback
        _cacheMock.Verify(c => c.SetSlidingAsync(
            TenantId,
            CacheResource,
            "session-rewarm",
            CacheVersion,
            It.IsAny<ChatSession>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsNull_WhenSessionNotFoundInCacheOrDataverse()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

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
            .Setup(c => c.RemoveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repoMock
            .Setup(r => r.ArchiveSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.DeleteSessionAsync(TenantId, "session-delete");

        // Assert
        _cacheMock.Verify(c => c.RemoveAsync(
            TenantId, CacheResource, "session-delete", CacheVersion,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteSessionAsync_ArchivesSessionInDataverse()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.RemoveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(c => c.RemoveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repoMock
            .Setup(r => r.ArchiveSessionAsync(TenantId, "session-r5-signal", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await sut.DeleteSessionAsync(TenantId, "session-r5-signal");

        // Assert
        cleanupSignalMock.Verify(
            s => s.SignalSessionEnded(TenantId, "session-r5-signal"),
            Times.Once,
            "DeleteSessionAsync must raise the cleanup signal at the end of the existing logic " +
            "(spec NFR-02 aggressive cleanup-on-session-end contract)");
    }

    [Fact]
    public async Task DeleteSessionAsync_SucceedsWhenCleanupSignalIsNull_BackCompat()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.RemoveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repoMock
            .Setup(r => r.ArchiveSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act + Assert — no exception, no dependency on the cleanup signal.
        var act = async () => await _sut.DeleteSessionAsync(TenantId, "session-no-cleanup-signal");
        await act.Should().NotThrowAsync(
            "the cleanup-signal injection is nullable + the call is fire-and-forget — " +
            "DeleteSessionAsync must continue to work when the signal is not registered");

        _cacheMock.Verify(c => c.RemoveAsync(
            TenantId, CacheResource, "session-no-cleanup-signal", CacheVersion,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.ArchiveSessionAsync(TenantId, "session-no-cleanup-signal", It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Cache TTL constants
    // =========================================================================

    [Fact]
    public void SessionCacheTtl_Is24Hours()
    {
        ChatSessionManager.SessionCacheTtl.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void CacheResource_Is_Session_ForFR14SmokeTest()
    {
        // FR-14 / Phase 3 smoke test contract: on-wire key MUST contain ":session:"
        // produced by the wrapper. Migration agent guards this constant.
        ChatSessionManager.CacheResource.Should().Be("session");
        ChatSessionManager.CacheVersion.Should().Be(1);
    }

    // =========================================================================
    // Cosmos write-through integration tests (decision D-06)
    // =========================================================================

    [Fact]
    public async Task GetSessionAsync_WarmRedisHit_DoesNotCallCosmos()
    {
        // Arrange
        var existingSession = CreateTestSession("session-warm");

        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                TenantId, CacheResource, "session-warm", CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSession);
        _cacheMock
            .Setup(c => c.RefreshAsync(
                TenantId, CacheResource, "session-warm", CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sutWithCosmos.GetSessionAsync(TenantId, "session-warm");

        // Assert
        result.Should().NotBeNull();
        result!.SessionId.Should().Be("session-warm");
        _persistenceMock.Verify(
            p => p.LoadSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Cosmos must not be consulted on a Redis hit (ADR-009 Redis-first)");
    }

    [Fact]
    public async Task GetSessionAsync_RedisMiss_CosmosFallback_RePopulatesRedisAndSkipsDataverse()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

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

        // Assert
        result.Should().NotBeNull();
        result!.SessionId.Should().Be("session-cold-cosmos");
        result.TenantId.Should().Be(TenantId);

        _cacheMock.Verify(c => c.SetSlidingAsync(
            TenantId,
            CacheResource,
            "session-cold-cosmos",
            CacheVersion,
            It.IsAny<ChatSession>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "Redis must be re-warmed from Cosmos so subsequent reads hit the hot path");

        _repoMock.Verify(r => r.GetSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Dataverse must not be called when Cosmos already has the session");
    }

    [Fact]
    public async Task GetSessionAsync_StaleSessionPastTtl_CosmosHasTranscript_RestoresHistory_NoEmptySessionDataLoss()
    {
        // Task 025 / spec FR-09 acceptance criterion: a Redis-expired (stale/TTL-expired) session
        // MUST resolve against the durable Cosmos transcript rather than returning an empty
        // session. This simulates the TTL-expiry reopen scenario end to end: Redis miss (as if
        // SessionCacheTtl elapsed), Cosmos holds the full prior message history.
        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        var priorMessages = new List<SessionMessage>
        {
            new() { MessageId = "m1", Role = "user", Content = "What are the key terms?", Timestamp = DateTimeOffset.UtcNow.AddHours(-30) },
            new() { MessageId = "m2", Role = "assistant", Content = "The key terms are...", Timestamp = DateTimeOffset.UtcNow.AddHours(-30) },
        };
        var storedSession = new StoredSession
        {
            Id = "session-ttl-expired",
            SessionId = "session-ttl-expired",
            TenantId = TenantId,
            PlaybookId = PlaybookId,
            Messages = priorMessages,
            WidgetStates = [],
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-30),
            LastActivity = DateTimeOffset.UtcNow.AddHours(-25), // past the 24h sliding TTL
        };
        _persistenceMock
            .Setup(p => p.LoadSessionAsync(TenantId, "session-ttl-expired", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedSession);

        SetupCacheSetSuccess();

        // Act
        var result = await _sutWithCosmos.GetSessionAsync(TenantId, "session-ttl-expired");

        // Assert — prior history restored, NOT an empty session.
        result.Should().NotBeNull();
        result!.Messages.Should().HaveCount(2, "the durable Cosmos transcript must be restored, not discarded");
        result.Messages[0].Content.Should().Be("What are the key terms?");
        result.Messages[1].Content.Should().Be("The key terms are...");

        _repoMock.Verify(r => r.GetSessionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Dataverse must not be consulted — Cosmos already restored the durable transcript");
    }

    [Fact]
    public async Task GetSessionAsync_CosmosReadThrows_FallsBackToDataverse_DoesNotThrow()
    {
        // Task 025 hardening: a transient Cosmos failure during the stale-session fallback read
        // must NOT abort the whole lookup — it must degrade to the Dataverse cold path instead of
        // propagating (which a caller could otherwise misread as "not found" and mint an empty
        // session on top of a still-existing durable transcript).
        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        _persistenceMock
            .Setup(p => p.LoadSessionAsync(TenantId, "session-cosmos-down", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cosmos DB transient failure"));

        var dataverseSession = CreateTestSession("session-cosmos-down");
        _repoMock
            .Setup(r => r.GetSessionAsync(TenantId, "session-cosmos-down", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dataverseSession);

        SetupCacheSetSuccess();

        // Act
        var act = async () => await _sutWithCosmos.GetSessionAsync(TenantId, "session-cosmos-down");

        // Assert
        var result = await act.Should().NotThrowAsync(
            "a Cosmos read failure must degrade gracefully to the Dataverse fallback, never throw");
        result.Subject.Should().NotBeNull();
        result.Subject!.SessionId.Should().Be("session-cosmos-down");
    }

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
            .Setup(p => p.PersistSessionAsync(It.IsAny<StoredSession>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("Cosmos DB unavailable"));

        // Act — must not throw despite Cosmos failure
        var act = async () => await _sutWithCosmos.CreateSessionAsync(TenantId, DocumentId, PlaybookId);
        await act.Should().NotThrowAsync(
            "Cosmos write failure must not surface to the caller (D-06 non-fatal policy)");

        // Assert — Redis was still written
        _cacheMock.Verify(c => c.SetSlidingAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<ChatSession>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "Redis write must succeed even when Cosmos is unavailable");
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    // =========================================================================
    // PromoteSessionToAnalysisAsync — explicit promotion (task 023, spec FR-07)
    // Binds an EXISTING loose session to a NEW sprk_analysis in place — a bind, not a mint.
    // =========================================================================

    [Fact]
    public async Task PromoteSessionToAnalysisAsync_BindsExistingRow_AndUpdatesHostContextInPlace()
    {
        // Arrange — a loose session (no HostContext) is currently cached (Redis hit path).
        var looseSession = CreateTestSession("session-promote");
        var analysisId = Guid.NewGuid();

        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                TenantId, CacheResource, "session-promote", CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(looseSession);
        _cacheMock
            .Setup(c => c.RefreshAsync(
                TenantId, CacheResource, "session-promote", CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        SetupCacheSetSuccess();

        _repoMock
            .Setup(r => r.BindSessionToAnalysisAsync(TenantId, "session-promote", analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.PromoteSessionToAnalysisAsync(TenantId, "session-promote", analysisId, "My Analysis");

        // Assert — the SAME session id, now Analysis-owned via the task-020 HostContext convention.
        result.Should().NotBeNull();
        result!.SessionId.Should().Be("session-promote");
        result.HostContext.Should().NotBeNull();
        result.HostContext!.EntityType.Should().Be("sprk_analysisoutput");
        result.HostContext.EntityId.Should().Be(analysisId.ToString());
        result.HostContext.EntityName.Should().Be("My Analysis");

        // The FK bind targeted the EXISTING row — no CreateSessionAsync (no new session/row minted).
        _repoMock.Verify(
            r => r.BindSessionToAnalysisAsync(TenantId, "session-promote", analysisId, It.IsAny<CancellationToken>()),
            Times.Once);
        _repoMock.Verify(
            r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "promotion must bind the existing session, never mint a new one");

        // The updated session was written through to the cache (Redis re-warm).
        _cacheMock.Verify(c => c.SetSlidingAsync(
            TenantId, CacheResource, "session-promote", CacheVersion,
            It.IsAny<ChatSession>(), It.IsAny<TimeSpan>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PromoteSessionToAnalysisAsync_WhenSessionNotFound_ReturnsNullAndDoesNotBind()
    {
        // Arrange — Redis miss + no Dataverse cold-tier row (session genuinely gone).
        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);
        _repoMock
            .Setup(r => r.GetSessionAsync(TenantId, "session-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        // Act
        var result = await _sut.PromoteSessionToAnalysisAsync(TenantId, "session-gone", Guid.NewGuid(), "Orphan Attempt");

        // Assert
        result.Should().BeNull();
        _repoMock.Verify(
            r => r.BindSessionToAnalysisAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PromoteSessionToAnalysisAsync_WhenFkBindThrows_PropagatesAndDoesNotUpdateCache()
    {
        // Arrange — the Dataverse FK write fails. UNLIKE CreateSessionAsync's tolerant posture, the
        // durable FK is promotion's entire deliverable — the failure MUST propagate so the caller
        // (AnalysisEndpoints.PromoteSession) can compensate by deleting the orphaned Analysis, rather
        // than silently leaving a "promoted" session with no durable, queryable bind.
        var looseSession = CreateTestSession("session-bind-fails");
        var analysisId = Guid.NewGuid();

        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                TenantId, CacheResource, "session-bind-fails", CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(looseSession);
        _cacheMock
            .Setup(c => c.RefreshAsync(
                TenantId, CacheResource, "session-bind-fails", CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.BindSessionToAnalysisAsync(TenantId, "session-bind-fails", analysisId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse outage"));

        // Act
        var act = async () => await _sut.PromoteSessionToAnalysisAsync(TenantId, "session-bind-fails", analysisId, "Failed Promote");

        // Assert — the exception propagates; no cache write (no partial-promotion state left behind).
        await act.Should().ThrowAsync<InvalidOperationException>();
        _cacheMock.Verify(c => c.SetSlidingAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<ChatSession>(), It.IsAny<TimeSpan>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

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
            .Setup(c => c.SetSlidingAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<ChatSession>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }
}
