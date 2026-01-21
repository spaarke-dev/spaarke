using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Workers.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Workers.Office;

/// <summary>
/// Unit tests for <see cref="OfficeJobStatusService"/>.
/// Tests job status update functionality for background workers.
/// </summary>
public class OfficeJobStatusServiceTests
{
    private readonly Mock<ILogger<OfficeJobStatusService>> _loggerMock;
    private readonly OfficeJobStatusService _sut;

    public OfficeJobStatusServiceTests()
    {
        _loggerMock = new Mock<ILogger<OfficeJobStatusService>>();
        _sut = new OfficeJobStatusService(_loggerMock.Object);
    }

    #region UpdateJobPhaseAsync Tests

    [Fact]
    public async Task UpdateJobPhaseAsync_LogsPhaseUpdate()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var phase = "Uploading";
        var status = "Running";

        // Act
        await _sut.UpdateJobPhaseAsync(jobId, phase, status);

        // Assert - verify logging was called
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(jobId.ToString())),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateJobPhaseAsync_IncludesErrorMessage_WhenProvided()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var phase = "Uploading";
        var status = "Failed";
        var errorMessage = "Connection timeout";

        // Act
        await _sut.UpdateJobPhaseAsync(jobId, phase, status, CancellationToken.None, errorMessage);

        // Assert - verify logging includes error message
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(errorMessage)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateJobPhaseAsync_CompletesWithoutException()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        var act = async () => await _sut.UpdateJobPhaseAsync(jobId, "Test", "Running");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("Uploading", "Running")]
    [InlineData("Indexing", "Completed")]
    [InlineData("Profile", "Failed")]
    [InlineData("Indexed", "Skipped")]
    public async Task UpdateJobPhaseAsync_HandlesVariousPhaseStatusCombinations(string phase, string status)
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        var act = async () => await _sut.UpdateJobPhaseAsync(jobId, phase, status);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region UpdateJobProgressAsync Tests

    [Fact]
    public async Task UpdateJobProgressAsync_LogsProgressUpdate()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var progress = 50;

        // Act
        await _sut.UpdateJobProgressAsync(jobId, progress);

        // Assert - verify logging was called at Debug level
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("progress")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateJobProgressAsync_CompletesWithoutException()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        var act = async () => await _sut.UpdateJobProgressAsync(jobId, 75);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public async Task UpdateJobProgressAsync_HandlesVariousProgressValues(int progress)
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        var act = async () => await _sut.UpdateJobProgressAsync(jobId, progress);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region CompleteJobAsync Tests

    [Fact]
    public async Task CompleteJobAsync_LogsCompletion()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var documentUrl = "https://spaarke.app/doc/123";

        // Act
        await _sut.CompleteJobAsync(jobId, documentId, documentUrl);

        // Assert - verify logging was called
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) =>
                    o.ToString()!.Contains(jobId.ToString()) &&
                    o.ToString()!.Contains("completed")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteJobAsync_HandlesNullDocumentId()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        var act = async () => await _sut.CompleteJobAsync(jobId, null, null);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CompleteJobAsync_CompletesWithoutException()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        // Act
        var act = async () => await _sut.CompleteJobAsync(jobId, documentId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region FailJobAsync Tests

    [Fact]
    public async Task FailJobAsync_LogsFailure()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var errorCode = "OFFICE_012";
        var errorMessage = "Upload failed";
        var retryable = true;

        // Act
        await _sut.FailJobAsync(jobId, errorCode, errorMessage, retryable);

        // Assert - verify logging was called at Warning level
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) =>
                    o.ToString()!.Contains(errorCode) &&
                    o.ToString()!.Contains(errorMessage)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task FailJobAsync_IncludesRetryableFlag()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var errorCode = "OFFICE_012";
        var errorMessage = "Upload failed";

        // Act
        await _sut.FailJobAsync(jobId, errorCode, errorMessage, retryable: true);

        // Assert - verify logging includes retryable flag
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("True")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task FailJobAsync_CompletesWithoutException()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        var act = async () => await _sut.FailJobAsync(jobId, "OFFICE_001", "Error");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("OFFICE_001", "Invalid request", false)]
    [InlineData("OFFICE_012", "Upload failed", true)]
    [InlineData("OFFICE_INTERNAL", "Internal error", false)]
    public async Task FailJobAsync_HandlesVariousErrorCodes(string errorCode, string errorMessage, bool retryable)
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        var act = async () => await _sut.FailJobAsync(jobId, errorCode, errorMessage, retryable);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        // Act
        var act = () => new OfficeJobStatusService(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region Cancellation Token Tests

    [Fact]
    public async Task UpdateJobPhaseAsync_HandlesCancellationToken()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        // Act
        await _sut.UpdateJobPhaseAsync(jobId, "Test", "Running", cts.Token);

        // Assert - should complete without issues
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpdateJobProgressAsync_HandlesCancellationToken()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        // Act
        await _sut.UpdateJobProgressAsync(jobId, 50, cts.Token);

        // Assert - should complete without issues
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CompleteJobAsync_HandlesCancellationToken()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        // Act
        await _sut.CompleteJobAsync(jobId, Guid.NewGuid(), null, cts.Token);

        // Assert - should complete without issues
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task FailJobAsync_HandlesCancellationToken()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        // Act
        await _sut.FailJobAsync(jobId, "OFFICE_001", "Error", false, cts.Token);

        // Assert - should complete without issues
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion
}
