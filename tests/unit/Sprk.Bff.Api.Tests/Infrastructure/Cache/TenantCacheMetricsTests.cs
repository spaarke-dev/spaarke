using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Cache;
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
/// </summary>
public sealed class TenantCacheMetricsTests
{
    [Fact]
    public async Task GetAsync_MissThenHit_IncrementsMissesThenHits()
    {
        // Arrange
        long hits = 0, misses = 0;

        // Production-equivalent wiring: inner cache + MetricsDistributedCache decorator + TenantCache wrapper.
        var inner = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var decorated = new MetricsDistributedCache(inner);
        var sut = new TenantCache(decorated, NullLogger<TenantCache>.Instance);

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Sprk.Bff.Api.Cache" &&
                (instrument.Name == "cache.hits" || instrument.Name == "cache.misses"))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "cache.hits") { Interlocked.Add(ref hits, value); }
            else if (instrument.Name == "cache.misses") { Interlocked.Add(ref misses, value); }
        });
        listener.Start();
        listener.EnableMeasurementEvents(TenantCache.HitsCounter);
        listener.EnableMeasurementEvents(TenantCache.MissesCounter);

        // Act
        var first = await sut.GetAsync<string>("t1", "session", "id-1", 1);
        await sut.SetAsync("t1", "session", "id-1", 1, "hello");
        var second = await sut.GetAsync<string>("t1", "session", "id-1", 1);
        listener.RecordObservableInstruments();

        // Assert
        first.Should().BeNull();
        second.Should().Be("hello");
        misses.Should().Be(1);
        hits.Should().Be(1);
    }
}
