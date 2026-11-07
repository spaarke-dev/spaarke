using System.Text.Json;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Field-mapping-server-write-path-r1 task 049 Part B (NFR-02; UAC-r2's conditions 1-6, notes/task-049-notes.md): the
/// I-12 L1 "Assigned *" pass of a NEW project, matter or work assignment, run seconds after
/// <see cref="Sprk.Bff.Api.Services.RecordCreation.RecordCreationService"/> returned the create, on the shared
/// <c>sdap-jobs</c> queue (ADR-004 <see cref="IJobHandler"/>; ADR-052: queue-driven BFF work).
/// </summary>
/// <remarks>
/// <para><b>Create only.</b> Only the create service queues this job. Every update path (chat update, the Update Record
/// node, the field-mapping push) keeps its inline pass, so an ended assignment is always removed in the request.</para>
/// <para><b>The same pass as inline.</b> The job runs <see cref="AssignedAccessMaterializer.MaterializeAsync"/> app-only,
/// with the L1 trigger (<see cref="AssignedAccessTrigger.Inline"/>, revoke-on-change on) and the grantor the create named
/// (<c>sprk_grantedby</c>, FR-33). The caller's checks (G5) ran in the request, before the create and the enqueue; the
/// job never re-derives them and never acts as the caller.</para>
/// <para><b>The message names the root, never the assignees.</b> The payload is the root, the grantor's oid and the cache
/// tenants. The materializer reads the root's CURRENT "Assigned *" columns, ledger and grants, so a re-assignment made
/// between the create and the job grants only the current assignee, and a second delivery writes nothing.</para>
/// <para><b>Serialized per root.</b> The pass runs under the per-record lease task 143's No Access enforcer, the
/// Restricted remover and <c>/unshare-user</c> take (<see cref="NoAccessShareEnforcer.RecordLockId"/> on
/// <see cref="IScheduledJobLease"/>; the project-scoped §6.5 path-A use of the scheduler's lease store as a keyed mutex,
/// UAC-r2 design §9). A delivery that finds the lease held waits for it (<see cref="LockWait"/>), then is redelivered.
/// That lease serializes this job with itself and with those removers only: the other materializer triggers (an update
/// path's inline pass, the sync endpoint, the reconciliation job) take none.</para>
/// <para><b>Settled against an unleased pass.</b> So a pass that wrote anything is followed by another, until one writes
/// nothing (at most <see cref="MaxPasses"/>). Without it, an update that re-assigns the root while this pass runs could
/// have its own inline pass find nothing to revoke, and this pass then grant the earlier assignee from its stale read. The
/// next pass re-reads the root and the ledger and removes that grant (L1 revoke-on-change). A pass that writes nothing read
/// the root after every earlier write of this job, so a later writer's own pass reads those writes. A pass that did not
/// complete but wrote something is settled the same way before the failure is reported (task 049 fix round 1, K2): its
/// writes may come from the same stale read. The last pass decides the delivery's outcome.</para>
/// <para><b>Failures.</b> A delivery whose pass did not complete is redelivered; after the last delivery it is
/// dead-lettered and logged <c>[ASSIGNED-ACCESS]</c> with the root and the failure kind only (no name, no email). The
/// backstop is <see cref="AssignedAccessReconciliationJob"/> (every 5 minutes; a root changed in the last 20 minutes is
/// taken first), which runs the same materializer over every root with an "Assigned *" column set. The job reports to no
/// user: a create's response never carried the grant's outcome.</para>
/// <para><b>Placement and §11.</b> (1) Existing — the inline call in the create service, and the reconciliation job
/// (schedule-driven, up to 5 minutes later). (2) Extension — the materializer and the queue are reused unchanged; only the
/// trigger is new, and ADR-052 places work moved out of a request on an ADR-004 handler. (3) Cost of doing nothing — the
/// pass stays inside every create of a root (median 1.3 s on dev, task 021), and NFR-02 fails. Placement Justification:
/// notes/task-049-notes.md.</para>
/// </remarks>
public sealed class AssignedAccessCreateGrantJobHandler : IJobHandler
{
    /// <summary>The job type the queue routes on.</summary>
    public const string JobTypeName = "AssignedAccessCreateGrant";

    /// <summary>The longest the create waits for the enqueue (the <c>GateDecisionRecordEnqueuer</c> bound). A slower or
    /// failed enqueue runs the full pass inline, once, as before; this includes the case where the maker's own subject
    /// was already granted inline (task 049b fix round 2, UAC-r2 ruling b).</summary>
    public static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A pass that could not take the root's lease within <see cref="LockWait"/>.</summary>
    internal const string RecordBusyKind = "record-busy";

    /// <summary>A pass whose lease store could not be reached.</summary>
    internal const string RecordLockUnavailableKind = "record-lock-unavailable";

    /// <summary>A pass that threw (the exception type is logged; the message is not, as it may carry record data).</summary>
    internal const string FaultedKind = "faulted";

    /// <summary>Passes that each still wrote something, <see cref="MaxPasses"/> times (the root kept changing).</summary>
    internal const string UnsettledKind = "unsettled";

    /// <summary>The most passes one delivery runs before it reports <see cref="UnsettledKind"/> and is redelivered.</summary>
    internal const int MaxPasses = 3;

    /// <summary>The payload's JSON shape: enums by name, so a message survives a re-ordered enum across a deploy.</summary>
    internal static readonly JsonSerializerOptions PayloadJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IServiceProvider _services;
    private readonly IScheduledJobLease _recordLock;
    private readonly ILogger<AssignedAccessCreateGrantJobHandler> _logger;

    /// <param name="services">The job's scope. The materializer is resolved only when this job type runs: the processor
    /// builds every handler for every message, and the materializer is a large graph.</param>
    /// <param name="recordLock">The per-record lease (see the class remarks).</param>
    /// <param name="logger">Logger.</param>
    public AssignedAccessCreateGrantJobHandler(
        IServiceProvider services, IScheduledJobLease recordLock, ILogger<AssignedAccessCreateGrantJobHandler> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _recordLock = recordLock ?? throw new ArgumentNullException(nameof(recordLock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>How long one delivery waits for the root's lease. Within the queue's message lock (5 minutes,
    /// auto-renewed to 10).</summary>
    internal TimeSpan LockWait { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How often a waiting delivery asks for the lease again.</summary>
    internal TimeSpan LockRetryInterval { get; init; } = TimeSpan.FromSeconds(1);

    public string JobType => JobTypeName;

    /// <summary>The job that grants <paramref name="payload"/>'s root. One per created root (its id is the key).</summary>
    internal static JobContract NewJob(AssignedAccessCreateGrantPayload payload, string correlationId) => new()
    {
        JobType = JobTypeName,
        SubjectId = $"{ExternalGrantRoot.LogicalNameFor(payload.RootType)}:{payload.RootId:D}",
        CorrelationId = correlationId,
        IdempotencyKey = $"assigned-access-create:{payload.RootId:N}",
        Payload = JsonSerializer.SerializeToDocument(payload, PayloadJson),
    };

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var started = TimeProvider.System.GetTimestamp();

        AssignedAccessCreateGrantPayload? payload = null;
        try
        {
            payload = job.Payload?.Deserialize<AssignedAccessCreateGrantPayload>(PayloadJson);
        }
        catch (JsonException)
        {
            // Not logged with the exception: its message quotes the payload.
        }

        if (payload is null || payload.RootId == Guid.Empty || !Enum.IsDefined(payload.RootType))
        {
            _logger.LogError(
                "[ASSIGNED-ACCESS] Create-grant job {JobId} names no root ({FailureKind}); dead-lettering. The reconciliation " +
                "job grants the record's assignees within minutes.", job.JobId, "invalid-payload");
            return JobOutcome.Poisoned(job.JobId, JobType, "invalid-payload", job.Attempt,
                TimeProvider.System.GetElapsedTime(started));
        }

        var failureKind = await GrantUnderRecordLockAsync(payload, ct).ConfigureAwait(false);
        var elapsed = TimeProvider.System.GetElapsedTime(started);

        if (failureKind is null)
            return JobOutcome.Success(job.JobId, JobType, elapsed);

        if (!job.IsFinalDelivery)
        {
            _logger.LogWarning(
                "[ASSIGNED-ACCESS] The create-time grant of {RootType} {RootId} did not complete ({FailureKind}, delivery " +
                "{Delivery}); it is retried.", payload.RootType, payload.RootId, failureKind, job.DeliveryCount);
            return JobOutcome.Failure(job.JobId, JobType, failureKind, job.Attempt, elapsed);
        }

        _logger.LogError(
            "[ASSIGNED-ACCESS] The create-time grant of {RootType} {RootId} did not complete after its last delivery " +
            "({FailureKind}); dead-lettered. The assigned-access reconciliation job (every 5 minutes) completes it.",
            payload.RootType, payload.RootId, failureKind);
        return JobOutcome.Poisoned(job.JobId, JobType, failureKind, job.Attempt, elapsed);
    }

    /// <summary>
    /// The pass's request. Fix round 3 (K3): the message is not trusted for what the pass acts on. The grantor's oid is used
    /// only when it names an ACTIVE systemuser (otherwise the pass runs with no grantor, a warning is logged, and the grant is
    /// never failed for it); the tenant the shared users' cached root sets are cleared under is the deployment's own
    /// (<c>AzureAd:TenantId</c>, as the inline pass used), not the payload's.
    /// </summary>
    internal async Task<AssignedAccessRequest> RequestForAsync(AssignedAccessCreateGrantPayload payload, CancellationToken ct)
    {
        var grantor = await ActiveGrantorOidAsync(payload, ct).ConfigureAwait(false);
        var tenant = _services.GetService<IConfiguration>() is { } configuration
            ? ImpersonatedRootSetSource.DeploymentCacheTenant(configuration)
            : null;
        return new AssignedAccessRequest(payload.RootType, payload.RootId, AssignedAccessTrigger.Inline,
            grantor, RevokeOnChange: true, tenant is null ? Array.Empty<string>() : [tenant]);
    }

    /// <summary>The payload's grantor oid when it names an active systemuser; otherwise <c>null</c> (no grantor), with a warning.</summary>
    private async Task<string?> ActiveGrantorOidAsync(AssignedAccessCreateGrantPayload payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload.GrantorOid))
            return null;

        if (Guid.TryParse(payload.GrantorOid.Trim(), out var oid) && oid != Guid.Empty
            && _services.GetService<DataverseWebApiClient>() is { } dataverse)
        {
            try
            {
                var users = await dataverse.QueryAsync<GrantExternalAccessEndpoint.SystemUserRow>(
                    "systemusers", filter: $"azureactivedirectoryobjectid eq {oid:D} and isdisabled eq false",
                    select: "systemuserid,isdisabled", top: 2, cancellationToken: ct).ConfigureAwait(false);
                if (users.Any(u => u.systemuserid is { } id && id != Guid.Empty && u.isdisabled == false))
                    return oid.ToString("D");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "[ASSIGNED-ACCESS] The grantor of the create-time grant of {RootType} {RootId} could not be verified " +
                    "({ExceptionType}); the pass runs with no grantor.", payload.RootType, payload.RootId, ex.GetType().Name);
                return null;
            }
        }

        _logger.LogWarning(
            "[ASSIGNED-ACCESS] The grantor of the create-time grant of {RootType} {RootId} is not an active systemuser; the " +
            "pass runs with no grantor.", payload.RootType, payload.RootId);
        return null;
    }

    /// <summary>
    /// Takes the root's lease (waiting up to <see cref="LockWait"/>), runs the pass, and releases the lease. Returns
    /// <c>null</c> when the pass completed (or found nothing to do), otherwise the failure kind.
    /// </summary>
    private async Task<string?> GrantUnderRecordLockAsync(AssignedAccessCreateGrantPayload payload, CancellationToken ct)
    {
        var logical = ExternalGrantRoot.LogicalNameFor(payload.RootType);
        var lockId = NoAccessShareEnforcer.RecordLockId(logical, payload.RootId);
        var waitStarted = TimeProvider.System.GetTimestamp();

        string token;
        while (true)
        {
            ScheduledJobLeaseGrant grant;
            try
            {
                grant = await _recordLock.TryAcquireAsync(lockId, occurrenceUtc: null, NoAccessShareEnforcer.RecordLockDuration, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "[ASSIGNED-ACCESS] The lease for {RootType} {RootId} could not be taken ({ExceptionType}).",
                    payload.RootType, payload.RootId, ex.GetType().Name);
                return RecordLockUnavailableKind;
            }

            if (grant.Status == ScheduledJobLeaseStatus.Granted && grant.Token is { } held)
            {
                token = held;
                break;
            }

            if (TimeProvider.System.GetElapsedTime(waitStarted) >= LockWait)
                return RecordBusyKind;

            await Task.Delay(LockRetryInterval, ct).ConfigureAwait(false);
        }

        try
        {
            var materializer = _services.GetRequiredService<AssignedAccessMaterializer>();
            var request = await RequestForAsync(payload, ct).ConfigureAwait(false);
            for (var pass = 1; ; pass++)
            {
                var outcome = await materializer.MaterializeAsync(request, ct).ConfigureAwait(false);

                // A root deleted since the create, and a root type with no registry, are answers, not failures.
                if (outcome.Status is AssignedAccessStatus.NotFound or AssignedAccessStatus.NoRegistry)
                    return null;

                if (!outcome.Complete)
                {
                    // K2 (task 049 fix round 1): a failing pass that wrote is settled first, while passes remain.
                    if (outcome.Writes > 0 && pass < MaxPasses)
                        continue;

                    // The status and the failure KINDS (stable codes); never a failure's message, which can name a person.
                    var kinds = string.Join(",", outcome.Failures.Select(f => f.Kind).Distinct(StringComparer.Ordinal));
                    return outcome.Status == AssignedAccessStatus.Evaluated ? kinds
                        : kinds.Length == 0 ? outcome.Status
                        : $"{outcome.Status}:{kinds}";
                }

                // Settled: this pass found the root as the earlier passes left it (see the class remarks).
                if (outcome.Writes == 0)
                    return null;

                if (pass >= MaxPasses)
                    return UnsettledKind;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[ASSIGNED-ACCESS] The create-time grant of {RootType} {RootId} threw {ExceptionType}.",
                payload.RootType, payload.RootId, ex.GetType().Name);
            return FaultedKind;
        }
        finally
        {
            try
            {
                await _recordLock.ReleaseAsync(lockId, token, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The lease expires on its own (NoAccessShareEnforcer.RecordLockDuration).
                _logger.LogWarning(
                    "[ASSIGNED-ACCESS] The lease for {RootType} {RootId} could not be released ({ExceptionType}).",
                    payload.RootType, payload.RootId, ex.GetType().Name);
            }
        }
    }
}

/// <summary>
/// The <see cref="AssignedAccessCreateGrantJobHandler.JobTypeName"/> payload (UAC-r2 condition 2): the created root, the
/// Entra oid of the person behind the create (recorded as <c>sprk_grantedby</c>; <c>null</c> when the create named nobody),
/// and the tenant namespaces a shared user's cached root set is cleared under. No assignee and no snapshot: the job reads
/// the root's current assignments. Fix round 3: the job uses the grantor only when it is an active systemuser, and takes the
/// cache tenant from the deployment's configuration; <c>CacheTenants</c> is not read by the job.
/// </summary>
public sealed record AssignedAccessCreateGrantPayload(
    ExternalGrantRootType RootType,
    Guid RootId,
    string? GrantorOid,
    IReadOnlyList<string> CacheTenants);
