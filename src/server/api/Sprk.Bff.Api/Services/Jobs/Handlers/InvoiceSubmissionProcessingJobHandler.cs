using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// ADR-004 job handler for <see cref="InvoiceSubmissionService.JobType"/> (ebilling-r1 task 049 part E, spec FR-20 / FR-21 / NFR-08, D-101):
/// the thin shell around the Processing Pipeline. It owns only the job-framework concerns - payload, the eBilling flag, one run per submission,
/// the step-0 re-read, the document download, the status writes that are the job's (Processing, Failed, and Rejected for a document that
/// could not be read) and the mapping of every component result to a <see cref="JobOutcome"/>. The pipeline itself is the three components it
/// composes: <see cref="IInvoiceSubmissionProcessor"/> (read, resolve, code, evaluate, decide; writes nothing), <see cref="IInvoiceSubmissionPersister"/>
/// (the one all-or-nothing transaction) and <see cref="IInvoiceSubmissionAfterSteps"/> (Signals, Decision Record, indexing, notification and, LAST,
/// the terminal status). Notes: <c>projects/ebilling-r1/notes/049-submission-processing-job.md</c>, section 14.
/// </summary>
/// <remarks>
/// <para><b>Step 0 (NFR-08).</b> (a) the payload <c>{submissionId}</c> (invalid: Poisoned); (b) the processing lock
/// <c>invoice-submission:{id:N}</c> owned by the job id, held for the whole run and released in <c>finally</c> (a delivery that cannot take it
/// returns Failure and is retried; two deliveries of one submission therefore never create two invoices or record the rejection twice - 074
/// K1 / K-4); (c) the re-read: missing row is Success (044 K4), a terminal status (Accepted, Rejected, Failed) is a no-op Success and comes
/// FIRST, a Processing submission that already links its invoice is RESUMED through <see cref="IInvoiceSubmissionAfterSteps.ResumeAsync"/>
/// (never re-evaluated, so the terminal cannot contradict the committed outcome), anything else restarts from the read; (d) a submission
/// without its tracking number (D-87) or another field the pipeline cannot default is Failed, Poisoned, before Processing is written.</para>
/// <para><b>A Failed submission is terminal.</b> A redelivered Failed submission is a no-op Success. Recovery from Failed is a new submission
/// (D-37: a resubmission), not a replay. A message that outlives a flag flipped off never overwrites a finished submission: the flag is checked
/// on the re-read, after the terminal rule.</para>
/// <para><b>Outcomes.</b> Permanent failures write <c>sprk_processingstatus = Failed</c> and <c>sprk_outcomereasoncode</c> (ADR-017: the status is
/// written, never left ambiguous) and return Poisoned, so nothing retries. Transient ones return Failure until
/// <see cref="JobContract.IsFinalDelivery"/>; on the final delivery a submission that committed no invoice is marked Failed and the job is
/// Poisoned, while a submission that already links its invoice stays Processing (replaying the dead-lettered message resumes it) and the job is
/// Poisoned. The handler writes Accepted or Rejected itself only for a document that could not be read (no invoice, so no after-steps);
/// every other terminal status is the after-steps' to write, last.</para>
/// <para><b>A submission that links its invoice is never marked Failed by this handler.</b> Failed is terminal and the write is unconditional, so
/// marking a linked submission Failed would strand an invoice that has no Signals, record or notification, make every replay after a code fix a
/// no-op, and could overwrite a reviewer's decision made since the read. An after-steps <c>Refused</c> (the stored state, the signal plan or the
/// link does not match, a bug; or, for a confirmed document, its re-file was refused after the commit, a real outcome the administrator must
/// resolve, 050a) therefore leaves the submission Processing, logs the id and code, sets the administrator's attention text and returns Poisoned.</para>
/// <para><b>Logging (ADR-015).</b> Submission id, invoice id, attempt and reason codes only; never a value read from the invoice.</para>
/// <para>§11: <i>Existing</i> - <c>InvoiceExtractionJobHandler</c> (payload, document read, pointer-container check, download) and
/// <c>BillingRuleDraftJobHandler</c> (lock, final-delivery rule); the pipeline components are 049 parts B-D. <i>Extension</i> - the extraction
/// handler is the MVP text-extraction pipeline with a different payload and no submission; the pipeline components deliberately know nothing of
/// the job framework, so this shell is the one place that does. <i>Cost of doing nothing</i> - 044 enqueues <c>InvoiceSubmissionProcessing</c>;
/// with no handler the message dead-letters and no submission ever becomes an invoice.</para>
/// </remarks>
public sealed class InvoiceSubmissionProcessingJobHandler : IJobHandler
{
    /// <summary>Job type constant: the type 044's intake enqueues.</summary>
    public const string JobTypeName = InvoiceSubmissionService.JobType;

    /// <summary>Processing-lock key prefix; one run per submission at a time.</summary>
    internal const string LockKeyPrefix = "invoice-submission:";

    /// <summary>Longer than a worst-case run (read, coding, evaluation, one transaction, the after-steps), so a live run is never overtaken; a dead run's lock expires.</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(20);

    /// <summary>How many times the core record may change under a run before the job gives up and retries (072 part L): one re-evaluation.</summary>
    internal const int MaxReEvaluations = 1;

    /// <summary>The code of a core record that kept changing while the job ran. Transient.</summary>
    internal const string CoreRecordChangedCode = "ebilling.submission.core_record_changed";

    private static readonly string[] StatusColumns = ["sprk_processingstatus", InvoiceSubmissionPersister.SubmissionInvoice];

    private readonly IGenericEntityService _entities;
    private readonly IDocumentDataverseService _documents;
    private readonly ISpeFileOperations _spe;
    private readonly RecordContainerResolver _containerResolver;
    private readonly IInvoiceSubmissionProcessor _processor;
    private readonly IInvoiceSubmissionPersister _persister;
    private readonly IInvoiceSubmissionAfterSteps _afterSteps;
    private readonly IEbillingNotificationSink _sink;
    private readonly IIdempotencyService _idempotency;
    private readonly IOptionsMonitor<EbillingOptions> _ebilling;
    private readonly ILogger<InvoiceSubmissionProcessingJobHandler> _logger;

    public InvoiceSubmissionProcessingJobHandler(
        IGenericEntityService entities,
        IDocumentDataverseService documents,
        ISpeFileOperations spe,
        RecordContainerResolver containerResolver,
        IInvoiceSubmissionProcessor processor,
        IInvoiceSubmissionPersister persister,
        IInvoiceSubmissionAfterSteps afterSteps,
        IEbillingNotificationSink sink,
        IIdempotencyService idempotency,
        IOptionsMonitor<EbillingOptions> ebilling,
        ILogger<InvoiceSubmissionProcessingJobHandler> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _persister = persister ?? throw new ArgumentNullException(nameof(persister));
        _afterSteps = afterSteps ?? throw new ArgumentNullException(nameof(afterSteps));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _ebilling = ebilling ?? throw new ArgumentNullException(nameof(ebilling));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        // 0a. The payload.
        var submissionId = ParseSubmissionId(job.Payload);
        if (submissionId is not { } id)
        {
            _logger.LogError("Invoice submission job {JobId}: the payload is missing or has no submissionId", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload: missing or invalid submissionId", job.Attempt, stopwatch.Elapsed);
        }

        // 0b. One run per submission, held for the whole run, owned by the job id (a redelivery of the same job may take back a dead run's lock).
        var lockKey = LockKeyPrefix + id.ToString("N");
        var ownerId = job.JobId.ToString("N");
        var lockHeld = false;
        try
        {
            lockHeld = await _idempotency.TryAcquireProcessingLockAsync(lockKey, ownerId, LockDuration, ct).ConfigureAwait(false);
            if (!lockHeld)
            {
                // Another delivery owns the submission: never touch it (not even to mark it Failed), just try again later.
                _logger.LogWarning("Invoice submission job {JobId}: another delivery is processing submission {SubmissionId}", job.JobId, id);
                return job.IsFinalDelivery
                    ? JobOutcome.Poisoned(job.JobId, JobType, "another delivery is processing this submission", job.Attempt, stopwatch.Elapsed)
                    : JobOutcome.Failure(job.JobId, JobType, "another delivery is processing this submission", job.Attempt, stopwatch.Elapsed);
            }

            return await RunAsync(job, id, stopwatch, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var code = ex switch
            {
                SubmissionStepFailure step => step.ReasonCode,
                InvoiceAiException ai => ai.ReasonCode,
                _ => SubmissionReasonCodes.ProcessingFailed,
            };
            try
            {
                if (ex is SubmissionStepFailure { Permanent: true })
                {
                    // Raised while reading the document, before any invoice exists - unless the row carries a link it should not (a data fault):
                    // a linked submission is never marked Failed, so look.
                    return await FailAsync(job, id, code, await IsLinkedAsync(id, ct).ConfigureAwait(false), stopwatch, ct).ConfigureAwait(false);
                }

                _logger.LogError("Invoice submission job {JobId} for submission {SubmissionId} failed ({ExceptionType}, {ReasonCode})",
                    job.JobId, id, ex.GetType().Name, code);
                return await TransientAsync(job, id, code, stopwatch, ct).ConfigureAwait(false);
            }
            catch (Exception writeFault) when (writeFault is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The failure itself could not be recorded: the submission is not left claiming a status it does not have; retry.
                _logger.LogError("Invoice submission job {JobId}: the failure of submission {SubmissionId} could not be recorded ({ExceptionType})",
                    job.JobId, id, writeFault.GetType().Name);
                return JobOutcome.Failure(job.JobId, JobType, code, job.Attempt, stopwatch.Elapsed);
            }
        }
        finally
        {
            if (lockHeld)
            {
                try
                {
                    await _idempotency.ReleaseProcessingLockAsync(lockKey, ownerId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // The lock expires on its own; a failed release must not turn a finished run into a failure.
                    _logger.LogWarning(ex, "Invoice submission job {JobId}: the processing lock for submission {SubmissionId} could not be released", job.JobId, id);
                }
            }
        }
    }

    private async Task<JobOutcome> RunAsync(JobContract job, Guid submissionId, Stopwatch stopwatch, CancellationToken ct)
    {
        _logger.LogInformation(
            "Invoice submission job {JobId} for submission {SubmissionId}, attempt {Attempt}, correlation {CorrelationId}",
            job.JobId, submissionId, job.Attempt, job.CorrelationId);
        var correlation = string.IsNullOrWhiteSpace(job.CorrelationId) ? null : job.CorrelationId;

        // 0c. Re-read. Missing row: 044 compensation deleted it (K4) - not a failure.
        var row = await ReadSubmissionAsync(submissionId, SubmissionColumns, ct).ConfigureAwait(false);
        if (row is null)
        {
            _logger.LogInformation("Invoice submission job {JobId}: submission {SubmissionId} no longer exists; nothing to do", job.JobId, submissionId);
            return Done(job, stopwatch);
        }

        var status = row.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value;
        var linked = row.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice) is { Id: var l } && l != Guid.Empty;

        // Terminal comes FIRST. The after-steps write Accepted / Rejected LAST, so a terminal status means complete; Failed is terminal too.
        if (IsTerminal(status))
        {
            _logger.LogInformation("Invoice submission job {JobId}: submission {SubmissionId} is already {Status}; nothing to do", job.JobId, submissionId, status);
            return Done(job, stopwatch);
        }

        // ADR-018: the same flag as the endpoints. After the terminal rule, so a flag flipped off never overwrites a finished submission.
        if (!_ebilling.CurrentValue.Enabled)
        {
            if (linked)
            {
                // The invoice is committed: leave the submission as it is (Processing + link); a replay after the flag is back on resumes it.
                _logger.LogWarning("Invoice submission job {JobId}: eBilling is disabled; submission {SubmissionId} keeps its committed invoice and stays Processing", job.JobId, submissionId);
                return JobOutcome.Poisoned(job.JobId, JobType, SubmissionReasonCodes.EbillingDisabled, job.Attempt, stopwatch.Elapsed);
            }

            return await FailAsync(job, submissionId, SubmissionReasonCodes.EbillingDisabled, linked: false, stopwatch, ct).ConfigureAwait(false);
        }

        // Processing with its invoice linked: the transaction committed, only the after-steps are left. Resume from the STORED state;
        // never evaluate or persist again (AI output is not deterministic, so a new outcome could contradict the committed one).
        if (linked && status == (int)InvoiceSubmissionStatus.Processing)
        {
            _logger.LogInformation("Invoice submission job {JobId}: submission {SubmissionId} links its invoice; resuming the after-steps", job.JobId, submissionId);
            return await MapAfterStepsAsync(job, submissionId, await _afterSteps.ResumeAsync(submissionId, correlation, ct).ConfigureAwait(false), stopwatch, ct)
                .ConfigureAwait(false);
        }

        // 0d. D-87 and the other fields the pipeline cannot default: fail closed before Processing is written, never allocate or repair.
        var (_, snapshotFailure) = SubmissionSnapshot.Read(row);
        if (snapshotFailure is not null)
        {
            return await FailAsync(job, submissionId, snapshotFailure, linked, stopwatch, ct).ConfigureAwait(false);
        }

        // 1. Processing, persisted before any AI call (ADR-017).
        if (status != (int)InvoiceSubmissionStatus.Processing)
        {
            await _entities.UpdateAsync(FinanceBaseValues.SubmissionEntity, submissionId, new Dictionary<string, object>
            {
                [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Processing),
            }, ct).ConfigureAwait(false);
        }

        // 2-5. Evaluate, persist, after-steps. A changed core record (072 part L) re-evaluates once.
        var current = row;
        for (var pass = 0; pass <= MaxReEvaluations; pass++)
        {
            var outcome = await _processor.EvaluateAsync(current, LoadDocumentAsync, ct).ConfigureAwait(false);
            switch (outcome.Kind)
            {
                case SubmissionEvaluationKind.Failed:
                    return await FailAsync(job, submissionId, outcome.ReasonCode ?? SubmissionReasonCodes.ProcessingFailed, linked, stopwatch, ct).ConfigureAwait(false);

                case SubmissionEvaluationKind.RejectedUnreadable:
                    return await RejectUnreadableAsync(job, submissionId, outcome, stopwatch, ct).ConfigureAwait(false);
            }

            // 3. Record-before-mutate (074 K-3): refuse an automatic rejection the recorder would refuse BEFORE the invoice is created.
            try
            {
                _afterSteps.PreflightRejection(outcome);
            }
            catch (DecisionRecordRefusedException ex)
            {
                return await FailAsync(job, submissionId, ex.Reason, linked, stopwatch, ct).ConfigureAwait(false);
            }

            var persisted = await _persister.PersistAsync(outcome, correlation, ct).ConfigureAwait(false);
            switch (persisted.Status)
            {
                case SubmissionPersistStatus.Committed:
                    return await MapAfterStepsAsync(job, submissionId, await _afterSteps.RunAsync(persisted, correlation, ct).ConfigureAwait(false), stopwatch, ct)
                        .ConfigureAwait(false);

                case SubmissionPersistStatus.Retry when persisted.InvoiceId is not null:
                case SubmissionPersistStatus.Resumed:
                    // R2: the submission links its invoice, so the after-steps run from the STORED state, never from this run's outcome. Resumed:
                    // the link appeared while this run evaluated (a lock that expired under a slow run), and the terminal this run's outcome
                    // implies could contradict the committed one. Retry with the invoice id: the transaction committed and only the totals
                    // failed; the resume recalculates them again, then does the Signals, the record and the rest. RunAsync is for Committed
                    // only - a run that committed in this delivery.
                    return await MapAfterStepsAsync(job, submissionId, await _afterSteps.ResumeAsync(submissionId, correlation, ct).ConfigureAwait(false), stopwatch, ct)
                        .ConfigureAwait(false);

                case SubmissionPersistStatus.Retry:
                    return await TransientAsync(job, submissionId, persisted.ReasonCode ?? SubmissionReasonCodes.ProcessingFailed, stopwatch, ct).ConfigureAwait(false);

                case SubmissionPersistStatus.Refused:
                    return await FailAsync(job, submissionId, persisted.ReasonCode ?? SubmissionReasonCodes.ProcessingFailed, linked, stopwatch, ct).ConfigureAwait(false);

                case SubmissionPersistStatus.SubmissionMissing:
                    _logger.LogInformation("Invoice submission job {JobId}: submission {SubmissionId} vanished before it was persisted; nothing to do", job.JobId, submissionId);
                    return Done(job, stopwatch);

                case SubmissionPersistStatus.ReEvaluate:
                    current = persisted.CurrentSubmission
                        ?? throw new InvalidOperationException("The persister asked to re-evaluate without the current submission.");
                    _logger.LogInformation("Invoice submission job {JobId}: the core record of submission {SubmissionId} changed; re-evaluating (pass {Pass})",
                        job.JobId, submissionId, pass + 1);
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled persist status {persisted.Status}.");
            }
        }

        // A second change under one run: do not loop. Another delivery judges the new state.
        return await TransientAsync(job, submissionId, CoreRecordChangedCode, stopwatch, ct).ConfigureAwait(false);
    }

    // ── Component results -> job outcomes ──

    private async Task<JobOutcome> MapAfterStepsAsync(
        JobContract job, Guid submissionId, SubmissionAfterStepsResult result, Stopwatch stopwatch, CancellationToken ct)
    {
        switch (result.Status)
        {
            case SubmissionAfterStepsStatus.Completed:
                _logger.LogInformation(
                    "Invoice submission job {JobId}: submission {SubmissionId} complete ({Status}), invoice {InvoiceId}, {Signals} Signals, in {Elapsed} ms",
                    job.JobId, submissionId, result.TerminalStatus, result.InvoiceId, result.SignalsWritten, stopwatch.ElapsedMilliseconds);
                return Done(job, stopwatch);

            case SubmissionAfterStepsStatus.SubmissionMissing:
                _logger.LogInformation("Invoice submission job {JobId}: submission {SubmissionId} vanished during the after-steps; nothing to do", job.JobId, submissionId);
                return Done(job, stopwatch);

            case SubmissionAfterStepsStatus.Refused:
                // Both callers reach here only with the invoice linked (Committed / Retry-with-invoice / Resumed / a Processing+link resume).
                // The refusal is either an inconsistency in the stored state (a bug) or a refused re-file of a confirmed document (050a: the
                // document was filed elsewhere, rejected or deleted after the commit, or the ownership resolver refused; a real outcome, not a
                // bug). Either way: never Failed (terminal, unconditional write), so the invoice is not stranded and a replay once the cause
                // is resolved resumes it. No status write at all - only the administrator's attention text (R3, D-133), which says the
                // invoice has no document when the re-file was refused (review round 2, 2(b)).
                return await LeaveLinkedSubmissionAsync(job, submissionId, result.ReasonCode ?? SubmissionReasonCodes.ProcessingFailed, stopwatch, ct,
                    result.DocumentNotFiled).ConfigureAwait(false);

            case SubmissionAfterStepsStatus.Retry:
                // The submission stays Processing with its invoice link; the redelivery resumes. A re-file Retry carries DocumentNotFiled (review
                // round 3, F2), so the final delivery tells the administrator the document is NOT filed under the invoice.
                return await TransientAsync(job, submissionId, result.ReasonCode ?? SubmissionReasonCodes.ProcessingFailed, stopwatch, ct,
                    result.DocumentNotFiled).ConfigureAwait(false);

            default:
                throw new InvalidOperationException($"Unhandled after-steps status {result.Status}.");
        }
    }

    /// <summary>FR-21 / D-101: a document that could not be read is Rejected with that code, no invoice. The firm is told (the sink's InvoiceId is null).</summary>
    private async Task<JobOutcome> RejectUnreadableAsync(
        JobContract job, Guid submissionId, SubmissionEvaluationOutcome outcome, Stopwatch stopwatch, CancellationToken ct)
    {
        var code = outcome.ReasonCode ?? SubmissionReasonCodes.ReadUnreadable;
        await _entities.UpdateAsync(FinanceBaseValues.SubmissionEntity, submissionId, new Dictionary<string, object>
        {
            [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Rejected),
            [InvoiceSubmissionPersister.SubmissionReasonCode] = code,
        }, ct).ConfigureAwait(false);
        _logger.LogInformation("Invoice submission job {JobId}: submission {SubmissionId} Rejected, document not read ({ReasonCode}); no invoice", job.JobId, submissionId, code);

        try
        {
            await _sink.SubmissionDecidedAsync(new SubmissionDecidedNotification(
                submissionId, SubmissionDecisionOutcome.Rejected, outcome.Submission!.Channel, null, code), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The sink's contract: a failure here never undoes or retries the decision.
            _logger.LogWarning("Invoice submission job {JobId}: the notification for submission {SubmissionId} failed ({Fault}); the decision stands",
                job.JobId, submissionId, ex.GetType().Name);
        }

        return Done(job, stopwatch);
    }

    /// <summary>
    /// A permanent failure of a submission with no invoice: the submission is Failed with the code (ADR-017) and the job is Poisoned, so nothing
    /// retries. <paramref name="linked"/> says whether the submission already links its invoice; every caller states it. A linked submission is
    /// never marked Failed (see <see cref="LeaveLinkedSubmission"/>).
    /// </summary>
    private async Task<JobOutcome> FailAsync(JobContract job, Guid submissionId, string reasonCode, bool linked, Stopwatch stopwatch, CancellationToken ct)
    {
        if (linked)
        {
            return await LeaveLinkedSubmissionAsync(job, submissionId, reasonCode, stopwatch, ct).ConfigureAwait(false);
        }

        _logger.LogWarning("Invoice submission job {JobId}: submission {SubmissionId} Failed ({ReasonCode})", job.JobId, submissionId, reasonCode);
        await MarkFailedAsync(submissionId, reasonCode, ct).ConfigureAwait(false);
        return JobOutcome.Poisoned(job.JobId, JobType, reasonCode, job.Attempt, stopwatch.Elapsed);
    }

    /// <summary>
    /// A permanent failure of a submission that links its invoice: NO status write. It stays Processing (a replay of the dead-lettered message
    /// resumes it once the cause is fixed), the id and code are logged at Error (ADR-015), and the job is Poisoned - the same rule as the final
    /// transient delivery with a link and the flag-off-with-link path. R3 (D-133): the submission is shown to the administrator through
    /// <c>sprk_adminattention</c> (a code-only "processing stopped" segment merged into the current text; not a status write, idempotent,
    /// and a fault writing it is logged and never changes this outcome).
    /// </summary>
    private async Task<JobOutcome> LeaveLinkedSubmissionAsync(
        JobContract job, Guid submissionId, string reasonCode, Stopwatch stopwatch, CancellationToken ct, bool documentNotFiled = false)
    {
        _logger.LogError(
            "Invoice submission job {JobId}: submission {SubmissionId} links its invoice and failed permanently ({ReasonCode}, document not filed: {DocumentNotFiled}); it stays Processing, not Failed - fix the cause, then replay the message",
            job.JobId, submissionId, reasonCode, documentNotFiled);
        await AdminAttentionText.NoteAsync(_entities, submissionId, null, AdminAttentionText.StoppedNote(reasonCode, documentNotFiled), _logger, ct)
            .ConfigureAwait(false);
        return JobOutcome.Poisoned(job.JobId, JobType, reasonCode, job.Attempt, stopwatch.Elapsed);
    }

    private async Task<bool> IsLinkedAsync(Guid submissionId, CancellationToken ct)
    {
        var row = await ReadSubmissionAsync(submissionId, StatusColumns, ct).ConfigureAwait(false);
        return row?.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice) is { Id: var l } && l != Guid.Empty;
    }

    /// <summary>
    /// A transient failure: Failure (the broker redelivers) until the final delivery. On the final delivery a submission that committed no
    /// invoice is marked Failed; one that links its invoice stays Processing so a replay of the dead-lettered message resumes it (and its
    /// <c>sprk_adminattention</c> says so, R3; with <paramref name="documentNotFiled"/>, that its document is NOT filed under the invoice,
    /// 050a review round 3, F2). Either way the job is Poisoned.
    /// </summary>
    private async Task<JobOutcome> TransientAsync(
        JobContract job, Guid submissionId, string reasonCode, Stopwatch stopwatch, CancellationToken ct, bool documentNotFiled = false)
    {
        if (!job.IsFinalDelivery)
        {
            return JobOutcome.Failure(job.JobId, JobType, reasonCode, job.Attempt, stopwatch.Elapsed);
        }

        try
        {
            var row = await ReadSubmissionAsync(submissionId, StatusColumns, ct).ConfigureAwait(false);
            if (row is null)
            {
                return JobOutcome.Poisoned(job.JobId, JobType, reasonCode, job.Attempt, stopwatch.Elapsed);
            }

            var status = row.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value;
            var linked = row.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice) is { Id: var l } && l != Guid.Empty;
            if (IsTerminal(status) || linked)
            {
                _logger.LogWarning(
                    "Invoice submission job {JobId}: final delivery failed ({ReasonCode}); submission {SubmissionId} keeps its state (linked: {Linked})",
                    job.JobId, reasonCode, submissionId, linked);
                if (linked && !IsTerminal(status))
                {
                    // R3 (D-133): it stays Processing for good (the message is dead-lettered), so the administrator is told. Not a status write.
                    await AdminAttentionText.NoteAsync(_entities, submissionId, null, AdminAttentionText.StoppedNote(reasonCode, documentNotFiled), _logger, ct)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                _logger.LogWarning("Invoice submission job {JobId}: final delivery failed; submission {SubmissionId} Failed ({ReasonCode})", job.JobId, submissionId, reasonCode);
                await MarkFailedAsync(submissionId, reasonCode, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Nothing more can be done on the last delivery; the dead-letter queue and this log are the operator's handle.
            _logger.LogError("Invoice submission job {JobId}: the final failure of submission {SubmissionId} could not be recorded ({ExceptionType})",
                job.JobId, submissionId, ex.GetType().Name);
        }

        return JobOutcome.Poisoned(job.JobId, JobType, reasonCode, job.Attempt, stopwatch.Elapsed);
    }

    /// <summary>
    /// Invariant: only called before an invoice is linked (every caller checks, or runs before the persister); a linked submission is never marked
    /// Failed, so this write is unconditional - it carries no status or version condition because nothing else may have decided the submission.
    /// </summary>
    private Task MarkFailedAsync(Guid submissionId, string reasonCode, CancellationToken ct) =>
        _entities.UpdateAsync(FinanceBaseValues.SubmissionEntity, submissionId, new Dictionary<string, object>
        {
            [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Failed),
            [InvoiceSubmissionPersister.SubmissionReasonCode] = reasonCode,
        }, ct);

    private JobOutcome Done(JobContract job, Stopwatch stopwatch) => JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);

    // ── Reads ──

    private static readonly string[] SubmissionColumns =
        [.. SubmissionSnapshot.Columns.Concat([InvoiceSubmissionPersister.SubmissionStatus, InvoiceSubmissionPersister.SubmissionInvoice]).Distinct()];

    private async Task<Entity?> ReadSubmissionAsync(Guid submissionId, string[] columns, CancellationToken ct)
    {
        try
        {
            return await _entities.RetrieveAsync(FinanceBaseValues.SubmissionEntity, submissionId, columns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecordNotFound(ex))
        {
            return null;
        }
    }

    private static bool IsRecordNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTerminal(int? status) =>
        status is (int)InvoiceSubmissionStatus.Accepted or (int)InvoiceSubmissionStatus.Rejected or (int)InvoiceSubmissionStatus.Failed;

    /// <summary>
    /// Step 1a, as <c>InvoiceExtractionJobHandler</c> does it: the document row, its pointer's container verified FIRST (the download follows
    /// the pointer as the application; fail closed, permanent), then the bytes. Called by the processor only after the flag and the row passed.
    /// </summary>
    private async Task<InvoiceReadRequest> LoadDocumentAsync(SubmissionSnapshot snapshot, CancellationToken ct)
    {
        var documentId = snapshot.DocumentId;
        var document = await _documents.GetDocumentAsync(documentId.ToString(), ct).ConfigureAwait(false);
        if (document is null || string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
        {
            _logger.LogError("Invoice submission {SubmissionId}: document {DocumentId} is missing or has no file pointer", snapshot.SubmissionId, documentId);
            throw new SubmissionStepFailure(SubmissionReasonCodes.DocumentUnavailable, permanent: true);
        }

        if (!await _containerResolver.IsDocumentPointerContainerAllowedAsync(documentId, document.GraphDriveId, document.GraphItemId, ct).ConfigureAwait(false))
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: the storage of document {DocumentId} could not be verified", snapshot.SubmissionId, documentId);
            throw new SubmissionStepFailure(SubmissionReasonCodes.DocumentStorageUnverified, permanent: true);
        }

        var stream = await _spe.DownloadFileAsync(document.GraphDriveId, document.GraphItemId, ct).ConfigureAwait(false);
        if (stream is null)
        {
            // Transient (SharePoint Embedded may answer later), as the rule-draft handler treats a download that returns nothing.
            _logger.LogWarning("Invoice submission {SubmissionId}: document {DocumentId} could not be downloaded", snapshot.SubmissionId, documentId);
            throw new SubmissionStepFailure(SubmissionReasonCodes.ProcessingFailed, permanent: false);
        }

        using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(document.MimeType)
                ? new InvoiceReadRequest { Content = buffer.ToArray(), DocumentId = documentId }
                : new InvoiceReadRequest { Content = buffer.ToArray(), DocumentId = documentId, ContentType = document.MimeType };
        }
    }

    private static Guid? ParseSubmissionId(JsonDocument? payload)
    {
        if (payload is null)
        {
            return null;
        }

        try
        {
            var parsed = payload.Deserialize<InvoiceSubmissionProcessingPayload>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed is { SubmissionId: var id } && id != Guid.Empty ? id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A step of the run that cannot continue: permanent (Failed with the code) or transient (retried).</summary>
    private sealed class SubmissionStepFailure : Exception
    {
        public SubmissionStepFailure(string reasonCode, bool permanent)
            : base(reasonCode)
        {
            ReasonCode = reasonCode;
            Permanent = permanent;
        }

        public string ReasonCode { get; }

        public bool Permanent { get; }
    }
}

/// <summary>Payload of an <see cref="InvoiceSubmissionProcessingJobHandler"/> job, as <c>InvoiceSubmissionService.BuildJob</c> writes it.</summary>
public sealed class InvoiceSubmissionProcessingPayload
{
    public Guid SubmissionId { get; set; }
}
