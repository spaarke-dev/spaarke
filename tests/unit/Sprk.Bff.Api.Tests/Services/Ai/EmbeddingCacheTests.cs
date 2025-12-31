using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for EmbeddingCache - Redis-based embedding caching.
/// Tests caching, hashing, serialization, and graceful error handling.
/// </summary>
public class EmbeddingCacheTests
{
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ILogger<EmbeddingCache>> _loggerMock;

    // Test embedding (1536 dimensions like text-embedding-3-small)
    private readonly ReadOnlyMemory<float> _testEmbedding;
    private readonly byte[] _serializedEmbedding;

    public EmbeddingCacheTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<EmbeddingCache>>();

        // Create a test embedding vector (1536 dimensions)
        var embedding = new float[1536];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(i % 10) / 10f;
        }
        _testEmbedding = new ReadOnlyMemory<float>(embedding);

        // Serialize for cache storage
        _serializedEmbedding = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, _serializedEmbedding, 0, _serializedEmbedding.Length);
    }

    private EmbeddingCache CreateCache(CacheMetrics? metrics = null)
    {
        return new EmbeddingCache(
            _cacheMock.Object,
            _loggerMock.Object,
            metrics);
    }

    #region ComputeContentHash Tests

    [Fact]
    public void ComputeContentHash_ValidContent_ReturnsBase64Hash()
    {
        // Arrange
        var cache = CreateCache();

        // Act
        var hash = cache.ComputeContentHash("test content");

        // Assert
        hash.Should().NotBeNullOrEmpty();
        // SHA256 Base64 is 44 characters (32 bytes = 256 bits → base64)
        hash.Should().HaveLength(44);
    }

    [Fact]
    public void ComputeContentHash_SameContent_ReturnsSameHash()
    {
        // Arrange
        var cache = CreateCache();
        var content = "test content";

        // Act
        var hash1 = cache.ComputeContentHash(content);
        var hash2 = cache.ComputeContentHash(content);

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeContentHash_DifferentContent_ReturnsDifferentHashes()
    {
        // Arrange
        var cache = CreateCache();

        // Act
        var hash1 = cache.ComputeContentHash("content one");
        var hash2 = cache.ComputeContentHash("content two");

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeContentHash_NullContent_ThrowsArgumentException()
    {
        // Arrange
        var cache = CreateCache();

        // Act & Assert
        var act = () => cache.ComputeContentHash(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeContentHash_EmptyContent_ThrowsArgumentException()
    {
        // Arrange
        var cache = CreateCache();

        // Act & Assert
        var act = () => cache.ComputeContentHash(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeContentHash_LongContent_ReturnsFixedLengthHash()
    {
        // Arrange
        var cache = CreateCache();
        var longContent = new string('x', 10000);

        // Act
        var hash = cache.ComputeContentHash(longContent);

        // Assert - Hash should always be same length
        hash.Should().HaveLength(44);
    }

    #endregion

    #region GetEmbeddingAsync Tests

    [Fact]
    public async Task GetEmbeddingAsync_NullHash_ThrowsArgumentException()
    {
        // Arrange
        var cache = CreateCache();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await cache.GetEmbeddingAsync(null!));
    }

    [Fact]
    public async Task GetEmbeddingAsync_EmptyHash_ThrowsArgumentException()
    {
        // Arrange
        var cache = CreateCache();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await cache.GetEmbeddingAsync(string.Empty));
    }

    [Fact]
    public async Task GetEmbeddingAsync_CacheHit_ReturnsEmbedding()
    {
        // Arrange
        var cache = CreateCache();
        var contentHash = "dGVzdGhhc2g="; // base64 "testhash"

        _cacheMock
            .Setup(x => x.GetAsync($"sdap:embedding:{contentHash}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_serializedEmbedding);

        // Act
        var result = await cache.GetEmbeddingAsync(contentHash);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Length.Should().Be(1536);
    }

    [Fact]
    public async Task GetEmbeddingAsync_CacheMiss_ReturnsNull()
    {
        // Arrange
        var cache = CreateCache();
        var contentHash = "dGVzdGhhc2g=";

        _cacheMock
            .Setup(x => x.GetAsync($"sdap:embedding:{contentHash}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Act
        var result = await cache.GetEmbeddingAsync(contentHash);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetEmbeddingAsync_CacheError_ReturnsNullGracefully()
    {
        // Arrange
        var cache = CreateCache();
        var contentHash = "dGVzdGhhc2g=";

        _cacheMock
            .Setup(x => x.GetAsync($"sdap:embedding:{contentHash}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        // Act
        var result = await cache.GetEmbeddingAsync(contentHash);

        // Assert - Should not throw, return null gracefully
        result.Should().BeNull();
    }

    // Note: CacheMetrics recording is tested implicitly through integration tests.
    // CacheMetrics methods are not virtual, so they cannot be mocked for unit testing.
    // The metrics functionality is optional (CacheMetrics parameter can be null).

    #endregion

    #region SetEmbeddingAsync Tests

    [Fact]
    public async Task SetEmbeddingAsync_NullHash_ThrowsArgumentException()
    {
        // Arrange
        var cache = CreateCache();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await cache.SetEmbeddingAsync(null!, _testEmbedding));
    }

    [Fact]
    public async Task SetEmbeddingAsync_EmptyHash_ThrowsArgumentException()
    {
        // Arrange
        var cache = CreateCache();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await cache.SetEmbeddingAsync(string.Empty, _testEmbedding));
    }

    [Fact]
    public async Task SetEmbeddingAsync_EmptyEmbedding_ThrowsArgumentException()
    {
        // Arrange
        var cache = CreateCache();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await cache.SetEmbeddingAsync("hash", ReadOnlyMemory<float>.Empty));
    }

    [Fact]
    public async Task SetEmbeddingAsync_ValidInput_StoresInCache()
    {
        // Arrange
        var cache = CreateCache();
        var contentHash = "dGVzdGhhc2g=";

        // Act
        await cache.SetEmbeddingAsync(contentHash, _testEmbedding);

        // Assert
        _cacheMock.Verify(
            x => x.SetAsync(
                $"sdap:embedding:{contentHash}",
                It.Is<byte[]>(b => b.Length == _serializedEmbedding.Length),
                It.Is<DistributedCacheEntryOptions>(o =>
                    o.AbsoluteExpirationRelativeToNow == TimeSpan.FromDays(7)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetEmbeddingAsync_CacheError_DoesNotThrow()
    {
        // Arrange
        var cache = CreateCache();
        var contentHash = "dGVzdGhhc2g=";

        _cacheMock
            .Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        // Act & Assert - Should not throw
        await cache.SetEmbeddingAsync(contentHash, _testEmbedding);
    }

    #endregion

    #region GetEmbeddingForContentAsync Tests

    [Fact]
    public async Task GetEmbeddingForContentAsync_ValidContent_ComputesHashAndLooksUp()
    {
        // Arrange
        var cache = CreateCache();
        var content = "test content";

        // Compute expected hash
        var expectedHash = cache.ComputeContentHash(content);

        _cacheMock
            .Setup(x => x.GetAsync($"sdap:embedding:{expectedHash}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_serializedEmbedding);

        // Act
        var result = await cache.GetEmbeddingForContentAsync(content);

        // Assert
        result.Should().NotBeNull();
        _cacheMock.Verify(
            x => x.GetAsync($"sdap:embedding:{expectedHash}", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region SetEmbeddingForContentAsync Tests

    [Fact]
    public async Task SetEmbeddingForContentAsync_ValidContent_ComputesHashAndStores()
    {
        // Arrange
        var cache = CreateCache();
        var content = "test content";

        // Compute expected hash
        var expectedHash = cache.ComputeContentHash(content);

        // Act
        await cache.SetEmbeddingForContentAsync(content, _testEmbedding);

        // Assert
        _cacheMock.Verify(
            x => x.SetAsync(
                $"sdap:embedding:{expectedHash}",
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Serialization Tests

    [Fact]
    public async Task Serialization_RoundTrip_PreservesEmbedding()
    {
        // Arrange
        var cache = CreateCache();
        var content = "test content for roundtrip";
        var contentHash = cache.ComputeContentHash(content);

        // Create embedding with known values
        var originalEmbedding = new float[1536];
        for (int i = 0; i < originalEmbedding.Length; i++)
        {
            originalEmbedding[i] = i * 0.001f;
        }
        var embedding = new ReadOnlyMemory<float>(originalEmbedding);

        // Capture what gets stored
        byte[]? storedData = null;
        _cacheMock
            .Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (key, data, options, ct) => storedData = data);

        // Store embedding
        await cache.SetEmbeddingAsync(contentHash, embedding);

        // Setup retrieval to return stored data
        _cacheMock
            .Setup(x => x.GetAsync($"sdap:embedding:{contentHash}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedData);

        // Retrieve embedding
        var retrieved = await cache.GetEmbeddingAsync(contentHash);

        // Assert - Values should match
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
