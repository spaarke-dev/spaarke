using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Spaarke.Core.Cache;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class RequestCacheTests
{
    [Fact]
    public void Get_WhenKeyNotExists_ReturnsNull()
    {
        var cache = new RequestCache();

        var result = cache.Get<string>("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public void Set_And_Get_ReturnsValue()
    {
        var cache = new RequestCache();
        var testValue = "test-value";

        cache.Set("test-key", testValue);
        var result = cache.Get<string>("test-key");

        result.Should().Be(testValue);
    }

    [Fact]
    public void GetOrCreate_WhenKeyNotExists_CallsFactory()
    {
        var cache = new RequestCache();
        var factoryCalled = false;

        var result = cache.GetOrCreate("test-key", () =>
        {
            factoryCalled = true;
            return "factory-value";
        });

        result.Should().Be("factory-value");
        factoryCalled.Should().BeTrue();
        cache.Get<string>("test-key").Should().Be("factory-value");
    }

    [Fact]
    public void GetOrCreate_WhenKeyExists_DoesNotCallFactory()
    {
        var cache = new RequestCache();
        cache.Set("test-key", "existing-value");
        var factoryCalled = false;

        var result = cache.GetOrCreate("test-key", () =>
        {
            factoryCalled = true;
            return "factory-value";
        });

        result.Should().Be("existing-value");
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenKeyNotExists_CallsFactory()
    {
        var cache = new RequestCache();
        var factoryCalled = false;

        var result = await cache.GetOrCreateAsync("test-key", async () =>
        {
            factoryCalled = true;
            await Task.Delay(1);
            return "factory-value";
        });

        result.Should().Be("factory-value");
        factoryCalled.Should().BeTrue();
        cache.Get<string>("test-key").Should().Be("factory-value");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenKeyExists_DoesNotCallFactory()
    {
        var cache = new RequestCache();
        cache.Set("test-key", "existing-value");
        var factoryCalled = false;

        var result = await cache.GetOrCreateAsync("test-key", async () =>
        {
            factoryCalled = true;
            await Task.Delay(1);
            return "factory-value";
        });

        result.Should().Be("existing-value");
        factoryCalled.Should().BeFalse();
    }
}

public class DistributedCacheExtensionsTests
{
    [Fact]
    public async Task GetOrCreateAsync_WhenKeyNotExists_CallsFactory()
    {
        var options = Options.Create(new MemoryDistributedCacheOptions());
        var memoryCache = new MemoryDistributedCache(options);
        var factoryCalled = false;

        var result = await memoryCache.GetOrCreateAsync("test-key", async () =>
        {
            factoryCalled = true;
            await Task.Delay(1);
            return "factory-value";
        }, TimeSpan.FromMinutes(5));

        result.Should().Be("factory-value");
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenKeyExists_DoesNotCallFactory()
    {
        var options = Options.Create(new MemoryDistributedCacheOptions());
        var memoryCache = new MemoryDistributedCache(options);
        await memoryCache.SetStringAsync("test-key", JsonSerializer.Serialize("existing-value"));
        var factoryCalled = false;

        var result = await memoryCache.GetOrCreateAsync("test-key", async () =>
        {
            factoryCalled = true;
            await Task.Delay(1);
            return "factory-value";
        }, TimeSpan.FromMinutes(5));

        result.Should().Be("existing-value");
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrCreateAsync_WithVersionKey_CreatesVersionedKey()
    {
        var options = Options.Create(new MemoryDistributedCacheOptions());
        var memoryCache = new MemoryDistributedCache(options);

        var result = await memoryCache.GetOrCreateAsync("test-key", "v1", async () =>
        {
            await Task.Delay(1);
            return "versioned-value";
        }, TimeSpan.FromMinutes(5));

        result.Should().Be("versioned-value");

        // Should be able to retrieve with the same version
        var result2 = await memoryCache.GetOrCreateAsync<string>("test-key", "v1", () =>
        {
            throw new InvalidOperationException("Should not call factory");
        }, TimeSpan.FromMinutes(5));

        result2.Should().Be("versioned-value");
    }

    [Fact]
    public async Task GetOrCreateAsync_WithDifferentVersionKey_CallsFactoryAgain()
    {
        var options = Options.Create(new MemoryDistributedCacheOptions());
        var memoryCache = new MemoryDistributedCache(options);

        await memoryCache.GetOrCreateAsync("test-key", "v1", async () =>
        {
            await Task.Delay(1);
            return "versioned-value-v1";
        }, TimeSpan.FromMinutes(5));

        var result = await memoryCache.GetOrCreateAsync("test-key", "v2", async () =>
        {
            await Task.Delay(1);
            return "versioned-value-v2";
        }, TimeSpan.FromMinutes(5));

        result.Should().Be("versioned-value-v2");
    }

    [Fact]
    public async Task GetOrCreateAsync_WithCancellationToken_PropagatesToken()
    {
        var options = Options.Create(new MemoryDistributedCacheOptions());
        var memoryCache = new MemoryDistributedCache(options);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await memoryCache.GetOrCreateAsync<string>("test-key", (ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult("value");
            }, TimeSpan.FromMinutes(5), cts.Token);
        });
    }
}
