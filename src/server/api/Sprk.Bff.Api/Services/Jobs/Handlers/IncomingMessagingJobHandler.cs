using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for inbound ACS chat messages (messaging-communication-app-r1 task 031 / FR-02, FR-04).
/// Consumes the <c>IncomingMessaging</c> job that task 030's Event Grid ingress enqueues
/// (<see cref="IncomingMessagingJobPayload"/>): it normalizes the ACS chat event to a
/// <see cref="NormalizedMessage"/> (via <see cref="AcsEventNormalizer"/>, the ACS analog of
/// <c>GraphMessageNormalizer</c>), then persists it as exactly ONE <c>sprk_communication</c> via the
/// task-021 <see cref="ICommunicationChannelIngestor"/> (<c>MessagingIngestor</c>) seam.
/// </summary>
/// <remarks>
/// <para><b>Idempotency (NFR-03 / FR-04) — the crux of this handler.</b> Event Grid delivery is
/// at-least-once, unordered, and may duplicate (design §8.3); additionally, outbound send is
/// persist-on-send (ADR-045 rule 3 / task 051) and ACS emits a <c>ChatMessageReceivedInThread</c> ECHO of
/// our own outbound message. All three duplicate sources — redelivery, genuine duplicate, and our own echo —
/// collapse to exactly one persisted record by deduping on the ACS message id through the EXISTING
/// <see cref="IIdempotencyService"/>. Ordering: <c>IsEventProcessed</c> fast-path → <c>TryAcquireProcessingLock</c>
/// (so two concurrent deliveries of the same id race cleanly — one wins, the other is retried and then
/// short-circuits) → re-check under lock → persist → <c>MarkEventAsProcessed</c> → release. Persist happens
/// BEFORE mark so a crash between them re-processes on redelivery (at-least-once) rather than dropping the
/// message (never at-most-once); the 24h processed-mark TTL matches Event Grid's retry window.</para>
/// <para><b>Own-echo dedup contract with task 051.</b> The outbound sender persists-on-send and MUST mark the
/// ACS message id processed using <see cref="IdempotencyKeyFor"/> — then when the Event Grid echo arrives here,
/// the <c>IsEventProcessed</c> fast-path makes it a no-op. This handler defines the canonical key so 051 reuses
/// exactly the same one.</para>
/// <para><b>Dead-letter, not drop (ADR-004 / ADR-036).</b> This handler returns a <see cref="JobOutcome"/>;
/// <c>CommunicationJobProcessor</c> translates a terminal <see cref="JobStatus.Poisoned"/> (or the max-delivery
/// ceiling) into a Service Bus dead-letter. A malformed payload poisons immediately; a transient failure returns
/// <see cref="JobStatus.Failed"/> (retried) until max attempts, then poisons — so repeated failure dead-letters
/// and is never silently dropped.</para>
/// <para><b>Scope.</b> Normalize + idempotent persist + DLQ signalling ONLY. The <c>sprk_communicationthread</c>
/// lookup is assigned later by task 040's <c>IThreadResolver</c>; the outbound send path is task 051; enrichment
/// is invoked inside <c>MessagingIngestor</c> best-effort (NFR-02) — a thrown enrichment error does NOT fail the
/// persist there, so it never reaches this handler.</para>
/// </remarks>
public sealed class IncomingMessagingJobHandler : IJobHandler
{
    private readonly AcsEventNormalizer _normalizer;
    private readonly CommunicationChannelDispatcher _dispatcher;
    private readonly IIdempotencyService _idempotency;
    private readonly ILogger<IncomingMessagingJobHandler> _logger;

    /// <summary>Job type constant — MUST match <see cref="AcsEventGridIngressService.JobTypeIncomingMessaging"/>.</summary>
    public const string JobTypeName = AcsEventGridIngressService.JobTypeIncomingMessaging;

    public IncomingMessagingJobHandler(
        AcsEventNormalizer normalizer,
        CommunicationChannelDispatcher dispatcher,
        IIdempotencyService idempotency,
        ILogger<IncomingMessagingJobHandler> logger)
    {
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    /// <summary>
    /// The canonical <see cref="IIdempotencyService"/> key for an ACS message id — the single dedupe key that
    /// makes redelivery, genuine duplicates, and our own outbound echo (task 051) all collapse to one persist.
    /// Task 051's persist-on-send MUST mark this key so the inbound echo is a no-op (FR-04).
    /// </summary>
    public static string IdempotencyKeyFor(string acsMessageId) => $"acs-msg:{acsMessageId}";

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        IncomingMessagingJobPayload? payload;
        try
        {
            payload = ParsePayload(job.Payload);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid IncomingMessaging payload for job {JobId} — dead-lettering", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType,
                "Invalid job payload: could not deserialize IncomingMessagingJobPayload", job.Attempt, stopwatch.Elapsed);
        }

        if (payload is null)
        {
            _logger.LogError("Null IncomingMessaging payload for job {JobId} — dead-lettering", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType,
                "Invalid job payload: null IncomingMessagingJobPayload", job.Attempt, stopwatch.Elapsed);
        }

        var acsMessageId = payload.AcsMessageId;

        // Events with no message id (participant add/remove) carry nothing to persist as a sprk_communication.
        // They are a successful no-op — NOT a failure — so they complete off the queue rather than retry/DLQ.
        if (string.IsNullOrWhiteSpace(acsMessageId))
        {
            _logger.LogInformation(
                "IncomingMessaging job {JobId} carries no ACS message id (EventType={EventType}) — no-op, nothing to persist",
                job.JobId, payload.EventType);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        var idempotencyKey = IdempotencyKeyFor(acsMessageId);

        // ── Fast-path dedup: redelivery / genuine duplicate / our own outbound echo (051) ──
        if (await _idempotency.IsEventProcessedAsync(idempotencyKey, ct))
        {
            _logger.LogInformation(
                "IncomingMessaging job {JobId} deduped (AcsMessageId={AcsMessageId} already processed — redelivery/duplicate/own-echo) — no-op",
                job.JobId, acsMessageId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        // ── Serialize concurrent duplicate deliveries of the SAME id: one wins the lock, the other is
        //    retried and then short-circuits on the fast-path above once the winner marks it processed. ──
        if (!await _idempotency.TryAcquireProcessingLockAsync(idempotencyKey, lockDuration: null, ct))
        {
            _logger.LogInformation(
                "IncomingMessaging job {JobId} lost the processing-lock race for AcsMessageId={AcsMessageId} — retrying (peer in flight)",
                job.JobId, acsMessageId);
            return JobOutcome.Failure(job.JobId, JobType,
                "Concurrent delivery holds the processing lock; retry will dedupe.", job.Attempt, stopwatch.Elapsed);
        }

        try
        {
            // Re-check under the lock: a peer may have finished between the fast-path check and lock acquisition.
            if (await _idempotency.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogInformation(
                    "IncomingMessaging job {JobId} deduped under lock (AcsMessageId={AcsMessageId}) — no-op",
                    job.JobId, acsMessageId);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            var normalized = _normalizer.Normalize(payload);
            var ingestor = _dispatcher.ResolveIngestor(CommunicationType.Message);

            var result = await ingestor.IngestAsync(
                new ChannelIngestRequest
                {
                    Message = normalized,
                    ProviderMessageId = acsMessageId,
                    ProviderThreadId = payload.AcsThreadId,
                    CorrelationId = string.IsNullOrEmpty(job.CorrelationId) ? job.JobId.ToString() : job.CorrelationId,
                },
                ct);

            // Persist succeeded (or the ingestor recognized a duplicate) → mark BEFORE releasing the lock so the
            // next redelivery/echo hits the fast-path. Mark AFTER persist so a crash in between re-processes
            // (at-least-once) rather than dropping the message.
            await _idempotency.MarkEventAsProcessedAsync(idempotencyKey, expiration: null, ct);

            _logger.LogInformation(
                "IncomingMessaging job {JobId} persisted sprk_communication {CommunicationId} " +
                "(AcsMessageId={AcsMessageId}, AcsThreadId={AcsThreadId}, WasDuplicate={WasDuplicate}) in {Duration}ms",
                job.JobId, result.CommunicationId, acsMessageId, payload.AcsThreadId, result.WasDuplicate,
                stopwatch.ElapsedMilliseconds);

            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "IncomingMessaging job {JobId} failed on attempt {Attempt} (AcsMessageId={AcsMessageId}): {Error}",
                job.JobId, job.Attempt, acsMessageId, ex.Message);

            // Retry transient failures until max attempts; then poison so the processor dead-letters (never drop).
            return (IsRetryableException(ex) && !job.IsAtMaxAttempts)
                ? JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed)
                : JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
        finally
        {
            // Release the lock we acquired so a legitimate retry (transient failure path) is not blocked by our
            // own stale lock. The processed-mark, not the lock, is what makes a completed message idempotent.
            await _idempotency.ReleaseProcessingLockAsync(idempotencyKey, ct);
        }
    }

    private static IncomingMessagingJobPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload is null)
            return null;

        return JsonSerializer.Deserialize<IncomingMessagingJobPayload>(
            payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    /// <summary>
    /// Transient failures worth retrying (network / throttling / timeout / Dataverse transient). Everything else
    /// is treated as terminal on the final attempt so the message dead-letters rather than looping.
    /// </summary>
    private static bool IsRetryableException(Exception ex)
    {
        if (ex is HttpRequestException or TaskCanceledException or TimeoutException)
            return true;

        var typeName = ex.GetType().Name;
        if (typeName.Contains("ServiceBus", StringComparison.OrdinalIgnoreCase))
            return true;

        var message = ex.Message;
        return message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || message.Contains("503", StringComparison.OrdinalIgnoreCase)
            || message.Contains("504", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }
}
