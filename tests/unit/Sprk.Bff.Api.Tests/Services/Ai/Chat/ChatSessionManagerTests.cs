using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
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
/// </summary>
public class ChatSessionManagerTests
{
    private const string TenantId = "tenant-abc";
    private const string DocumentId = "doc-001";
    private static readonly Guid PlaybookId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");

    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<IChatDataverseRepository> _repoMock;
    private readonly Mock<ILogger<ChatSessionManager>> _loggerMock;
    private readonly ChatSessionManager _sut;

    public ChatSessionManagerTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _repoMock = new Mock<IChatDataverseRepository>();
        _loggerMock = new Mock<ILogger<ChatSessionManager>>();
        _sut = new ChatSessionManager(_cacheMock.Object, _repoMock.Object, _loggerMock.Object);
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
