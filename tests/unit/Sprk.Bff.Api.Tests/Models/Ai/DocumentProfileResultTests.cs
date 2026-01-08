using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Ai;

/// <summary>
/// Unit tests for DocumentProfileResult model.
/// </summary>
public class DocumentProfileResultTests
{
    #region FullSuccess Tests

    [Fact]
    public void FullSuccess_CreatesSuccessResult()
    {
        // Arrange
        var analysisId = Guid.NewGuid();

        // Act
        var result = DocumentProfileResult.FullSuccess(analysisId);

        // Assert
        result.Success.Should().BeTrue();
        result.PartialStorage.Should().BeFalse();
        result.AnalysisId.Should().Be(analysisId);
        result.Message.Should().Be("Document profile generated successfully");
    }

    #endregion

    #region PartialSuccess Tests

    [Fact]
    public void PartialSuccess_CreatesPartialSuccessResult()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var message = "Field mapping failed but outputs preserved in Analysis Workspace";

        // Act
        var result = DocumentProfileResult.PartialSuccess(analysisId, message);

        // Assert
        result.Success.Should().BeTrue();
        result.PartialStorage.Should().BeTrue();
        result.AnalysisId.Should().Be(analysisId);
        result.Message.Should().Be(message);
    }

    [Fact]
    public void PartialSuccess_StandardMessage_HasUserFriendlyText()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var message = "Document Profile completed. Some fields could not be updated. View full results in the Analysis tab.";

        // Act
        var result = DocumentProfileResult.PartialSuccess(analysisId, message);

        // Assert
        result.Message.Should().Contain("Document Profile completed");
        result.Message.Should().Contain("View full results");
        result.Message.Should().Contain("Analysis tab");
    }

    #endregion

    #region Failure Tests

    [Fact]
    public void Failure_WithoutAnalysisId_CreatesFailureResult()
    {
        // Arrange
        var message = "Analysis execution failed";

        // Act
        var result = DocumentProfileResult.Failure(message);

        // Assert
        result.Success.Should().BeFalse();
        result.PartialStorage.Should().BeFalse();
        result.AnalysisId.Should().BeNull();
        result.Message.Should().Be(message);
    }

    [Fact]
    public void Failure_WithAnalysisId_CreatesFailureResultWithId()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var message = "Storage failed after creating analysis record";

        // Act
        var result = DocumentProfileResult.Failure(message, analysisId);

        // Assert
        result.Success.Should().BeFalse();
        result.PartialStorage.Should().BeFalse();
        result.AnalysisId.Should().Be(analysisId);
        result.Message.Should().Be(message);
    }

    #endregion

    #region State Validation Tests

    [Fact]
    public void FullSuccess_IsNotPartialStorage()
    {
        // Arrange
        var result = DocumentProfileResult.FullSuccess(Guid.NewGuid());

        // Assert
        result.Success.Should().BeTrue();
        result.PartialStorage.Should().BeFalse("full success means all storage operations completed");
    }

    [Fact]
    public void PartialSuccess_IsStillSuccess()
    {
        // Arrange
        var result = DocumentProfileResult.PartialSuccess(Guid.NewGuid(), "Partial");

        // Assert
        result.Success.Should().BeTrue("partial success is still considered successful");
        result.PartialStorage.Should().BeTrue();
    }

    [Fact]
    public void Failure_HasNoPartialStorage()
    {
        // Arrange
        var result = DocumentProfileResult.Failure("Failed");

        // Assert
        result.Success.Should().BeFalse();
        result.PartialStorage.Should().BeFalse("failure cannot have partial storage");
    }

    #endregion

    #region AnalysisId Navigation Tests

    [Fact]
    public void FullSuccess_AlwaysHasAnalysisId()
    {
        // Arrange
        var analysisId = Guid.NewGuid();

        // Act
        var result = DocumentProfileResult.FullSuccess(analysisId);

        // Assert
        result.AnalysisId.Should().NotBeNull();
        result.AnalysisId.Should().Be(analysisId);
    }

    [Fact]
    public void PartialSuccess_AlwaysHasAnalysisId()
    {
        // Arrange
        var analysisId = Guid.NewGuid();

        // Act
        var result = DocumentProfileResult.PartialSuccess(analysisId, "Message");

        // Assert
        result.AnalysisId.Should().NotBeNull("partial success means outputs were stored");
        result.AnalysisId.Should().Be(analysisId);
    }

    [Fact]
    public void Failure_MayHaveAnalysisId()
    {
        // Arrange
        var analysisId = Guid.NewGuid();

        // Act
        var withId = DocumentProfileResult.Failure("Message", analysisId);
        var withoutId = DocumentProfileResult.Failure("Message");

        // Assert
        withId.AnalysisId.Should().Be(analysisId, "failure after analysis creation should have ID");
        withoutId.AnalysisId.Should().BeNull("failure before analysis creation has no ID");
    }

    #endregion
}
