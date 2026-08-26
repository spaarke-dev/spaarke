// -----------------------------------------------------------------------------
// DispatchIdempotencyServiceTests.cs
//
// L2 CONTROL-PLANE tests for the Redis-backed DispatchIdempotencyService
// (task 105, Phase C'' Wave G-1). Mirrors the BFF's
// Sprk.Bff.Api.Services.Jobs.IdempotencyService test coverage shape --
// canonical reference cited in this task's POML -- against
// AddDistributedMemoryCache() (in-memory IDistributedCache) so the suite
// runs with zero live Redis dependency, plus a throwing fake IDistributedCache
// to exercise the fail-open branches that a live-Redis outage would trigger.
//
// TESTED BEHAVIORS (POML 105 acceptance criterion #2 + step 3):
//   - Processed-marker set/hit round trip (MarkProcessedAsync -> IsProcessedAsync).
//   - Lock acquire/release round trip; a second acquire while held returns false;
//     after release (or TTL-elapsed simulation), a subsequent acquire succeeds.
//   - Lock TTL is sourced from the CALLER-supplied ttl parameter (i.e. what
//     production wires as DispatcherOptions.MaxHandlerDuration), NOT a
//     hard-coded 5-min BFF-style default -- verified via a config-override
//     test using a fake IDistributedCache that records the TTL passed to
//     SetAsync.
//   - Fail-open on every method when the cache throws: IsProcessedAsync ->
//     false, TryAcquireLockAsync -> true, MarkProcessedAsync / ReleaseLockAsync
//     -> swallow (no throw).
//   - Key format matches DS-2 §4-L2:
//     "provisioning:idempotency:processed:{messageId}" /
//     "provisioning:idempotency:lock:{messageId}".
//
// SEAM STRATEGY (docs/standards/TEST-ARCHITECTURE.md §5):
//   AddDistributedMemoryCache() for the happy-path round-trip tests (real
//   IDistributedCache contract, zero external dependency -- ADR-038 KEEP
//   category: unit test, in-process, no external resource). A hand-rolled
//   ThrowingDistributedCache + a hand-rolled RecordingDistributedCache
//   (records SetAsync key/value/options without touching a real store) cover
//   the fail-open + TTL-assertion branches that AddDistributedMemoryCache
//   cannot deterministically simulate (a real MemoryDistributedCache does not
//   expose a way to force GetAsync/SetAsync to throw, and its TTL eviction is
//   wall-clock-driven rather than inspectable). No Moq -- matches the
//   project's hand-rolled-fake convention (StateReconcilerServiceTests,
//   HandlerOutcomeApplierTests, ReconcilerEnqueuePayloadAttemptTests).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Dispatch;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

public sealed class DispatchIdempotencyServiceTests
{
    private const string MessageId = "abc123deadbeef";

    // -------------------------------------------------------------------
    // Happy path -- real in-memory IDistributedCache (AddDistributedMemoryCache).
    // -------------------------------------------------------------------

    [Fact]
    public async Task IsProcessedAsync_NoPriorMark_ReturnsFalse()
    {
        var sut = BuildSutWithMemoryCache();

        var result = await sut.IsProcessedAsync(MessageId, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MarkProcessedAsync_ThenIsProcessedAsync_ReturnsTrue()
    {
        var sut = BuildSutWithMemoryCache();

        await sut.MarkProcessedAsync(MessageId, CancellationToken.None);
        var result = await sut.IsProcessedAsync(MessageId, CancellationToken.None);

        result.Should().BeTrue(
            "MarkProcessedAsync sets the processed-marker; a subsequent IsProcessedAsync " +
            "within the TTL window must observe it (DS-2 §2.5 step 3 duplicate-suppression).");
    }

    [Fact]
    public async Task TryAcquireLockAsync_NoPriorLock_ReturnsTrue()
    {
        var sut = BuildSutWithMemoryCache();

        var acquired = await sut.TryAcquireLockAsync(MessageId, TimeSpan.FromMinutes(65), CancellationToken.None);

        acquired.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireLockAsync_SecondCallWhileHeld_ReturnsFalse()
    {
        var sut = BuildSutWithMemoryCache();
        await sut.TryAcquireLockAsync(MessageId, TimeSpan.FromMinutes(65), CancellationToken.None);

        var secondAcquire = await sut.TryAcquireLockAsync(MessageId, TimeSpan.FromMinutes(65), CancellationToken.None);

        secondAcquire.Should().BeFalse(
            "a peer dispatcher instance holds the lock -- DS-2 §2.5 step 3 maps this to Abandon.");
    }

    [Fact]
    public async Task ReleaseLockAsync_ThenTryAcquireLockAsync_ReturnsTrueAgain()
    {
        var sut = BuildSutWithMemoryCache();
        await sut.TryAcquireLockAsync(MessageId, TimeSpan.FromMinutes(65), CancellationToken.None);

        await sut.ReleaseLockAsync(MessageId, CancellationToken.None);
        var reacquired = await sut.TryAcquireLockAsync(MessageId, TimeSpan.FromMinutes(65), CancellationToken.None);

        reacquired.Should().BeTrue(
            "ReleaseLockAsync clears the lock key so a subsequent acquire (e.g. after a " +
            "successful dispatch, or a redelivery once the peer's own release ran) succeeds.");
    }

    [Fact]
    public async Task ReleaseLockAsync_AbsentKey_DoesNotThrow()
    {
        var sut = BuildSutWithMemoryCache();

        var act = async () => await sut.ReleaseLockAsync("never-locked", CancellationToken.None);

        await act.Should().NotThrowAsync(
            "ReleaseLockAsync is idempotent -- absent-key MUST NOT be an error per the interface contract.");
    }

    [Fact]
    public async Task DifferentMessageIds_AreIndependent()
    {
        var sut = BuildSutWithMemoryCache();

        await sut.MarkProcessedAsync("message-a", CancellationToken.None);

        (await sut.IsProcessedAsync("message-a", CancellationToken.None)).Should().BeTrue();
        (await sut.IsProcessedAsync("message-b", CancellationToken.None)).Should().BeFalse(
            "processed-marker keys are per-messageId -- marking one message must not affect another.");
    }

    // -------------------------------------------------------------------
    // Key format -- DS-2 §4-L2 verbatim contract.
    // -------------------------------------------------------------------

    [Fact]
    public async Task MarkProcessedAsync_UsesCanonicalProcessedKeyPrefix()
    {
        var recorder = new RecordingDistributedCache();
        var sut = new DispatchIdempotencyService(recorder, NullLogger<DispatchIdempotencyService>.Instance);

        await sut.MarkProcessedAsync(MessageId, CancellationToken.None);

        recorder.SetCalls.Should().ContainSingle()
            .Which.Key.Should().Be($"provisioning:idempotency:processed:{MessageId}",
                "DS-2 §4-L2 fixes the processed key format verbatim.");
    }

    [Fact]
    public async Task TryAcquireLockAsync_UsesCanonicalLockKeyPrefix()
    {
        var recorder = new RecordingDistributedCache();
        var sut = new DispatchIdempotencyService(recorder, NullLogger<DispatchIdempotencyService>.Instance);

        await sut.TryAcquireLockAsync(MessageId, TimeSpan.FromMinutes(65), CancellationToken.None);

        recorder.SetCalls.Should().ContainSingle()
            .Which.Key.Should().Be($"provisioning:idempotency:lock:{MessageId}",
                "DS-2 §4-L2 fixes the lock key format verbatim.");
    }

    // -------------------------------------------------------------------
    // TTL -- lock TTL is sourced from the CALLER-supplied parameter (i.e.
    // production wires DispatcherOptions.MaxHandlerDuration), never a
    // hard-coded BFF-style 5-min default (POML acceptance criterion #2).
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(65)]
    [InlineData(90)]
    [InlineData(15)]
    public async Task TryAcquireLockAsync_PassesCallerSuppliedTtl_NotAHardCodedDefault(int ttlMinutes)
    {
        var recorder = new RecordingDistributedCache();
        var sut = new DispatchIdempotencyService(recorder, NullLogger<DispatchIdempotencyService>.Instance);
        var ttl = TimeSpan.FromMinutes(ttlMinutes);

        await sut.TryAcquireLockAsync(MessageId, ttl, CancellationToken.None);

        recorder.SetCalls.Should().ContainSingle()
            .Which.Options.AbsoluteExpirationRelativeToNow.Should().Be(
                ttl,
                "the lock TTL MUST equal the ttl parameter callers supply (production: " +
                "DispatcherOptions.MaxHandlerDuration, default 65 min -- NOT the BFF " +
                "IdempotencyService's hard-coded 5-min DefaultLockDuration) because a " +
                "30-60 min handler holds the lock for its whole runtime with no mid-flight " +
                "renewal on IDistributedCache (DS-2 §4-L2).");
    }

    [Fact]
    public async Task MarkProcessedAsync_UsesTwentyFourHourTtl_IndependentOfLockTtl()
    {
        var recorder = new RecordingDistributedCache();
        var sut = new DispatchIdempotencyService(recorder, NullLogger<DispatchIdempotencyService>.Instance);

        await sut.MarkProcessedAsync(MessageId, CancellationToken.None);

        recorder.SetCalls.Should().ContainSingle()
            .Which.Options.AbsoluteExpirationRelativeToNow.Should().Be(
                TimeSpan.FromHours(24),
                "DS-2 §4-L2 fixes the processed-marker TTL at 24h -- independent of " +
                "DispatcherOptions.MaxHandlerDuration, which only scopes the LOCK TTL.");
    }

    // -------------------------------------------------------------------
    // Fail-open -- every method degrades to the PERMISSIVE outcome on cache
    // failure (mirror of BFF IdempotencyService.cs:39-44,92-97).
    // -------------------------------------------------------------------

    [Fact]
    public async Task IsProcessedAsync_CacheThrows_FailsOpen_ReturnsFalse()
    {
        var sut = new DispatchIdempotencyService(
            new ThrowingDistributedCache(), NullLogger<DispatchIdempotencyService>.Instance);

        var result = await sut.IsProcessedAsync(MessageId, CancellationToken.None);

        result.Should().BeFalse(
            "on cache outage, IsProcessedAsync MUST fail open (treat as NOT a duplicate) -- " +
            "L1 (SB dedup) + L3 (handler-body dedup) backstop correctness per DS-2 §4-L2.");
    }

    [Fact]
    public async Task TryAcquireLockAsync_CacheThrows_FailsOpen_ReturnsTrue()
    {
        var sut = new DispatchIdempotencyService(
            new ThrowingDistributedCache(), NullLogger<DispatchIdempotencyService>.Instance);

        var acquired = await sut.TryAcquireLockAsync(MessageId, TimeSpan.FromMinutes(65), CancellationToken.None);

        acquired.Should().BeTrue(
            "on cache outage, TryAcquireLockAsync MUST fail open (grant the lock) -- " +
            "provisioning must not hard-depend on cache availability per DS-2 §4-L2.");
    }

    [Fact]
    public async Task MarkProcessedAsync_CacheThrows_SwallowsException()
    {
        var sut = new DispatchIdempotencyService(
            new ThrowingDistributedCache(), NullLogger<DispatchIdempotencyService>.Instance);

        var act = async () => await sut.MarkProcessedAsync(MessageId, CancellationToken.None);

        await act.Should().NotThrowAsync(
            "the Cosmos transition already landed by the time MarkProcessedAsync runs -- a " +
            "cache-write failure here must not fail the whole dispatch.");
    }

    [Fact]
    public async Task ReleaseLockAsync_CacheThrows_SwallowsException()
    {
        var sut = new DispatchIdempotencyService(
            new ThrowingDistributedCache(), NullLogger<DispatchIdempotencyService>.Instance);

        var act = async () => await sut.ReleaseLockAsync(MessageId, CancellationToken.None);

        await act.Should().NotThrowAsync(
            "the lock self-expires at its TTL -- a release failure must not deadlock the message.");
    }

    // -------------------------------------------------------------------
    // Argument validation (ArgumentException.ThrowIfNullOrWhiteSpace parity
    // with NoOpDispatchIdempotencyService + the interface's documented contract).
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IsProcessedAsync_NullOrWhitespaceMessageId_Throws(string? messageId)
    {
        var sut = BuildSutWithMemoryCache();

        var act = async () => await sut.IsProcessedAsync(messageId!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // -------------------------------------------------------------------
    // Helpers + test doubles.
    //
    // NOTE: constructor null-guard tests (ArgumentNullException on null
    // cache/logger) are DELIBERATELY OMITTED -- ADR-038 §4 (docs/adr/
    // ADR-038-testing-strategy.md) bans constructor null-check tests as a
    // scaffolding class (B4): the compiler's nullable-reference-types
    // annotations already communicate the non-null contract; the runtime
    // guard exists for callers who bypass NRT (e.g. reflection, external
    // assemblies), and a test asserting `ArgumentNullException.ThrowIfNull`
    // fires protects zero real behavior.
    // -------------------------------------------------------------------

    private static DispatchIdempotencyService BuildSutWithMemoryCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        return new DispatchIdempotencyService(cache, NullLogger<DispatchIdempotencyService>.Instance);
    }

    /// <summary>
    /// Records every SetAsync call's key/value/options without touching a
    /// real store. GetAsync always misses (returns null) -- these tests only
    /// need to inspect what DispatchIdempotencyService WROTE, not round-trip
    /// reads.
    /// </summary>
    private sealed class RecordingDistributedCache : IDistributedCache
    {
        public List<(string Key, byte[] Value, DistributedCacheEntryOptions Options)> SetCalls { get; } = new();

        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult<byte[]?>(null);

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            SetCalls.Add((key, value, options));

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            SetCalls.Add((key, value, options));
            return Task.CompletedTask;
        }
    }

    /// <summary>Every member throws -- simulates a Redis outage for the fail-open tests.</summary>
    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        private static InvalidOperationException Simulated() =>
            new("Simulated cache outage (RedisConnectionException analog).");

        public byte[]? Get(string key) => throw Simulated();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Simulated();
        public void Refresh(string key) => throw Simulated();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Simulated();
        public void Remove(string key) => throw Simulated();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Simulated();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Simulated();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw Simulated();
    }
}
