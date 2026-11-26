using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The payload of one <see cref="CommunicationPostSendJobHandler"/> job: ids and flags only (ADR-004, ADR-015). Everything else (To,
/// Cc, Bcc, From, subject, body, In-Reply-To) is read from the <c>sprk_communication</c> row by id.
/// </summary>
/// <param name="CommunicationId">The sent, persisted communication.</param>
/// <param name="RequestedByObjectId">The sender's Entra object id: the archive document's requester (task 146 c1-r1). Null when unknown.</param>
/// <param name="Archive">True when the send decided to archive the .eml (D-217 or <c>ArchiveToSpe</c>, and the sender's opt-in).</param>
/// <param name="Enrich">True when the send would have enriched the email inline.</param>
/// <param name="CreateThreadPrivate">The one fact not on the row: a thread this email creates is created Private (task 176, D-216).</param>
public sealed record CommunicationPostSendPayload(
    Guid CommunicationId, Guid? RequestedByObjectId, bool Archive, bool Enrich, bool CreateThreadPrivate);

/// <summary>Event ids of <see cref="CommunicationPostSendJobHandler"/>.</summary>
public static class CommunicationPostSendEvents
{
    /// <summary>Information: the post-send work of one communication is done (per-step outcomes).</summary>
    public static readonly EventId PostSendCompleted = new(50420, nameof(PostSendCompleted));

    /// <summary>Warning: one delivery did not finish (a refused lock, an archive fault that will be retried).</summary>
    public static readonly EventId PostSendRetry = new(50421, nameof(PostSendRetry));

    /// <summary>Error: the .eml could not be archived and no retry will be made (final delivery, or a fault no retry can fix).</summary>
    public static readonly EventId ArchiveFailedFinal = new(50422, nameof(ArchiveFailedFinal));

    /// <summary>Error or Information: the payload is unreadable, or the communication no longer exists.</summary>
    public static readonly EventId PostSendSkipped = new(50423, nameof(PostSendSkipped));
}

/// <summary>
/// ISS-010 part E (#3184): the ADR-004 <see cref="IJobHandler"/> that runs a deferred send's post-send work off the request
/// (<see cref="SendCommunicationRequest.DeferPostSend"/>): thread → .eml archive → enrichment (with the archive id) → participant index
/// → communication-arrived, today's order. The send, the <c>sprk_communication</c> create, the Internet-Message-Id stamp and the archive
/// opt-in read already ran in the request.
/// </summary>
/// <remarks>
/// <para><b>Once per communication.</b> A completion entry under <c>comm-postsend:{id:N}</c> (set after a successful run) and a processing
/// lock owned by this job id (a refused lock is a retry, never a completion). Both are cache entries that fail open (#984), so each step
/// also has a durable guard: the thread is skipped when the row already carries one (create-when-absent would mint a second thread); the
/// archive goes through <see cref="CommunicationService.ArchiveExistingAsync"/>, which answers AlreadyArchived when an .eml Document
/// exists; enrichment is skipped when a <c>sprk_emailreviewlog</c> row already names the communication (field-mapping-r1 Q6: its
/// create-task step closes its own proposal on create, so a re-run could create a second task) and after its <c>:enriched</c> marker;
/// the participant indexer is idempotent per (role, address); communication-arrived runs once per <c>:arrived</c> marker (known limit:
/// a crash between the emit and the mark can repeat one outbox row; the bell inside is deduplicated per thread).</para>
/// <para><b>Outcomes.</b> Success: done, or the row is gone (nothing to fix). Poisoned: an unreadable payload, and an archive fault no
/// retry can fix (the owner refusal 409, <c>ARCHIVE_NOT_CONFIGURED</c>). Failure, redelivered (1/2/4/8 minutes, five deliveries,
/// <see cref="JobContract.MaxDeliveryCount"/>) then dead-lettered: a refused lock, a failed row read, any other archive fault. A
/// non-final archive failure skips enrichment for that delivery (RAG indexing needs the document); the participant index and arrived
/// still run. On the final delivery, or when poisoned, enrichment runs without a document (the inline behaviour after a failed archive)
/// and one <see cref="CommunicationPostSendEvents.ArchiveFailedFinal"/> Error line records it. Faults are logged by exception type and
/// error code, never the message (it can name an address).</para>
/// <para>Placement (ADR-004, ADR-052, spec ADR Tensions row for ADR-052): a queue consumer on the main queue, consumed by
/// <c>ServiceBusJobProcessor</c>; every step is app-only. §11: no existing handler takes a sent communication id
/// (<c>AppOnlyDocumentAnalysis</c> needs the Document this job creates; <c>AssignTaskEmail</c> sends, it does not process after a send);
/// without it the decision emails keep ~40-50 SDK calls and the AI inside the commit request.</para>
/// </remarks>
public class CommunicationPostSendJobHandler : IJobHandler
{
    /// <summary>The job type.</summary>
    public const string JobTypeName = "CommunicationPostSend";

    /// <summary>The <see cref="JobOutcome"/> error text of a job refused the communication's processing lock.</summary>
    internal const string LockRefusedError = "ProcessingLockRefused";

    /// <summary>How long a job holds the communication's processing lock.</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);

    /// <summary>How long the completion and step markers are kept (beyond the broker's redelivery window).</summary>
    internal static readonly TimeSpan MarkerRetention = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] RowColumns =
        ["sprk_communicationthread", "sprk_subject", "sprk_inreplyto", "sprk_from", "sprk_to", "sprk_cc", "sprk_bcc"];

    private readonly CommunicationService _communication;
    private readonly IGenericEntityService _entities;
    private readonly ICommunicationEnrichmentService _enrichment;
    private readonly IIdempotencyService _idempotency;
    private readonly CommunicationArrivedProducer? _arrived;
    private readonly ILogger<CommunicationPostSendJobHandler> _logger;

    public CommunicationPostSendJobHandler(
        CommunicationService communication,
        IGenericEntityService entities,
        ICommunicationEnrichmentService enrichment,
        IIdempotencyService idempotency,
        ILogger<CommunicationPostSendJobHandler> logger,
        CommunicationArrivedProducer? arrived = null)
    {
        _communication = communication ?? throw new ArgumentNullException(nameof(communication));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _enrichment = enrichment ?? throw new ArgumentNullException(nameof(enrichment));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _arrived = arrived;
    }

    public string JobType => JobTypeName;

    /// <summary>The idempotency key (and the subject of every marker) of one communication's post-send work.</summary>
    internal static string KeyFor(Guid communicationId) => $"comm-postsend:{communicationId:N}";

    /// <summary>The job for <paramref name="payload"/>: keyed on the communication, carrying the send's correlation id, five attempts.</summary>
    internal static JobContract CreateJob(CommunicationPostSendPayload payload, string correlationId) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = JobTypeName,
        SubjectId = payload.CommunicationId.ToString("D", CultureInfo.InvariantCulture),
        CorrelationId = correlationId,
        IdempotencyKey = KeyFor(payload.CommunicationId),
        Attempt = 1,
        MaxAttempts = JobContract.MaxDeliveryCount,
        CreatedAt = DateTimeOffset.UtcNow,
        Payload = JsonSerializer.SerializeToDocument(payload),
    };

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var stopwatch = Stopwatch.StartNew();

        if (!TryReadPayload(job, out var payload))
        {
            _logger.LogError(CommunicationPostSendEvents.PostSendSkipped,
                "[COMM-POSTSEND] job {JobId} has an unreadable payload; it is poisoned (a retry cannot fix it).", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid communication post-send payload", job.Attempt, stopwatch.Elapsed);
        }

        var id = payload.CommunicationId;
        var key = KeyFor(id);
        if (await _idempotency.IsEventProcessedAsync(key, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(CommunicationPostSendEvents.PostSendCompleted,
                "[COMM-POSTSEND] job {JobId}: communication {CommunicationId} was already done.", job.JobId, id);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        var owner = job.JobId.ToString("N", CultureInfo.InvariantCulture);
        if (!await _idempotency.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false))
        {
            _logger.LogWarning(CommunicationPostSendEvents.PostSendRetry,
                "[COMM-POSTSEND] job {JobId} not run: the processing lock for communication {CommunicationId} is held; it will be redelivered " +
                "(attempt {Attempt}, delivery {Delivery}).", job.JobId, id, job.Attempt, job.DeliveryCount);
            return JobOutcome.Failure(job.JobId, JobType, LockRefusedError, job.Attempt, stopwatch.Elapsed);
        }

        try
        {
            return await RunAsync(job, payload, stopwatch, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(CommunicationPostSendEvents.PostSendRetry,
                "[COMM-POSTSEND] job {JobId}: the post-send work of communication {CommunicationId} failed ({ExceptionType}); it will be " +
                "redelivered (attempt {Attempt}, delivery {Delivery}).", job.JobId, id, ex.GetType().Name, job.Attempt, job.DeliveryCount);
            return JobOutcome.Failure(job.JobId, JobType, ex.GetType().Name, job.Attempt, stopwatch.Elapsed);
        }
        finally
        {
            // Not with ct: a cancelled token never reaches the cache (task 068, #1086).
            await _idempotency.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<JobOutcome> RunAsync(JobContract job, CommunicationPostSendPayload payload, Stopwatch stopwatch, CancellationToken ct)
    {
        var id = payload.CommunicationId;
        var key = KeyFor(id);

        Entity? row;
        try
        {
            row = await _entities.RetrieveAsync("sprk_communication", id, RowColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFoundInChain(ex))
        {
            row = null;
        }

        if (row is null)
        {
            // The row was deleted after the send: nothing to thread, archive or index.
            _logger.LogInformation(CommunicationPostSendEvents.PostSendSkipped,
                "[COMM-POSTSEND] job {JobId}: communication {CommunicationId} no longer exists; nothing to do.", job.JobId, id);
            await _idempotency.MarkEventAsProcessedAsync(key, MarkerRetention, CancellationToken.None).ConfigureAwait(false);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        // 1. Thread (skipped when the row already has one: a redelivery, or the inline fallback ran).
        var threadStep = "skipped";
        if (row.GetAttributeValue<EntityReference>("sprk_communicationthread") is null)
        {
            await _communication.ResolveOutboundThreadAsync(id, new SendCommunicationRequest
            {
                To = CommunicationService.SplitRecipients(row.GetAttributeValue<string>("sprk_to")),
                Subject = row.GetAttributeValue<string>("sprk_subject") ?? string.Empty,
                Body = string.Empty,
                CommunicationType = CommunicationType.Email,
                InReplyToMessageId = row.GetAttributeValue<string>("sprk_inreplyto"),
                CreateThreadPrivate = payload.CreateThreadPrivate,
            }, job.CorrelationId, ct).ConfigureAwait(false);
            threadStep = "resolved";
        }

        // 2. Archive.
        Guid? archivedDocumentId = null;
        var archiveStep = payload.Archive ? "failed" : "not-requested";
        JobOutcome? archiveFailure = null;
        if (payload.Archive)
        {
            try
            {
                var result = await _communication.ArchiveExistingAsync(
                    id, ct, Sprk.Bff.Api.Services.Dataverse.RecordRequester.Of(objectId: payload.RequestedByObjectId)).ConfigureAwait(false);
                archivedDocumentId = result.ArchiveDocumentId;
                archiveStep = result.AlreadyArchived ? "already-archived" : "archived";
            }
            catch (SdapProblemException ex) when (ex.StatusCode == 409 || ex.Code == "ARCHIVE_NOT_CONFIGURED")
            {
                LogArchiveFailedFinal(job, id, ex.GetType().Name, ex.Code);
                archiveFailure = JobOutcome.Poisoned(job.JobId, JobType, $"{ex.GetType().Name} {ex.Code}", job.Attempt, stopwatch.Elapsed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                var code = (ex as SdapProblemException)?.Code;
                if (job.IsFinalDelivery)
                {
                    LogArchiveFailedFinal(job, id, ex.GetType().Name, code);
                }
                else
                {
                    _logger.LogWarning(CommunicationPostSendEvents.PostSendRetry,
                        "[COMM-POSTSEND] job {JobId}: the .eml of communication {CommunicationId} was not archived ({ExceptionType}, {ErrorCode}); " +
                        "it will be redelivered (attempt {Attempt}, delivery {Delivery}).",
                        job.JobId, id, ex.GetType().Name, code, job.Attempt, job.DeliveryCount);
                }

                archiveFailure = JobOutcome.Failure(job.JobId, JobType, ex.GetType().Name, job.Attempt, stopwatch.Elapsed);
            }
        }

        // 3. Enrichment, with the archive id. A failure that will be retried skips it (RAG indexing needs the document); a final or
        //    poisoned one runs it without a document, as the inline path does after a failed archive.
        var willRetryArchive = archiveFailure is { Status: JobStatus.Failed } && !job.IsFinalDelivery;
        var enrichStep = payload.Enrich ? (willRetryArchive ? "deferred" : await EnrichOnceAsync(job, id, archivedDocumentId, ct).ConfigureAwait(false)) : "not-requested";

        // 4. Participant index (idempotent per role and address).
        await _communication.WriteParticipantIndexAsync(
            id,
            row.GetAttributeValue<string>("sprk_from"),
            null,
            CommunicationService.SplitRecipients(row.GetAttributeValue<string>("sprk_to")),
            CommunicationService.SplitRecipients(row.GetAttributeValue<string>("sprk_cc")),
            CommunicationService.SplitRecipients(row.GetAttributeValue<string>("sprk_bcc")),
            ct).ConfigureAwait(false);

        // 5. communication-arrived, once.
        var arrivedKey = $"{key}:arrived";
        var arrivedStep = "already-emitted";
        if (!await _idempotency.IsEventProcessedAsync(arrivedKey, ct).ConfigureAwait(false))
        {
            await EmitArrivedAsync(id, ct).ConfigureAwait(false);
            await _idempotency.MarkEventAsProcessedAsync(arrivedKey, MarkerRetention, CancellationToken.None).ConfigureAwait(false);
            arrivedStep = "emitted";
        }

        if (archiveFailure is not null)
            return archiveFailure;

        await _idempotency.MarkEventAsProcessedAsync(key, MarkerRetention, CancellationToken.None).ConfigureAwait(false);
        _logger.LogInformation(CommunicationPostSendEvents.PostSendCompleted,
            "[COMM-POSTSEND] job {JobId} done: communication {CommunicationId}: thread {ThreadStep}, archive {ArchiveStep}, enrichment " +
            "{EnrichStep}, arrived {ArrivedStep}.", job.JobId, id, threadStep, archiveStep, enrichStep, arrivedStep);
        return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
    }

    /// <summary>Enrichment, at most once: skipped after its marker, and when a <c>sprk_emailreviewlog</c> row already names the
    /// communication (durable; field-mapping-r1 Q6). The marker is written after <see cref="ICommunicationEnrichmentService.EnrichAsync"/>
    /// returns; a fault there is logged and not marked.</summary>
    private async Task<string> EnrichOnceAsync(JobContract job, Guid id, Guid? archivedDocumentId, CancellationToken ct)
    {
        var enrichedKey = $"{KeyFor(id)}:enriched";
        if (await _idempotency.IsEventProcessedAsync(enrichedKey, ct).ConfigureAwait(false))
            return "already-enriched";

        if (await HasReviewRowsAsync(id, ct).ConfigureAwait(false))
        {
            await _idempotency.MarkEventAsProcessedAsync(enrichedKey, MarkerRetention, CancellationToken.None).ConfigureAwait(false);
            return "already-enriched";
        }

        try
        {
            var (message, _) = await _communication.ReconstructEnvelopeAsync(id, ct).ConfigureAwait(false);
            await _enrichment.EnrichAsync(id, CommunicationDirection.Outgoing, message, archivedDocumentId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(CommunicationPostSendEvents.PostSendRetry,
                "[COMM-POSTSEND] job {JobId}: enrichment of communication {CommunicationId} failed (non-fatal, {ExceptionType}).",
                job.JobId, id, ex.GetType().Name);
            return "failed";
        }

        await _idempotency.MarkEventAsProcessedAsync(enrichedKey, MarkerRetention, CancellationToken.None).ConfigureAwait(false);
        return "enriched";
    }

    /// <summary>True when enrichment already wrote a review row for the communication (its propose and create-task steps do).</summary>
    private async Task<bool> HasReviewRowsAsync(Guid id, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_emailreviewlog")
        {
            ColumnSet = new ColumnSet("sprk_emailreviewlogid"),
            TopCount = 1,
        };
        query.Criteria.AddCondition("sprk_communication", ConditionOperator.Equal, id);
        var result = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result?.Entities.Count > 0;
    }

    /// <summary>The communication-arrived emit (spine task 024): a seam so a test can count it. No-op when the producer is not composed.</summary>
    internal virtual Task EmitArrivedAsync(Guid communicationId, CancellationToken ct) =>
        _arrived?.EmitCommunicationArrivedAsync(communicationId, ct) ?? Task.CompletedTask;

    private void LogArchiveFailedFinal(JobContract job, Guid id, string exceptionType, string? errorCode) =>
        _logger.LogError(CommunicationPostSendEvents.ArchiveFailedFinal,
            "[COMM-POSTSEND] job {JobId}: the .eml of communication {CommunicationId} could not be archived and will not be retried " +
            "(attempt {Attempt}, delivery {Delivery}, {ExceptionType}, {ErrorCode}).",
            job.JobId, id, job.Attempt, job.DeliveryCount, exceptionType, errorCode);

    private static bool TryReadPayload(JobContract job, out CommunicationPostSendPayload payload)
    {
        payload = null!;
        if (job.Payload is null) return false;

        CommunicationPostSendPayload? read;
        try
        {
            read = job.Payload.Deserialize<CommunicationPostSendPayload>(PayloadOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (read is null || read.CommunicationId == Guid.Empty)
            return false;

        payload = read;
        return true;
    }
}
