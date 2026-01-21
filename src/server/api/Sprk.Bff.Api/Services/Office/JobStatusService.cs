using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Office;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Implementation of <see cref="IJobStatusService"/> using Redis pub/sub for
/// real-time job status distribution to SSE clients.
/// </summary>
/// <remarks>
/// <para>
/// This service:
/// - Publishes status updates to Redis channels for real-time SSE delivery
/// - Manages subscriptions for SSE endpoints
/// - Updates ProcessingJob records in Dataverse (TODO: integrate when ready)
/// - Handles Redis connection failures gracefully
/// </para>
/// <para>
/// Per ADR-009:
/// - Uses IConnectionMultiplexer for Redis operations
/// - Channel naming: "sdap:job:{jobId}:status"
/// - Handles reconnection automatically
/// </para>
/// <para>
/// Per spec.md:
/// - Delivers updates within 1 second (target: &lt;100ms)
/// - Supports reconnection via sequence numbers
/// - Graceful degradation when Redis unavailable
/// </para>
/// </remarks>
public class JobStatusService : IJobStatusService, IDisposable
{
    private const string ChannelPrefix = "sdap:job:";
    private const string ChannelSuffix = ":status";

    private readonly IConnectionMultiplexer? _redis;
    private readonly ISubscriber? _subscriber;
    private readonly ILogger<JobStatusService> _logger;
    private readonly SemaphoreSlim _sequenceLock = new(1, 1);
    private readonly Dictionary<Guid, long> _jobSequences = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly ActivitySource ActivitySource = new("Sprk.Bff.Api.JobStatus");

    /// <summary>
    /// Initializes a new instance of the <see cref="JobStatusService"/> class.
    /// </summary>
    /// <param name="redis">Optional Redis connection multiplexer (null if Redis not configured).</param>
    /// <param name="logger">Logger instance.</param>
    public JobStatusService(
        IConnectionMultiplexer? redis,
        ILogger<JobStatusService> logger)
    {
        _redis = redis;
        _subscriber = redis?.GetSubscriber();
        _logger = logger;

        if (redis is null)
        {
            _logger.LogWarning(
                "JobStatusService initialized without Redis - pub/sub will be disabled, " +
                "SSE will fall back to polling");
        }
        else
        {
            _logger.LogInformation(
                "JobStatusService initialized with Redis pub/sub enabled");
        }
    }

    /// <inheritdoc />
    public async Task<bool> PublishStatusUpdateAsync(
        JobStatusUpdate update,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("PublishStatusUpdate");
        activity?.SetTag("job.id", update.JobId.ToString());
        activity?.SetTag("update.type", update.UpdateType.ToString());

        if (_subscriber is null)
        {
            _logger.LogDebug(
                "Redis not available, skipping pub/sub for job {JobId}",
                update.JobId);
            return false;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Assign sequence number for ordering
            var sequence = await GetNextSequenceAsync(update.JobId, cancellationToken);
            var updateWithSequence = update with { Sequence = sequence };

            // Serialize and publish
            var channelName = GetChannelName(update.JobId);
            var message = JsonSerializer.Serialize(updateWithSequence, JsonOptions);

            var subscriberCount = await _subscriber.PublishAsync(
                RedisChannel.Literal(channelName),
                message);

            stopwatch.Stop();

            _logger.LogDebug(
                "Published status update for job {JobId}: Type={UpdateType}, Progress={Progress}, " +
                "Sequence={Sequence}, Subscribers={SubscriberCount}, Duration={Duration}ms",
                update.JobId,
                update.UpdateType,
                update.Progress,
                sequence,
                subscriberCount,
                stopwatch.ElapsedMilliseconds);

            activity?.SetTag("subscribers.count", subscriberCount);
            activity?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds);

            // Warn if latency exceeds target
            if (stopwatch.ElapsedMilliseconds > 100)
            {
                _logger.LogWarning(
                    "Status update publish exceeded 100ms target: {Duration}ms for job {JobId}",
                    stopwatch.ElapsedMilliseconds,
                    update.JobId);
            }

            return true;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(
                ex,
                "Redis connection error publishing status update for job {JobId}",
                update.JobId);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error publishing status update for job {JobId}",
                update.JobId);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<JobStatusUpdate> SubscribeToJobAsync(
        Guid jobId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("SubscribeToJob");
        activity?.SetTag("job.id", jobId.ToString());

        if (_subscriber is null)
        {
            _logger.LogDebug(
                "Redis not available, subscription will complete immediately for job {JobId}",
                jobId);
            yield break;
        }

        var channelName = GetChannelName(jobId);
        var channel = Channel.CreateUnbounded<JobStatusUpdate>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        ChannelMessageQueue? messageQueue = null;

        try
        {
            _logger.LogInformation(
                "Subscribing to job status updates for job {JobId} on channel {Channel}",
                jobId,
                channelName);

            // Subscribe to Redis channel
            messageQueue = await _subscriber.SubscribeAsync(RedisChannel.Literal(channelName));

            // Start background task to process Redis messages
            var processingTask = ProcessRedisMessagesAsync(
                jobId,
                messageQueue,
                channel.Writer,
                cancellationToken);

            // Yield updates as they arrive
            await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return update;

                // Stop after terminal state
                if (update.UpdateType is JobStatusUpdateType.JobCompleted
                    or JobStatusUpdateType.JobFailed
                    or JobStatusUpdateType.JobCancelled)
                {
                    _logger.LogInformation(
                        "Subscription ending for job {JobId} due to terminal state {State}",
                        jobId,
                        update.UpdateType);
                    break;
                }
            }

            // Wait for processing task to complete
            await processingTask;
        }
        finally
        {
            // Unsubscribe from Redis channel
            if (messageQueue is not null)
            {
                try
                {
                    await messageQueue.UnsubscribeAsync();
                    _logger.LogDebug(
                        "Unsubscribed from job status updates for job {JobId}",
                        jobId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Error unsubscribing from job {JobId} channel",
                        jobId);
                }
            }

            channel.Writer.TryComplete();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateJobStatusAsync(
        Guid jobId,
        JobStatus status,
        int progress,
        string? currentPhase = null,
        CompletedPhase? completedPhase = null,
        JobResult? result = null,
        JobError? error = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("UpdateJobStatus");
        activity?.SetTag("job.id", jobId.ToString());
        activity?.SetTag("status", status.ToString());
        activity?.SetTag("progress", progress);

        _logger.LogInformation(
            "Updating job status: JobId={JobId}, Status={Status}, Progress={Progress}, Phase={Phase}",
            jobId,
            status,
            progress,
            currentPhase);

        try
        {
            // TODO: Update Dataverse ProcessingJob record when SDK is integrated
            // For now, just publish the update to Redis for SSE subscribers

            // Determine update type based on status
            var updateType = status switch
            {
                JobStatus.Completed => JobStatusUpdateType.JobCompleted,
                JobStatus.Failed => JobStatusUpdateType.JobFailed,
                JobStatus.Cancelled => JobStatusUpdateType.JobCancelled,
                _ when completedPhase is not null => JobStatusUpdateType.StageComplete,
                _ => JobStatusUpdateType.Progress
            };

            var update = new JobStatusUpdate
            {
                JobId = jobId,
                UpdateType = updateType,
                Status = status,
                Progress = progress,
                CurrentPhase = currentPhase,
                CompletedPhase = completedPhase,
                Result = result,
                Error = error,
                Timestamp = DateTimeOffset.UtcNow
            };

            // Publish to Redis for SSE subscribers
            var published = await PublishStatusUpdateAsync(update, cancellationToken);

            if (!published)
            {
                _logger.LogWarning(
                    "Failed to publish status update for job {JobId} - SSE clients may not receive update",
                    jobId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error updating job status for job {JobId}",
                jobId);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompleteJobAsync(
        Guid jobId,
        JobResult result,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Marking job as completed: JobId={JobId}, ArtifactId={ArtifactId}",
            jobId,
            result.Artifact?.Id);

        return await UpdateJobStatusAsync(
            jobId,
            status: JobStatus.Completed,
            progress: 100,
            currentPhase: "Completed",
            result: result,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> FailJobAsync(
        Guid jobId,
        JobError error,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Marking job as failed: JobId={JobId}, ErrorCode={ErrorCode}, Message={Message}",
            jobId,
            error.Code,
            error.Message);

        return await UpdateJobStatusAsync(
            jobId,
            status: JobStatus.Failed,
            progress: 0,
            currentPhase: "Failed",
            error: error,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (_redis is null)
        {
            return false;
        }

        try
        {
            var database = _redis.GetDatabase();
            var pong = await database.PingAsync();

            var healthy = pong.TotalMilliseconds < 1000;

            if (!healthy)
            {
                _logger.LogWarning(
                    "Redis ping latency high: {Latency}ms",
                    pong.TotalMilliseconds);
            }

            return healthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Redis health check failed");
            return false;
        }
    }

    /// <summary>
    /// Gets the next sequence number for a job's status updates.
    /// </summary>
    private async Task<long> GetNextSequenceAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await _sequenceLock.WaitAsync(cancellationToken);
        try
        {
            if (!_jobSequences.TryGetValue(jobId, out var sequence))
            {
                sequence = 0;
            }

            sequence++;
            _jobSequences[jobId] = sequence;

            return sequence;
        }
        finally
        {
            _sequenceLock.Release();
        }
    }

    /// <summary>
    /// Processes incoming Redis messages and writes them to the channel.
    /// </summary>
    private async Task ProcessRedisMessagesAsync(
        Guid jobId,
        ChannelMessageQueue messageQueue,
        ChannelWriter<JobStatusUpdate> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in messageQueue.WithCancellation(cancellationToken))
            {
                try
                {
                    var update = JsonSerializer.Deserialize<JobStatusUpdate>(
                        message.Message.ToString(),
                        JsonOptions);

                    if (update is not null)
                    {
                        await writer.WriteAsync(update, cancellationToken);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to deserialize job status update for job {JobId}",
                        jobId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
            _logger.LogDebug(
                "Redis message processing cancelled for job {JobId}",
                jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing Redis messages for job {JobId}",
                jobId);
        }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>
    /// Gets the Redis channel name for a job's status updates.
    /// </summary>
    private static string GetChannelName(Guid jobId)
    {
        return $"{ChannelPrefix}{jobId}{ChannelSuffix}";
    }

    /// <summary>
    /// Cleans up resources.
    /// </summary>
    public void Dispose()
    {
        _sequenceLock.Dispose();
    }
}
