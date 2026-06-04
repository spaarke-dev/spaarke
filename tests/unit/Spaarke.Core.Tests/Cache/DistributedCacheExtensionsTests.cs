using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Spaarke.Core.Cache;
using Xunit;

namespace Spaarke.Core.Tests.Cache;

/// <summary>
/// Tests for the generic <see cref="DistributedCacheExtensions.GetOrCreateAsync{T}(IDistributedCache, string, Func{CancellationToken, Task{T}}, TimeSpan, CancellationToken)"/>
/// helper introduced (relaxed to <c>where T : notnull</c>) by task 024.
///
/// Acceptance criteria from <c>projects/ai-spaarke-insights-engine-r1/tasks/024-generic-cache-helper.poml</c>:
///   - GetOrCreateAsync&lt;T&gt; invokes factory on cache miss and caches result with TTL.
///   - GetOrCreateAsync&lt;T&gt; returns cached value on cache hit without invoking factory.
///   - Generic over T (works with custom POCOs serialized via System.Text.Json).
///   - Existing caches unbroken (regression covered by the BFF API build, not these tests).
/// </summary>
public class DistributedCacheExtensionsTests
{
    private static IDistributedCache CreateMemoryCache()
        => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    public sealed record SamplePoco(string Name, int Value, IReadOnlyList<string> Tags);

    public readonly record struct SampleStruct(string Key, int Count);

    // ---------- Cache miss ----------

    [Fact]
    public async Task GetOrCreateAsync_CacheMiss_InvokesFactoryAndCachesResult()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var factoryInvocations = 0;
        var expected = new SamplePoco("alpha", 42, new[] { "tag1", "tag2" });

        // Act
        var result = await cache.GetOrCreateAsync(
            "test:miss",
            ct => { factoryInvocations++; return Task.FromResult(expected); },
            TimeSpan.FromMinutes(5));

        // Assert: factory invoked, value returned
        factoryInvocations.Should().Be(1);
        result.Should().BeEquivalentTo(expected);

        // Assert: value is in the cache (subsequent direct read returns the same JSON)
        var cachedRaw = await cache.GetStringAsync("test:miss");
        cachedRaw.Should().NotBeNull();
        var roundtripped = JsonSerializer.Deserialize<SamplePoco>(
            cachedRaw!,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        roundtripped.Should().BeEquivalentTo(expected);
    }

    // ---------- Cache hit ----------

    [Fact]
    public async Task GetOrCreateAsync_CacheHit_ReturnsCachedValueWithoutInvokingFactory()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var expected = new SamplePoco("beta", 7, new[] { "only" });
        var firstFactoryInvocations = 0;
        var secondFactoryInvocations = 0;

        // First call seeds the cache
        await cache.GetOrCreateAsync(
            "test:hit",
            ct => { firstFactoryInvocations++; return Task.FromResult(expected); },
            TimeSpan.FromMinutes(5));

        // Act: second call should hit the cache
        var result = await cache.GetOrCreateAsync(
            "test:hit",
            ct => { secondFactoryInvocations++; return Task.FromResult(new SamplePoco("WRONG", -1, Array.Empty<string>())); },
            TimeSpan.FromMinutes(5));

        // Assert
        firstFactoryInvocations.Should().Be(1, "first call seeds the cache");
        secondFactoryInvocations.Should().Be(0, "second call must NOT invoke the factory");
        result.Should().BeEquivalentTo(expected);
    }

    // ---------- TTL handling ----------

    [Fact]
    public async Task GetOrCreateAsync_TtlExpires_FactoryInvokedAgain()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var factoryInvocations = 0;
        var ttl = TimeSpan.FromMilliseconds(50);

        // Act 1: seed cache with short TTL
        await cache.GetOrCreateAsync(
            "test:ttl",
            ct => { factoryInvocations++; return Task.FromResult(new SamplePoco("v1", 1, Array.Empty<string>())); },
            ttl);

        // Wait past TTL
        await Task.Delay(150);

        // Act 2: cache should be expired, factory invoked again
        var result = await cache.GetOrCreateAsync(
            "test:ttl",
            ct => { factoryInvocations++; return Task.FromResult(new SamplePoco("v2", 2, Array.Empty<string>())); },
            ttl);

        // Assert
        factoryInvocations.Should().Be(2, "TTL expiration should force factory re-invocation");
        result.Name.Should().Be("v2");
        result.Value.Should().Be(2);
    }

    [Fact]
    public async Task GetOrCreateAsync_PersistsValueForTtlDuration()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var factoryInvocations = 0;
        var expected = new SamplePoco("persistent", 99, new[] { "alive" });

        // Act: seed with a long TTL
        await cache.GetOrCreateAsync(
            "test:persist",
            ct => { factoryInvocations++; return Task.FromResult(expected); },
            TimeSpan.FromMinutes(10));

        // Multiple subsequent reads should all hit the cache
        for (var i = 0; i < 5; i++)
        {
            var result = await cache.GetOrCreateAsync(
                "test:persist",
                ct => { factoryInvocations++; return Task.FromResult(new SamplePoco("nope", -1, Array.Empty<string>())); },
                TimeSpan.FromMinutes(10));
            result.Should().BeEquivalentTo(expected);
        }

        // Assert: factory invoked exactly once across all 6 calls
        factoryInvocations.Should().Be(1);
    }

    // ---------- Generic over T ----------

    [Fact]
    public async Task GetOrCreateAsync_GenericOverCustomPoco_RoundTripsCorrectly()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var expected = new SamplePoco(
            Name: "complex",
            Value: 12345,
            Tags: new[] { "alpha", "beta", "gamma" });

        // Act
        var seeded = await cache.GetOrCreateAsync(
            "test:poco",
            ct => Task.FromResult(expected),
            TimeSpan.FromMinutes(5));

        var retrieved = await cache.GetOrCreateAsync<SamplePoco>(
            "test:poco",
            ct => throw new InvalidOperationException("factory should not be invoked"),
            TimeSpan.FromMinutes(5));

        // Assert
        seeded.Should().BeEquivalentTo(expected);
        retrieved.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task GetOrCreateAsync_GenericOverPrimitive_StringRoundTrips()
    {
        // Arrange
        var cache = CreateMemoryCache();

        // Act
        var first = await cache.GetOrCreateAsync(
            "test:string",
            ct => Task.FromResult("hello-world"),
            TimeSpan.FromMinutes(5));
        var second = await cache.GetOrCreateAsync<string>(
            "test:string",
            ct => throw new InvalidOperationException("factory should not be invoked"),
            TimeSpan.FromMinutes(5));

        // Assert
        first.Should().Be("hello-world");
        second.Should().Be("hello-world");
    }

    [Fact]
    public async Task GetOrCreateAsync_GenericOverValueTypeRecordStruct_RoundTrips()
    {
        // Arrange — the Q5 motivation: the original `where T : class` excluded record structs.
        var cache = CreateMemoryCache();
        var expected = new SampleStruct("scope-A", 17);

        // Act
        var first = await cache.GetOrCreateAsync(
            "test:struct",
            ct => Task.FromResult(expected),
            TimeSpan.FromMinutes(5));
        var second = await cache.GetOrCreateAsync<SampleStruct>(
            "test:struct",
            ct => throw new InvalidOperationException("factory should not be invoked"),
            TimeSpan.FromMinutes(5));

        // Assert
        first.Should().Be(expected);
        second.Should().Be(expected);
    }

    // ---------- Cancellation token propagation ----------

    [Fact]
    public async Task GetOrCreateAsync_PropagatesCancellationTokenToFactory()
    {
        // Arrange
        var cache = CreateMemoryCache();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        CancellationToken receivedToken = default;

        // Act
        await cache.GetOrCreateAsync(
            "test:ct",
            ct => { receivedToken = ct; return Task.FromResult(new SamplePoco("x", 1, Array.Empty<string>())); },
            TimeSpan.FromMinutes(5),
            cts.Token);

        // Assert: factory received the same cancelled token
        receivedToken.IsCancellationRequested.Should().BeTrue();
    }

    // ---------- Convenience overload (no CancellationToken factory) ----------

    [Fact]
    public async Task GetOrCreateAsync_NoTokenFactoryOverload_WorksEquivalently()
    {
        // Arrange
        var cache = CreateMemoryCache();
        var factoryInvocations = 0;
        var expected = new SamplePoco("convenience", 3, Array.Empty<string>());

        // Act
        var first = await cache.GetOrCreateAsync(
            "test:no-token",
            () => { factoryInvocations++; return Task.FromResult(expected); },
            TimeSpan.FromMinutes(5));
        var second = await cache.GetOrCreateAsync(
            "test:no-token",
            () => { factoryInvocations++; return Task.FromResult(new SamplePoco("wrong", -1, Array.Empty<string>())); },
            TimeSpan.FromMinutes(5));

        // Assert
        factoryInvocations.Should().Be(1);
        first.Should().BeEquivalentTo(expected);
        second.Should().BeEquivalentTo(expected);
    }

    // ---------- Argument validation ----------

    [Fact]
    public async Task GetOrCreateAsync_NullKey_Throws()
    {
        var cache = CreateMemoryCache();

        Func<Task> act = () => cache.GetOrCreateAsync<SamplePoco>(
            null!,
            ct => Task.FromResult(new SamplePoco("x", 1, Array.Empty<string>())),
            TimeSpan.FromMinutes(5));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOrCreateAsync_EmptyKey_Throws()
    {
        var cache = CreateMemoryCache();

        Func<Task> act = () => cache.GetOrCreateAsync<SamplePoco>(
            string.Empty,
            ct => Task.FromResult(new SamplePoco("x", 1, Array.Empty<string>())),
            TimeSpan.FromMinutes(5));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOrCreateAsync_NullFactory_Throws()
    {
        var cache = CreateMemoryCache();

        Func<Task> act = () => cache.GetOrCreateAsync<SamplePoco>(
            "test:null-factory",
            (Func<CancellationToken, Task<SamplePoco>>)null!,
            TimeSpan.FromMinutes(5));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---------- Versioned-key overload (existing behaviour, regression coverage) ----------

    [Fact]
    public async Task GetOrCreateAsync_VersionedKey_DifferentVersionsAreIsolated()
    {
        var cache = CreateMemoryCache();

        var v1 = await cache.GetOrCreateAsync(
            "test:versioned",
            version: "v1",
            ct => Task.FromResult(new SamplePoco("first", 1, Array.Empty<string>())),
            TimeSpan.FromMinutes(5));

        var v2 = await cache.GetOrCreateAsync(
            "test:versioned",
            version: "v2",
            ct => Task.FromResult(new SamplePoco("second", 2, Array.Empty<string>())),
            TimeSpan.FromMinutes(5));

        v1.Name.Should().Be("first");
        v2.Name.Should().Be("second");
    }
}
