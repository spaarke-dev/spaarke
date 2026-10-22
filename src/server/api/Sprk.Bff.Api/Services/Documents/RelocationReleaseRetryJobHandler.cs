using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Documents;

/// <summary>
/// #2159 fix round — the DURABLE retry of an F3 release a BFF re-file could not record inline (the document's relocation
/// lock stayed held by another relocation, or a read or write failed): ADR-004 <see cref="IJobHandler"/> on the shared
/// <c>sdap-jobs</c> queue, which gives it the queue's redelivery and dead-letter. It records the release through the
/// relocator's ONE ledger writer, under the document's lock, only while the move the re-file permitted still holds
/// (<see cref="DocumentContainerRelocator.RecordQueuedReleaseAsync"/>) — never a release the inline path would refuse.
/// </summary>
/// <remarks>
/// <para>Without it a release not recorded inline was lost: the file stayed in the secure record's container it left while
/// the row is filed under another, so the strict rule refuses it, and a re-file to the same parent again changes nothing
/// (<see cref="RecordOwnerResolution.ParentsUnchanged"/>) and carries no F3 permission.</para>
/// <para><b>Idempotent</b>: recording the same release again keeps the one recorded; a release already consumed by the
/// move finds the file in place (<see cref="RelocationReleaseState.NotNeeded"/>).</para>
/// <para><b>Component justification</b> (CLAUDE.md §11): (1) Existing — no relocation or secure-child job type exists; the
/// re-file pass (<see cref="SecureChildReconciliationJob"/>) is schedule-driven and never sees the re-file's F3 permission,
/// which exists only in the writer's request. (2) Extension — the relocator stays the one ledger writer and its locked
/// release path is reused as it is; only the trigger is new, and ADR-052 places queue-driven BFF work in an ADR-004
/// handler. (3) Cost of doing nothing — a release that meets a busy lock or a transient fault is lost for good, and the
/// re-filed document's file is refused (409) until someone files it back and re-files it with F3.</para>
/// <para><b>Trust</b>: the payload carries the permission's facts. Only the stamp BFF's own identity can send to its queue
/// (Service Bus local auth is disabled; <c>service-bus.bicep</c>), and that identity can already write the ledger itself,
/// so the queue widens no trust boundary; and the job re-checks the move under the lock either way.</para>
/// </remarks>
public sealed class RelocationReleaseRetryJobHandler : IJobHandler
{
    /// <summary>The job type the queue routes on.</summary>
    public const string JobTypeName = "DocumentRelocationReleaseRetry";

    /// <summary>The payload's JSON shape: enums by name, so a message survives a re-ordered enum across a deploy.</summary>
    internal static readonly JsonSerializerOptions PayloadJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DocumentContainerRelocator _relocator;
    private readonly ILogger<RelocationReleaseRetryJobHandler> _logger;

    public RelocationReleaseRetryJobHandler(DocumentContainerRelocator relocator, ILogger<RelocationReleaseRetryJobHandler> logger)
    {
        _relocator = relocator ?? throw new ArgumentNullException(nameof(relocator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>How long one delivery waits for the document's lock (<see cref="DocumentContainerRelocator.ReleaseRetryLockWait"/>).</summary>
    internal TimeSpan LockWait { get; init; } = DocumentContainerRelocator.ReleaseRetryLockWait;

    public string JobType => JobTypeName;

    /// <summary>The job that retries <paramref name="payload"/>'s release.</summary>
    internal static JobContract NewJob(RelocationReleaseRetryPayload payload) => new()
    {
        JobType = JobTypeName,
        SubjectId = $"sprk_document:{payload.DocumentId:D}",
        CorrelationId = Guid.NewGuid().ToString("D"),
        IdempotencyKey = $"relocation-release:{payload.DocumentId:N}:{payload.PermittedAt.UtcTicks}",
        Payload = JsonSerializer.SerializeToDocument(payload, PayloadJson),
    };

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var started = TimeProvider.System.GetTimestamp();

        RelocationReleaseRetryPayload? payload;
        try
        {
            payload = job.Payload?.Deserialize<RelocationReleaseRetryPayload>(PayloadJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[DOCUMENT-RELOCATE] Invalid release-retry payload for job {JobId}; dead-lettering.", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid DocumentRelocationReleaseRetry payload.", job.Attempt,
                TimeProvider.System.GetElapsedTime(started));
        }

        if (payload is null || !payload.IsWellFormed)
        {
            // PR #2161 round 5 (K3): resubmitting cannot help — the payload is what is incomplete (one queued before round 4 has
            // no RootsAfter) — so the log names the document and the one recovery.
            _logger.LogError(
                "[DOCUMENT-RELOCATE] Release-retry job {JobId} for document {DocumentId} names no complete release; "
                + "dead-lettering. Its file stays held; recovery: {Recovery}.",
                job.JobId, payload?.DocumentId, DocumentContainerRelocator.UnreleasedRecovery);
            return JobOutcome.Poisoned(job.JobId, JobType, "The DocumentRelocationReleaseRetry payload names no complete release.",
                job.Attempt, TimeProvider.System.GetElapsedTime(started));
        }

        RelocationReleaseOutcome outcome;
        try
        {
            outcome = await _relocator.RecordQueuedReleaseAsync(payload, LockWait, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // PR #2161 re-check: a fault that escapes the relocator (anything it does not classify itself) is NOT left to the processor,
            // which would dead-letter the job without the one recovery. It is a failed delivery: retried, then poisoned below.
            _logger.LogError(ex,
                "[DOCUMENT-RELOCATE] Queued F3 release of document {DocumentId} faulted (delivery {Delivery}); its file stays held "
                + "until a delivery records it. Recovery if none does: resubmit the dead-lettered job within {Days} days of the "
                + "re-file, or {Recovery}.", payload.DocumentId, job.Attempt, DocumentContainerRelocator.ReleaseLifetime.TotalDays,
                DocumentContainerRelocator.UnreleasedRecovery);
            outcome = new RelocationReleaseOutcome(RelocationReleaseState.Failed,
                $"the release retry faulted ({ex.GetType().Name}); no release was recorded");
        }

        var elapsed = TimeProvider.System.GetElapsedTime(started);
        switch (outcome.State)
        {
            case RelocationReleaseState.Recorded or RelocationReleaseState.NotNeeded:
                _logger.LogInformation(
                    "[DOCUMENT-RELOCATE] Queued F3 release of document {DocumentId}: {State} ({Detail}).",
                    payload.DocumentId, outcome.State, outcome.Detail);
                return JobOutcome.Success(job.JobId, JobType, elapsed);

            case RelocationReleaseState.NotCovered:
                // The move the re-file permitted no longer holds (the row moved, was re-filed, or the permission aged out):
                // a retry finds the same, so it is done. Its file stays held unless another F3 act released it.
                _logger.LogWarning(
                    "[DOCUMENT-RELOCATE] Queued F3 release of document {DocumentId} NOT recorded: {Detail}. Its file stays held "
                    + "unless a later F3 re-file released it.", payload.DocumentId, outcome.Detail);
                return JobOutcome.Success(job.JobId, JobType, elapsed);
        }

        if (!job.IsFinalDelivery)
        {
            return JobOutcome.Failure(job.JobId, JobType, outcome.Detail, job.Attempt, elapsed);
        }

        _logger.LogError(
            "[DOCUMENT-RELOCATE] Queued F3 release of document {DocumentId} NOT recorded after its last delivery ({State}: "
            + "{Detail}); its file stays held in the secure record's container it left. Recovery: resubmit this dead-lettered "
            + "job within {Days} days of the re-file, or {Recovery}.", payload.DocumentId, outcome.State, outcome.Detail,
            DocumentContainerRelocator.ReleaseLifetime.TotalDays, DocumentContainerRelocator.UnreleasedRecovery);
        return JobOutcome.Poisoned(job.JobId, JobType, outcome.Detail, job.Attempt, elapsed);
    }
}

/// <summary>
/// The <see cref="RelocationReleaseRetryJobHandler.JobTypeName"/> payload: the move a BFF re-file with F3 permitted — the
/// document's file out of the container holding it (<see cref="SourceDrive"/>) into <see cref="TargetContainer"/>, read
/// right after the re-file — and the permission's facts the release records: the secure roots it left, who
/// (<see cref="AuthorizedBy"/>, a <c>systemuserid</c>), on what basis, and when (<see cref="PermittedAt"/>, which the
/// release keeps). Not bound to the item (PR #2161 re-check): the relocator re-points the row inside the source container
/// when it closes an edited source in place, and the release must survive that. PR #2161 round 4 (F1-b): it also carries
/// the secure roots the row was filed under after the change (<see cref="RootsAfter"/>, the filing the F3 decision approved),
/// and the retry records nothing whose target is no such root's own container — exactly as the inline path. A payload
/// without them (queued before this round) names no complete release.
/// </summary>
public sealed record RelocationReleaseRetryPayload(
    Guid DocumentId,
    string SourceDrive,
    string TargetContainer,
    IReadOnlyList<RecordOwnershipParent> RootsLeft,
    Guid AuthorizedBy,
    SecureRemovalBasis Basis,
    DateTimeOffset PermittedAt,
    IReadOnlyList<RecordOwnershipParent>? RootsAfter)
{
    /// <summary>Every fact the release is bound to or records is present, and the basis is one F3 permits on.</summary>
    [JsonIgnore]
    public bool IsWellFormed
        => DocumentId != Guid.Empty && !string.IsNullOrWhiteSpace(SourceDrive) && !string.IsNullOrWhiteSpace(TargetContainer) && RootsLeft is { Count: > 0 } && RootsLeft.All(r => r is { IsSpecified: true })
           && AuthorizedBy != Guid.Empty && Basis is SecureRemovalBasis.Creator or SecureRemovalBasis.FullAccess
           && PermittedAt != default
           && RootsAfter is not null && RootsAfter.All(r => r is { IsSpecified: true });
}
