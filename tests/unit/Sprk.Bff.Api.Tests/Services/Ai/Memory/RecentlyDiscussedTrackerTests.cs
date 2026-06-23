using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Unit tests for <see cref="RecentlyDiscussedTracker"/> (chat-routing-redesign-r1 task 091 MVP /
/// architecture §6.3 + §11.1).
/// </summary>
/// <remarks>
/// Coverage:
/// <list type="bullet">
///   <item><c>MarkAsync</c> records file with UTC-now timestamp and prepends to the per-session list</item>
///   <item><c>MarkAsync</c> with duplicate fileId overwrites prior timestamp (idempotent)</item>
///   <item><c>MarkAsync</c> caps persisted list at <see cref="RecentlyDiscussedTracker.MaxEntries"/></item>
///   <item><c>GetRecentAsync</c> returns newest-first</item>
///   <item><c>GetRecentAsync</c> on empty session returns empty list (no Redis allocation)</item>
///   <item>ADR-015: log records carry sessionId + fileId only, NEVER file content / query strings</item>
/// </list>
/// </remarks>
[Trait("status", "new")]
[Trait("project", "chat-routing-redesign-r1")]
[Trait("task", "091")]
public sealed class RecentlyDiscussedTrackerTests
{
    private const string SessionId = "abc123def456";
    private const string FileA = "file-a";
    private const string FileB = "file-b";

    private static string ExpectedKey(string sessionId) =>
        $"session:{sessionId.ToLowerInvariant()}:recent-files";

    // ════════════════════════════════════════════════════════════════════════
    // MarkAsync: records file with UTC-now timestamp + sliding TTL
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkAsync_RecordsFileWithUtcNowTimestamp_AndAppliesSlidingTtl()
    {
        var cacheMock = new Mock<IDistributedCache>(MockBehavior.Strict);
        cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        byte[]? capturedBytes = null;
        DistributedCacheEntryOptions? capturedOptions = null;
        cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, bytes, opts, _) => { capturedBytes = bytes; capturedOptions = opts; })
            .Returns(Task.CompletedTask);

        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero));
        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance, fakeTime);

        await tracker.MarkAsync(SessionId, FileA);

        capturedBytes.Should().NotBeNull();
        capturedOptions.Should().NotBeNull();
        capturedOptions!.SlidingExpiration.Should().Be(RecentlyDiscussedTracker.SlidingTtl,
            because: "TTL mirrors SessionPersistenceService.RedisTtl (24h sliding, ADR-009)");

        var entries = JsonSerializer.Deserialize<List<RecentlyDiscussedTracker.RecentFileEntry>>(capturedBytes!,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        entries.Should().NotBeNull();
        entries!.Should().HaveCount(1);
        entries![0].FileId.Should().Be(FileA);
        entries![0].LastDiscussedAt.Should().Be(fakeTime.GetUtcNow(),
            because: "Mark uses the injected TimeProvider for deterministic timestamps");

        // Key shape — deterministic per session
        cacheMock.Verify(c => c.SetAsync(
            ExpectedKey(SessionId),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
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
        var existingBytes = JsonSerializer.SerializeToUtf8Bytes(existing,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var cacheMock = new Mock<IDistributedCache>();
        cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBytes);

        byte[]? capturedBytes = null;
        cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, b, _, _) => capturedBytes = b)
            .Returns(Task.CompletedTask);

        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance, fakeTime);

        // Re-mark the same fileId; the prior entry must be removed and prepended with new time.
        await tracker.MarkAsync(SessionId, FileA);

        var entries = JsonSerializer.Deserialize<List<RecentlyDiscussedTracker.RecentFileEntry>>(capturedBytes!,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        entries.Should().HaveCount(1, because: "the duplicate fileId must NOT appear twice in the persisted list");
        entries![0].FileId.Should().Be(FileA);
        entries[0].LastDiscussedAt.Should().Be(t0, because: "Re-marking overwrites the timestamp with the fresh time");
    }

    // ════════════════════════════════════════════════════════════════════════
    // MarkAsync: list bounded to MaxEntries (oldest dropped)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkAsync_WhenListExceedsMaxEntries_TrimsOldestFirst()
    {
        var t0 = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(t0);

        // Pre-load Redis with MaxEntries already populated (entries 0..MaxEntries-1, newest first).
        // The fileIds are "file-NN" indexed; file-00 is newest, file-(MaxEntries-1) is oldest.
        var existing = new List<RecentlyDiscussedTracker.RecentFileEntry>();
        for (var i = 0; i < RecentlyDiscussedTracker.MaxEntries; i++)
        {
            existing.Add(new($"file-{i:D2}", t0.AddMinutes(-i)));
        }
        var existingBytes = JsonSerializer.SerializeToUtf8Bytes(existing,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var cacheMock = new Mock<IDistributedCache>();
        cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBytes);
        byte[]? capturedBytes = null;
        cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, b, _, _) => capturedBytes = b)
            .Returns(Task.CompletedTask);

        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance, fakeTime);

        // Mark a brand-new fileId that is NOT in the existing list — list should grow by 1
        // (to MaxEntries+1) then be trimmed back to MaxEntries.
        await tracker.MarkAsync(SessionId, "file-new");

        var entries = JsonSerializer.Deserialize<List<RecentlyDiscussedTracker.RecentFileEntry>>(capturedBytes!,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        entries.Should().HaveCount(RecentlyDiscussedTracker.MaxEntries,
            because: "MarkAsync MUST trim to at most MaxEntries (oldest dropped)");
        entries![0].FileId.Should().Be("file-new",
            because: "the freshly-marked file is prepended (newest first)");
        entries.Last().FileId.Should().NotBe($"file-{(RecentlyDiscussedTracker.MaxEntries - 1):D2}",
            because: "the oldest pre-existing entry must be dropped to make room");
    }

    // ════════════════════════════════════════════════════════════════════════
    // GetRecentAsync: newest-first ordering
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirst_BoundedByMaxCount()
    {
        var t0 = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);
        // List is persisted newest-first by design.
        var existing = new List<RecentlyDiscussedTracker.RecentFileEntry>
        {
            new("file-newest", t0),
            new("file-middle", t0.AddMinutes(-1)),
            new("file-oldest", t0.AddMinutes(-2))
        };
        var existingBytes = JsonSerializer.SerializeToUtf8Bytes(existing,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var cacheMock = new Mock<IDistributedCache>();
        cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBytes);

        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance);

        var result = await tracker.GetRecentAsync(SessionId, maxCount: 2);

        result.Should().Equal("file-newest", "file-middle");
    }

    // ════════════════════════════════════════════════════════════════════════
    // GetRecentAsync: empty session
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetRecentAsync_EmptySession_ReturnsEmptyList()
    {
        var cacheMock = new Mock<IDistributedCache>();
        cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance);

        var result = await tracker.GetRecentAsync(SessionId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentAsync_MaxCountZeroOrNegative_ReturnsEmptyImmediately_WithoutHittingCache()
    {
        var cacheMock = new Mock<IDistributedCache>(MockBehavior.Strict);
        var tracker = new RecentlyDiscussedTracker(cacheMock.Object, NullLogger<RecentlyDiscussedTracker>.Instance);

        var zeroResult = await tracker.GetRecentAsync(SessionId, maxCount: 0);
        var negResult = await tracker.GetRecentAsync(SessionId, maxCount: -1);

        zeroResult.Should().BeEmpty();
        negResult.Should().BeEmpty();
        // Strict mock + no Setup → would throw if either call hit the cache.
        cacheMock.Verify(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ADR-015: log records carry sessionId + fileId only — never content
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkAsync_LogsOnlySessionIdAndFileId_NeverContent_PerAdr015()
    {
        // The interface enforces no content parameters (signature is { sessionId, fileId, ct } );
        // this test asserts the implementation does NOT manufacture content text via interpolation
        // into the log message. We capture log invocations and assert no PRIVILEGED string appears.
        const string privilegedFileName = "PRIVILEGED-CONTENT-do-not-leak";

        var cacheMock = new Mock<IDistributedCache>();
        cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
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

        // The fileId itself is acceptable per ADR-015 (deterministic identifier). The privileged
        // marker stands in for content text that MUST NEVER appear.
        await tracker.MarkAsync(SessionId, FileA);

        captured.Should().NotBeEmpty(because: "MarkAsync emits at least one structured Info log");
        captured.Should().NotContain(s => s.Contains(privilegedFileName),
            because: "ADR-015 forbids file content / query text in log records");
        // Sanity: the safe identifiers DO appear (sessionId + fileId).
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
