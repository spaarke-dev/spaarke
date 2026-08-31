using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Cache;

/// <summary>
/// Verifies FR-16: cache.hits / cache.misses Meter counters fire on the
/// Sprk.Bff.Api.Cache meter (matches the existing AddMeter registration in
/// TelemetryModule.cs) when a typical TenantCache call path is exercised end-to-end.
///
/// R7-S7 sub-gap #2 closure (2026-06-26): emission moved from TenantCache to the
/// MetricsDistributedCache decorator so all cache I/O — including the system-cache
/// exception path — is counted exactly once. This test now wraps the inner cache
/// with MetricsDistributedCache (mirroring the production DI registration in
/// CacheModule.DecorateDistributedCacheWithMetrics) so it still proves the
/// end-to-end metric flow.
///
/// <para><b>FLAKE FIXED 2026-08-27</b> (unified-access-control-r2, on merging task 079).
/// This class failed ~2 of 5 full-suite runs and passed in isolation every time. The
/// mechanism, stated precisely because the first diagnosis was imprecise and would have
/// sent a fixer at the wrong seam: the accumulators below are LOCAL variables — nothing
/// is shared there. What is process-global is the <b>instrument</b>
/// (<c>CacheMetrics.HitsCounter</c>/<c>MissesCounter</c> are <c>static readonly</c> on one
/// <c>Meter</c>), and a <c>MeterListener</c> subscribes <b>by instrument, not by emitter</b>.
/// So every parallel xUnit class that touches a <c>MetricsDistributedCache</c>-decorated
/// cache landed in this test's counters. Because a foreign emitter can only ADD, the
/// failure was always an over-count.</para>
///
/// <para><b>Why the obvious fixes are wrong.</b> Relaxing to <c>BeGreaterOrEqualTo(1)</c>
/// would delete the test's reason to exist — <i>counted exactly once</i> IS the invariant
/// the R7-S7 closure established, and the relaxed form passes even if the decorator
/// double-counts. Tags cannot discriminate either: every decorator emission carries the
/// same <c>tier=raw</c>. And xUnit 2.9.x has no per-collection
/// <c>DisableParallelization</c> (that is v3), while disabling parallelism assembly-wide
/// to fix one test is not a trade worth making on a 3-minute suite.</para>
///
/// <para><b>The fix: an <see cref="AsyncLocal{T}"/> correlation token, test-side only.</b>
/// <c>Counter.Add</c> invokes listener callbacks synchronously and inline, inside the
/// emitting call's execution context — so a token set here flows through
/// <c>TenantCache</c> → <c>MetricsDistributedCache</c> → <c>Counter.Add</c> → the callback,
/// while a concurrent test's emission carries a different token (or none). Counting only
/// our own emissions keeps the exactly-once assertion intact and needs NO production
/// change. <see cref="Emissions_FromAConcurrentEmitter_AreNotCounted"/> is the control
/// that proves the isolation actually isolates — without it this would just be a green
/// test that might still be blind.</para>
/// </summary>
public sealed class TenantCacheMetricsTests
{
    /// <summary>
    /// Correlation token. Set inside the test's own async flow; read in the meter callback to
    /// discard measurements emitted by any other test running in parallel.
    /// </summary>
    private static readonly AsyncLocal<Guid> EmissionScope = new();

    private sealed record Tally(Func<long> Hits, Func<long> Misses);

    /// <summary>
    /// Starts a listener that counts <c>cache.hits</c>/<c>cache.misses</c> ONLY for emissions
    /// originating inside the supplied scope token.
    /// </summary>
    private static (MeterListener Listener, Tally Tally) ListenScoped(Guid scope)
    {
        long hits = 0, misses = 0;

        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == CacheMetrics.MeterName &&
                    (instrument.Name == "cache.hits" || instrument.Name == "cache.misses"))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            // The discriminator. Without it this callback counts the whole process.
            if (EmissionScope.Value != scope)
            {
                return;
            }

            if (instrument.Name == "cache.hits") { Interlocked.Add(ref hits, value); }
            else if (instrument.Name == "cache.misses") { Interlocked.Add(ref misses, value); }
        });

        listener.Start();
        listener.EnableMeasurementEvents(CacheMetrics.HitsCounter);
        listener.EnableMeasurementEvents(CacheMetrics.MissesCounter);

        return (listener, new Tally(() => Interlocked.Read(ref hits), () => Interlocked.Read(ref misses)));
    }

    private static TenantCache NewProductionEquivalentCache()
    {
        // Production-equivalent wiring: inner cache + MetricsDistributedCache decorator + TenantCache wrapper.
        var inner = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var decorated = new MetricsDistributedCache(inner);
        return new TenantCache(decorated, NullLogger<TenantCache>.Instance);
    }

    [Fact]
    public async Task GetAsync_MissThenHit_IncrementsMissesThenHits()
    {
        // Arrange
        var scope = Guid.NewGuid();
        EmissionScope.Value = scope;

        var sut = NewProductionEquivalentCache();
        var (listener, tally) = ListenScoped(scope);
        using var _ = listener;

        // Act
        var first = await sut.GetAsync<string>("t1", "session", "id-1", 1);
        await sut.SetAsync("t1", "session", "id-1", 1, "hello");
        var second = await sut.GetAsync<string>("t1", "session", "id-1", 1);

        // Assert
        first.Should().BeNull();
        second.Should().Be("hello");
        tally.Misses().Should().Be(1);
        tally.Hits().Should().Be(1);
    }

    /// <summary>
    /// THE CONTROL for the flake fix. Reproduces the race deterministically: a second cache,
    /// outside this test's emission scope, performs a miss and a hit WHILE the listener is
    /// running. Before the AsyncLocal discriminator this test's counters would read 2/2 —
    /// which is exactly the over-count the full suite hit intermittently. Asserting 1/1 here
    /// proves the isolation works, rather than assuming it because the suite went green.
    /// </summary>
    [Fact]
    public async Task Emissions_FromAConcurrentEmitter_AreNotCounted()
    {
        // Arrange
        var scope = Guid.NewGuid();
        EmissionScope.Value = scope;

        var sut = NewProductionEquivalentCache();
        var (listener, tally) = ListenScoped(scope);
        using var _ = listener;

        // Act — our own miss + hit …
        await sut.GetAsync<string>("t1", "session", "id-1", 1);
        await sut.SetAsync("t1", "session", "id-1", 1, "hello");
        await sut.GetAsync<string>("t1", "session", "id-1", 1);

        // … and a foreign emitter on a flow that never inherited our token, standing in for a
        // parallel xUnit class.
        //
        // ExecutionContext.SuppressFlow() is load-bearing and was learned the hard way: the first
        // version of this control used a bare Task.Run and FAILED at 2/2. Task.Run *flows* the
        // ExecutionContext, so the "foreign" emitter inherited EmissionScope.Value and was counted —
        // the control was modelling the wrong thing, not the fix failing. Suppressing the flow gives
        // the delegate a default token, which is what a separate xUnit test class actually has.
        //
        // The caveat this exposes, stated rather than hidden: AsyncLocal isolates flows that do NOT
        // DESCEND from this one. A cache operation started as a CHILD of this test would still be
        // counted. That is correct for the real failure mode (parallel test classes are separate
        // roots) and would be wrong if this test ever spawned its own background cache work.
        Task foreignWork;
        using (ExecutionContext.SuppressFlow())
        {
            foreignWork = Task.Run(async () =>
            {
                var foreign = NewProductionEquivalentCache();
                await foreign.GetAsync<string>("t2", "session", "id-2", 1);   // miss
                await foreign.SetAsync("t2", "session", "id-2", 1, "other");
                await foreign.GetAsync<string>("t2", "session", "id-2", 1);   // hit
            });
        }

        await foreignWork;

        // Assert — the foreign miss/hit must NOT appear in our tally.
        tally.Misses().Should().Be(1, "a parallel test's cache miss must not be attributed to this one");
        tally.Hits().Should().Be(1, "a parallel test's cache hit must not be attributed to this one");
    }
}
