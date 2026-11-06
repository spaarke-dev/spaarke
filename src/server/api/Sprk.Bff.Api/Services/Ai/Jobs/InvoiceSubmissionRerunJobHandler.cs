// ebilling-r1 task 095d-2b-1 (EB-D-185..187; uac-r2 2026-11-05 conditions 1-3, Q7; notes/095d2-brief.md section 3 step 0 and section 4a).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceSubmissionProcessingJobHandler (049) runs a submission's FIRST evaluation; InvoiceSubmissionRerun (095d-2a-2) is the
//       re-run's evaluation and its one commit, with no queue, no retry and no registration.
//   (2) Extension — the 049 handler's "a linked or terminal submission is never re-evaluated" invariant protects every committed outcome, so
//       the re-run cannot ride on its job type; the re-run service deliberately knows nothing of the job framework. This thin shell owns only
//       the job concerns: the payload, the flag, one run per submission, the final-delivery flag, the after-commit totals, and the outcome map.
//   (3) Cost of doing nothing — Set firm leaves the invoice held (the rerun-pending marker) until a re-run commits; without a durable, retried
//       job that re-run never runs and the invoice is held for ever (no line decision, Override, approval or Reject, EB-D-185).
//
// Placement (bff-extensions.md section D; ADR-052): BFF, queue-driven, so an ADR-004 IJobHandler behind ServiceBusJobProcessor. In
// Services/Ai/Jobs because the re-run can call AI coding (owner EB-D-186: lines whose code set changes are re-coded through IInvoiceAi, the
// processor's facade); uac-r2 Q7, Path C comply. No new direct model call, no package.

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Jobs;

/// <summary>
/// ADR-004 job handler for <see cref="JobTypeName"/> (ebilling-r1 task 095d-2b-1): the queued rules re-run after Set firm. It calls
/// <see cref="InvoiceSubmissionRerun.RunAsync"/> once per delivery and maps every typed outcome to a <see cref="JobOutcome"/>.
/// </summary>
/// <remarks>
/// <para><b>Step 0.</b> (a) the payload <c>{submissionId, decisionId}</c> (invalid: Poisoned, nothing read); (b) the eBilling flag (off: Poisoned
/// with <see cref="SubmissionReasonCodes.EbillingDisabled"/>, nothing written; a replay after the flag is back on runs it); (c) the 049 processing
/// lock <c>invoice-submission:{id:N}</c> (<see cref="InvoiceSubmissionProcessingJobHandler.LockKeyPrefix"/>), owned by the job id and held for the
/// run, so a 049 delivery and a re-run of one submission never overlap and two re-run deliveries never run at once (not acquired: retry).</para>
/// <para><b>The outcome map.</b></para>
/// <list type="bullet">
/// <item><see cref="InvoiceSubmissionRerunStatus.Completed"/> and <see cref="InvoiceSubmissionRerunStatus.AlreadyCompleted"/> (stored state
/// <see cref="InvoiceSubmissionRerun.StateCommitted"/>): the after-commit steps (<see cref="AfterCommitAsync"/>: the invoice totals, 016), then
/// Success. A totals fault is a retry; the retry reads AlreadyCompleted and runs the after-commit steps again (never a second commit).</item>
/// <item><see cref="InvoiceSubmissionRerunStatus.Refused"/> (permanent): Poisoned, no retry. The re-run wrote nothing and logged
/// <c>Permanent=true</c>; this handler logs the reason code at Error and counts it (<see cref="FinanceTelemetry.RecordRerunRefused"/>, uac-r2
/// K2b), and the dead-lettered message carries the code. <c>Permanent=true</c> = operator, not retry.</item>
/// <item><see cref="InvoiceSubmissionRerunStatus.Busy"/>, <see cref="InvoiceSubmissionRerunStatus.Changed"/> and
/// <see cref="InvoiceSubmissionRerunStatus.Failed"/> (transient): Failure, so the broker redelivers; on the final delivery Poisoned (dead-letter).
/// Nothing was written, so the marker, the firm markers and <c>rulesRerun = pending</c> all stay: the invoice stays held and its submission
/// stays withheld from partners (uac-r2 condition 2).</item>
/// </list>
/// <para><b>Final delivery.</b> <see cref="JobContract.IsFinalDelivery"/> is passed as <c>finalDelivery</c>: only then does an unresolved Procedure
/// commit the reviewable <c>procedures_unresolved</c> hold instead of failing the run (095d-2a-2, C1).</para>
/// <para><b>Idempotent under redelivery</b> (queue duplicate detection is off, ADR-004): the lock, the re-run's own gate (<c>rulesRerun</c>
/// completed answers AlreadyCompleted and writes nothing) and the row versions of its one commit. The state machine is hand-rolled (ADR-052
/// section 7): two repeatable Dataverse phases (the conditional commit, then the after-commit steps keyed on the stored state), each retried
/// from the stored rows, which a Durable host would add nothing to.</para>
/// <para><b>K3b (uac-r2).</b> <see cref="InvoiceSubmissionRerunResult.RerunState"/> is raw row data: this handler never logs it and never puts it
/// in a <see cref="JobOutcome"/> (pinned by <c>InvoiceSubmissionRerunStateExposureGuardTests</c>).</para>
/// <para><b>Logging (ADR-015).</b> Job, submission and invoice ids, codes and counts only.</para>
/// <para><b>Seam for 095d-2b-2.</b> The after-commit Signal steps (the re-map, the shared Signal's ConditionCleared close and its link, the
/// supersedes link, and moving the stored state on from <see cref="InvoiceSubmissionRerun.StateCommitted"/>) belong in
/// <see cref="AfterCommitAsync"/>, after the totals.</para>
/// </remarks>
public sealed class InvoiceSubmissionRerunJobHandler : IJobHandler
{
    /// <summary>Job type constant: the type Set firm enqueues.</summary>
    public const string JobTypeName = "InvoiceSubmissionRerun";

    /// <summary>Longer than a worst-case run (reads, the re-coding of changed lines, one transaction, the totals); a dead run's lock expires.</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(20);

    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private readonly Func<Guid, Guid, bool, CancellationToken, Task<InvoiceSubmissionRerunResult>> _run;
    private readonly IInvoiceTotalsService _totals;
    private readonly IIdempotencyService _locks;
    private readonly IOptionsMonitor<EbillingOptions> _ebilling;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<InvoiceSubmissionRerunJobHandler> _logger;

    public InvoiceSubmissionRerunJobHandler(
        InvoiceSubmissionRerun rerun,
        IInvoiceTotalsService totals,
        IIdempotencyService locks,
        IOptionsMonitor<EbillingOptions> ebilling,
        FinanceTelemetry telemetry,
        ILogger<InvoiceSubmissionRerunJobHandler> logger)
        : this((rerun ?? throw new ArgumentNullException(nameof(rerun))).RunAsync, totals, locks, ebilling, telemetry, logger)
    {
    }

    /// <summary>The seam the unit tests use: the re-run is passed as its call (the service is sealed and concrete, ADR-010).</summary>
    internal InvoiceSubmissionRerunJobHandler(
        Func<Guid, Guid, bool, CancellationToken, Task<InvoiceSubmissionRerunResult>> run,
        IInvoiceTotalsService totals,
        IIdempotencyService locks,
        IOptionsMonitor<EbillingOptions> ebilling,
        FinanceTelemetry telemetry,
        ILogger<InvoiceSubmissionRerunJobHandler> logger)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _totals = totals ?? throw new ArgumentNullException(nameof(totals));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _ebilling = ebilling ?? throw new ArgumentNullException(nameof(ebilling));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    /// <summary>The deterministic idempotency key of a submission's re-run (ADR-004): one re-run per submission and decision.</summary>
    internal static string IdempotencyKeyFor(Guid submissionId) => $"ebilling-assign-firm-rerun-{submissionId:N}";

    /// <summary>
    /// The re-run job of <paramref name="submissionId"/> for the Set-firm decision <paramref name="decisionId"/> (Set firm enqueues it after its
    /// Decision Record). Ids only in the payload (ADR-004: no document content).
    /// </summary>
    public static JobContract BuildJob(Guid submissionId, Guid decisionId, string? correlationId)
    {
        if (submissionId == Guid.Empty || decisionId == Guid.Empty)
        {
            throw new ArgumentException("A submission and a decision are required.");
        }

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = JobTypeName,
            SubjectId = submissionId.ToString(),
            CorrelationId = correlationId ?? Activity.Current?.Id ?? Guid.NewGuid().ToString(),
            IdempotencyKey = IdempotencyKeyFor(submissionId),
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonSerializer.SerializeToDocument(new RerunPayload(submissionId, decisionId), PayloadOptions),
        };
    }

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        // 0a. The payload.
        if (ParsePayload(job.Payload) is not { } payload)
        {
            _logger.LogError("Re-run job {JobId}: the payload is missing or has no submissionId / decisionId", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload: missing or invalid submissionId or decisionId", job.Attempt, stopwatch.Elapsed);
        }

        // 0b. ADR-018: the same flag as the routes. Off: nothing is read or written; the marker keeps the invoice held; a replay runs it.
        if (!_ebilling.CurrentValue.Enabled)
        {
            _logger.LogWarning("Re-run job {JobId}: eBilling is disabled; submission {SubmissionId} keeps its rerun-pending hold", job.JobId, payload.SubmissionId);
            return JobOutcome.Poisoned(job.JobId, JobType, SubmissionReasonCodes.EbillingDisabled, job.Attempt, stopwatch.Elapsed);
        }

        // 0c. One run per submission (the 049 lock), owned by the job id.
        var lockKey = InvoiceSubmissionProcessingJobHandler.LockKeyPrefix + payload.SubmissionId.ToString("N");
        var ownerId = job.JobId.ToString("N");
        var lockHeld = false;
        try
        {
            lockHeld = await _locks.TryAcquireProcessingLockAsync(lockKey, ownerId, LockDuration, ct).ConfigureAwait(false);
            if (!lockHeld)
            {
                _logger.LogWarning("Re-run job {JobId}: another delivery is processing submission {SubmissionId}", job.JobId, payload.SubmissionId);
                return Transient(job, InvoiceSubmissionRerunCodes.Busy, stopwatch);
            }

            InvoiceSubmissionRerunResult result;
            try
            {
                result = await _run(payload.SubmissionId, payload.DecisionId, job.IsFinalDelivery, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The re-run returns its faults typed; anything that escapes it wrote nothing (its one commit is all or nothing).
                _logger.LogWarning("Re-run job {JobId}: the re-run of submission {SubmissionId} faulted ({ExceptionType}); nothing is written and the hold stays",
                    job.JobId, payload.SubmissionId, ex.GetType().Name);
                return Transient(job, InvoiceSubmissionRerunCodes.Unavailable, stopwatch);
            }

            return await MapAsync(job, payload.SubmissionId, result, stopwatch, ct).ConfigureAwait(false);
        }
        finally
        {
            if (lockHeld)
            {
                try
                {
                    await _locks.ReleaseProcessingLockAsync(lockKey, ownerId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Re-run job {JobId}: the processing lock could not be released ({ExceptionType}); it expires", job.JobId, ex.GetType().Name);
                }
            }
        }
    }

    /// <summary>Every typed outcome of the re-run to a job outcome (see the class remarks). A status this handler does not know is a retry.</summary>
    private async Task<JobOutcome> MapAsync(JobContract job, Guid submissionId, InvoiceSubmissionRerunResult result, Stopwatch stopwatch, CancellationToken ct)
    {
        switch (result.Status)
        {
            case InvoiceSubmissionRerunStatus.Completed:
                return await AfterCommitAsync(job, submissionId, stopwatch, ct).ConfigureAwait(false);

            case InvoiceSubmissionRerunStatus.AlreadyCompleted:
                // The after-commit steps are keyed on the stored state: committed means they may not have finished. Any other state is a
                // later step's (095d-2b-2) and is complete here. The raw state is compared, never logged or returned (K3b).
                if (string.Equals(result.RerunState, InvoiceSubmissionRerun.StateCommitted, StringComparison.Ordinal))
                {
                    return await AfterCommitAsync(job, submissionId, stopwatch, ct).ConfigureAwait(false);
                }

                _logger.LogInformation("Re-run job {JobId}: submission {SubmissionId} was already re-run; nothing to do", job.JobId, submissionId);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);

            case InvoiceSubmissionRerunStatus.Refused:
                // Permanent: the re-run wrote nothing, so the marker keeps the invoice held and its submission withheld from partners.
                _telemetry.RecordRerunRefused(result.Code);
                _logger.LogError(
                    "Re-run job {JobId}: the re-run of submission {SubmissionId} was refused ({Code}); the invoice stays held (Permanent={Permanent}: operator, not retry)",
                    job.JobId, submissionId, result.Code, true);
                return JobOutcome.Poisoned(job.JobId, JobType, result.Code, job.Attempt, stopwatch.Elapsed);

            case InvoiceSubmissionRerunStatus.Busy:
            case InvoiceSubmissionRerunStatus.Changed:
            case InvoiceSubmissionRerunStatus.Failed:
            default:
                return Transient(job, result.Code, stopwatch);
        }
    }

    /// <summary>
    /// The steps after the re-run's commit, repeatable from the stored state: the invoice totals through 016 (<see cref="IInvoiceTotalsService"/>,
    /// the one owner; the commit reset decided lines to the re-run's derivation, so the stored totals are stale). A fault is a retry; a refused
    /// calculation writes nothing and is logged (a retry gives the same answer). 095d-2b-2 adds the Signal steps here, after the totals.
    /// </summary>
    private async Task<JobOutcome> AfterCommitAsync(JobContract job, Guid submissionId, Stopwatch stopwatch, CancellationToken ct)
    {
        var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(submissionId);
        try
        {
            var totals = await _totals.RecalculateAsync(invoiceId, ct).ConfigureAwait(false);
            if (totals.IsRefused)
            {
                _logger.LogWarning("Re-run job {JobId}: the totals of invoice {InvoiceId} were refused ({Refusal}); nothing was written",
                    job.JobId, invoiceId, totals.Refusal!.Code);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Re-run job {JobId}: the totals of invoice {InvoiceId} failed after the commit ({ExceptionType}); a retry recomputes them",
                job.JobId, invoiceId, ex.GetType().Name);
            return Transient(job, InvoiceSubmissionPersister.TotalsFailedCode, stopwatch);
        }

        _logger.LogInformation("Re-run job {JobId}: submission {SubmissionId} re-run committed; totals of invoice {InvoiceId} recomputed",
            job.JobId, submissionId, invoiceId);
        return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
    }

    /// <summary>A transient outcome: Failure (the broker redelivers) until the final delivery, then Poisoned (dead-letter). Nothing was written.</summary>
    private JobOutcome Transient(JobContract job, string code, Stopwatch stopwatch)
    {
        if (!job.IsFinalDelivery)
        {
            return JobOutcome.Failure(job.JobId, JobType, code, job.Attempt, stopwatch.Elapsed);
        }

        _logger.LogError("Re-run job {JobId}: the final delivery failed ({Code}); the invoice stays held and the message is dead-lettered for a replay",
            job.JobId, code);
        return JobOutcome.Poisoned(job.JobId, JobType, code, job.Attempt, stopwatch.Elapsed);
    }

    private static RerunPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload is null)
        {
            return null;
        }

        try
        {
            var parsed = payload.Deserialize<RerunPayload>(PayloadOptions);
            return parsed is { SubmissionId: var s, DecisionId: var d } && s != Guid.Empty && d != Guid.Empty ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The job payload: ids only.</summary>
    internal sealed record RerunPayload(Guid SubmissionId, Guid DecisionId);
}
