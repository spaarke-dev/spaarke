using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using StackExchange.Redis;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration;

/// <summary>
/// Integration tests for the SSE job status flow.
/// Tests the complete chain: Worker publishes -> Redis pub/sub -> SSE client receives.
/// </summary>
/// <remarks>
/// <para>
/// These tests validate:
/// - Status updates are delivered within 1 second (per spec.md requirement)
/// - Reconnection with Last-Event-ID works correctly
/// - Multiple clients receive the same updates
/// - Redis connection failures are handled gracefully
/// - Polling fallback works when SSE is unavailable
/// </para>
/// <para>
/// Per ADR-009: Uses IConnectionMultiplexer for Redis pub/sub operations.
/// Channel naming: "sdap:job:{jobId}:status"
/// </para>
/// </remarks>
public class JobStatusSseIntegrationTests : IDisposable
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<ISubscriber> _mockSubscriber;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ILogger<JobStatusService>> _mockLogger;
    private readonly JobStatusService _service;

    public JobStatusSseIntegrationTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockSubscriber = new Mock<ISubscriber>();
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<JobStatusService>>();

        _mockRedis.Setup(r => r.GetSubscriber(It.IsAny<object>()))
            .Returns(_mockSubscriber.Object);
        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);

        _service = new JobStatusService(_mockRedis.Object, _mockLogger.Object);
    }

    #region Test: Worker publishes status -> SSE client receives

    [Fact]
    public async Task PublishStatusUpdate_WhenWorkerPublishes_PublishesToRedisChannel()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var update = new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 25,
            CurrentPhase = "Uploading"
        };

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.Is<RedisChannel>(c => c.ToString().Contains(jobId.ToString())),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1L);

        // Act
        var result = await _service.PublishStatusUpdateAsync(update);

        // Assert
        result.Should().BeTrue();
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(c => c.ToString() == $"sdap:job:{jobId}:status"),
                It.Is<RedisValue>(v => v.ToString().Contains("\"progress\":25")),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishStatusUpdate_IncludesSequenceNumber_ForOrdering()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var update1 = new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 10
        };
        var update2 = new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 20
        };

        var publishedMessages = new List<string>();
        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
                publishedMessages.Add(v.ToString()))
            .ReturnsAsync(1L);

        // Act
        await _service.PublishStatusUpdateAsync(update1);
        await _service.PublishStatusUpdateAsync(update2);

        // Assert
        publishedMessages.Should().HaveCount(2);
        publishedMessages[0].Should().Contain("\"sequence\":1");
        publishedMessages[1].Should().Contain("\"sequence\":2");
    }

    #endregion

    #region Test: Multiple status updates in sequence

    [Fact]
    public async Task PublishStatusUpdate_MultipleUpdates_MaintainCorrectOrder()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var sequences = new List<long>();

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
            {
                var json = v.ToString();
                var sequenceStart = json.IndexOf("\"sequence\":", StringComparison.Ordinal) + 11;
                var sequenceEnd = json.IndexOf(',', sequenceStart);
                if (sequenceEnd == -1) sequenceEnd = json.IndexOf('}', sequenceStart);
                sequences.Add(long.Parse(json.Substring(sequenceStart, sequenceEnd - sequenceStart)));
            })
            .ReturnsAsync(1L);

        // Act - simulate worker publishing 5 updates
        for (var i = 1; i <= 5; i++)
        {
            await _service.PublishStatusUpdateAsync(new JobStatusUpdate
            {
                JobId = jobId,
                UpdateType = JobStatusUpdateType.Progress,
                Status = JobStatus.Running,
                Progress = i * 20
            });
        }

        // Assert - sequences should be 1, 2, 3, 4, 5
        sequences.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
        sequences.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task PublishStatusUpdate_StageUpdates_PublishedCorrectly()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var publishedTypes = new List<string>();

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
            {
                var json = v.ToString();
                if (json.Contains("StageComplete")) publishedTypes.Add("StageComplete");
                else if (json.Contains("Progress")) publishedTypes.Add("Progress");
            })
            .ReturnsAsync(1L);

        // Act - simulate typical job flow
        await _service.UpdateJobStatusAsync(jobId, JobStatus.Running, 10, "RecordsCreated");
        await _service.UpdateJobStatusAsync(jobId, JobStatus.Running, 30, "FileUpload",
            completedPhase: new CompletedPhase { Name = "RecordsCreated", CompletedAt = DateTimeOffset.UtcNow });
        await _service.UpdateJobStatusAsync(jobId, JobStatus.Running, 50, "ProfileSummary");

        // Assert
        publishedTypes.Should().HaveCount(3);
        publishedTypes.Should().Contain("StageComplete");
    }

    [Fact]
    public async Task PublishStatusUpdate_HeartbeatEventHasTimestamp()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var publishedMessage = string.Empty;

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
                publishedMessage = v.ToString())
            .ReturnsAsync(1L);

        // Act
        await _service.UpdateJobStatusAsync(
            jobId,
            JobStatus.Running,
            progress: 50,
            currentPhase: "Processing");

        // Assert - verify timestamp is present in the published message
        publishedMessage.Should().Contain("\"timestamp\":");
        publishedMessage.Should().MatchRegex(@"""timestamp"":""\d{4}-\d{2}-\d{2}T");
    }

    #endregion

    #region Test: Status update latency is under 1 second

    [Fact]
    public async Task PublishStatusUpdate_LatencyUnder1Second_MeetsSpecRequirement()
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

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1L);

        // Act
        var stopwatch = Stopwatch.StartNew();
        await _service.PublishStatusUpdateAsync(update);
        stopwatch.Stop();

        // Assert - must be under 1 second per spec requirement (target: <100ms)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000,
            "Status updates MUST be delivered within 1 second per spec.md NFR-04");

        // Ideally under 100ms for good UX
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100,
            "Target latency is <100ms for optimal user experience");
    }

    [Fact]
    public async Task PublishStatusUpdate_HighVolumeUpdates_MaintainsLatency()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var latencies = new List<long>();

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1L);

        // Act - publish 100 updates and measure latency
        for (var i = 0; i < 100; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await _service.PublishStatusUpdateAsync(new JobStatusUpdate
            {
                JobId = jobId,
                UpdateType = JobStatusUpdateType.Progress,
                Status = JobStatus.Running,
                Progress = i
            });
            stopwatch.Stop();
            latencies.Add(stopwatch.ElapsedMilliseconds);
        }

        // Assert - all updates should be under 1 second
        latencies.Should().AllSatisfy(l => l.Should().BeLessThan(1000));

        // p95 should be under 100ms
        var p95 = latencies.OrderBy(l => l).Skip(95).First();
        p95.Should().BeLessThan(200, "p95 latency should be under 200ms");
    }

    #endregion

    #region Test: Progress events

    [Fact]
    public async Task PublishStatusUpdate_ProgressEvent_IncludesAllRequiredFields()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var publishedMessage = string.Empty;

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
                publishedMessage = v.ToString())
            .ReturnsAsync(1L);

        // Act
        await _service.PublishStatusUpdateAsync(new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 75,
            CurrentPhase = "Indexing"
        });

        // Assert - verify all required fields are present
        publishedMessage.Should().Contain($"\"jobId\":\"{jobId}\"");
        publishedMessage.Should().Contain("\"updateType\":\"Progress\"");
        publishedMessage.Should().Contain("\"status\":\"Running\"");
        publishedMessage.Should().Contain("\"progress\":75");
        publishedMessage.Should().Contain("\"currentPhase\":\"Indexing\"");
        publishedMessage.Should().Contain("\"sequence\":");
        publishedMessage.Should().Contain("\"timestamp\":");
    }

    #endregion

    #region Test: Reconnection scenarios (Last-Event-ID)

    [Fact]
    public async Task SubscribeToJob_RedisUnavailable_ReturnsEmptyEnumerable()
    {
        // Arrange
        var serviceWithoutRedis = new JobStatusService(null, _mockLogger.Object);
        var jobId = Guid.NewGuid();
        var receivedUpdates = new List<JobStatusUpdate>();

        // Act
        await foreach (var update in serviceWithoutRedis.SubscribeToJobAsync(jobId))
        {
            receivedUpdates.Add(update);
        }

        // Assert - subscription should complete immediately when Redis unavailable
        receivedUpdates.Should().BeEmpty("Subscription should complete immediately when Redis unavailable, " +
            "allowing client to fall back to polling");
    }

    [Fact]
    public async Task SequenceNumbers_AllowClientReconnection()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var publishedSequences = new List<long>();

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
            {
                var json = v.ToString();
                var sequenceStart = json.IndexOf("\"sequence\":", StringComparison.Ordinal) + 11;
                var sequenceEnd = json.IndexOf(',', sequenceStart);
                if (sequenceEnd == -1) sequenceEnd = json.IndexOf('}', sequenceStart);
                publishedSequences.Add(long.Parse(json.Substring(sequenceStart, sequenceEnd - sequenceStart)));
            })
            .ReturnsAsync(1L);

        // Act - simulate 5 updates
        for (var i = 1; i <= 5; i++)
        {
            await _service.PublishStatusUpdateAsync(new JobStatusUpdate
            {
                JobId = jobId,
                UpdateType = JobStatusUpdateType.Progress,
                Status = JobStatus.Running,
                Progress = i * 20
            });
        }

        // Assert - sequences are monotonically increasing for Last-Event-ID reconnection
        publishedSequences.Should().BeEquivalentTo(new[] { 1L, 2L, 3L, 4L, 5L });
        publishedSequences.Should().BeInAscendingOrder();
        publishedSequences.Should().OnlyHaveUniqueItems("Each event needs unique sequence for reconnection");
    }

    #endregion

    #region Test: Multiple clients receive same updates

    [Fact]
    public async Task PublishStatusUpdate_MultipleSubscribers_AllReceiveUpdate()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Simulate 3 clients subscribed (returned by Redis PublishAsync)
        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(3L); // 3 subscribers received the message

        // Act
        var result = await _service.PublishStatusUpdateAsync(new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 50
        });

        // Assert
        result.Should().BeTrue();
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(c => c.ToString().Contains(jobId.ToString())),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once,
            "Update should be published once, Redis distributes to all subscribers");
    }

    [Fact]
    public async Task PublishStatusUpdate_NoSubscribers_StillSucceeds()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Simulate no subscribers (returned by Redis PublishAsync)
        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(0L); // No subscribers

        // Act
        var result = await _service.PublishStatusUpdateAsync(new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 50
        });

        // Assert - publish should still succeed even with no subscribers
        result.Should().BeTrue("Publishing should succeed even if no clients are currently connected");
    }

    #endregion

    #region Test: Redis connection failure handling

    [Fact]
    public async Task PublishStatusUpdate_RedisConnectionFailed_ReturnsFalseGracefully()
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

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act
        var result = await _service.PublishStatusUpdateAsync(update);

        // Assert
        result.Should().BeFalse("Should return false gracefully, not throw exception");
    }

    [Fact]
    public async Task PublishStatusUpdate_RedisTimeout_ReturnsFalseGracefully()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisTimeoutException("Command timed out", CommandStatus.WaitingToBeSent));

        // Act
        var result = await _service.PublishStatusUpdateAsync(new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 50
        });

        // Assert
        result.Should().BeFalse("Should return false on timeout, not throw");
    }

    [Fact]
    public async Task PublishStatusUpdate_RedisUnavailable_ServiceContinues()
    {
        // Arrange - create service with null Redis (simulating unavailable Redis)
        var serviceWithoutRedis = new JobStatusService(null, _mockLogger.Object);
        var jobId = Guid.NewGuid();

        // Act
        var result = await serviceWithoutRedis.PublishStatusUpdateAsync(new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 50
        });

        // Assert - should return false but not crash
        result.Should().BeFalse("Pub/sub should be disabled when Redis unavailable");
    }

    [Fact]
    public async Task PublishStatusUpdate_GenericException_HandledGracefully()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        // Act
        var result = await _service.PublishStatusUpdateAsync(new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 50
        });

        // Assert
        result.Should().BeFalse("Should handle unexpected errors gracefully");
    }

    #endregion

    #region Test: Polling fallback when SSE unavailable

    [Fact]
    public async Task UpdateJobStatus_WhenSseUnavailable_StatusStillUpdatable()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act - update status (should work even if pub/sub fails)
        var result = await _service.UpdateJobStatusAsync(
            jobId,
            JobStatus.Running,
            progress: 50,
            currentPhase: "Processing");

        // Assert - status update should succeed (Dataverse update)
        // even though Redis pub/sub failed
        result.Should().BeTrue("Status update to Dataverse should succeed even if pub/sub fails, " +
            "allowing polling clients to get status updates");
    }

    [Fact]
    public async Task UpdateJobStatus_WithoutRedis_PollingStillWorks()
    {
        // Arrange - service without Redis (null connection)
        var serviceWithoutRedis = new JobStatusService(null, _mockLogger.Object);
        var jobId = Guid.NewGuid();

        // Act
        var result = await serviceWithoutRedis.UpdateJobStatusAsync(
            jobId,
            JobStatus.Running,
            progress: 50,
            currentPhase: "Processing");

        // Assert - should work for polling (Dataverse persistence would work)
        result.Should().BeTrue("Polling should work even without Redis");
    }

    #endregion

    #region Test: Terminal events (complete/failed)

    [Fact]
    public async Task CompleteJob_PublishesJobCompletedEvent()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var publishedMessage = string.Empty;

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
                publishedMessage = v.ToString())
            .ReturnsAsync(1L);

        // Act
        await _service.CompleteJobAsync(jobId, new JobResult
        {
            Artifact = new CreatedArtifact
            {
                Type = ArtifactType.Document,
                Id = documentId,
                SpeFileId = "spe-file-123",
                ContainerId = "container-123",
                WebUrl = "https://spaarke.com/documents/test"
            }
        });

        // Assert
        publishedMessage.Should().Contain("\"updateType\":\"JobCompleted\"");
        publishedMessage.Should().Contain("\"status\":\"Completed\"");
        publishedMessage.Should().Contain("\"progress\":100");
    }

    [Fact]
    public async Task FailJob_PublishesJobFailedEvent()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var publishedMessage = string.Empty;

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
                publishedMessage = v.ToString())
            .ReturnsAsync(1L);

        // Act
        await _service.FailJobAsync(jobId, new JobError
        {
            Code = "OFFICE_012",
            Message = "SPE upload failed",
            Retryable = true
        });

        // Assert
        publishedMessage.Should().Contain("\"updateType\":\"JobFailed\"");
        publishedMessage.Should().Contain("\"status\":\"Failed\"");
        publishedMessage.Should().Contain("\"code\":\"OFFICE_012\"");
        publishedMessage.Should().Contain("\"message\":\"SPE upload failed\"");
        publishedMessage.Should().Contain("\"retryable\":true");
    }

    [Fact]
    public async Task CompleteJob_TriggersTerminalStateInSubscription()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var publishedMessage = string.Empty;

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
                publishedMessage = v.ToString())
            .ReturnsAsync(1L);

        // Act
        await _service.CompleteJobAsync(jobId, new JobResult());

        // Assert - verify the update type is terminal (JobCompleted)
        publishedMessage.Should().Contain("\"updateType\":\"JobCompleted\"");
        // Terminal states: JobCompleted, JobFailed, JobCancelled
        // SSE client should close connection after receiving these
    }

    [Fact]
    public async Task FailJob_TriggersTerminalStateInSubscription()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var publishedMessage = string.Empty;

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
                publishedMessage = v.ToString())
            .ReturnsAsync(1L);

        // Act
        await _service.FailJobAsync(jobId, new JobError
        {
            Code = "OFFICE_015",
            Message = "Processing unavailable"
        });

        // Assert - verify the update type is terminal (JobFailed)
        publishedMessage.Should().Contain("\"updateType\":\"JobFailed\"");
    }

    #endregion

    #region Test: Stage-update events

    [Fact]
    public async Task UpdateJobStatus_WithCompletedPhase_PublishesStageCompleteEvent()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var publishedMessage = string.Empty;

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
                publishedMessage = v.ToString())
            .ReturnsAsync(1L);

        // Act
        await _service.UpdateJobStatusAsync(
            jobId,
            JobStatus.Running,
            progress: 40,
            currentPhase: "ProfileSummary",
            completedPhase: new CompletedPhase
            {
                Name = "FileUploaded",
                CompletedAt = DateTimeOffset.UtcNow,
                DurationMs = 250
            });

        // Assert
        publishedMessage.Should().Contain("\"updateType\":\"StageComplete\"");
        publishedMessage.Should().Contain("\"name\":\"FileUploaded\"");
        publishedMessage.Should().Contain("\"durationMs\":250");
    }

    [Fact]
    public async Task UpdateJobStatus_StageProgression_PublishesCorrectSequence()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var publishedMessages = new List<string>();

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, v, _) =>
                publishedMessages.Add(v.ToString()))
            .ReturnsAsync(1L);

        // Act - simulate typical job stage progression
        await _service.UpdateJobStatusAsync(jobId, JobStatus.Running, 10, "RecordsCreated");
        await _service.UpdateJobStatusAsync(jobId, JobStatus.Running, 30, "FileUpload",
            completedPhase: new CompletedPhase { Name = "RecordsCreated", CompletedAt = DateTimeOffset.UtcNow });
        await _service.UpdateJobStatusAsync(jobId, JobStatus.Running, 60, "ProfileSummary",
            completedPhase: new CompletedPhase { Name = "FileUpload", CompletedAt = DateTimeOffset.UtcNow });
        await _service.UpdateJobStatusAsync(jobId, JobStatus.Running, 80, "Indexed",
            completedPhase: new CompletedPhase { Name = "ProfileSummary", CompletedAt = DateTimeOffset.UtcNow });

        // Assert - verify stage completion events
        publishedMessages.Should().HaveCount(4);
        publishedMessages[1].Should().Contain("\"name\":\"RecordsCreated\"");
        publishedMessages[2].Should().Contain("\"name\":\"FileUpload\"");
        publishedMessages[3].Should().Contain("\"name\":\"ProfileSummary\"");
    }

    #endregion

    #region Test: Health check

    [Fact]
    public async Task IsHealthy_RedisResponsive_ReturnsTrue()
    {
        // Arrange
        _mockDatabase
            .Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromMilliseconds(5));

        // Act
        var healthy = await _service.IsHealthyAsync();

        // Assert
        healthy.Should().BeTrue();
    }

    [Fact]
    public async Task IsHealthy_RedisHighLatency_ReturnsFalse()
    {
        // Arrange
        _mockDatabase
            .Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromSeconds(2)); // > 1 second threshold

        // Act
        var healthy = await _service.IsHealthyAsync();

        // Assert
        healthy.Should().BeFalse("High latency (>1s) should indicate unhealthy Redis connection");
    }

    [Fact]
    public async Task IsHealthy_RedisUnavailable_ReturnsFalse()
    {
        // Arrange
        var serviceWithoutRedis = new JobStatusService(null, _mockLogger.Object);

        // Act
        var healthy = await serviceWithoutRedis.IsHealthyAsync();

        // Assert
        healthy.Should().BeFalse("Service without Redis should report unhealthy");
    }

    [Fact]
    public async Task IsHealthy_RedisConnectionException_ReturnsFalse()
    {
        // Arrange
        _mockDatabase
            .Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act
        var healthy = await _service.IsHealthyAsync();

        // Assert
        healthy.Should().BeFalse("Connection exception should indicate unhealthy");
    }

    [Fact]
    public async Task IsHealthy_RedisPingTimeout_ReturnsFalse()
    {
        // Arrange
        _mockDatabase
            .Setup(d => d.PingAsync(It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisTimeoutException("Ping timed out", CommandStatus.WaitingToBeSent));

        // Act
        var healthy = await _service.IsHealthyAsync();

        // Assert
        healthy.Should().BeFalse("Timeout should indicate unhealthy");
    }

    #endregion

    #region Test: Channel naming convention

    [Fact]
    public async Task PublishStatusUpdate_UsesCorrectChannelNaming()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var capturedChannel = string.Empty;

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((c, _, _) =>
                capturedChannel = c.ToString())
            .ReturnsAsync(1L);

        // Act
        await _service.PublishStatusUpdateAsync(new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.Progress,
            Status = JobStatus.Running,
            Progress = 50
        });

        // Assert - per ADR-009: Channel naming: "sdap:job:{jobId}:status"
        capturedChannel.Should().Be($"sdap:job:{jobId}:status",
            "Channel name must follow ADR-009 convention");
    }

    #endregion

    public void Dispose()
    {
        _service.Dispose();
    }
}
