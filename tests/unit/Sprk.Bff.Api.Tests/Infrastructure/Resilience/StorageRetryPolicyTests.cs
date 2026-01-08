using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Resilience;

/// <summary>
/// Integration tests for StorageRetryPolicy.
/// Tests retry behavior with simulated storage errors.
/// </summary>
public class StorageRetryPolicyTests
{
    private readonly Mock<ILogger<StorageRetryPolicy>> _loggerMock;
    private readonly StorageRetryPolicy _policy;

    public StorageRetryPolicyTests()
    {
        _loggerMock = new Mock<ILogger<StorageRetryPolicy>>();
        _policy = new StorageRetryPolicy(_loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new StorageRetryPolicy(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        // Assert - verify documented constants
        StorageRetryPolicy.MaxRetryAttempts.Should().Be(3);
        StorageRetryPolicy.BaseDelaySeconds.Should().Be(2);
    }

    #endregion

    #region Successful Operation Tests

    [Fact]
    public async Task ExecuteAsync_SuccessfulOperation_ReturnsResultImmediately()
    {
        // Arrange
        var expectedResult = "success";

        // Act
        var result = await _policy.ExecuteAsync(
            ct => Task.FromResult(expectedResult));

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task ExecuteAsync_VoidOperation_CompletesSuccessfully()
    {
        // Arrange
        var executed = false;

        // Act
        await _policy.ExecuteAsync(ct =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        // Assert
        executed.Should().BeTrue();
    }

    #endregion

    #region Retry on 404 Tests

    [Fact]
    public async Task ExecuteAsync_404Error_SuccessAfterOneRetry()
    {
        // Arrange
        var attemptCount = 0;
        var documentId = Guid.NewGuid();

        // Act
        var result = await _policy.ExecuteAsync(ct =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                throw StorageRetryableException.DocumentNotFound(documentId);
            }
            return Task.FromResult("success");
        });

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(2); // 1 initial + 1 retry
    }

    [Fact]
    public async Task ExecuteAsync_HttpRequestException404_SuccessAfterOneRetry()
    {
        // Arrange
        var attemptCount = 0;

        // Act
        var result = await _policy.ExecuteAsync(ct =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                throw new HttpRequestException("Document not found", null, HttpStatusCode.NotFound);
            }
            return Task.FromResult("success");
        });

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(2);
    }

    #endregion

    #region Retry on 503 Tests

    [Fact]
    public async Task ExecuteAsync_503Error_SuccessAfterTwoRetries()
    {
        // Arrange
        var attemptCount = 0;

        // Act
        var result = await _policy.ExecuteAsync(ct =>
        {
            attemptCount++;
            if (attemptCount <= 2)
            {
                throw StorageRetryableException.ServiceUnavailable("Service temporarily unavailable");
            }
            return Task.FromResult("success");
        });

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(3); // 1 initial + 2 retries
    }

    [Fact]
    public async Task ExecuteAsync_HttpRequestException503_SuccessAfterOneRetry()
    {
        // Arrange
        var attemptCount = 0;

        // Act
        var result = await _policy.ExecuteAsync(ct =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                throw new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable);
            }
            return Task.FromResult("success");
        });

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(2);
    }

    #endregion

    #region Exhausted Retries Tests

    [Fact]
    public async Task ExecuteAsync_AllRetriesExhausted_ThrowsOriginalException()
    {
        // Arrange
        var attemptCount = 0;
        var documentId = Guid.NewGuid();

        // Act
        var act = async () => await _policy.ExecuteAsync<string>(ct =>
        {
            attemptCount++;
            throw StorageRetryableException.DocumentNotFound(documentId);
        });

        // Assert
        await act.Should().ThrowAsync<StorageRetryableException>()
            .WithMessage($"Document {documentId} not found*");

        // Should have tried: 1 initial + 3 retries = 4 attempts
        attemptCount.Should().Be(4);
    }

    [Fact]
    public async Task ExecuteAsync_503AllRetriesExhausted_ThrowsOriginalException()
    {
        // Arrange
        var attemptCount = 0;

        // Act
        var act = async () => await _policy.ExecuteAsync<string>(ct =>
        {
            attemptCount++;
            throw StorageRetryableException.ServiceUnavailable("Service unavailable");
        });

        // Assert
        await act.Should().ThrowAsync<StorageRetryableException>()
            .WithMessage("Service unavailable*");
        attemptCount.Should().Be(4); // 1 initial + 3 retries
    }

    #endregion

    #region Non-Retryable Errors Tests

    [Fact]
    public async Task ExecuteAsync_NonRetryableException_ThrowsImmediately()
    {
        // Arrange
        var attemptCount = 0;

        // Act
        var act = async () => await _policy.ExecuteAsync<string>(ct =>
        {
            attemptCount++;
            throw new InvalidOperationException("This should not be retried");
        });

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        attemptCount.Should().Be(1); // No retries
    }

    [Fact]
    public async Task ExecuteAsync_HttpRequestException400_ThrowsImmediately()
    {
        // Arrange - 400 Bad Request is not retryable
        var attemptCount = 0;

        // Act
        var act = async () => await _policy.ExecuteAsync<string>(ct =>
        {
            attemptCount++;
            throw new HttpRequestException("Bad request", null, HttpStatusCode.BadRequest);
        });

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        attemptCount.Should().Be(1); // No retries
    }

    [Fact]
    public async Task ExecuteAsync_HttpRequestException500_ThrowsImmediately()
    {
        // Arrange - 500 Internal Server Error is not retryable (only 503)
        var attemptCount = 0;

        // Act
        var act = async () => await _policy.ExecuteAsync<string>(ct =>
        {
            attemptCount++;
            throw new HttpRequestException("Internal server error", null, HttpStatusCode.InternalServerError);
        });

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        attemptCount.Should().Be(1); // No retries
    }

    #endregion

    #region Exponential Backoff Tests

    [Fact]
    public async Task ExecuteAsync_RetryDelays_AreExponential()
    {
        // Arrange
        var attemptTimes = new List<DateTime>();
        var attemptCount = 0;

        // Act
        var act = async () => await _policy.ExecuteAsync<string>(ct =>
        {
            attemptTimes.Add(DateTime.UtcNow);
            attemptCount++;
            throw StorageRetryableException.ServiceUnavailable("Service unavailable");
        });

        // Assert - let it fail with retries
        await act.Should().ThrowAsync<StorageRetryableException>();

        // Verify we got 4 attempts (1 initial + 3 retries)
        attemptTimes.Should().HaveCount(4);

        // Calculate delays between attempts
        var delays = new List<TimeSpan>();
        for (int i = 1; i < attemptTimes.Count; i++)
        {
            delays.Add(attemptTimes[i] - attemptTimes[i - 1]);
        }

        // Expected delays: 2s, 4s, 8s (exponential with base 2)
        // Allow some tolerance for test execution time
        delays[0].TotalSeconds.Should().BeApproximately(2.0, 0.5, "First retry should be ~2s");
        delays[1].TotalSeconds.Should().BeApproximately(4.0, 0.5, "Second retry should be ~4s");
        delays[2].TotalSeconds.Should().BeApproximately(8.0, 0.5, "Third retry should be ~8s");
    }

    #endregion

    #region Void Operation Retry Tests

    [Fact]
    public async Task ExecuteAsync_VoidOperation_RetriesOn404()
    {
        // Arrange
        var attemptCount = 0;

        // Act
        await _policy.ExecuteAsync(ct =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                throw StorageRetryableException.DocumentNotFound(Guid.NewGuid());
            }
            return Task.CompletedTask;
        });

        // Assert
        attemptCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_VoidOperation_ExhaustedRetries_Throws()
    {
        // Arrange
        var attemptCount = 0;

        // Act
        var act = async () => await _policy.ExecuteAsync(ct =>
        {
            attemptCount++;
            throw StorageRetryableException.ServiceUnavailable("Always fails");
        });

        // Assert
        await act.Should().ThrowAsync<StorageRetryableException>();
        attemptCount.Should().Be(4);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange - cancel before operation starts
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        // Act
        var act = async () => await _policy.ExecuteAsync(ct =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult("success");
        }, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringRetry_StopsRetrying()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var attemptCount = 0;

        // Act - operation throws retryable exception, then we cancel during delay
        var act = async () => await _policy.ExecuteAsync(ct =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                // Schedule cancellation to occur during the retry delay
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500); // Cancel during 2s delay
                    cts.Cancel();
                });
                throw StorageRetryableException.DocumentNotFound(Guid.NewGuid());
            }
            return Task.FromResult("success");
        }, cts.Token);

        // Assert - should throw OperationCanceledException during the delay
        await act.Should().ThrowAsync<OperationCanceledException>();
        attemptCount.Should().Be(1); // Only one attempt before cancellation
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task ExecuteAsync_OnRetry_LogsWarning()
    {
        // Arrange
        var attemptCount = 0;

        // Act
        await _policy.ExecuteAsync(ct =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                throw StorageRetryableException.DocumentNotFound(Guid.NewGuid());
            }
            return Task.FromResult("success");
        });

        // Assert - verify warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[STORAGE-RETRY]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}

/// <summary>
/// Tests for StorageRetryableException.
/// </summary>
public class StorageRetryableExceptionTests
{
    [Fact]
    public void DocumentNotFound_CreatesCorrectException()
    {
        // Arrange
        var documentId = Guid.NewGuid();

        // Act
        var exception = StorageRetryableException.DocumentNotFound(documentId);

        // Assert
        exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exception.DocumentId.Should().Be(documentId);
        exception.Message.Should().Contain(documentId.ToString());
        exception.Message.Should().Contain("replication lag");
    }

    [Fact]
    public void ServiceUnavailable_CreatesCorrectException()
    {
        // Arrange
        var message = "Dataverse is temporarily unavailable";

        // Act
        var exception = StorageRetryableException.ServiceUnavailable(message);

        // Assert
        exception.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        exception.DocumentId.Should().BeNull();
        exception.Message.Should().Be(message);
    }

    [Fact]
    public void Constructor_WithInnerException_PreservesInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");
        var documentId = Guid.NewGuid();

        // Act
        var exception = StorageRetryableException.DocumentNotFound(documentId, innerException);

        // Assert
        exception.InnerException.Should().Be(innerException);
    }

    [Fact]
    public void Constructor_AllParameters_SetsAllProperties()
    {
        // Arrange
        var message = "Custom message";
        var statusCode = HttpStatusCode.GatewayTimeout;
        var documentId = Guid.NewGuid();
        var innerException = new Exception("Inner");

        // Act
        var exception = new StorageRetryableException(message, statusCode, documentId, innerException);

        // Assert
        exception.Message.Should().Be(message);
        exception.StatusCode.Should().Be(statusCode);
        exception.DocumentId.Should().Be(documentId);
        exception.InnerException.Should().Be(innerException);
    }
}
