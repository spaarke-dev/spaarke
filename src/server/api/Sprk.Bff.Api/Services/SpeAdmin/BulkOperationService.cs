using System.Collections.Concurrent;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;

namespace Sprk.Bff.Api.Services.SpeAdmin;

/// <summary>
/// Background service that executes bulk SPE container operations (delete, permission assignment)
/// with per-item progress tracking.
///
/// Architecture:
///   - Endpoints enqueue a <see cref="BulkOperationJob"/> and return the operation ID immediately (202 Accepted).
///   - This BackgroundService dequeues jobs and processes items sequentially per job, updating
///     the in-memory status record (<see cref="BulkOperationStatus"/>) after each item.
///   - Callers poll GET /api/spe/bulk/{operationId}/status to observe progress.
///   - Status records are retained in memory for <see cref="StatusRetentionMinutes"/> minutes after completion.
///
/// ADR-001: BackgroundService, not Azure Functions.
/// ADR-007: No Graph SDK types exposed in public API surface.
/// ADR-010: Registered as Singleton in DI; hosted via AddHostedService factory delegate.
/// </summary>
public sealed class BulkOperationService : BackgroundService
{
    // =========================================================================
    // Constants
    // =========================================================================

    /// <summary>
    /// How long completed operation status records are retained in memory after finishing.
    /// Allows the UI polling loop to receive the final state before expiry.
    /// </summary>
    private const int StatusRetentionMinutes = 30;

    // =========================================================================
    // Internal state
    // =========================================================================

    /// <summary>
    /// Pending jobs waiting to be processed by the background loop.
    /// Channel provides back-pressure-free FIFO delivery with minimal overhead.
    /// </summary>
    private readonly System.Threading.Channels.Channel<BulkOperationJob> _queue =
        System.Threading.Channels.Channel.CreateUnbounded<BulkOperationJob>(
            new System.Threading.Channels.UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });

    /// <summary>
    /// Live and recently-completed operation status records keyed by operation ID.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, MutableOperationStatus> _statuses = new();

    private readonly SpeAdminGraphService _graphService;
    private readonly ILogger<BulkOperationService> _logger;

    // =========================================================================
    // Constructor
    // =========================================================================

    public BulkOperationService(
        SpeAdminGraphService graphService,
        ILogger<BulkOperationService> logger)
    {
        _graphService = graphService;
        _logger = logger;
    }

    // =========================================================================
    // Public API — used by endpoints
    // =========================================================================

    /// <summary>
    /// Enqueues a bulk delete job and returns the tracking operation ID.
    /// The caller should return 202 Accepted with the operation ID to the HTTP client.
    /// </summary>
    /// <param name="request">Validated bulk delete request.</param>
    /// <returns>Operation ID for status polling.</returns>
    public Guid EnqueueDelete(BulkDeleteRequest request)
    {
        var operationId = Guid.NewGuid();

        _statuses[operationId] = new MutableOperationStatus
        {
            OperationId = operationId,
            OperationType = BulkOperationType.Delete,
            Total = request.ContainerIds.Count,
            StartedAt = DateTimeOffset.UtcNow,
        };

        _queue.Writer.TryWrite(new BulkOperationJob(operationId, BulkOperationType.Delete, request, null));

        _logger.LogInformation(
            "BulkOperationService: enqueued Delete job {OperationId} for {Count} containers, configId={ConfigId}",
            operationId, request.ContainerIds.Count, request.ConfigId);

        return operationId;
    }

    /// <summary>
    /// Enqueues a bulk permission assignment job and returns the tracking operation ID.
    /// The caller should return 202 Accepted with the operation ID to the HTTP client.
    /// </summary>
    /// <param name="request">Validated bulk permissions request.</param>
    /// <returns>Operation ID for status polling.</returns>
    public Guid EnqueuePermissions(BulkPermissionsRequest request)
    {
        var operationId = Guid.NewGuid();

        _statuses[operationId] = new MutableOperationStatus
        {
            OperationId = operationId,
            OperationType = BulkOperationType.AssignPermissions,
            Total = request.ContainerIds.Count,
            StartedAt = DateTimeOffset.UtcNow,
        };

        _queue.Writer.TryWrite(new BulkOperationJob(operationId, BulkOperationType.AssignPermissions, null, request));

        _logger.LogInformation(
            "BulkOperationService: enqueued AssignPermissions job {OperationId} for {Count} containers, configId={ConfigId}",
            operationId, request.ContainerIds.Count, request.ConfigId);

        return operationId;
    }

    /// <summary>
    /// Returns the current status of a bulk operation, or <c>null</c> if the operation ID
    /// is unknown or has expired from the in-memory store.
    /// </summary>
    /// <param name="operationId">Operation ID returned by the enqueue endpoint.</param>
    public BulkOperationStatus? GetStatus(Guid operationId)
    {
        if (!_statuses.TryGetValue(operationId, out var mutable))
            return null;

        return mutable.ToImmutable();
    }

    // =========================================================================
    // BackgroundService — processing loop
    // =========================================================================

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BulkOperationService: background processing loop started.");

        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("BulkOperationService: shutting down — cancellation requested.");
                break;
            }
            catch (Exception ex)
            {
                // Unexpected outer exception — mark entire job as failed
                _logger.LogError(ex,
                    "BulkOperationService: unexpected error processing job {OperationId}",
                    job.OperationId);

                if (_statuses.TryGetValue(job.OperationId, out var status))
                {
                    status.IsFinished = true;
                    status.CompletedAt = DateTimeOffset.UtcNow;
                }
            }
        }

        _logger.LogInformation("BulkOperationService: background processing loop stopped.");
    }

    // =========================================================================
    // Job processing
    // =========================================================================

    private async Task ProcessJobAsync(BulkOperationJob job, CancellationToken ct)
    {
        if (!_statuses.TryGetValue(job.OperationId, out var status))
        {
            _logger.LogWarning(
                "BulkOperationService: job {OperationId} has no status record — skipping.",
                job.OperationId);
            return;
        }

        _logger.LogInformation(
            "BulkOperationService: starting job {OperationId} ({Type}, {Count} items)",
            job.OperationId, job.OperationType, status.Total);

        switch (job.OperationType)
        {
            case BulkOperationType.Delete when job.DeleteRequest is not null:
                await ProcessDeleteJobAsync(job.OperationId, job.DeleteRequest, status, ct);
                break;

            case BulkOperationType.AssignPermissions when job.PermissionsRequest is not null:
                await ProcessPermissionsJobAsync(job.OperationId, job.PermissionsRequest, status, ct);
                break;

            default:
                _logger.LogError(
                    "BulkOperationService: job {OperationId} has unexpected type {Type} or missing request payload.",
                    job.OperationId, job.OperationType);
                status.IsFinished = true;
                status.CompletedAt = DateTimeOffset.UtcNow;
                break;
        }

        // Schedule status expiry after retention window (fire-and-forget background task)
        _ = ExpireStatusAfterDelayAsync(job.OperationId);
    }

    /// <summary>
    /// Processes a bulk delete job: soft-deletes each container sequentially via Graph API.
    /// Progress is updated after every item (increment or error).
    /// </summary>
    private async Task ProcessDeleteJobAsync(
        Guid operationId,
        BulkDeleteRequest request,
        MutableOperationStatus status,
        CancellationToken ct)
    {
        if (!Guid.TryParse(request.ConfigId, out var configGuid))
        {
            _logger.LogError(
                "BulkOperationService: Delete job {OperationId} — invalid configId '{ConfigId}'",
                operationId, request.ConfigId);
            status.IsFinished = true;
            status.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }

        SpeAdminGraphService.ContainerTypeConfig? config;
        Microsoft.Graph.GraphServiceClient? graphClient;

        try
        {
            config = await _graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
            {
                _logger.LogError(
                    "BulkOperationService: Delete job {OperationId} — configId {ConfigId} not found.",
                    operationId, configGuid);
                status.IsFinished = true;
                status.CompletedAt = DateTimeOffset.UtcNow;
                return;
            }

            graphClient = await _graphService.GetClientForConfigAsync(config, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "BulkOperationService: Delete job {OperationId} — failed to resolve config or Graph client.",
                operationId);
            status.IsFinished = true;
            status.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }

        // Process each container sequentially — error per item does not stop the batch
        foreach (var containerId in request.ContainerIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Soft-delete: move to recycle bin (not permanent delete)
                await _graphService.SoftDeleteContainerAsync(graphClient, containerId, ct);

                status.Completed++;

                _logger.LogDebug(
                    "BulkOperationService: Delete job {OperationId} — container '{ContainerId}' soft-deleted ({Done}/{Total}).",
                    operationId, containerId, status.Completed, status.Total);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ODataError odataError)
            {
                var msg = odataError.Error?.Message
                    ?? $"Graph API error (HTTP {odataError.ResponseStatusCode})";

                _logger.LogWarning(
                    "BulkOperationService: Delete job {OperationId} — container '{ContainerId}' failed: {Error}",
                    operationId, containerId, msg);

                status.Failed++;
                status.Errors.Add(new BulkOperationItemError(containerId, msg));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "BulkOperationService: Delete job {OperationId} — container '{ContainerId}' failed with unexpected error.",
                    operationId, containerId);

                status.Failed++;
                status.Errors.Add(new BulkOperationItemError(containerId, ex.Message));
            }
        }

        status.IsFinished = true;
        status.CompletedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "BulkOperationService: Delete job {OperationId} finished — {Completed}/{Total} succeeded, {Failed} failed.",
            operationId, status.Completed, status.Total, status.Failed);
    }

    /// <summary>
    /// Processes a bulk permission assignment job: grants the requested role on each container
    /// sequentially via Graph API. Progress is updated after every item.
    /// </summary>
    private async Task ProcessPermissionsJobAsync(
        Guid operationId,
        BulkPermissionsRequest request,
        MutableOperationStatus status,
        CancellationToken ct)
    {
        if (!Guid.TryParse(request.ConfigId, out var configGuid))
        {
            _logger.LogError(
                "BulkOperationService: Permissions job {OperationId} — invalid configId '{ConfigId}'",
                operationId, request.ConfigId);
            status.IsFinished = true;
            status.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }

        SpeAdminGraphService.ContainerTypeConfig? config;
        Microsoft.Graph.GraphServiceClient? graphClient;

        try
        {
            config = await _graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
            {
                _logger.LogError(
                    "BulkOperationService: Permissions job {OperationId} — configId {ConfigId} not found.",
                    operationId, configGuid);
                status.IsFinished = true;
                status.CompletedAt = DateTimeOffset.UtcNow;
                return;
            }

            graphClient = await _graphService.GetClientForConfigAsync(config, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "BulkOperationService: Permissions job {OperationId} — failed to resolve config or Graph client.",
                operationId);
            status.IsFinished = true;
            status.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }

        // Process each container sequentially — error per item does not stop the batch
        foreach (var containerId in request.ContainerIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await _graphService.GrantContainerPermissionAsync(
                    graphClient, containerId, request.UserId, request.GroupId, request.Role, ct);

                status.Completed++;

                _logger.LogDebug(
                    "BulkOperationService: Permissions job {OperationId} — container '{ContainerId}' permission granted ({Done}/{Total}).",
                    operationId, containerId, status.Completed, status.Total);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ODataError odataError)
            {
                var msg = odataError.Error?.Message
                    ?? $"Graph API error (HTTP {odataError.ResponseStatusCode})";

                _logger.LogWarning(
                    "BulkOperationService: Permissions job {OperationId} — container '{ContainerId}' failed: {Error}",
                    operationId, containerId, msg);

                status.Failed++;
                status.Errors.Add(new BulkOperationItemError(containerId, msg));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "BulkOperationService: Permissions job {OperationId} — container '{ContainerId}' failed with unexpected error.",
                    operationId, containerId);

                status.Failed++;
                status.Errors.Add(new BulkOperationItemError(containerId, ex.Message));
            }
        }

        status.IsFinished = true;
        status.CompletedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "BulkOperationService: Permissions job {OperationId} finished — {Completed}/{Total} succeeded, {Failed} failed.",
            operationId, status.Completed, status.Total, status.Failed);
    }

    /// <summary>
    /// Removes a completed operation's status record after the retention window expires.
    /// </summary>
    private async Task ExpireStatusAfterDelayAsync(Guid operationId)
    {
        await Task.Delay(TimeSpan.FromMinutes(StatusRetentionMinutes)).ConfigureAwait(false);
        _statuses.TryRemove(operationId, out _);

        _logger.LogDebug(
            "BulkOperationService: status for operation {OperationId} expired and removed.",
            operationId);
    }

    // =========================================================================
    // Internal types
    // =========================================================================

    /// <summary>
    /// Mutable status record held in-memory while a job is running.
    /// Converted to immutable <see cref="BulkOperationStatus"/> for API responses.
    /// </summary>
    private sealed class MutableOperationStatus
    {
        public Guid OperationId { get; init; }
        public BulkOperationType OperationType { get; init; }
        public int Total { get; init; }
        public int Completed;
        public int Failed;
        public bool IsFinished;
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? CompletedAt;
        public List<BulkOperationItemError> Errors { get; } = [];

        public BulkOperationStatus ToImmutable() => new(
            OperationId: OperationId,
            OperationType: OperationType,
            Total: Total,
            Completed: Completed,
            Failed: Failed,
            IsFinished: IsFinished,
            Errors: Errors.AsReadOnly(),
            StartedAt: StartedAt,
            CompletedAt: CompletedAt);
    }

    /// <summary>
    /// Internal job descriptor passed through the channel to the background processor.
    /// </summary>
    private sealed record BulkOperationJob(
        Guid OperationId,
        BulkOperationType OperationType,
        BulkDeleteRequest? DeleteRequest,
        BulkPermissionsRequest? PermissionsRequest);
}
