using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for spend snapshot generation jobs.
/// Calls SpendSnapshotService to aggregate BillingEvents into spend snapshots,
/// then calls SignalEvaluationService to evaluate thresholds and create signals.
///
/// Idempotency: Both services use upsert operations via alternate keys (SpendSnapshot)
/// or deterministic IDs (SpendSignal), making re-execution safe.
///
/// Follows ADR-004 for job contract patterns and idempotency requirements.
/// </summary>
public class SpendSnapshotGenerationJobHandler : IJobHandler
{
    private readonly ISpendSnapshotService _snapshotService;
    private readonly ISignalEvaluationService _signalService;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<SpendSnapshotGenerationJobHandler> _logger;

    /// <summary>
    /// Job type constant - must match the JobType used when enqueueing.
    /// </summary>
    public const string JobTypeName = "SpendSnapshotGeneration";

    public SpendSnapshotGenerationJobHandler(
        ISpendSnapshotService snapshotService,
        ISignalEvaluationService signalService,
        FinanceTelemetry telemetry,
        ILogger<SpendSnapshotGenerationJobHandler> logger)
    {
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _signalService = signalService ?? throw new ArgumentNullException(nameof(signalService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = _telemetry.StartActivity("SpendSnapshotGeneration.ProcessJob", correlationId: job.CorrelationId);

        try
        {
            _logger.LogInformation(
                "Processing spend snapshot generation job {JobId} for subject {SubjectId}, Attempt {Attempt}, CorrelationId {CorrelationId}",
                job.JobId, job.SubjectId, job.Attempt, job.CorrelationId);

            // Parse payload to get matterId
            var payload = ParsePayload(job.Payload);
            if (payload == null || payload.MatterId == Guid.Empty)
            {
                _logger.LogError("Invalid payload for spend snapshot generation job {JobId}", job.JobId);
                stopwatch.Stop();
                return JobOutcome.Poisoned(
                    job.JobId, JobType,
                    "Invalid job payload: missing or invalid matterId",
                    job.Attempt, stopwatch.Elapsed);
            }

            var matterId = payload.MatterId;

            _logger.LogDebug("Generating spend snapshots for matter {MatterId}", matterId);

            // Step 1: Generate spend snapshots
            // Aggregates BillingEvents and creates/updates SpendSnapshot records
            await _snapshotService.GenerateAsync(matterId, job.CorrelationId, ct);

            _logger.LogInformation(
                "Spend snapshot generation completed for matter {MatterId}",
                matterId);

            // Step 2: Evaluate signals
            // Checks snapshots against threshold rules and creates/updates SpendSignal records
            var signalsTriggered = await _signalService.EvaluateAsync(matterId, ct);

            _logger.LogInformation(
                "Signal evaluation completed for matter {MatterId}. Signals triggered: {SignalCount}",
                matterId, signalsTriggered);

            // No downstream job enqueues - this is the end of the analytics chain

            stopwatch.Stop();

            _logger.LogInformation(
                "Spend snapshot generation job {JobId} completed in {Duration}ms. Matter {MatterId}, Signals: {SignalCount}",
                job.JobId, stopwatch.ElapsedMilliseconds, matterId, signalsTriggered);

            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Spend snapshot generation job {JobId} failed: {Error}", job.JobId, ex.Message);

            // Check for retryable vs permanent failures
            var isRetryable = IsRetryableException(ex);

            stopwatch.Stop();

            if (isRetryable && job.Attempt < job.MaxAttempts)
            {
                // Will retry
                _logger.LogWarning(
                    "Spend snapshot generation failed (attempt {Attempt}/{MaxAttempts}), will retry: {Error}",
                    job.Attempt, job.MaxAttempts, ex.Message);
                return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
            }

            // Permanent failure - no more retries
            _logger.LogError(
                "Spend snapshot generation permanently failed after {Attempts} attempts: {Error}",
                job.Attempt, ex.Message);

            return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    private SpendSnapshotGenerationPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<SpendSnapshotGenerationPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse spend snapshot generation job payload");
            return null;
        }
    }

    private static bool IsRetryableException(Exception ex)
    {
        // HTTP 429 (throttling), 503 (service unavailable), etc.
        if (ex is HttpRequestException)
        {
            return true;
        }

        // Check for known throttling exception types
        var exceptionName = ex.GetType().Name;
        return exceptionName.Contains("Throttling", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("Timeout", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Payload structure for spend snapshot generation jobs.
/// </summary>
public class SpendSnapshotGenerationPayload
{
    /// <summary>
    /// The matter ID to generate snapshots for.
    /// </summary>
    public Guid MatterId { get; set; }
}
