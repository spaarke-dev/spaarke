using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Unit tests for <see cref="RecentlyDiscussedTracker"/> (chat-routing-redesign-r1 task 091 MVP /
/// architecture §6.3 + §11.1; migrated to <see cref="ITenantCache"/> per
/// spaarke-redis-cache-remediation-r1 FR-05).
/// </summary>
[Trait("status", "new")]
[Trait("project", "chat-routing-redesign-r1")]
[Trait("task", "091")]
public sealed class RecentlyDiscussedTrackerTests
{
    private const string TenantId = "tenant-rdt";
    private const string SessionId = "abc123def456";
    private const string FileA = "file-a";
    private const string CacheResource = RecentlyDiscussedTracker.CacheResource;
    private const int CacheVersion = RecentlyDiscussedTracker.CacheVersion;

    private static string ExpectedCacheId(string sessionId) => sessionId.ToLowerInvariant();

    // ════════════════════════════════════════════════════════════════════════
    // MarkAsync: records file with UTC-now timestamp + sliding TTL
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkAsync_RecordsFileWithUtcNowTimestamp_AndAppliesSlidingTtl()
    {
        var cacheMock = new Mock<ITenantCache>(MockBehavior.Strict);
        cacheMock
            .Setup(c => c.GetAsync<List<RecentlyDiscussedTracker.RecentFileEntry>>(
                TenantId, CacheResource, ExpectedCacheId(SessionId), CacheVersion,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<RecentlyDiscussedTracker.RecentFileEntry>?)null);

        List<RecentlyDiscussedTracker.RecentFileEntry>? capturedEntries = null;
        TimeSpan capturedSliding = TimeSpan.Zero;
        cacheMock
            .Setup(c => c.SetSlidingAsync(
                TenantId,
                CacheResource,
                ExpectedCacheId(SessionId),
                CacheVersion,
                It.IsAny<List<RecentlyDiscussedTracker.RecentFileEntry>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, List<RecentlyDiscussedTracker.RecentFileEntry>, TimeSpan, string, CancellationToken>(
                (_, _, _, _, entries, sliding, _, _) =>
                {
                    capturedEntries = entries;
                    capturedSliding = sliding;
                })
            .Returns(Task.CompletedTask);

        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero));
        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance, fakeTime);

        await tracker.MarkAsync(TenantId, SessionId, FileA);

        capturedEntries.Should().NotBeNull();
        capturedSliding.Should().Be(RecentlyDiscussedTracker.SlidingTtl,
            because: "TTL mirrors SessionPersistenceService.RedisTtl (24h sliding, ADR-009)");
        capturedEntries!.Should().HaveCount(1);
        capturedEntries![0].FileId.Should().Be(FileA);
        capturedEntries![0].LastDiscussedAt.Should().Be(fakeTime.GetUtcNow(),
            because: "Mark uses the injected TimeProvider for deterministic timestamps");
    }

    // ════════════════════════════════════════════════════════════════════════
    // MarkAsync: duplicate fileId overwrites timestamp (idempotent prepend)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkAsync_DuplicateFileId_OverwritesTimestamp_AndKeepsListUnique()
    {
        var t0 = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(t0);

        var existing = new List<RecentlyDiscussedTracker.RecentFileEntry>
        {
            new(FileA, t0.AddMinutes(-5))
        };

        var cacheMock = new Mock<ITenantCache>();
        cacheMock
            .Setup(c => c.GetAsync<List<RecentlyDiscussedTracker.RecentFileEntry>>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        List<RecentlyDiscussedTracker.RecentFileEntry>? capturedEntries = null;
        cacheMock
            .Setup(c => c.SetSlidingAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<List<RecentlyDiscussedTracker.RecentFileEntry>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, List<RecentlyDiscussedTracker.RecentFileEntry>, TimeSpan, string, CancellationToken>(
                (_, _, _, _, entries, _, _, _) => capturedEntries = entries)
            .Returns(Task.CompletedTask);

        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance, fakeTime);

        await tracker.MarkAsync(TenantId, SessionId, FileA);

        capturedEntries.Should().HaveCount(1, because: "the duplicate fileId must NOT appear twice in the persisted list");
        capturedEntries![0].FileId.Should().Be(FileA);
        capturedEntries[0].LastDiscussedAt.Should().Be(t0, because: "Re-marking overwrites the timestamp with the fresh time");
    }

    // ════════════════════════════════════════════════════════════════════════
    // MarkAsync: list bounded to MaxEntries (oldest dropped)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkAsync_WhenListExceedsMaxEntries_TrimsOldestFirst()
    {
        var t0 = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(t0);

        var existing = new List<RecentlyDiscussedTracker.RecentFileEntry>();
        for (var i = 0; i < RecentlyDiscussedTracker.MaxEntries; i++)
        {
            existing.Add(new($"file-{i:D2}", t0.AddMinutes(-i)));
        }

        var cacheMock = new Mock<ITenantCache>();
        cacheMock
            .Setup(c => c.GetAsync<List<RecentlyDiscussedTracker.RecentFileEntry>>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        List<RecentlyDiscussedTracker.RecentFileEntry>? capturedEntries = null;
        cacheMock
            .Setup(c => c.SetSlidingAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<List<RecentlyDiscussedTracker.RecentFileEntry>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, List<RecentlyDiscussedTracker.RecentFileEntry>, TimeSpan, string, CancellationToken>(
                (_, _, _, _, entries, _, _, _) => capturedEntries = entries)
            .Returns(Task.CompletedTask);

        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance, fakeTime);

        await tracker.MarkAsync(TenantId, SessionId, "file-new");

        capturedEntries.Should().HaveCount(RecentlyDiscussedTracker.MaxEntries,
            because: "MarkAsync MUST trim to at most MaxEntries (oldest dropped)");
        capturedEntries![0].FileId.Should().Be("file-new",
            because: "the freshly-marked file is prepended (newest first)");
        capturedEntries.Last().FileId.Should().NotBe($"file-{(RecentlyDiscussedTracker.MaxEntries - 1):D2}",
            because: "the oldest pre-existing entry must be dropped to make room");
    }

    // ════════════════════════════════════════════════════════════════════════
    // GetRecentAsync: newest-first ordering
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirst_BoundedByMaxCount()
    {
        var t0 = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);
        var existing = new List<RecentlyDiscussedTracker.RecentFileEntry>
        {
            new("file-newest", t0),
            new("file-middle", t0.AddMinutes(-1)),
            new("file-oldest", t0.AddMinutes(-2))
        };

        var cacheMock = new Mock<ITenantCache>();
        cacheMock
            .Setup(c => c.GetAsync<List<RecentlyDiscussedTracker.RecentFileEntry>>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance);

        var result = await tracker.GetRecentAsync(TenantId, SessionId, maxCount: 2);

        result.Should().Equal("file-newest", "file-middle");
    }

    // ════════════════════════════════════════════════════════════════════════
    // GetRecentAsync: empty session
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetRecentAsync_EmptySession_ReturnsEmptyList()
    {
        var cacheMock = new Mock<ITenantCache>();
        cacheMock
            .Setup(c => c.GetAsync<List<RecentlyDiscussedTracker.RecentFileEntry>>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<RecentlyDiscussedTracker.RecentFileEntry>?)null);

        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance);

        var result = await tracker.GetRecentAsync(TenantId, SessionId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentAsync_MaxCountZeroOrNegative_ReturnsEmptyImmediately_WithoutHittingCache()
    {
        var cacheMock = new Mock<ITenantCache>(MockBehavior.Strict);
        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance);

        var zeroResult = await tracker.GetRecentAsync(TenantId, SessionId, maxCount: 0);
        var negResult = await tracker.GetRecentAsync(TenantId, SessionId, maxCount: -1);

        zeroResult.Should().BeEmpty();
        negResult.Should().BeEmpty();
        cacheMock.Verify(c => c.GetAsync<List<RecentlyDiscussedTracker.RecentFileEntry>>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ADR-015: log records carry sessionId + fileId only — never content
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkAsync_LogsOnlySessionIdAndFileId_NeverContent_PerAdr015()
    {
        const string privilegedFileName = "PRIVILEGED-CONTENT-do-not-leak";

        var cacheMock = new Mock<ITenantCache>();
        cacheMock
            .Setup(c => c.GetAsync<List<RecentlyDiscussedTracker.RecentFileEntry>>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<RecentlyDiscussedTracker.RecentFileEntry>?)null);
        cacheMock
            .Setup(c => c.SetSlidingAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<List<RecentlyDiscussedTracker.RecentFileEntry>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerMock = new Mock<ILogger<RecentlyDiscussedTracker>>();
        var captured = new List<string>();
        loggerMock
            .Setup(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(invocation =>
            {
                var state = invocation.Arguments[2];
                captured.Add(state?.ToString() ?? string.Empty);
            }));

        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, loggerMock.Object);

        await tracker.MarkAsync(TenantId, SessionId, FileA);

        captured.Should().NotBeEmpty(because: "MarkAsync emits at least one structured Info log");
        captured.Should().NotContain(s => s.Contains(privilegedFileName),
            because: "ADR-015 forbids file content / query text in log records");
        captured.Should().Contain(s => s.Contains(SessionId) && s.Contains(FileA));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Minimal TimeProvider with a frozen UTC clock for deterministic timestamp assertions.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
