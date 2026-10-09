using System.Collections.Concurrent;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
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
/// Runs in the BFF as a BackgroundService, governed by ADR-052.
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
    /// <param name="scope">
    /// The caller's reach, captured when the request is accepted (owner round 20 item 2): the job acts only on
    /// containers bound inside it. The job runs with no caller context, so this snapshot is the caller's authority.
    /// </param>
    /// <param name="startedBy">The caller's Entra object id; only that caller may read the operation's status.</param>
    /// <returns>Operation ID for status polling.</returns>
    public Guid EnqueueDelete(BulkDeleteRequest request, SpeAdminCallerScope scope, string? startedBy)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var operationId = Guid.NewGuid();

        _statuses[operationId] = new MutableOperationStatus
        {
            OperationId = operationId,
            OperationType = BulkOperationType.Delete,
            Total = request.ContainerIds.Count,
            StartedAt = DateTimeOffset.UtcNow,
            StartedBy = startedBy,
        };

        _queue.Writer.TryWrite(new BulkOperationJob(operationId, BulkOperationType.Delete, request, null, scope));

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
    /// <param name="scope">The caller's reach, captured at acceptance (see <see cref="EnqueueDelete"/>).</param>
    /// <param name="startedBy">The caller's Entra object id; only that caller may read the operation's status.</param>
    /// <returns>Operation ID for status polling.</returns>
    public Guid EnqueuePermissions(BulkPermissionsRequest request, SpeAdminCallerScope scope, string? startedBy)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var operationId = Guid.NewGuid();

        _statuses[operationId] = new MutableOperationStatus
        {
            OperationId = operationId,
            OperationType = BulkOperationType.AssignPermissions,
            Total = request.ContainerIds.Count,
            StartedAt = DateTimeOffset.UtcNow,
            StartedBy = startedBy,
        };

        _queue.Writer.TryWrite(new BulkOperationJob(operationId, BulkOperationType.AssignPermissions, null, request, scope));

        _logger.LogInformation(
            "BulkOperationService: enqueued AssignPermissions job {OperationId} for {Count} containers, configId={ConfigId}",
            operationId, request.ContainerIds.Count, request.ConfigId);

        return operationId;
    }

    /// <summary>
    /// Returns the current status of a bulk operation, or <c>null</c> if the operation ID is unknown, has expired
    /// from the in-memory store, or was started by someone else (task 165: ONE answer for all three, so the status
    /// route is not an oracle for — or a window into — another administrator's operation and its container ids).
    /// </summary>
    /// <param name="operationId">Operation ID returned by the enqueue endpoint.</param>
    /// <param name="callerObjectId">The Entra object id of the caller asking.</param>
    public BulkOperationStatus? GetStatus(Guid operationId, string? callerObjectId)
    {
        if (!_statuses.TryGetValue(operationId, out var mutable))
            return null;

        if (string.IsNullOrWhiteSpace(mutable.StartedBy)
            || string.IsNullOrWhiteSpace(callerObjectId)
            || !string.Equals(mutable.StartedBy, callerObjectId.Trim(), StringComparison.OrdinalIgnoreCase))
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
                await ProcessDeleteJobAsync(job.OperationId, job.DeleteRequest, job.Scope, status, ct);
                break;

            case BulkOperationType.AssignPermissions when job.PermissionsRequest is not null:
                await ProcessPermissionsJobAsync(job.OperationId, job.PermissionsRequest, job.Scope, status, ct);
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
        SpeAdminCallerScope scope,
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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "BulkOperationService: Delete job {OperationId} — failed to resolve config.",
                operationId);
            status.IsFinished = true;
            status.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }

        // Process each container sequentially — error per item does not stop the batch.
        // Every write goes through DeleteContainerInScopeAsync, which refuses a container that is not of the
        // config's container type or is bound outside the caller's business units (task 165, owner round 20).
        foreach (var containerId in request.ContainerIds)
        {
            ct.ThrowIfCancellationRequested();

            // Per container: one this stamp does not own is refused before any read (task 227d, owner D29).
            var (graphClient, clientError) = await ClientForContainerAsync(config, containerId, operationId, ct);
            var error = clientError
                ?? await DeleteContainerInScopeAsync(graphClient!, config, scope, containerId, operationId, ct);
            if (error is null)
            {
                status.Completed++;
            }
            else
            {
                status.Failed++;
                status.Errors.Add(error);
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
        SpeAdminCallerScope scope,
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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "BulkOperationService: Permissions job {OperationId} — failed to resolve config.",
                operationId);
            status.IsFinished = true;
            status.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }

        // Process each container sequentially — error per item does not stop the batch.
        // Every grant goes through GrantOnContainerInScopeAsync (task 165, owner round 20).
        foreach (var containerId in request.ContainerIds)
        {
            ct.ThrowIfCancellationRequested();

            var (graphClient, clientError) = await ClientForContainerAsync(config, containerId, operationId, ct);
            var error = clientError
                ?? await GrantOnContainerInScopeAsync(
                    graphClient!, config, scope, containerId, request.UserId, request.GroupId, request.Role, operationId, ct);
            if (error is null)
            {
                status.Completed++;
            }
            else
            {
                status.Failed++;
                status.Errors.Add(error);
            }
        }

        status.IsFinished = true;
        status.CompletedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "BulkOperationService: Permissions job {OperationId} finished — {Completed}/{Total} succeeded, {Failed} failed.",
            operationId, status.Completed, status.Total, status.Failed);
    }

    // =========================================================================
    // Per-item processing (task 165) — the ONLY call sites of the two Graph writes
    // =========================================================================

    /// <summary>
    /// The per-item error for a container this job will not touch. ONE text for "not found", "of another
    /// container type", "of a type Graph does not report", "bound to a business unit the caller does not administer",
    /// "unbound" or "malformed" (for EVERY caller, root included — round 35 item 2) and "the read failed", so a refused
    /// item's status does not tell the caller which of those it was. The reason is logged.
    /// </summary>
    internal const string ContainerNotInScopeError =
        "The container was not found among the containers you administer under this configuration; it was not changed.";

    /// <summary>
    /// Soft-deletes <paramref name="containerId"/> ONLY if it is of <paramref name="config"/>'s container type AND
    /// bound to a business unit <paramref name="scope"/> reaches. Returns null on success, or the item's error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why</b> (unified-access-control-r2 task 165, sweep finding #44; owner round 20 item 2). The job runs with no
    /// caller context and an app-only Graph client for the config's owning app. The tenant-scope filter confines the
    /// CONFIG a request names to the caller's business units, but the container ids are caller-chosen, and one
    /// container type serves several customers (Model 1). Each container is read first — its type and its
    /// business-unit stamp (<see cref="SpeContainerBusinessUnitStamp"/>) — and written only when the caller's scope
    /// captured at acceptance reaches it (<see cref="SpeAdminCallerScope.CanReach"/>), or refused.
    /// </para>
    /// </remarks>
    internal async Task<BulkOperationItemError?> DeleteContainerInScopeAsync(
        Microsoft.Graph.GraphServiceClient graphClient,
        SpeAdminGraphService.ContainerTypeConfig config,
        SpeAdminCallerScope scope,
        string containerId,
        Guid operationId,
        CancellationToken ct)
    {
        if (!await IsContainerInScopeAsync(graphClient, config, scope, containerId, operationId, ct))
        {
            return new BulkOperationItemError(containerId, ContainerNotInScopeError);
        }

        try
        {
            // Soft-delete: move to recycle bin (not permanent delete).
            // GraphCallScope translates ODataError -> SpaarkeStorageException inside
            // Infrastructure.Graph, so this file catches a Spaarke-domain type (ADR-007 §1).
            await GraphCallScope.Run(
                () => _graphService.SoftDeleteContainerAsync(graphClient, containerId, ct),
                $"SoftDeleteContainer({containerId})");

            _logger.LogDebug(
                "BulkOperationService: Delete job {OperationId} — container '{ContainerId}' soft-deleted.",
                operationId, containerId);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SpaarkeStorageException storageEx)
        {
            var msg = ProblemDetailsHelper.Redact(storageEx.Message)
                ?? $"Graph API error (HTTP {storageEx.StatusCode})";

            _logger.LogWarning(
                "BulkOperationService: Delete job {OperationId} — container '{ContainerId}' failed: {Error}",
                operationId, containerId, msg);
            return new BulkOperationItemError(containerId, msg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "BulkOperationService: Delete job {OperationId} — container '{ContainerId}' failed with unexpected error.",
                operationId, containerId);
            return new BulkOperationItemError(containerId, ex.Message);
        }
    }

    /// <summary>
    /// Grants <paramref name="role"/> on <paramref name="containerId"/> ONLY if it is of <paramref name="config"/>'s
    /// container type AND bound inside <paramref name="scope"/> (sweep finding #72; owner round 20 item 2). Returns
    /// null on success, or the item's error. See <see cref="DeleteContainerInScopeAsync"/> for why.
    /// </summary>
    internal async Task<BulkOperationItemError?> GrantOnContainerInScopeAsync(
        Microsoft.Graph.GraphServiceClient graphClient,
        SpeAdminGraphService.ContainerTypeConfig config,
        SpeAdminCallerScope scope,
        string containerId,
        string? userId,
        string? groupId,
        string role,
        Guid operationId,
        CancellationToken ct)
    {
        if (!await IsContainerInScopeAsync(graphClient, config, scope, containerId, operationId, ct))
        {
            return new BulkOperationItemError(containerId, ContainerNotInScopeError);
        }

        try
        {
            // See the delete path — GraphCallScope keeps the ODataError inside
            // Infrastructure.Graph so this file stays ADR-007 §1 clean.
            await GraphCallScope.Run(
                () => _graphService.GrantContainerPermissionAsync(
                    graphClient, containerId, userId, groupId, role, ct),
                $"GrantContainerPermission({containerId})");

            _logger.LogDebug(
                "BulkOperationService: Permissions job {OperationId} — container '{ContainerId}' permission granted.",
                operationId, containerId);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SpaarkeStorageException storageEx)
        {
            var msg = ProblemDetailsHelper.Redact(storageEx.Message)
                ?? $"Graph API error (HTTP {storageEx.StatusCode})";

            _logger.LogWarning(
                "BulkOperationService: Permissions job {OperationId} — container '{ContainerId}' failed: {Error}",
                operationId, containerId, msg);
            return new BulkOperationItemError(containerId, msg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "BulkOperationService: Permissions job {OperationId} — container '{ContainerId}' failed with unexpected error.",
                operationId, containerId);
            return new BulkOperationItemError(containerId, ex.Message);
        }
    }

    /// <summary>
    /// The app-only client for ONE container (task 227d, owner D29): refused — 404 <c>spe_container_not_owned</c> —
    /// unless this stamp owns it. A refusal is reported as <see cref="ContainerNotInScopeError"/>, the same item error as
    /// a container outside the caller's scope, so a batch reveals nothing about other customers' containers. Any other
    /// failure to get the client is that item's error; the batch continues.
    /// </summary>
    internal async Task<(Microsoft.Graph.GraphServiceClient? Client, BulkOperationItemError? Error)> ClientForContainerAsync(
        SpeAdminGraphService.ContainerTypeConfig config, string containerId, Guid operationId, CancellationToken ct)
    {
        try
        {
            return (await _graphService.GetClientForContainerAsync(config, containerId, ct), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdapProblemException ex) when (ex.Code == SpeContainerOwnershipGuard.NotOwnedErrorCode)
        {
            _logger.LogWarning(
                "BulkOperationService: job {OperationId} — container '{ContainerId}' is not this stamp's; refused without a read.",
                operationId, containerId);
            return (null, new BulkOperationItemError(containerId, ContainerNotInScopeError));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "BulkOperationService: job {OperationId} — no Graph client for container '{ContainerId}'; not changed.",
                operationId, containerId);
            return (null, new BulkOperationItemError(containerId, ProblemDetailsHelper.Redact(ex.Message) ?? ex.GetType().Name));
        }
    }

    /// <summary>
    /// True only when the container can be read AND its container type (as Graph reports it) is the config's AND
    /// <paramref name="scope"/> reaches its business-unit binding. Every other outcome — not found, another type, an
    /// unparseable type on either side, a binding outside the scope, a read fault — is false (fail closed, ADR-003).
    /// </summary>
    private async Task<bool> IsContainerInScopeAsync(
        Microsoft.Graph.GraphServiceClient graphClient,
        SpeAdminGraphService.ContainerTypeConfig config,
        SpeAdminCallerScope scope,
        string containerId,
        Guid operationId,
        CancellationToken ct)
    {
        try
        {
            var read = await GraphCallScope.Run(
                () => _graphService.GetContainerBindingAsync(graphClient, containerId, deleted: false, ct),
                $"GetContainerBinding({containerId})");

            // Bulk requires the type to be REPORTED and equal: a container whose type Graph does not report is refused
            // (the job acts app-only with no per-request filter, so an unreported type is not taken on trust — D10).
            var typeReportedAndEqual = read is not null && SpeAdminTenantScope.SameGuid(read.ContainerTypeId, config.ContainerTypeId);
            var refusal = SpeAdminTenantScope.ClassifyContainer(scope, config.ContainerTypeId, read);
            var permitted = typeReportedAndEqual && refusal == SpeContainerRefusal.None;

            if (!permitted)
            {
                _logger.LogWarning(
                    "BulkOperationService: job {OperationId} — container '{ContainerId}' refused through config {ConfigId} " +
                    "without a write — reason {Reason}.",
                    operationId, containerId, config.ConfigId,
                    refusal != SpeContainerRefusal.None ? SpeAdminTenantScope.RefusalReason(refusal) : "type_not_reported");
            }

            return permitted;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "BulkOperationService: job {OperationId} — could not read container '{ContainerId}' to verify its " +
                "container type and business-unit binding; refused without a write.",
                operationId, containerId);
            return false;
        }
    }

    /// <summary>
    /// The number of operations currently tracked. Read-only, for tests: the service is sealed, and
    /// "a refused request enqueued nothing" cannot otherwise be asserted deterministically.
    /// </summary>
    internal int TrackedOperationCount => _statuses.Count;

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

        /// <summary>The Entra object id of the caller who started the operation (task 165).</summary>
        public string? StartedBy { get; init; }

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
        BulkPermissionsRequest? PermissionsRequest,
        SpeAdminCallerScope Scope);
}
