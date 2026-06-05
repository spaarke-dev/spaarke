using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Jobs.Insights;

/// <summary>
/// Job handler for <c>InsightsUniversalIngest</c> jobs (D-P8 SPE-upload consumer, task 050).
/// Receives an SPE-upload-derived job from the existing <c>sdap-jobs</c> Service Bus queue
/// (routed by <see cref="ServiceBusJobProcessor"/>) and dispatches the universal ingest
/// pipeline via <see cref="IInsightsAi.RunIngestAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B placement per SPEC §3.5</b> — this handler sits at the dispatch boundary
/// between the existing SDAP upload pipeline (Zone B) and the Insights Engine (Zone A).
/// The ONLY Zone-A type it may import is <see cref="IInsightsAi"/>. SPEC §3.5.4
/// forbidden namespaces (<c>Services.Ai.Insights.*</c>, <c>Services.Ai.Chat.*</c>,
/// <c>IOpenAiClient</c>, <c>IPlaybookService</c>, <c>Microsoft.Extensions.AI.*</c>,
/// <c>OpenAI.*</c>, <c>Azure.AI.OpenAI.*</c>) MUST NOT appear in this file or sibling
/// files under <c>Services/Jobs/Insights/</c>. Verified by the project-level grep gate
/// in <c>projects/ai-spaarke-insights-engine-r1/CLAUDE.md §3.5</c>.
/// </para>
/// <para>
/// <b>No new infrastructure</b> per ADR-001 + ADR-004: the existing
/// <see cref="ServiceBusJobProcessor"/> already provides the BackgroundService dispatch,
/// peek-lock receive, per-message retry, delivery-count dead-lettering, and concurrency
/// (5 messages) backpressure inherited at the queue level. Adding a new
/// BackgroundService or Azure Function for SPE uploads would duplicate cross-cutting
/// concerns already covered (cf. ADR-001 single-runtime principle). The opt-in queue
/// happens in <c>UploadFinalizationWorker.QueueNextStageAsync</c> gated by
/// <c>AiProcessingOptions.InsightsIngest = true</c> (default false in Phase 1).
/// </para>
/// <para>
/// <b>Failure semantics</b> (per ADR-004 + IInsightsAi.RunIngestAsync contract;
/// originally established by task 040's IngestOrchestrator, retired Wave C-G4 / task 022):
/// <list type="bullet">
///   <item><b>Success</b> → <see cref="JobOutcome.Success"/>: ingest returned cleanly
///   regardless of how many Observations the gates produced. Zero Observations is a
///   valid outcome (non-outcome-bearing document gates off Layer 2 per D-59; or all
///   per-field Observations failed the D-P10 confidence + grounding gates).</item>
///   <item><b>Retry</b> → <see cref="JobOutcome.Failure"/>: transient failures
///   (HTTP 429 throttling, network timeouts, transient Azure Search availability).
///   The <see cref="ServiceBusJobProcessor"/> abandons the message which Service Bus
///   redelivers; on delivery count >= 5 it dead-letters.</item>
///   <item><b>DeadLetter</b> → <see cref="JobOutcome.Poisoned"/>: unrecoverable
///   conditions (invalid payload, document not in <c>spaarke-files-index</c>, missing
///   MatterId, malformed configuration). Sent directly to DLQ without further retry.</item>
/// </list>
/// Note: mirror failures inside the ingest pipeline are non-fatal and handled within
/// the universal-ingest@v1 ObservationEmitterNodeExecutor (substrate is the
/// system-of-record; mirror is a review convenience). They never surface to this handler
/// as failures.
/// </para>
/// <para>
/// <b>Idempotency</b> (per ADR-004): uses
/// <c>insights-ingest-{documentId}-{matterId}</c> as the idempotency key. Substrate
/// writes inside <c>ObservationIndexUpserter</c> already use deterministic Observation
/// ids (<c>MergeOrUpload</c> overwrites in place), so re-running the pipeline for the
/// same document is safe even without the idempotency check; the idempotency check
/// here avoids unnecessary LLM cost on duplicate deliveries.
/// </para>
/// <para>
/// <b>Cost observability</b> (D-59 + Phase 1 decision per task 050 brief): typical
/// production cost per document is ~$0.001 for non-outcome-bearing (Layer 2 gated off)
/// and ~$0.05-$0.07 for outcome-bearing (Layer 1 + Layer 2). Phase 1 enforces a
/// per-document <em>observability</em> threshold of $0.10 (warn + metric emit, no
/// hard block); per-tenant monthly cap deferred to Phase 1.5 per user signoff
/// (<c>projects/ai-spaarke-insights-engine-r1/notes/cost-projection-d-p8.md</c>).
/// LLM token telemetry is emitted by <c>OpenAiClient</c> at a layer beneath this
/// handler, so cost-exceeded detection is best surfaced from there; this handler
/// emits the duration + success/failure events.
/// </para>
/// </remarks>
public sealed class InsightsIngestJobHandler : IJobHandler
{
    private readonly IInsightsAi _insightsAi;
    private readonly IIdempotencyService _idempotencyService;
    private readonly ILogger<InsightsIngestJobHandler> _logger;

    /// <summary>
    /// Job type constant — MUST match the JobType used when enqueuing ingest jobs in
    /// <c>UploadFinalizationWorker.QueueNextStageAsync</c>.
    /// </summary>
    public const string JobTypeName = "InsightsUniversalIngest";

    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public InsightsIngestJobHandler(
        IInsightsAi insightsAi,
        IIdempotencyService idempotencyService,
        ILogger<InsightsIngestJobHandler> logger)
    {
        _insightsAi = insightsAi ?? throw new ArgumentNullException(nameof(insightsAi));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobType => JobTypeName;

    /// <inheritdoc />
    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "insights_ingest_invoked: jobId={JobId} subjectId={SubjectId} correlationId={CorrelationId} attempt={Attempt}/{MaxAttempts}",
                job.JobId, job.SubjectId, job.CorrelationId, job.Attempt, job.MaxAttempts);

            // Parse payload.
            var payload = ParsePayload(job.Payload);
            if (payload is null
                || string.IsNullOrWhiteSpace(payload.DocumentId)
                || string.IsNullOrWhiteSpace(payload.MatterId)
                || string.IsNullOrWhiteSpace(payload.TenantId))
            {
                _logger.LogError(
                    "insights_ingest_failed: jobId={JobId} reason=invalid-payload payload={Payload}",
                    job.JobId,
                    job.Payload?.RootElement.ToString() ?? "<null>");
                return JobOutcome.Poisoned(
                    job.JobId,
                    JobType,
                    "Invalid InsightsIngest payload: DocumentId, MatterId, TenantId are required.",
                    job.Attempt,
                    stopwatch.Elapsed);
            }

            // Build idempotency key (per ADR-004 pattern). Job.IdempotencyKey wins if
            // the producer already supplied one; otherwise compose a deterministic key.
            var idempotencyKey = !string.IsNullOrWhiteSpace(job.IdempotencyKey)
                ? job.IdempotencyKey
                : $"insights-ingest-{payload.DocumentId}-{payload.MatterId}";

            // Short-circuit duplicates (avoids re-running LLM cost). Substrate writes
            // are already idempotent on Observation id, but skipping re-runs is cheaper.
            if (await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct).ConfigureAwait(false))
            {
                _logger.LogInformation(
                    "insights_ingest_skipped: jobId={JobId} documentId={DocumentId} reason=already-processed idempotencyKey={IdempotencyKey}",
                    job.JobId, payload.DocumentId, idempotencyKey);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // Acquire processing lock to prevent concurrent runs for the same document.
            if (!await _idempotencyService.TryAcquireProcessingLockAsync(idempotencyKey, TimeSpan.FromMinutes(10), ct).ConfigureAwait(false))
            {
                _logger.LogInformation(
                    "insights_ingest_skipped: jobId={JobId} documentId={DocumentId} reason=lock-held-by-other idempotencyKey={IdempotencyKey}",
                    job.JobId, payload.DocumentId, idempotencyKey);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            try
            {
                // Dispatch through the §3.5 facade. All AI internals stay in Zone A.
                var request = new InsightsIngestRequest(
                    DocumentId: payload.DocumentId!,
                    MatterId: payload.MatterId!,
                    TenantId: payload.TenantId!);

                var result = await _insightsAi.RunIngestAsync(request, ct).ConfigureAwait(false);

                // Mark processed so duplicates short-circuit cheaply for 7 days.
                await _idempotencyService.MarkEventAsProcessedAsync(
                    idempotencyKey,
                    TimeSpan.FromDays(7),
                    ct).ConfigureAwait(false);

                stopwatch.Stop();
                _logger.LogInformation(
                    "insights_ingest_succeeded: jobId={JobId} documentId={DocumentId} matterId={MatterId} tenantId={TenantId} observationsEmitted={ObservationsEmitted} layer1Classification={Layer1Classification} layer2Triggered={Layer2Triggered} elapsedMs={ElapsedMs}",
                    job.JobId,
                    payload.DocumentId,
                    payload.MatterId,
                    payload.TenantId,
                    result.ObservationsEmitted,
                    result.Layer1Classification ?? "<null>",
                    result.Layer2Triggered,
                    stopwatch.ElapsedMilliseconds);

                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }
            finally
            {
                // Always release the lock so a future retry/run isn't blocked.
                await _idempotencyService.ReleaseProcessingLockAsync(idempotencyKey, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Treat host shutdown as transient — message will be redelivered on next start.
            stopwatch.Stop();
            _logger.LogInformation(
                "insights_ingest_canceled: jobId={JobId} elapsedMs={ElapsedMs}",
                job.JobId, stopwatch.ElapsedMilliseconds);
            return JobOutcome.Failure(
                job.JobId,
                JobType,
                "Operation canceled (host shutdown).",
                job.Attempt,
                stopwatch.Elapsed);
        }
        catch (ArgumentException ex)
        {
            // Facade-level argument validation failure → unrecoverable (bad payload shape).
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "insights_ingest_failed: jobId={JobId} reason=argument-invalid error={Error}",
                job.JobId, ex.Message);
            return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            // Transient failure → allow Service Bus to redeliver (counts toward DeliveryCount).
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "insights_ingest_failed: jobId={JobId} reason=transient error={Error}",
                job.JobId, ex.Message);
            return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            // Unknown failure → poison directly. Cheaper than burning retry budget.
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "insights_ingest_failed: jobId={JobId} reason=unexpected error={Error}",
                job.JobId, ex.Message);
            return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    private InsightsIngestPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InsightsIngestPayload>(payload, PayloadSerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "insights_ingest_payload_parse_failed: error={Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Returns <c>true</c> for exceptions that warrant Service Bus redelivery.
    /// Conservative list — we prefer to poison-fast than to burn retry budget on
    /// non-transient bugs.
    /// </summary>
    private static bool IsTransient(Exception ex)
    {
        if (ex is HttpRequestException)
        {
            return true;
        }

        if (ex is TaskCanceledException)
        {
            // Distinguish from OperationCanceledException (host shutdown); TaskCanceled
            // commonly wraps HTTP timeouts from HttpClient.
            return true;
        }

        var name = ex.GetType().Name;
        return name.Contains("Throttling", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Transient", StringComparison.OrdinalIgnoreCase)
            || name.Contains("RequestFailed", StringComparison.OrdinalIgnoreCase); // Azure SDK
    }
}
