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
/// Verifies FR-16: TenantCache emits cache.hits / cache.misses counters
/// (with a resource dimension) on the Sprk.Bff.Api.Cache meter (matches the
/// existing AddMeter registration in TelemetryModule.cs).
/// </summary>
public sealed class TenantCacheMetricsTests
{
    [Fact]
    public async Task GetAsync_MissThenHit_IncrementsMissesThenHits()
    {
        // Arrange — set up the MeterListener BEFORE constructing the SUT so it sees the
        // static Meter+Counters as they are first published (instrument enable runs in
        // the InstrumentPublished callback, which fires synchronously during static cctor).
        long hits = 0, misses = 0;
        string? hitResource = null, missResource = null;

        // Construct the SUT first to ensure the static Meter+Counter are published.
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var sut = new TenantCache(cache, NullLogger<TenantCache>.Instance);

        // Eagerly enable measurement events on the known instruments. We use both paths
        // (InstrumentPublished + post-Start direct enable) so the test is robust regardless
        // of whether the instruments were already published before this MeterListener
        // was constructed (process-level isolation can differ across test orderings).
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Sprk.Bff.Api.Cache" &&
                (instrument.Name == "cache.hits" || instrument.Name == "cache.misses"))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? resource = null;
            foreach (var t in tags)
            {
                if (t.Key == "resource") { resource = t.Value as string; break; }
            }
            if (instrument.Name == "cache.hits") { Interlocked.Add(ref hits, value); hitResource = resource; }
            else if (instrument.Name == "cache.misses") { Interlocked.Add(ref misses, value); missResource = resource; }
        });
        listener.Start();

        // Direct enable in case the instruments were already published before Start() —
        // works around process-level Meter caching across tests.
        listener.EnableMeasurementEvents(TenantCache.HitsCounter);
        listener.EnableMeasurementEvents(TenantCache.MissesCounter);

        // Act — first GetAsync is a miss
        var first = await sut.GetAsync<string>("t1", "session", "id-1", 1);
        await sut.SetAsync("t1", "session", "id-1", 1, "hello");
        var second = await sut.GetAsync<string>("t1", "session", "id-1", 1);

        // flush so callbacks fire deterministically
        listener.RecordObservableInstruments();

        // Assert
        first.Should().BeNull();
        second.Should().Be("hello");
        misses.Should().Be(1);
        hits.Should().Be(1);
        missResource.Should().Be("session");
        hitResource.Should().Be("session");
    }
}
