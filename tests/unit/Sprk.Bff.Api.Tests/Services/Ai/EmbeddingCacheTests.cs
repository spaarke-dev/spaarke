using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for EmbeddingCache - Redis-based embedding caching.
/// Tests caching, hashing, serialization, and graceful error handling.
/// Updated 2026-06-25 (redis remediation r1 task 013): tests now go through ITenantCache wrapper
/// (NFR-08 system-level allow-listed tenant scope "system").
/// </summary>
public class EmbeddingCacheTests
{
    private readonly Mock<ILogger<EmbeddingCache>> _loggerMock;

    // Test embedding (1536 dimensions like text-embedding-3-small)
    private readonly ReadOnlyMemory<float> _testEmbedding;

    public EmbeddingCacheTests()
    {
        _loggerMock = new Mock<ILogger<EmbeddingCache>>();

        // Create a test embedding vector (1536 dimensions)
        var embedding = new float[1536];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(i % 10) / 10f;
        }
        _testEmbedding = new ReadOnlyMemory<float>(embedding);
    }

    private static ITenantCache CreateTenantCache(IDistributedCache? distributedCache = null)
    {
        var dc = distributedCache ?? new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        return new TenantCache(dc, NullLogger<TenantCache>.Instance);
    }

    private EmbeddingCache CreateCache(CacheMetrics? metrics = null, ITenantCache? tenantCache = null)
    {
        return new EmbeddingCache(
            tenantCache ?? CreateTenantCache(),
            _loggerMock.Object,
            metrics);
    }

    #region ComputeContentHash Tests

    [Fact]
    public void ComputeContentHash_ValidContent_ReturnsBase64Hash()
    {
        var cache = CreateCache();

        var hash = cache.ComputeContentHash("test content");

        hash.Should().NotBeNullOrEmpty();
        // SHA256 Base64 is 44 characters (32 bytes = 256 bits → base64)
        hash.Should().HaveLength(44);
    }

    [Fact]
    public void ComputeContentHash_SameContent_ReturnsSameHash()
    {
        var cache = CreateCache();
        var content = "test content";

        var hash1 = cache.ComputeContentHash(content);
        var hash2 = cache.ComputeContentHash(content);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeContentHash_DifferentContent_ReturnsDifferentHashes()
    {
        var cache = CreateCache();

        var hash1 = cache.ComputeContentHash("content one");
        var hash2 = cache.ComputeContentHash("content two");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeContentHash_NullContent_ThrowsArgumentException()
    {
        var cache = CreateCache();

        var act = () => cache.ComputeContentHash(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeContentHash_EmptyContent_ThrowsArgumentException()
    {
        var cache = CreateCache();

        var act = () => cache.ComputeContentHash(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeContentHash_LongContent_ReturnsFixedLengthHash()
    {
        var cache = CreateCache();
        var longContent = new string('x', 10000);

        var hash = cache.ComputeContentHash(longContent);

        hash.Should().HaveLength(44);
    }

    #endregion

    #region GetEmbeddingAsync Tests

    [Fact]
    public async Task GetEmbeddingAsync_NullHash_ThrowsArgumentException()
    {
        var cache = CreateCache();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await cache.GetEmbeddingAsync(null!));
    }

    [Fact]
    public async Task GetEmbeddingAsync_EmptyHash_ThrowsArgumentException()
    {
        var cache = CreateCache();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await cache.GetEmbeddingAsync(string.Empty));
    }

    [Fact]
    public async Task GetEmbeddingAsync_CacheHit_ReturnsEmbedding()
    {
        // Arrange: write then read through the same ITenantCache instance.
        var tenantCache = CreateTenantCache();
        var cache = CreateCache(tenantCache: tenantCache);
        var contentHash = "dGVzdGhhc2g="; // base64 "testhash"

        await cache.SetEmbeddingAsync(contentHash, _testEmbedding);

        // Act
        var result = await cache.GetEmbeddingAsync(contentHash);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Length.Should().Be(1536);
    }

    [Fact]
    public async Task GetEmbeddingAsync_CacheMiss_ReturnsNull()
    {
        var cache = CreateCache();
        var contentHash = "dGVzdGhhc2g=";

        var result = await cache.GetEmbeddingAsync(contentHash);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetEmbeddingAsync_CacheError_ReturnsNullGracefully()
    {
        // Throwing ITenantCache to exercise the graceful-degradation path.
        var throwingTenantCache = new Mock<ITenantCache>();
        throwingTenantCache
            .Setup(c => c.GetAsync<byte[]>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        var cache = CreateCache(tenantCache: throwingTenantCache.Object);
        var contentHash = "dGVzdGhhc2g=";

        var result = await cache.GetEmbeddingAsync(contentHash);

        result.Should().BeNull();
    }

    #endregion

    #region SetEmbeddingAsync Tests

    [Fact]
    public async Task SetEmbeddingAsync_NullHash_ThrowsArgumentException()
    {
        var cache = CreateCache();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await cache.SetEmbeddingAsync(null!, _testEmbedding));
    }

    [Fact]
    public async Task SetEmbeddingAsync_EmptyHash_ThrowsArgumentException()
    {
        var cache = CreateCache();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await cache.SetEmbeddingAsync(string.Empty, _testEmbedding));
    }

    [Fact]
    public async Task SetEmbeddingAsync_EmptyEmbedding_ThrowsArgumentException()
    {
        var cache = CreateCache();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await cache.SetEmbeddingAsync("hash", ReadOnlyMemory<float>.Empty));
    }

    [Fact]
    public async Task SetEmbeddingAsync_ValidInput_StoresInCache()
    {
        // Verify by round-tripping through the wrapper.
        var tenantCache = CreateTenantCache();
        var cache = CreateCache(tenantCache: tenantCache);
        var contentHash = "dGVzdGhhc2g=";

        await cache.SetEmbeddingAsync(contentHash, _testEmbedding);

        // Read back to confirm the value landed.
        var retrieved = await cache.GetEmbeddingAsync(contentHash);
        retrieved.Should().NotBeNull();
        retrieved!.Value.Length.Should().Be(_testEmbedding.Length);
    }


    #endregion

    #region GetEmbeddingForContentAsync Tests

    [Fact]
    public async Task GetEmbeddingForContentAsync_ValidContent_ComputesHashAndLooksUp()
    {
        var tenantCache = CreateTenantCache();
        var cache = CreateCache(tenantCache: tenantCache);
        var content = "test content";

        await cache.SetEmbeddingForContentAsync(content, _testEmbedding);

        var result = await cache.GetEmbeddingForContentAsync(content);

        result.Should().NotBeNull();
        result!.Value.Length.Should().Be(1536);
    }

    #endregion

    #region SetEmbeddingForContentAsync Tests


    #endregion

    #region Serialization Tests

    [Fact]
    public async Task Serialization_RoundTrip_PreservesEmbedding()
    {
        var tenantCache = CreateTenantCache();
        var cache = CreateCache(tenantCache: tenantCache);
        var content = "test content for roundtrip";
        var contentHash = cache.ComputeContentHash(content);

        // Create embedding with known values
        var originalEmbedding = new float[1536];
        for (int i = 0; i < originalEmbedding.Length; i++)
        {
            originalEmbedding[i] = i * 0.001f;
        }
        var embedding = new ReadOnlyMemory<float>(originalEmbedding);

        await cache.SetEmbeddingAsync(contentHash, embedding);
        var retrieved = await cache.GetEmbeddingAsync(contentHash);

        retrieved.Should().NotBeNull();
        retrieved!.Value.Length.Should().Be(originalEmbedding.Length);

        var retrievedArray = retrieved.Value.ToArray();
        for (int i = 0; i < originalEmbedding.Length; i++)
        {
            retrievedArray[i].Should().BeApproximately(originalEmbedding[i], 0.0001f);
        }
    }

    #endregion
}
