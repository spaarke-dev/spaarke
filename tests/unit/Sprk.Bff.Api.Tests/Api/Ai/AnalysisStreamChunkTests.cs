using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for AnalysisStreamChunk SSE response model.
/// </summary>
public class AnalysisStreamChunkTests
{
    #region Factory Method Tests

    [Fact]
    public void Metadata_CreatesCorrectChunk()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var documentName = "Test Document.pdf";

        // Act
        var chunk = AnalysisStreamChunk.Metadata(analysisId, documentName);

        // Assert
        chunk.Type.Should().Be("metadata");
        chunk.Done.Should().BeFalse();
        chunk.AnalysisId.Should().Be(analysisId);
        chunk.DocumentName.Should().Be(documentName);
        chunk.Content.Should().BeNull();
        chunk.TokenUsage.Should().BeNull();
        chunk.PartialStorage.Should().BeNull();
        chunk.StorageMessage.Should().BeNull();
    }

    [Fact]
    public void TextChunk_CreatesCorrectChunk()
    {
        // Arrange
        var content = "Sample text content";

        // Act
        var chunk = AnalysisStreamChunk.TextChunk(content);

        // Assert
        chunk.Type.Should().Be("chunk");
        chunk.Content.Should().Be(content);
        chunk.Done.Should().BeFalse();
        chunk.AnalysisId.Should().BeNull();
        chunk.PartialStorage.Should().BeNull();
        chunk.StorageMessage.Should().BeNull();
    }

    [Fact]
    public void Completed_WithoutStorageInfo_CreatesBasicCompletionChunk()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var tokenUsage = new TokenUsage(100, 50);

        // Act
        var chunk = AnalysisStreamChunk.Completed(analysisId, tokenUsage);

        // Assert
        chunk.Type.Should().Be("done");
        chunk.Done.Should().BeTrue();
        chunk.AnalysisId.Should().Be(analysisId);
        chunk.TokenUsage.Should().Be(tokenUsage);
        chunk.PartialStorage.Should().BeNull("no storage info provided");
        chunk.StorageMessage.Should().BeNull("no storage info provided");
    }

    [Fact]
    public void Completed_WithPartialStorage_IncludesStorageInfo()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var tokenUsage = new TokenUsage(100, 50);
        var message = "Document Profile completed. Some fields could not be updated. View full results in the Analysis tab.";

        // Act
        var chunk = AnalysisStreamChunk.Completed(
            analysisId,
            tokenUsage,
            partialStorage: true,
            storageMessage: message);

        // Assert
        chunk.Type.Should().Be("done");
        chunk.Done.Should().BeTrue();
        chunk.AnalysisId.Should().Be(analysisId);
        chunk.TokenUsage.Should().Be(tokenUsage);
        chunk.PartialStorage.Should().BeTrue();
        chunk.StorageMessage.Should().Be(message);
    }

    [Fact]
    public void Completed_WithFullSuccess_HasNoPartialStorage()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var tokenUsage = new TokenUsage(100, 50);

        // Act
        var chunk = AnalysisStreamChunk.Completed(
            analysisId,
            tokenUsage,
            partialStorage: false,
            storageMessage: null);

        // Assert
        chunk.Type.Should().Be("done");
        chunk.PartialStorage.Should().BeFalse();
        chunk.StorageMessage.Should().BeNull();
    }

    [Fact]
    public void FromError_CreatesErrorChunk()
    {
        // Arrange
        var error = "Analysis failed: Invalid input";

        // Act
        var chunk = AnalysisStreamChunk.FromError(error);

        // Assert
        chunk.Type.Should().Be("error");
        chunk.Done.Should().BeTrue();
        chunk.Error.Should().Be(error);
        chunk.PartialStorage.Should().BeNull();
        chunk.StorageMessage.Should().BeNull();
    }

    #endregion

    #region Backward Compatibility Tests

    [Fact]
    public void Completed_BackwardCompatible_OldCallsStillWork()
    {
        // Arrange - Simulate old code that doesn't know about storage fields
        var analysisId = Guid.NewGuid();
        var tokenUsage = new TokenUsage(100, 50);

        // Act - Old-style call without optional parameters
        var chunk = AnalysisStreamChunk.Completed(analysisId, tokenUsage);

        // Assert - Should work exactly as before
        chunk.Type.Should().Be("done");
        chunk.Done.Should().BeTrue();
        chunk.AnalysisId.Should().Be(analysisId);
        chunk.TokenUsage.Should().Be(tokenUsage);
        chunk.PartialStorage.Should().BeNull("backward compatibility - null by default");
        chunk.StorageMessage.Should().BeNull("backward compatibility - null by default");
    }

    [Fact]
    public void Completed_WithNullStorageInfo_IsEquivalentToNoStorageInfo()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var tokenUsage = new TokenUsage(100, 50);

        // Act
        var chunk1 = AnalysisStreamChunk.Completed(analysisId, tokenUsage);
        var chunk2 = AnalysisStreamChunk.Completed(analysisId, tokenUsage, null, null);

        // Assert - Both should be equivalent
        chunk1.Should().BeEquivalentTo(chunk2);
    }

    #endregion

    #region Storage Message Format Tests

    [Fact]
    public void Completed_PartialStorageMessage_HasUserFriendlyText()
    {
        // Arrange
        var message = "Document Profile completed. Some fields could not be updated. View full results in the Analysis tab.";

        // Act
        var chunk = AnalysisStreamChunk.Completed(
            Guid.NewGuid(),
            new TokenUsage(100, 50),
            partialStorage: true,
            storageMessage: message);

        // Assert
        chunk.StorageMessage.Should().Contain("Document Profile");
        chunk.StorageMessage.Should().Contain("could not be updated");
        chunk.StorageMessage.Should().Contain("Analysis tab");
    }

    [Fact]
    public void Completed_OnlyPartialStorageTrue_MessageOptional()
    {
        // Arrange & Act - Can set partialStorage without message
        var chunk = AnalysisStreamChunk.Completed(
            Guid.NewGuid(),
            new TokenUsage(100, 50),
            partialStorage: true,
            storageMessage: null);

        // Assert
        chunk.PartialStorage.Should().BeTrue();
        chunk.StorageMessage.Should().BeNull("message is optional even with partial storage");
    }

    #endregion

    #region TokenUsage Tests

    [Fact]
    public void TokenUsage_CreatesCorrectStructure()
    {
        // Arrange & Act
        var usage = new TokenUsage(100, 50);

        // Assert
        usage.Input.Should().Be(100);
        usage.Output.Should().Be(50);
    }

    [Fact]
    public void TokenUsage_InCompletedChunk_IsIncluded()
    {
        // Arrange
        var usage = new TokenUsage(200, 150);

        // Act
        var chunk = AnalysisStreamChunk.Completed(Guid.NewGuid(), usage);

        // Assert
        chunk.TokenUsage.Should().NotBeNull();
        chunk.TokenUsage!.Input.Should().Be(200);
        chunk.TokenUsage!.Output.Should().Be(150);
    }

    #endregion
}
