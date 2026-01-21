using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using StackExchange.Redis;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// Unit tests for <see cref="JobStatusService"/>.
/// Tests the job status pub/sub functionality for SSE streaming.
/// </summary>
public class JobStatusServiceTests : IDisposable
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<ISubscriber> _subscriberMock;
    private readonly Mock<IDatabase> _databaseMock;
    private readonly Mock<ILogger<JobStatusService>> _loggerMock;
    private readonly JobStatusService _sut;

    public JobStatusServiceTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _subscriberMock = new Mock<ISubscriber>();
        _databaseMock = new Mock<IDatabase>();
        _loggerMock = new Mock<ILogger<JobStatusService>>();

        _redisMock.Setup(r => r.GetSubscriber(It.IsAny<object>()))
            .Returns(_subscriberMock.Object);
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_databaseMock.Object);

        _sut = new JobStatusService(_redisMock.Object, _loggerMock.Object);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    #region PublishStatusUpdateAsync Tests

    [Fact]
    public async Task PublishStatusUpdateAsync_PublishesToRedis_WhenConnected()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var update = new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 50,
            CurrentPhase = "Processing"
        };

        _subscriberMock.Setup(s => s.PublishAsync(
                It.Is<RedisChannel>(c => c.ToString().Contains(jobId.ToString())),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1); // 1 subscriber received the message

        // Act
        var result = await _sut.PublishStatusUpdateAsync(update);

        // Assert
        result.Should().BeTrue();
        _subscriberMock.Verify(s => s.PublishAsync(
            It.Is<RedisChannel>(c => c.ToString() == $"sdap:job:{jobId}:status"),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task PublishStatusUpdateAsync_ReturnsFalse_WhenRedisNotAvailable()
    {
        // Arrange
        var serviceWithoutRedis = new JobStatusService(null, _loggerMock.Object);
        var update = new JobStatusUpdate
        {
            JobId = Guid.NewGuid(),
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 25
        };

        // Act
        var result = await serviceWithoutRedis.PublishStatusUpdateAsync(update);

        // Assert
        result.Should().BeFalse();

        serviceWithoutRedis.Dispose();
    }

    [Fact]
    public async Task PublishStatusUpdateAsync_ReturnsFalse_OnRedisConnectionException()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var update = new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 50
        };

        _subscriberMock.Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act
        var result = await _sut.PublishStatusUpdateAsync(update);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task PublishStatusUpdateAsync_IncrementsSequence_ForSameJob()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var updates = new List<JobStatusUpdate>
        {
            new()
            {
                JobId = jobId,
                UpdateType = JobStatusUpdateType.Progress,
                Status = JobStatus.Running,
                Progress = 25
            },
            new()
            {
                JobId = jobId,
                UpdateType = JobStatusUpdateType.Progress,
                Status = JobStatus.Running,
                Progress = 50
            },
            new()
            {
                JobId = jobId,
                UpdateType = JobStatusUpdateType.Progress,
                Status = JobStatus.Running,
                Progress = 75
            }
        };

        var capturedMessages = new List<RedisValue>();
        _subscriberMock.Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) => capturedMessages.Add(v))
            .ReturnsAsync(1);

        // Act
        foreach (var update in updates)
        {
            await _sut.PublishStatusUpdateAsync(update);
        }

        // Assert
        capturedMessages.Should().HaveCount(3);

        // Verify sequences increment
        var sequences = capturedMessages
            .Select(m => System.Text.Json.JsonSerializer.Deserialize<JobStatusUpdate>(m.ToString())!)
            .Select(u => u.Sequence)
            .ToList();

        sequences.Should().BeInAscendingOrder();
        sequences.Should().BeEquivalentTo(new[] { 1L, 2L, 3L });
    }

    #endregion

    #region UpdateJobStatusAsync Tests

    [Fact]
    public async Task UpdateJobStatusAsync_PublishesProgressUpdate()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        _subscriberMock.Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        // Act
        var result = await _sut.UpdateJobStatusAsync(
            jobId,
            status: JobStatus.Running,
            progress: 50,
            currentPhase: "Uploading");

        // Assert
        result.Should().BeTrue();
        _subscriberMock.Verify(s => s.PublishAsync(
            It.IsAny<RedisChannel>(),
            It.Is<RedisValue>(v => v.ToString().Contains("\"progress\":50")),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task UpdateJobStatusAsync_IncludesCompletedPhase_WhenProvided()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var completedPhase = new CompletedPhase
        {
            Name = "Upload",
            CompletedAt = DateTimeOffset.UtcNow,
            DurationMs = 1500
        };

        RedisValue? capturedMessage = null;
        _subscriberMock.Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) => capturedMessage = v)
            .ReturnsAsync(1);

        // Act
        await _sut.UpdateJobStatusAsync(
            jobId,
            status: JobStatus.Running,
            progress: 50,
            currentPhase: "Processing",
            completedPhase: completedPhase);

        // Assert
        capturedMessage.Should().NotBeNull();
        var messageJson = capturedMessage!.Value.ToString();
        messageJson.Should().Contain("\"updateType\":\"StageComplete\"");
        messageJson.Should().Contain("\"completedPhase\"");
    }

    #endregion

    #region CompleteJobAsync Tests

    [Fact]
    public async Task CompleteJobAsync_PublishesJobCompletedUpdate()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var result = new JobResult
        {
            Artifact = new CreatedArtifact
            {
                Type = ArtifactType.Document,
                Id = artifactId,
                WebUrl = $"https://spaarke.app/doc/{artifactId}"
            }
        };

        RedisValue? capturedMessage = null;
        _subscriberMock.Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) => capturedMessage = v)
            .ReturnsAsync(1);

        // Act
        var success = await _sut.CompleteJobAsync(jobId, result);

        // Assert
        success.Should().BeTrue();
        capturedMessage.Should().NotBeNull();
        var messageJson = capturedMessage!.Value.ToString();
        messageJson.Should().Contain("\"updateType\":\"JobCompleted\"");
        messageJson.Should().Contain("\"progress\":100");
        messageJson.Should().Contain("\"status\":\"Completed\"");
    }

    #endregion

    #region FailJobAsync Tests

    [Fact]
    public async Task FailJobAsync_PublishesJobFailedUpdate()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var error = new JobError
        {
            Code = "OFFICE_012",
            Message = "Upload failed",
            Retryable = true
        };

        RedisValue? capturedMessage = null;
        _subscriberMock.Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) => capturedMessage = v)
            .ReturnsAsync(1);

        // Act
        var success = await _sut.FailJobAsync(jobId, error);

        // Assert
        success.Should().BeTrue();
        capturedMessage.Should().NotBeNull();
        var messageJson = capturedMessage!.Value.ToString();
        messageJson.Should().Contain("\"updateType\":\"JobFailed\"");
        messageJson.Should().Contain("\"status\":\"Failed\"");
        messageJson.Should().Contain("\"code\":\"OFFICE_012\"");
    }

    #endregion

    #region IsHealthyAsync Tests

    [Fact]
    public async Task IsHealthyAsync_ReturnsTrue_WhenRedisPingSucceeds()
    {
        // Arrange
        _databaseMock.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromMilliseconds(5));

        // Act
        var healthy = await _sut.IsHealthyAsync();

        // Assert
        healthy.Should().BeTrue();
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_WhenPingLatencyHigh()
    {
        // Arrange
        _databaseMock.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromMilliseconds(1500)); // > 1000ms threshold

        // Act
        var healthy = await _sut.IsHealthyAsync();

        // Assert
        healthy.Should().BeFalse();
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_WhenRedisNotAvailable()
    {
        // Arrange
        var serviceWithoutRedis = new JobStatusService(null, _loggerMock.Object);

        // Act
        var healthy = await serviceWithoutRedis.IsHealthyAsync();

        // Assert
        healthy.Should().BeFalse();

        serviceWithoutRedis.Dispose();
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_OnException()
    {
        // Arrange
        _databaseMock.Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act
        var healthy = await _sut.IsHealthyAsync();

        // Assert
        healthy.Should().BeFalse();
    }

    #endregion

    #region SubscribeToJobAsync Tests

    [Fact]
    public async Task SubscribeToJobAsync_CompletesImmediately_WhenRedisNotAvailable()
    {
        // Arrange
        var serviceWithoutRedis = new JobStatusService(null, _loggerMock.Object);
        var jobId = Guid.NewGuid();
        var updates = new List<JobStatusUpdate>();

        // Act
        await foreach (var update in serviceWithoutRedis.SubscribeToJobAsync(jobId))
        {
            updates.Add(update);
        }

        // Assert
        updates.Should().BeEmpty();

        serviceWithoutRedis.Dispose();
    }

    #endregion

    #region JobStatusUpdate Tests

    [Fact]
    public void JobStatusUpdate_HasDefaultTimestamp()
    {
        // Arrange & Act
        var update = new JobStatusUpdate
        {
            JobId = Guid.NewGuid(),
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running
        };

        // Assert
        update.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(JobStatusUpdateType.Progress)]
    [InlineData(JobStatusUpdateType.StageComplete)]
    [InlineData(JobStatusUpdateType.StageStarted)]
    [InlineData(JobStatusUpdateType.JobCompleted)]
    [InlineData(JobStatusUpdateType.JobFailed)]
    [InlineData(JobStatusUpdateType.JobCancelled)]
    public void JobStatusUpdateType_HasAllExpectedValues(JobStatusUpdateType updateType)
    {
        // Arrange & Act
        var update = new JobStatusUpdate
        {
            JobId = Guid.NewGuid(),
            UpdateType = updateType,
            Status = JobStatus.Running
        };

        // Assert
        update.UpdateType.Should().Be(updateType);
    }

    #endregion
}
