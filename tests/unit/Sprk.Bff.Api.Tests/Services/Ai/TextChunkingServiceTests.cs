using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for TextChunkingService - text chunking for RAG pipeline.
/// Tests chunk size, overlap, sentence boundary preservation, and edge cases.
/// </summary>
public class TextChunkingServiceTests
{
    private readonly Mock<ILogger<TextChunkingService>> _loggerMock;

    public TextChunkingServiceTests()
    {
        _loggerMock = new Mock<ILogger<TextChunkingService>>();
    }

    private TextChunkingService CreateService()
    {
        return new TextChunkingService(_loggerMock.Object);
    }

    #region Empty/Null Text Tests

    [Fact]
    public async Task ChunkTextAsync_EmptyText_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.ChunkTextAsync(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ChunkTextAsync_NullText_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.ChunkTextAsync(null);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region Single Chunk Tests

    [Fact]
    public async Task ChunkTextAsync_TextShorterThanChunkSize_ReturnsSingleChunk()
    {
        // Arrange
        var service = CreateService();
        var text = "This is a short text that is less than 4000 characters.";

        // Act
        var result = await service.ChunkTextAsync(text);

        // Assert
        result.Should().HaveCount(1);
        result[0].Content.Should().Be(text);
        result[0].Index.Should().Be(0);
        result[0].StartPosition.Should().Be(0);
        result[0].EndPosition.Should().Be(text.Length);
    }

    [Fact]
    public async Task ChunkTextAsync_TextExactlyChunkSize_ReturnsSingleChunk()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions { ChunkSize = 100, Overlap = 20 };
        var text = new string('x', 100);

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        result.Should().HaveCount(1);
        result[0].Content.Should().Be(text);
    }

    #endregion

    #region Multiple Chunks Tests

    [Fact]
    public async Task ChunkTextAsync_TextLongerThanChunkSize_ReturnsMultipleChunks()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions
        {
            ChunkSize = 100,
            Overlap = 0,
            PreserveSentenceBoundaries = false
        };
        var text = new string('a', 250);

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        result.Should().HaveCount(3);
        result[0].Content.Should().HaveLength(100);
        result[1].Content.Should().HaveLength(100);
        result[2].Content.Should().HaveLength(50);
    }

    [Fact]
    public async Task ChunkTextAsync_MultipleChunks_HaveCorrectIndices()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions
        {
            ChunkSize = 100,
            Overlap = 0,
            PreserveSentenceBoundaries = false
        };
        var text = new string('a', 250);

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        result[0].Index.Should().Be(0);
        result[1].Index.Should().Be(1);
        result[2].Index.Should().Be(2);
    }

    #endregion

    #region Overlap Tests

    [Fact]
    public async Task ChunkTextAsync_WithOverlap_ChunksHaveOverlappingContent()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions
        {
            ChunkSize = 100,
            Overlap = 20,
            PreserveSentenceBoundaries = false
        };
        // Create text with identifiable patterns at overlap boundaries
        var text = new string('a', 80) + new string('b', 40) + new string('c', 80);

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        result.Should().HaveCountGreaterThan(1);

        // First chunk should end with 'b's
        result[0].Content.Should().EndWith("bbbbbbbbbbbbbbbbbbbb"); // 20 b's at end of first chunk

        // Second chunk should start with 'b's (the overlap from first chunk)
        result[1].Content.Should().StartWith("bbbbbbbbbbbbbbbbbbbb"); // 20 b's at start
    }

    [Fact]
    public async Task ChunkTextAsync_WithOverlap_AdvancesCorrectly()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions
        {
            ChunkSize = 100,
            Overlap = 20,
            PreserveSentenceBoundaries = false
        };
        var text = new string('x', 200);

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        // With 100 char chunks and 20 char overlap, advance is 80 chars
        // First chunk at 0 (0-100), second at 80 (80-180), third at 160 (160-200=40 chars), fourth at 180 (180-200=20 chars)
        result.Should().HaveCountGreaterThanOrEqualTo(3);
        result[0].StartPosition.Should().Be(0);
        result[1].StartPosition.Should().Be(80);
        result[2].StartPosition.Should().Be(160);
    }

    #endregion

    #region Sentence Boundary Tests

    [Fact]
    public async Task ChunkTextAsync_PreserveSentenceBoundaries_ChunksEndAtSentences()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions
        {
            ChunkSize = 50,
            Overlap = 0,
            PreserveSentenceBoundaries = true
        };
        // Create text where sentence boundary is near chunk boundary
        var text = "This is sentence one. This is sentence two. This is sentence three. This is sentence four.";

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        // All chunks except potentially the last should end at sentence boundaries
        foreach (var chunk in result.Take(result.Count - 1))
        {
            var trimmed = chunk.Content.TrimEnd();
            (trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?'))
                .Should().BeTrue($"Chunk '{chunk.Content}' should end at sentence boundary");
        }
    }

    [Fact]
    public async Task ChunkTextAsync_PreserveSentenceBoundaries_HandlesQuestionMarks()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions
        {
            ChunkSize = 40,
            Overlap = 0,
            PreserveSentenceBoundaries = true
        };
        var text = "Is this a question? Yes it is. Another question?";

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        result.Should().NotBeEmpty();
        // First chunk should end at a sentence boundary (., ?, or !)
        var firstChunkEnd = result[0].Content.TrimEnd();
        (firstChunkEnd.EndsWith('.') || firstChunkEnd.EndsWith('?') || firstChunkEnd.EndsWith('!'))
            .Should().BeTrue($"Chunk '{firstChunkEnd}' should end at sentence boundary");
    }

    [Fact]
    public async Task ChunkTextAsync_PreserveSentenceBoundaries_HandlesExclamationMarks()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions
        {
            ChunkSize = 30,
            Overlap = 0,
            PreserveSentenceBoundaries = true
        };
        var text = "Hello world! This is great! More text here.";

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        result.Should().NotBeEmpty();
        // First chunk should end at "world!" or "great!"
        var firstChunkEnd = result[0].Content.TrimEnd();
        (firstChunkEnd.EndsWith('!') || firstChunkEnd.EndsWith('.'))
            .Should().BeTrue();
    }

    #endregion

    #region No Sentence Boundary Fallback Tests

    [Fact]
    public async Task ChunkTextAsync_NoSentenceBoundaries_FallsBackToCharacterSplit()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions
        {
            ChunkSize = 50,
            Overlap = 0,
            PreserveSentenceBoundaries = true
        };
        // Text without any sentence terminators
        var text = new string('a', 120);

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        result.Should().HaveCount(3);
        result[0].Content.Should().HaveLength(50);
        result[1].Content.Should().HaveLength(50);
        result[2].Content.Should().HaveLength(20);
    }

    [Fact]
    public async Task ChunkTextAsync_SentenceBoundaryDisabled_ChunksAtExactSize()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions
        {
            ChunkSize = 50,
            Overlap = 0,
            PreserveSentenceBoundaries = false
        };
        var text = "This is sentence one. This is sentence two. This is sentence three.";

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        // First chunk should be exactly 50 chars (not adjusted for sentence boundary)
        result[0].Content.Should().HaveLength(50);
    }

    #endregion

    #region Custom Configuration Tests

    [Fact]
    public async Task ChunkTextAsync_CustomChunkSize_RespectsConfiguration()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions
        {
            ChunkSize = 200,
            Overlap = 0,
            PreserveSentenceBoundaries = false
        };
        var text = new string('x', 500);

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        result.Should().HaveCount(3);
        result[0].Content.Should().HaveLength(200);
        result[1].Content.Should().HaveLength(200);
        result[2].Content.Should().HaveLength(100);
    }

    [Fact]
    public async Task ChunkTextAsync_DefaultOptions_UsesSpecDefaults()
    {
        // Arrange
        var service = CreateService();
        var text = new string('x', 5000);

        // Act
        var result = await service.ChunkTextAsync(text);

        // Assert
        // Default chunk size is 4000, overlap is 200, so advance is 3800
        // First chunk 0-4000, second 3800-5000 (1200 chars), third continues from there
        result.Should().HaveCountGreaterThanOrEqualTo(2);
        result[0].Content.Should().HaveLength(4000);
        // Verify default chunk size is used
        ChunkingOptions.DefaultChunkSize.Should().Be(4000);
        ChunkingOptions.DefaultOverlap.Should().Be(200);
    }

    [Fact]
    public async Task ChunkTextAsync_WithCancellationToken_DoesNotThrowWhenNotCancelled()
    {
        // Arrange
        var service = CreateService();
        var text = "Test text";
        var cts = new CancellationTokenSource();

        // Act
        var act = async () => await service.ChunkTextAsync(text, null, cts.Token);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Position Tracking Tests

    [Fact]
    public async Task ChunkTextAsync_PositionTracking_ReturnsCorrectPositions()
    {
        // Arrange
        var service = CreateService();
        var options = new ChunkingOptions
        {
            ChunkSize = 100,
            Overlap = 0,
            PreserveSentenceBoundaries = false
        };
        var text = new string('a', 250);

        // Act
        var result = await service.ChunkTextAsync(text, options);

        // Assert
        result[0].StartPosition.Should().Be(0);
        result[0].EndPosition.Should().Be(100);
        result[1].StartPosition.Should().Be(100);
        result[1].EndPosition.Should().Be(200);
        result[2].StartPosition.Should().Be(200);
        result[2].EndPosition.Should().Be(250);
    }

    [Fact]
    public async Task ChunkTextAsync_ChunkLength_MatchesContentLength()
    {
        // Arrange
        var service = CreateService();
        var text = "This is a test string for chunking.";

        // Act
        var result = await service.ChunkTextAsync(text);

        // Assert
        foreach (var chunk in result)
        {
            chunk.Length.Should().Be(chunk.Content.Length);
        }
    }

    #endregion
}
