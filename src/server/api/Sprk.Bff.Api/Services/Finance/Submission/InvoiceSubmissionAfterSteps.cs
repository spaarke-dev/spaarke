// ebilling-r1 task 049 part D (map rows 7, 7a, 8, 9; D-100, D-101, D-112, D-122, D-123, D-125, D-130 a; notes 049-submission-processing-job.md
// section 13).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — SignalWriter writes one Signal and closes one as a system resolution; BillingFindingSignalMapper builds the requests;
//       BillingDecisionRecorder records a billing decision; JobSubmissionService enqueues a job; IEbillingNotificationSink reports a
//       decision; InvoiceSubmissionPersister (part C) commits the invoice and stores the findings. Nothing runs the after-commit sequence:
//       the option-a Signal lifecycle (refresh / Superseded + episode n+1 / skip resolved), the system-policy gate (D-125), the automatic
//       rejection's record, and the terminal status as the last write.
//   (2) Extension — the persister's contract ends at the commit marker (the link) and is the only creator of the invoice; adding Signal,
//       record, queue and notification writes to it would put five more write owners into the one transactional component. The handler
//       (part E) owns delivery, the lock and the download; folding these steps in would make the resume rules untestable without a job.
//       The writers themselves are ontology-owned and are used unchanged.
//   (3) Cost of doing nothing — D-130 (a) fails: no Signal, Decision Record, indexing or notification is ever written for a submission,
//       and nothing writes Accepted / Rejected, so every submission stays Processing.
//
// Placement (bff-extensions.md): Services/Finance/Submission beside the processor and the persister; an in-process step of an ADR-004
// job in the BFF (ADR-052: no Function / Container Apps Job signal). No endpoint, no package, no AI call. DI: registered by part E with
// the handler (section 13).

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// <see cref="IInvoiceSubmissionAfterSteps"/>: steps 7 to 9 of the Processing Pipeline, in this order (D-130 a, D-122):
/// <list type="number">
/// <item><b>Signals.</b> The rule Signals from the stored plan (<see cref="InvoiceSubmissionPersister.SignalPlanJson"/>) and the system
/// Signals (D-123 / D-125) mapped from the stored pipeline and coding findings under <c>BILL-SYSTEM-SUBMISSION</c>, each through the
/// option-a lifecycle (notes section 6).</item>
/// <item><b>Decision Record</b>, AutoRejected only, after the Signals (D-122 moved it there). It resolves every open Signal on the invoice
/// and its lines: the set is read once and stored in the findings JSON before the record, and each Signal in it is closed as Acted by the
/// record after it (D-122, notes 13c).</item>
/// <item><b>Indexing</b> (Accepted, AcceptedUncoded, Blocked).</item>
/// <item><b>Notification</b>, once per submission (an idempotency marker).</item>
/// <item><b>Terminal status</b>, last: derived from the STORED kind and root reason (<see cref="InvoiceSubmissionPersister.TerminalFor(SubmissionEvaluationKind, string?)"/>),
/// written only while the submission is Processing and conditional on its row version (notes section 13a).</item>
/// </list>
/// A step that does not complete leaves the submission Processing with its invoice link (a Retry result); every step is idempotent, so a
/// re-delivery resumes. Logs carry ids, counts and codes only (ADR-015).
/// </summary>
public sealed class InvoiceSubmissionAfterSteps : IInvoiceSubmissionAfterSteps
{
    // ── Reason codes of this component (Retry / Refused) ──
    internal const string FindingsUnreadableCode = "ebilling.submission.findings_unreadable";
    internal const string InvoiceLinkMismatchCode = "ebilling.submission.invoice_link_mismatch";
    internal const string SignalWriteSkippedCode = "ebilling.submission.signal_write_skipped";
    internal const string SignalChangedCode = "ebilling.submission.signal_changed";
    internal const string IndexingEnqueueFailedCode = "ebilling.submission.indexing_enqueue_failed";
    internal const string SignalPlanMissingCode = "ebilling.submission.signal_plan_missing";
    internal const string NotProcessingCode = "ebilling.submission.not_processing";
    internal const string TerminalWriteConflictCode = "ebilling.submission.terminal_write_conflict";
    internal const string RowVersionMissingCode = "ebilling.submission.row_version_missing";
    internal const string DocumentRefileFailedCode = "ebilling.submission.document_refile_failed";

    /// <summary>The automatic rejection's action code (074's billing catalog).</summary>
    internal const string RejectActionCode = "reject-invoice";

    /// <summary>The automatic rejection's decider (notes section 7): <c>automated: submission rejection rule</c>.</summary>
    internal const string RejectDecider = "submission rejection rule";

    /// <summary>How long the "notified" marker lives: well beyond any redelivery window (ADR-004 retries are minutes apart).</summary>
    internal static readonly TimeSpan NotifyMarkerLifetime = TimeSpan.FromDays(30);

    /// <summary>The <c>sprk_ruletype</c> option value of the system policy's version (D-125).</summary>
    private const int SubmissionIntegrityOption = (int)RuleType.SubmissionIntegrity;

    private const int StateActive = 0;
    private const string SignalEntity = "sprk_signal";
    private const string RowVersionColumn = "versionnumber";

    internal static readonly string[] SubmissionColumns =
    [
        InvoiceSubmissionPersister.SubmissionFindingsJson, InvoiceSubmissionPersister.SubmissionInvoice, "sprk_channel", "sprk_document",
        InvoiceSubmissionPersister.SubmissionCorrelationId, InvoiceSubmissionPersister.SubmissionStatus, RowVersionColumn,
        InvoiceSubmissionPersister.SubmissionAdminAttention, StoredSignalSetCheck.ProcessingStartColumn,
    ];

    /// <summary>
    /// The columns the terminal write re-reads after a row-version conflict (B: write only while Processing). The attention text is read
    /// WITH the row version, so the terminal update that withdraws the "Processing stopped" segment (R7) rests on the version it was
    /// computed from.
    /// </summary>
    internal static readonly string[] StateColumns =
        [InvoiceSubmissionPersister.SubmissionStatus, RowVersionColumn, InvoiceSubmissionPersister.SubmissionAdminAttention];

    // The "indexing needs DocumentIntelligence:Enabled" note (map row 9) is logged once per process.
    private static int _indexingNoteLogged;

    private readonly IGenericEntityService _entities;
    private readonly IIdempotencyService _idempotency;
    private readonly IInvoiceTotalsService _totals;
    private readonly IEbillingNotificationSink _sink;
    private readonly ILogger<InvoiceSubmissionAfterSteps> _logger;
    private readonly Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>> _writeSignal;
    private readonly Func<Guid, string, string, CancellationToken, Task<SignalCloseOutcome>> _supersedeSignal;
    private readonly Func<Guid, Guid, DateTime, string, CancellationToken, Task<SignalAutomatedCloseOutcome>> _closeActed;
    private readonly TimeProvider _time;
    private readonly Func<BillingDecision, CancellationToken, Task<BillingDecisionRecordResult>> _record;
    private readonly Action<BillingDecision> _preflight;
    private readonly Func<JobContract, CancellationToken, Task> _submitJob;
    private readonly Func<Guid, Guid?, Guid, CancellationToken, Task<InvoiceDocumentRefileResult>> _refileDocument;

    public InvoiceSubmissionAfterSteps(
        IGenericEntityService entities,
        SignalWriter signals,
        BillingDecisionRecorder recorder,
        JobSubmissionService jobs,
        IIdempotencyService idempotency,
        IInvoiceTotalsService totals,
        IEbillingNotificationSink sink,
        InvoiceDocumentRefile refile,
        ILogger<InvoiceSubmissionAfterSteps> logger)
        : this(
            entities,
            idempotency,
            totals,
            sink,
            logger,
            (signals ?? throw new ArgumentNullException(nameof(signals))).WriteAsync,
            (id, rowVersion, policyCode, ct) => signals.CloseAsync(id, rowVersion, SignalWriter.ResolutionSuperseded, policyCode, ct),
            // An automated decision: no person, so no sprk_resolvedby (decidedByUserId null).
            (id, recordId, setReadAtUtc, policyCode, ct) => signals.CloseActedByDecisionAsync(id, recordId, setReadAtUtc, policyCode, decidedByUserId: null, ct),
            (recorder ?? throw new ArgumentNullException(nameof(recorder))).RecordAsync,
            recorder.Preflight,
            (jobs ?? throw new ArgumentNullException(nameof(jobs))).SubmitJobAsync,
            refileDocument: (refile ?? throw new ArgumentNullException(nameof(refile))).RefileAsync)
    {
    }

    /// <summary>The seam the unit tests use: the ontology-owned writers and the recorder are concrete, so they are passed as their calls.</summary>
    internal InvoiceSubmissionAfterSteps(
        IGenericEntityService entities,
        IIdempotencyService idempotency,
        IInvoiceTotalsService totals,
        IEbillingNotificationSink sink,
        ILogger<InvoiceSubmissionAfterSteps> logger,
        Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>> writeSignal,
        Func<Guid, string, string, CancellationToken, Task<SignalCloseOutcome>> supersedeSignal,
        Func<Guid, Guid, DateTime, string, CancellationToken, Task<SignalAutomatedCloseOutcome>> closeActed,
        Func<BillingDecision, CancellationToken, Task<BillingDecisionRecordResult>> record,
        Action<BillingDecision> preflight,
        Func<JobContract, CancellationToken, Task> submitJob,
        TimeProvider? time = null,
        Func<Guid, Guid?, Guid, CancellationToken, Task<InvoiceDocumentRefileResult>>? refileDocument = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _totals = totals ?? throw new ArgumentNullException(nameof(totals));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _writeSignal = writeSignal ?? throw new ArgumentNullException(nameof(writeSignal));
        _supersedeSignal = supersedeSignal ?? throw new ArgumentNullException(nameof(supersedeSignal));
        _closeActed = closeActed ?? throw new ArgumentNullException(nameof(closeActed));
        _time = time ?? TimeProvider.System;
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
        _submitJob = submitJob ?? throw new ArgumentNullException(nameof(submitJob));

        // The unit-test seam defaults to "no existing document" (every 049 test submission was created by intake with an upload).
        _refileDocument = refileDocument
            ?? ((_, _, _, _) => Task.FromResult(new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.NotApplicable)));
    }

    /// <summary>The automatic rejection's decision id: deterministic from the submission id, so a retry or a resume reuses it (notes section 7).</summary>
    public static Guid RejectionDecisionIdFor(Guid submissionId) => InvoiceSubmissionPersister.IdOf($"ebilling-auto-reject-{submissionId:D}");

    /// <summary>The indexing job's id: deterministic from the invoice id (its idempotency key is the extraction handler's <c>invoice-index-{id}</c>).</summary>
    public static Guid IndexingJobIdFor(Guid invoiceId) => InvoiceSubmissionPersister.IdOf($"ebilling-index-{invoiceId:D}");

    /// <summary>The idempotency marker of the one notification per submission.</summary>
    internal static string NotifyMarkerFor(Guid submissionId) => $"ebilling-submission-notified:{submissionId:N}";

    /// <inheritdoc />
    public void PreflightRejection(SubmissionEvaluationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Kind != SubmissionEvaluationKind.AutoRejected)
        {
            return;
        }

        var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(outcome.SubmissionId);
        var stored = StoredFindings.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, invoiceId))
            ?? throw new InvalidOperationException("The findings JSON the persister builds could not be read back.");
        _preflight(BuildRejectionDecision(stored, outcome.SubmissionId, invoiceId)
            ?? throw new InvalidOperationException("An AutoRejected outcome names no deciding finding."));
    }

    /// <inheritdoc />
    public async Task<SubmissionAfterStepsResult> RunAsync(SubmissionPersistResult persisted, string? correlationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        var totalsRetry = persisted.Status == SubmissionPersistStatus.Retry;
        if (persisted.Status is not (SubmissionPersistStatus.Committed or SubmissionPersistStatus.Resumed or SubmissionPersistStatus.Retry)
            || persisted.InvoiceId is not { } invoiceId || invoiceId == Guid.Empty || persisted.TerminalStatus is not { } claimed)
        {
            throw new ArgumentException($"A {persisted.Status} result carries no committed invoice; the after-commit steps need one.", nameof(persisted));
        }

        var submissionId = persisted.SubmissionId;
        var submission = await ReadSubmissionAsync(submissionId, SubmissionColumns, ct).ConfigureAwait(false);
        if (submission is null)
        {
            return Missing(submissionId);
        }

        return await RunFromStoredAsync(
            submission,
            submissionId,
            invoiceId,
            (claimed, persisted.TerminalReasonCode),
            totalsRetry ? persisted.ReasonCode ?? InvoiceSubmissionPersister.TotalsFailedCode : null,
            persisted.Totals,
            correlationId,
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SubmissionAfterStepsResult> ResumeAsync(Guid submissionId, string? correlationId, CancellationToken ct = default)
    {
        var submission = await ReadSubmissionAsync(submissionId, SubmissionColumns, ct).ConfigureAwait(false);
        if (submission is null)
        {
            return Missing(submissionId);
        }

        if (AlreadyTerminal(submission, submissionId) is { } done)
        {
            return done;
        }

        if (submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id is not { } invoiceId || invoiceId == Guid.Empty)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: a resume was asked for, but it links no invoice; no after-commit step ran", submissionId);
            return new SubmissionAfterStepsResult
            {
                Status = SubmissionAfterStepsStatus.Refused, SubmissionId = submissionId, ReasonCode = InvoiceLinkMismatchCode,
            };
        }

        // 016 again (idempotent; the persister's Resumed path re-runs it too). A fault is a totals Retry after the Signals and the record.
        InvoiceTotalsCalculation? totals = null;
        string? totalsRetryCode = null;
        try
        {
            totals = await _totals.RecalculateAsync(invoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: totals for invoice {InvoiceId} failed on resume ({Fault}); retry resumes",
                submissionId, invoiceId, ex.GetType().Name);
            totalsRetryCode = InvoiceSubmissionPersister.TotalsFailedCode;
        }

        return await RunFromStoredAsync(submission, submissionId, invoiceId, null, totalsRetryCode, totals, correlationId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The steps, from the stored submission (<paramref name="submission"/>, read with <see cref="SubmissionColumns"/>). The terminal status,
    /// its reason code and the notification come from the STORED kind and root reason
    /// (<see cref="InvoiceSubmissionPersister.TerminalFor(SubmissionEvaluationKind, string?)"/>); <paramref name="claimed"/> (the persister's,
    /// first-run entry only) must agree with them, or nothing is written.
    /// </summary>
    private async Task<SubmissionAfterStepsResult> RunFromStoredAsync(
        Entity submission,
        Guid submissionId,
        Guid invoiceId,
        (InvoiceSubmissionStatus Status, string? ReasonCode)? claimed,
        string? totalsRetryCode,
        InvoiceTotalsCalculation? totals,
        string? correlationId,
        CancellationToken ct)
    {
        // ── B: only a Processing submission is finished here; a terminal one (a reviewer, an earlier run, the job) is never overwritten ──
        if (AlreadyTerminal(submission, submissionId) is { } done)
        {
            return done;
        }

        if (submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id != invoiceId)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: it does not link invoice {InvoiceId}; no after-commit step ran", submissionId, invoiceId);
            return Stop(SubmissionAfterStepsStatus.Refused, submissionId, invoiceId, InvoiceLinkMismatchCode);
        }

        var findingsJson = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        var stored = StoredFindings.Parse(findingsJson);
        if (stored is null || stored.InvoiceId != invoiceId)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: its findings JSON is missing, unreadable or names another invoice; no after-commit step ran",
                submissionId);
            return Stop(SubmissionAfterStepsStatus.Refused, submissionId, invoiceId, FindingsUnreadableCode);
        }

        // G: the persister always stores the plan (every persisted kind carries an evaluation); a missing one is not an empty plan.
        if (!stored.HasSignalPlan)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: its findings JSON has no Signal plan; no after-commit step ran", submissionId);
            return Stop(SubmissionAfterStepsStatus.Refused, submissionId, invoiceId, SignalPlanMissingCode);
        }

        // ── A: the terminal comes from the STORED state; a caller that evaluated again and got another answer writes nothing ──
        var (terminal, terminalReasonCode) = InvoiceSubmissionPersister.TerminalFor(stored.Kind, stored.ReasonCode);
        if (claimed is { } c && (c.Status != terminal || !string.Equals(c.ReasonCode, terminalReasonCode, StringComparison.Ordinal)))
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: the persisted result says {Claimed} ({ClaimedCode}) but the stored state is {Kind} ({Stored}, {StoredCode}); no after-commit step ran",
                submissionId, c.Status, c.ReasonCode, stored.Kind, terminal, terminalReasonCode);
            return Stop(SubmissionAfterStepsStatus.Refused, submissionId, invoiceId, SubmissionReasonCodes.StoredStateMismatch);
        }

        // ── The re-file (050a, D-134 item 3): a confirmed EXISTING document is linked to the invoice and re-filed under it by this step, the
        //    link's one writer (live-row check → isolation read → ReparentAsync, whose conditional write sets the link → AfterRefileAsync;
        //    InvoiceDocumentRefile; 050a review #1). Before every other after-step, on
        //    the first run and on every resume: a Retry or a refusal here leaves the submission Processing and writes no Signal or terminal. ──
        var existingDocumentId = submission.GetAttributeValue<EntityReference>("sprk_document")?.Id;
        InvoiceDocumentRefileResult refiled;
        try
        {
            refiled = await _refileDocument(submissionId, existingDocumentId, invoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: re-filing document {DocumentId} under invoice {InvoiceId} failed ({Fault}); retry resumes",
                submissionId, existingDocumentId, invoiceId, ex.GetType().Name);
            // Review round 3 (uac-r2 F2): the invoice is committed and its document is not filed under it yet. Flagged, so that if the
            // retries run out the administrator is told the document is NOT filed (the handler's final-delivery note).
            return Stop(SubmissionAfterStepsStatus.Retry, submissionId, invoiceId, DocumentRefileFailedCode) with { DocumentNotFiled = true };
        }

        switch (refiled.Status)
        {
            case InvoiceDocumentRefileStatus.IsolationUnreadable:
                return Stop(SubmissionAfterStepsStatus.Retry, submissionId, invoiceId, refiled.ReasonCode ?? InvoiceDocumentRefile.IsolationUnreadableCode)
                    with { DocumentNotFiled = true };
            case InvoiceDocumentRefileStatus.Refused:
                // Review round 2, 2(b): the invoice is committed and its document is not filed under it. Stopped here (no Signal, record or
                // terminal), flagged for the handler, which names it in the administrator's attention text.
                return Stop(SubmissionAfterStepsStatus.Refused, submissionId, invoiceId, refiled.ReasonCode ?? InvoiceDocumentRefile.RefileRefusedCode)
                    with { DocumentNotFiled = true };
        }

        var rowVersion = RowVersionOf(submission);

        // ── The totals refusal (map row 6c): recorded in the findings JSON, once; the invoice stands ──
        if (totals?.Refusal is { } refusal
            && await RecordTotalsRefusalAsync(submissionId, findingsJson!, refusal, ct).ConfigureAwait(false) is { } withRefusal)
        {
            findingsJson = withRefusal;
            rowVersion = null; // our own write moved the row version: the terminal write re-reads it first
        }

        // ── 1. Signals ──
        var requests = new List<(SignalWriteRequest Request, string Fingerprint)>();
        foreach (var entry in stored.Plan)
        {
            requests.Add((entry.Request, entry.Fingerprint));
        }

        var (systemRequests, wroteAttention) = await SystemSignalsAsync(stored, submissionId, invoiceId, ct).ConfigureAwait(false);
        requests.AddRange(systemRequests);
        if (wroteAttention)
        {
            rowVersion = null; // our own attention write moved the row version: the terminal write re-reads it first
        }

        var written = 0;
        string? signalFailure = null;
        var refusedSignals = new List<SignalWriteRefused>();
        foreach (var (request, fingerprint) in requests)
        {
            // F1-3: a refusal the writer would repeat on every attempt (PermanentWriteRefusal, PermanentSkipCodes) is recorded and the run goes
            // on: one refused Signal never stops the others, the Decision Record or the terminal status. Any other exception (a Dataverse
            // read or write fault) still propagates, as before: the job retries, and every write here is idempotent.
            SignalStep step;
            try
            {
                step = await WriteWithLifecycleAsync(request, fingerprint, submissionId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when ((ex is not OperationCanceledException || !ct.IsCancellationRequested) && PermanentWriteRefusal(request, ex) is { } code)
            {
                step = new SignalStep(false, null, code);
            }

            if (step.Wrote)
            {
                written++;
            }

            if (step.RefusedCode is { } refusedCode)
            {
                _logger.LogError("Invoice submission {SubmissionId}: the Signal of policy {PolicyCode} on {Subject} {SubjectId} is refused permanently ({Code}); recorded in the findings, the run continues without it",
                    submissionId, request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, refusedCode);
                refusedSignals.Add(new SignalWriteRefused(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, refusedCode));
            }

            // A retryable failure is remembered and the remaining Signals are still attempted (each write is idempotent); the run then
            // stops before the Decision Record.
            signalFailure ??= step.FailureCode;
        }

        if (refusedSignals.Count > 0 && await RecordSignalWriteRefusalsAsync(submissionId, findingsJson!, refusedSignals, ct).ConfigureAwait(false) is { } withRefusals)
        {
            findingsJson = withRefusals;
            rowVersion = null; // our own write moved the row version: the terminal write re-reads it first
        }

        if (signalFailure is not null)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: {Failed} of {Count} Signals did not complete ({Code}); it stays Processing for a retry",
                submissionId, requests.Count - written - refusedSignals.Count, requests.Count, signalFailure);
            return Stop(SubmissionAfterStepsStatus.Retry, submissionId, invoiceId, signalFailure) with { SignalsWritten = written, SignalsRefused = refusedSignals.Count };
        }

        // ── 2. Decision Record: the automatic rejection only (Blocked and Accepted get none; 105's Override / Return record those) ──
        //    D-122 (notes 13c): (a) the open Signals on the invoice and its lines, read ONCE and stored before the record; (b) the record
        //    lists that stored set; (c) each Signal in it is closed as Acted by the record.
        Guid? recordId = null;
        if (stored.Kind == SubmissionEvaluationKind.AutoRejected)
        {
            if (BuildRejectionDecision(stored, submissionId, invoiceId) is null)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: an automatic rejection with no deciding finding in its findings JSON", submissionId);
                return Stop(SubmissionAfterStepsStatus.Refused, submissionId, invoiceId, FindingsUnreadableCode) with { SignalsWritten = written };
            }

            // (a) A stored set is frozen: a resume never reads again (the record's hash covers the set).
            var set = stored.ResolvedSet;
            if (set is null)
            {
                set = await ReadOpenSignalSetAsync(_entities, _time, invoiceId, _logger, ct).ConfigureAwait(false);
                findingsJson = await StoreResolvedSetAsync(submissionId, findingsJson!, set, ct).ConfigureAwait(false);
                rowVersion = null; // our own write moved the row version
            }
            else if (await StoredSignalSetCheck.CheckAsync(_entities, _time, invoiceId, set,
                    submission.GetAttributeValue<DateTime?>(StoredSignalSetCheck.ProcessingStartColumn), ct).ConfigureAwait(false) is { Trusted: false } untrusted)
            {
                // F1-1 (ontology-r1): the findings column is user-writable, so a stored set is a claim. A Signal off this invoice and its
                // lines, or a read time outside the submission's life, is never closed by the app identity: refused, permanently.
                _logger.LogWarning("Invoice submission {SubmissionId}: the stored Signal set cannot be trusted ({Why}); nothing is recorded or closed", submissionId, untrusted.Why);
                return Stop(SubmissionAfterStepsStatus.Refused, submissionId, invoiceId, FindingsUnreadableCode) with { SignalsWritten = written };
            }

            // (b)
            var step = await RecordRejectionAsync(stored, submissionId, invoiceId, set, findingsJson!, ct).ConfigureAwait(false);
            if (step.Stop is { } refused)
            {
                return refused with { SignalsWritten = written };
            }

            if (!ReferenceEquals(step.FindingsJson, findingsJson))
            {
                findingsJson = step.FindingsJson;
                rowVersion = null;
            }

            recordId = step.Recorded!.RecordId;
            _logger.LogInformation("Invoice submission {SubmissionId}: automatic rejection of invoice {InvoiceId} recorded as {RecordId} (already recorded: {Already}), resolving {Count} Signals",
                submissionId, invoiceId, recordId, step.Recorded.AlreadyRecorded, step.Set.Signals.Count);

            // (c)
            if (await CloseResolvedSetAsync(step.Set, recordId.Value, submissionId, ct).ConfigureAwait(false) is { } closeFailure)
            {
                return Stop(SubmissionAfterStepsStatus.Retry, submissionId, invoiceId, closeFailure) with { SignalsWritten = written, DecisionRecordId = recordId };
            }
        }

        if (totalsRetryCode is not null)
        {
            // The totals failed after the commit: the Signals and the record are written; the redelivery re-runs the totals, then the rest.
            return Stop(SubmissionAfterStepsStatus.Retry, submissionId, invoiceId, totalsRetryCode)
                with { SignalsWritten = written, DecisionRecordId = recordId };
        }

        // ── 3. Indexing: an invoice In Review (Accepted, AcceptedUncoded, Blocked); a rejected invoice is not indexed ──
        if (stored.Kind != SubmissionEvaluationKind.AutoRejected)
        {
            var documentId = submission.GetAttributeValue<EntityReference>("sprk_document")?.Id;
            var correlation = correlationId ?? submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionCorrelationId) ?? submissionId.ToString("D");
            if (documentId is not { } document || document == Guid.Empty)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: no document to index for invoice {InvoiceId}; indexing skipped", submissionId, invoiceId);
            }
            else
            {
                try
                {
                    await _submitJob(IndexingJob(invoiceId, document, correlation), ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Invoice submission {SubmissionId}: the indexing job for invoice {InvoiceId} could not be enqueued ({Fault}); it stays Processing for a retry",
                        submissionId, invoiceId, ex.GetType().Name);
                    return Stop(SubmissionAfterStepsStatus.Retry, submissionId, invoiceId, IndexingEnqueueFailedCode)
                        with { SignalsWritten = written, DecisionRecordId = recordId };
                }

                if (Interlocked.Exchange(ref _indexingNoteLogged, 1) == 0)
                {
                    _logger.LogInformation(
                        "InvoiceIndexing jobs are handled only when DocumentIntelligence:Enabled is true; otherwise the message dead-letters (task 049 map row 9)");
                }
            }
        }

        // ── 4. Notification: once per submission (the marker). The sink's contract: a failure is logged and never retries the pipeline ──
        await NotifyOnceAsync(submission, submissionId, invoiceId, terminal, terminalReasonCode, ct).ConfigureAwait(false);

        // ── 5. Terminal status, LAST (D-130 a): Accepted / Rejected means complete; written only while Processing, on the row version ──
        if (await WriteTerminalAsync(submissionId, rowVersion, submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention),
                terminal, terminalReasonCode, ct).ConfigureAwait(false) is { } stopped)
        {
            return stopped with { InvoiceId = stopped.InvoiceId ?? invoiceId, SignalsWritten = written, DecisionRecordId = recordId };
        }

        _logger.LogInformation("Invoice submission {SubmissionId}: after-commit steps complete for invoice {InvoiceId} ({Kind}): {Written} Signals ({Refused} refused), status {Status} ({ReasonCode})",
            submissionId, invoiceId, stored.Kind, written, refusedSignals.Count, terminal, terminalReasonCode);
        return new SubmissionAfterStepsResult
        {
            Status = SubmissionAfterStepsStatus.Completed,
            SubmissionId = submissionId,
            InvoiceId = invoiceId,
            TerminalStatus = terminal,
            DecisionRecordId = recordId,
            SignalsWritten = written,
            SignalsRefused = refusedSignals.Count,
        };
    }

    // ── Terminal status (B: written only while Processing) ──

    /// <summary>
    /// Writes the terminal status only while the submission is Processing, conditional on the row version
    /// (<see cref="IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync"/>, one row). A version conflict re-reads once: still Processing →
    /// the write is tried once more on the new version; now terminal → Completed with nothing written (a reviewer or another run decided).
    /// A <paramref name="rowVersion"/> of null (our own findings write moved it) re-reads before the first attempt. Returns null when the
    /// terminal was written, else the result the run ends with.
    /// <para><b>R7 (the submission completes, so "Processing stopped" is no longer true).</b> The terminal update also carries
    /// <c>sprk_adminattention</c> WITHOUT its "Processing stopped" segment (<see cref="AdminAttentionText.WithoutStopped"/>; null when that was
    /// all there was), computed from the text read with the SAME row version the update is conditional on: nothing is added to the update, and
    /// its row-version condition is the one it always had, when the stored text has no such segment (then the column is not in the update at
    /// all). The computation cannot fault; a fault of the update itself is the terminal write's, as before. The Signal and policy segments
    /// and any text an administrator typed stay. <paramref name="attention"/> is the text of the read that gave <paramref name="rowVersion"/>
    /// (ignored when that is null: the re-read supplies both).</para>
    /// </summary>
    private async Task<SubmissionAfterStepsResult?> WriteTerminalAsync(
        Guid submissionId, long? rowVersion, string? attention, InvoiceSubmissionStatus terminal, string? reasonCode, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (rowVersion is null)
            {
                var state = await ReadSubmissionAsync(submissionId, StateColumns, ct).ConfigureAwait(false);
                if (state is null)
                {
                    return Missing(submissionId);
                }

                if (AlreadyTerminal(state, submissionId) is { } done)
                {
                    return done;
                }

                rowVersion = RowVersionOf(state);
                attention = state.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention);
            }

            if (rowVersion is not { } version)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: read without a row version; the terminal status is not written", submissionId);
                return new SubmissionAfterStepsResult { Status = SubmissionAfterStepsStatus.Retry, SubmissionId = submissionId, ReasonCode = RowVersionMissingCode };
            }

            var fields = new Dictionary<string, object>
            {
                [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)terminal),
                [InvoiceSubmissionPersister.SubmissionReasonCode] = reasonCode!,
            };
            var withdrawn = AdminAttentionText.WithoutStopped(attention);
            if (withdrawn.Changed)
            {
                // R7: DBNull.Value is the explicit clear (the builder's convention); a C# null would be skipped.
                fields[InvoiceSubmissionPersister.SubmissionAdminAttention] = withdrawn.Text is { } kept ? (object)kept : DBNull.Value;
            }

            try
            {
                await _entities.BulkUpdateIfRowVersionsMatchAsync(FinanceBaseValues.SubmissionEntity, [(submissionId, fields, version)], ct).ConfigureAwait(false);
                if (withdrawn.Changed)
                {
                    _logger.LogInformation("Invoice submission {SubmissionId}: it completed, so its Processing stopped note was withdrawn from the attention text",
                        submissionId);
                }

                return null;
            }
            catch (System.Data.DBConcurrencyException)
            {
                _logger.LogInformation("Invoice submission {SubmissionId}: it changed since it was read; re-reading before the terminal status", submissionId);
                rowVersion = null;
            }
        }

        _logger.LogWarning("Invoice submission {SubmissionId}: the terminal status lost to a concurrent change twice; it stays Processing for a retry", submissionId);
        return new SubmissionAfterStepsResult { Status = SubmissionAfterStepsStatus.Retry, SubmissionId = submissionId, ReasonCode = TerminalWriteConflictCode };
    }

    /// <summary>
    /// Null when the submission is Processing (the steps run). Accepted, Rejected or Failed: Completed with
    /// <see cref="SubmissionAfterStepsResult.AlreadyTerminal"/>, nothing written and nobody notified. Any other status (Received, none):
    /// Refused (<see cref="NotProcessingCode"/>).
    /// </summary>
    private SubmissionAfterStepsResult? AlreadyTerminal(Entity submission, Guid submissionId)
    {
        var status = submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value;
        if (status == (int)InvoiceSubmissionStatus.Processing)
        {
            return null;
        }

        if (status is (int)InvoiceSubmissionStatus.Accepted or (int)InvoiceSubmissionStatus.Rejected or (int)InvoiceSubmissionStatus.Failed)
        {
            _logger.LogInformation("Invoice submission {SubmissionId}: already {Status}; no after-commit step runs and the status is not written",
                submissionId, (InvoiceSubmissionStatus)status.Value);
            return new SubmissionAfterStepsResult
            {
                Status = SubmissionAfterStepsStatus.Completed,
                SubmissionId = submissionId,
                InvoiceId = submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id,
                TerminalStatus = (InvoiceSubmissionStatus)status.Value,
                AlreadyTerminal = true,
            };
        }

        _logger.LogWarning("Invoice submission {SubmissionId}: status {Status} is neither Processing nor terminal; no after-commit step ran", submissionId, status);
        return new SubmissionAfterStepsResult { Status = SubmissionAfterStepsStatus.Refused, SubmissionId = submissionId, ReasonCode = NotProcessingCode };
    }

    // ── Totals refusal (map row 6c) ──

    /// <summary>The findings JSON member that records the 016 refusal: <c>{ "code": ..., "lineId": ... }</c> (no message, no amounts).</summary>
    internal const string TotalsRefusalMember = "totalsRefusal";

    /// <summary>
    /// Records the totals refusal's code in <c>sprk_findingsjson</c> (the invoice is not undone, map row 6c). Idempotent: no write when the
    /// stored member already holds the same code. Returns the JSON it wrote (later findings writes build on it), or null when it wrote nothing.
    /// </summary>
    private async Task<string?> RecordTotalsRefusalAsync(Guid submissionId, string findingsJson, InvoiceTotalsRefusal refusal, CancellationToken ct)
    {
        var root = JsonNode.Parse(findingsJson)!.AsObject();
        if (root[TotalsRefusalMember] is JsonObject existing && string.Equals(Text(existing, "code"), refusal.Code, StringComparison.Ordinal))
        {
            return null;
        }

        root[TotalsRefusalMember] = new JsonObject { ["code"] = refusal.Code, ["lineId"] = refusal.LineId?.ToString("D") };
        var json = root.ToJsonString();
        await _entities.UpdateAsync(FinanceBaseValues.SubmissionEntity, submissionId, new Dictionary<string, object>
        {
            [InvoiceSubmissionPersister.SubmissionFindingsJson] = json,
        }, ct).ConfigureAwait(false);
        _logger.LogWarning("Invoice submission {SubmissionId}: the invoice totals were refused ({Code}); recorded in the findings, the invoice stands",
            submissionId, refusal.Code);
        return json;
    }

    // ── D-122: the resolved Signal set (notes 13c) ──

    /// <summary>The findings JSON member that freezes the set the automatic rejection resolves:
    /// <c>{ "setReadAtUtc": "...Z", "signals": [ { "id": ..., "policyCode": ... } ] }</c>.</summary>
    internal const string ResolvedSetMember = "resolvedSignalSet";

    /// <summary>One Signal the automatic rejection resolves: its id and its policy code (from its dedupe key).</summary>
    internal sealed record ResolvedSignal(Guid Id, string PolicyCode);

    /// <summary>
    /// The set the automatic rejection's record resolves (D-122), as stored: <paramref name="SetReadAtUtc"/> is the instant taken just
    /// before the read (the close re-closes a Signal the SYSTEM closed at or after it).
    /// </summary>
    internal sealed record ResolvedSignalSet(DateTime SetReadAtUtc, IReadOnlyList<ResolvedSignal> Signals)
    {
        public JsonObject ToJson()
        {
            return new JsonObject
            {
                ["setReadAtUtc"] = SetReadAtUtc.ToString("O", CultureInfo.InvariantCulture),
                ["signals"] = new JsonArray(Signals.Select(s => (JsonNode)new JsonObject { ["id"] = s.Id.ToString("D"), ["policyCode"] = s.PolicyCode }).ToArray()),
            };
        }

        /// <exception cref="FormatException">A member is missing or unreadable (the findings are then unreadable: never re-read).</exception>
        public static ResolvedSignalSet Parse(JsonObject json)
        {
            if (!DateTime.TryParseExact(Text(json, "setReadAtUtc"), "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var readAt))
            {
                throw new FormatException("The stored resolved set has no readable setReadAtUtc.");
            }

            var signals = (json["signals"] as JsonArray ?? throw new FormatException("The stored resolved set has no signals array."))
                .Select(n => n is JsonObject s && Guid.TryParse(Text(s, "id"), out var id) && id != Guid.Empty
                    && Text(s, "policyCode") is { Length: > 0 } code
                        ? new ResolvedSignal(id, code)
                        : throw new FormatException("A stored resolved Signal has no id or policy code."))
                .ToList();
            return new ResolvedSignalSet(DateTime.SpecifyKind(readAt, DateTimeKind.Utc), signals);
        }
    }

    /// <summary>
    /// D-122: every OPEN Signal (Open or Acknowledged) on the invoice and each of its lines, read once, app-only. The lines are the
    /// invoice's rows (the persister's resume query); the Signals are found by their subject lookups. EVERY Open or Acknowledged Signal of an
    /// in-scope subject is listed, not the latest episode per (policy, subject): two open episodes of one (policy, subject) are both
    /// resolved (K1, notes 13c). A row whose dedupe key does not parse, or names a subject outside the invoice and its lines, is skipped
    /// (fails closed). Never the write results (a skipped or refreshed write returns no id). The clock is read BEFORE the query, so a
    /// system close after the read is at or after it. Shared with 105's override and return (<see cref="SubmissionOverrideService"/>), which
    /// resolve the same set.
    /// </summary>
    internal static async Task<ResolvedSignalSet> ReadOpenSignalSetAsync(
        IGenericEntityService entities, TimeProvider time, Guid invoiceId, ILogger logger, CancellationToken ct) =>
        (await ReadOpenSignalSetWithSubjectsAsync(entities, time, invoiceId, logger, ct).ConfigureAwait(false)).Set;

    /// <summary><see cref="ReadOpenSignalSetAsync"/> with the distinct subjects (the invoice and the lines) its Signals are on, which a
    /// human decision's caller must be able to Write (105, FR-51).</summary>
    internal static async Task<(ResolvedSignalSet Set, IReadOnlyList<(string Entity, Guid Id)> Subjects)> ReadOpenSignalSetWithSubjectsAsync(
        IGenericEntityService entities, TimeProvider time, Guid invoiceId, ILogger logger, CancellationToken ct)
    {
        var setReadAt = time.GetUtcNow().UtcDateTime;

        // K2 (notes 13c): a single RetrieveMultiple with no paging, fine below 5,000 lines per invoice.
        var lineQuery = new QueryExpression(InvoiceSubmissionPersister.LineEntity) { ColumnSet = new ColumnSet(false), NoLock = true };
        lineQuery.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
        var lineIds = (await entities.RetrieveMultipleAsync(lineQuery, ct).ConfigureAwait(false)).Entities
            .Select(r => r.Id).Where(id => id != Guid.Empty).Distinct().ToList();

        var open = await ReadOpenSignalsAsync(entities, invoiceId, lineIds, logger, ct).ConfigureAwait(false);

        // Ordered by id so the record's hash is stable.
        return (
            new ResolvedSignalSet(setReadAt, open.Select(s => s.Signal).DistinctBy(s => s.Id).OrderBy(s => s.Id).ToList()),
            open.Select(s => (s.SubjectEntity, s.SubjectId)).Distinct().ToList());
    }

    /// <summary>
    /// D-122: every OPEN Signal (Open or Acknowledged), of every policy, whose subject is the invoice (when <paramref name="invoiceId"/> is
    /// given) or one of <paramref name="lineIds"/>, with the subject its dedupe key names; one query, app-only. A row whose dedupe key does not
    /// parse, or names a subject outside those, is skipped (fails closed). Used by <see cref="ReadOpenSignalSetAsync"/> (the invoice and every
    /// line) and by 100's line decisions (the decided lines only, no invoice). The caller reads its clock BEFORE calling. K3 (ontology-r1):
    /// the subject is the ROW's (<see cref="StoredSignalSetCheck.RowSubjectOf"/>); an open row whose dedupe-key subject differs from its
    /// regarding row is skipped with one Warning, so a row whose rights were not asked is never closed.
    /// </summary>
    internal static async Task<IReadOnlyList<(ResolvedSignal Signal, string SubjectEntity, Guid SubjectId)>> ReadOpenSignalsAsync(
        IGenericEntityService entities, Guid? invoiceId, IReadOnlyCollection<Guid> lineIds, ILogger logger, CancellationToken ct)
    {
        if (invoiceId is null && lineIds.Count == 0)
        {
            return [];
        }

        // K2 (notes 13c): a single RetrieveMultiple with no paging, fine below 5,000 Signals per invoice.
        var query = new QueryExpression(SignalEntity) { ColumnSet = new ColumnSet([.. StoredSignalSetCheck.SubjectColumns, "sprk_signalstatus"]), NoLock = true };
        query.Criteria.FilterOperator = LogicalOperator.Or;
        if (invoiceId is { } invoice)
        {
            query.Criteria.AddCondition(SignalWriter.RegardingLookupByEntity[InvoiceReviewService.InvoiceEntity], ConditionOperator.Equal, invoice);
        }

        if (lineIds.Count > 0)
        {
            query.Criteria.AddCondition(SignalWriter.RegardingLookupByEntity[InvoiceSubmissionPersister.LineEntity], ConditionOperator.In, lineIds.Cast<object>().ToArray());
        }

        var rows = await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var subjects = new HashSet<(string Entity, Guid Id)>(lineIds.Select(id => (InvoiceSubmissionPersister.LineEntity, id)));
        if (invoiceId is { } own)
        {
            subjects.Add((InvoiceReviewService.InvoiceEntity, own));
        }

        var open = new List<(ResolvedSignal Signal, string SubjectEntity, Guid SubjectId)>();
        foreach (var row in rows.Entities)
        {
            if (row.Id == Guid.Empty
                || row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value is not (SignalWriter.SignalStatusOpen or SignalWriter.SignalStatusAcknowledged))
            {
                continue;
            }

            if (StoredSignalSetCheck.RowSubjectOf(row) is not { } subject)
            {
                logger.LogWarning("Signal {SignalId}: its dedupe key does not name the subject its regarding row is filed on; it is skipped", row.Id);
                continue;
            }

            if (subjects.Contains((subject.Entity, subject.Id)))
            {
                open.Add((new ResolvedSignal(row.Id, subject.PolicyCode), subject.Entity, subject.Id));
            }
        }

        return open;
    }

    /// <summary>Writes <paramref name="set"/> into <c>sprk_findingsjson</c> (<see cref="ResolvedSetMember"/>) and returns the JSON written.</summary>
    private async Task<string> StoreResolvedSetAsync(Guid submissionId, string findingsJson, ResolvedSignalSet set, CancellationToken ct)
    {
        var root = JsonNode.Parse(findingsJson)!.AsObject();
        root[ResolvedSetMember] = set.ToJson();
        var json = root.ToJsonString();
        await _entities.UpdateAsync(FinanceBaseValues.SubmissionEntity, submissionId, new Dictionary<string, object>
        {
            [InvoiceSubmissionPersister.SubmissionFindingsJson] = json,
        }, ct).ConfigureAwait(false);
        _logger.LogInformation("Invoice submission {SubmissionId}: the automatic rejection resolves {Count} open Signals (stored before the record)",
            submissionId, set.Signals.Count);
        return json;
    }

    private sealed record RejectionStep(BillingDecisionRecordResult? Recorded, ResolvedSignalSet Set, string FindingsJson, SubmissionAfterStepsResult? Stop);

    /// <summary>
    /// Records the automatic rejection with <c>ResolvedSignalIds</c> = the stored set. A refusal is classified by <see cref="RecordRefused"/>
    /// (<c>review_invalid</c> is permanent: it means a bug, there is no fallback). One refusal is read further when the set is not empty:
    /// <b>owner_refused</b> with some listed Signals no longer existing (one query by id). Then ONLY those ids are removed, the same
    /// <see cref="ResolvedSignalSet.SetReadAtUtc"/> is kept, the shrunk set is stored again, and the record is tried ONCE more with it; if
    /// that fails too, its refusal is classified as any refusal. When nothing is missing, owner_refused stands (notes 13c).
    /// </summary>
    private async Task<RejectionStep> RecordRejectionAsync(
        StoredFindings stored, Guid submissionId, Guid invoiceId, ResolvedSignalSet set, string findingsJson, CancellationToken ct)
    {
        try
        {
            return await RecordOnceAsync(stored, submissionId, invoiceId, set, findingsJson, ct).ConfigureAwait(false);
        }
        catch (DecisionRecordRefusedException ex) when (ex.Reason == DecisionRecordRefusalReason.OwnerRefused && set.Signals.Count > 0)
        {
            var missing = await MissingSignalIdsAsync(set.Signals.Select(s => s.Id).ToList(), ct).ConfigureAwait(false);
            if (missing.Count == 0)
            {
                return new RejectionStep(null, set, findingsJson, RecordRefused(submissionId, invoiceId, ex.Reason));
            }

            var shrunk = set with { Signals = set.Signals.Where(s => !missing.Contains(s.Id)).ToList() };
            _logger.LogWarning("Invoice submission {SubmissionId}: {Missing} of {Count} Signals listed for the automatic rejection no longer exist; the set shrinks to {Left} and the record is tried once more",
                submissionId, missing.Count, set.Signals.Count, shrunk.Signals.Count);
            var shrunkJson = await StoreResolvedSetAsync(submissionId, findingsJson, shrunk, ct).ConfigureAwait(false);
            try
            {
                return await RecordOnceAsync(stored, submissionId, invoiceId, shrunk, shrunkJson, ct).ConfigureAwait(false);
            }
            catch (DecisionRecordRefusedException again)
            {
                return new RejectionStep(null, shrunk, shrunkJson, RecordRefused(submissionId, invoiceId, again.Reason));
            }
        }
        catch (DecisionRecordRefusedException ex)
        {
            return new RejectionStep(null, set, findingsJson, RecordRefused(submissionId, invoiceId, ex.Reason));
        }
    }

    private async Task<RejectionStep> RecordOnceAsync(
        StoredFindings stored, Guid submissionId, Guid invoiceId, ResolvedSignalSet set, string findingsJson, CancellationToken ct)
    {
        var ids = set.Signals.Select(s => s.Id).ToList();
        return new RejectionStep(await _record(BuildRejectionDecision(stored, submissionId, invoiceId, ids)!, ct).ConfigureAwait(false), set, findingsJson, null);
    }

    private SubmissionAfterStepsResult RecordRefused(Guid submissionId, Guid invoiceId, string reason)
    {
        var permanent = PermanentRecordRefusals.Contains(reason);
        _logger.LogWarning("Invoice submission {SubmissionId}: the automatic rejection's Decision Record was refused ({Reason}); {Next}",
            submissionId, reason, permanent ? "a retry cannot succeed" : "it stays Processing for a retry");
        return Stop(permanent ? SubmissionAfterStepsStatus.Refused : SubmissionAfterStepsStatus.Retry, submissionId, invoiceId, reason);
    }

    /// <summary>The ones of <paramref name="ids"/> that no longer exist (one query by id, app-only).</summary>
    private async Task<HashSet<Guid>> MissingSignalIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var query = new QueryExpression(SignalEntity) { ColumnSet = new ColumnSet(false), NoLock = true, TopCount = ids.Count };
        query.Criteria.AddCondition("sprk_signalid", ConditionOperator.In, ids.Cast<object>().ToArray());
        var found = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.Select(e => e.Id).ToHashSet();
        return ids.Where(id => !found.Contains(id)).ToHashSet();
    }

    /// <summary>
    /// Closes every Signal of the set as Acted by <paramref name="recordId"/> (<see cref="SignalWriter.CloseActedByDecisionAsync"/>, no person).
    /// Closed and AlreadyClosedByThisRecord are done; LeftAsIs (a person or another record resolved it) stays listed in the record, one
    /// Information line; a Signal that no longer exists has nothing to close, one Warning; ChangedSinceRead is a retry (every Signal is still
    /// attempted). Any other exception propagates (transient, as a Signal write's). Returns the retry code, or null.
    /// </summary>
    private async Task<string?> CloseResolvedSetAsync(ResolvedSignalSet set, Guid recordId, Guid submissionId, CancellationToken ct)
    {
        string? failure = null;
        foreach (var signal in set.Signals)
        {
            SignalAutomatedCloseOutcome outcome;
            try
            {
                outcome = await _closeActed(signal.Id, recordId, set.SetReadAtUtc, signal.PolicyCode, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRecordNotFound(ex))
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: Signal {SignalId} (policy {PolicyCode}) listed in Decision Record {RecordId} no longer exists; nothing to close",
                    submissionId, signal.Id, signal.PolicyCode, recordId);
                continue;
            }

            switch (outcome)
            {
                case SignalAutomatedCloseOutcome.LeftAsIs:
                    _logger.LogInformation("Invoice submission {SubmissionId}: Signal {SignalId} (policy {PolicyCode}) was resolved otherwise; it stays listed in Decision Record {RecordId}",
                        submissionId, signal.Id, signal.PolicyCode, recordId);
                    break;
                case SignalAutomatedCloseOutcome.ChangedSinceRead:
                    failure ??= SignalChangedCode;
                    break;
            }
        }

        return failure;
    }

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

    private SubmissionAfterStepsResult Missing(Guid submissionId)
    {
        _logger.LogInformation("Invoice submission {SubmissionId}: the row no longer exists; no after-commit step ran", submissionId);
        return new SubmissionAfterStepsResult { Status = SubmissionAfterStepsStatus.SubmissionMissing, SubmissionId = submissionId };
    }

    private static long? RowVersionOf(Entity row) =>
        row.GetAttributeValue<long?>(RowVersionColumn)
        ?? (long.TryParse(row.RowVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null);

    // ── Notification ──

    /// <summary>
    /// Step 9: <see cref="IEbillingNotificationSink.SubmissionDecidedAsync"/> once per submission. Blocked is reported as Rejected with the
    /// persister's <c>ebilling.submission.blocked</c> code and the invoice id (the invoice exists, In Review; the firm learns the
    /// submission did not pass automatically). The marker is set after the sink returns, so a crash between the two, or a cache that fails
    /// open, notifies again on the resume (at-least-once there; the sink's contract makes it idempotent per submission and outcome). A
    /// sink or marker fault is logged and the run continues (the sink's contract: a failure never retries the pipeline).
    /// </summary>
    private async Task NotifyOnceAsync(
        Entity submission, Guid submissionId, Guid invoiceId, InvoiceSubmissionStatus terminal, string? terminalReasonCode, CancellationToken ct)
    {
        var marker = NotifyMarkerFor(submissionId);
        if (await _idempotency.IsEventProcessedAsync(marker, ct).ConfigureAwait(false))
        {
            return;
        }

        if (submission.GetAttributeValue<OptionSetValue>("sprk_channel")?.Value is not { } channelValue
            || !Enum.IsDefined(typeof(InvoiceSubmissionChannel), channelValue))
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: no readable channel; the notification is not sent", submissionId);
            return;
        }

        var outcome = terminal == InvoiceSubmissionStatus.Accepted ? SubmissionDecisionOutcome.Accepted : SubmissionDecisionOutcome.Rejected;
        try
        {
            await _sink.SubmissionDecidedAsync(new SubmissionDecidedNotification(
                submissionId,
                outcome,
                (InvoiceSubmissionChannel)channelValue,
                invoiceId,
                outcome == SubmissionDecisionOutcome.Rejected ? terminalReasonCode : null), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: the notification failed ({Fault}); the decision stands and the pipeline is not retried for it",
                submissionId, ex.GetType().Name);
            return;
        }

        try
        {
            await _idempotency.MarkEventAsProcessedAsync(marker, NotifyMarkerLifetime, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: the notified marker could not be set ({Fault}); a resume may notify again",
                submissionId, ex.GetType().Name);
        }
    }

    // ── Signals ──

    /// <summary>One Signal's step: written, a retryable <paramref name="FailureCode"/>, or a permanent <paramref name="RefusedCode"/> (F1-3).</summary>
    private readonly record struct SignalStep(bool Wrote, string? FailureCode, string? RefusedCode = null);

    // ── F1-3: permanent Signal-write refusals ──

    /// <summary>The findings JSON member that records the Signals the writer refused permanently: <c>[{ policyCode, subject: { entity, id },
    /// code }]</c>, no values; idempotent (one entry per Signal and code).</summary>
    internal const string SignalWriteRefusalsMember = "signalWriteRefusals";

    /// <summary>One Signal the writer refused permanently: its policy, subject and the writer's code.</summary>
    internal sealed record SignalWriteRefused(string PolicyCode, string SubjectEntity, Guid SubjectId, string Code);

    /// <summary>
    /// The <see cref="SignalWriteResult.SkippedRefusalCode"/> values a redelivery cannot change (F1-3): the reconcile met a Signal whose core
    /// record or no-core owner changed (<see cref="OntologyWriterFailureReason.CoreRecordChanged"/>, re-grouping is task 031's), and the
    /// ownership resolver's refusals of the data or configuration it read (<see cref="RecordOwnerRefusal"/>). Not here, so retried:
    /// <see cref="RecordOwnerRefusal.ParentUndetermined"/> (a derivation that FAILED, which a retry can pass) and any code not listed.
    /// </summary>
    internal static readonly IReadOnlySet<string> PermanentSkipCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        OntologyWriterFailureReason.CoreRecordChanged,
        RecordOwnerRefusal.ParentUnresolved,
        RecordOwnerRefusal.SecureParentNotIsolated,
        RecordOwnerRefusal.SecureOwnerTeamUnresolved,
        RecordOwnerRefusal.SecureBusinessUnitAmbiguous,
        RecordOwnerRefusal.NoDefaultOwnerTeam,
        RecordOwnerRefusal.ActingUserUnresolved,
        RecordOwnerRefusal.NoOwnerSource,
        RecordOwnerRefusal.RecordMissing,
    };

    /// <summary>
    /// The code of a Signal-write exception the writer would throw again on every attempt (F1-3), or null for a retryable one:
    /// <list type="bullet">
    /// <item><see cref="ArgumentException"/> and <see cref="SignalSentenceTemplateException"/>: the request itself (a sentence over 2,000
    /// characters, a headline over 200, an unrenderable template, ...); the code is <see cref="SignalWriter.PreflightRefusal"/>'s.</item>
    /// <item><see cref="SignalWriterEscalationException"/> that wraps no fault (or an argument error): a state the writer refuses (an
    /// unverified subject, a core record it cannot derive or that is ambiguous, an owner that does not read back, a reconcile meeting a
    /// mis-owned row); the code is its <see cref="SignalWriterEscalationException.Reason"/>. One that wraps a read or resolver fault
    /// (<see cref="OntologyWriterFailureReason.OwnerReadBackFailed"/>, a catalog read fault) is the writer's refusal of an UNKNOWN state and
    /// is retried, as <see cref="SignalWriter.IsDeterministicPrivilegeRefusal"/> treats it.</item>
    /// <item>A privilege refusal (<see cref="SignalWriter.IsDeterministicPrivilegeRefusal"/>): the team's role lacks a privilege.</item>
    /// </list>
    /// </summary>
    internal static string? PermanentWriteRefusal(SignalWriteRequest request, Exception ex) => ex switch
    {
        SignalSentenceTemplateException => SignalWriter.PreflightRefusal(request) ?? SignalWriteRefusal.SentenceTemplateInvalid,
        ArgumentException => SignalWriter.PreflightRefusal(request) ?? RequestRefusedCode,
        SignalWriterEscalationException { InnerException: null or ArgumentException } escalation => escalation.Reason,
        _ when SignalWriter.IsDeterministicPrivilegeRefusal(ex) => OntologyWriterFailureReason.DataverseAccessDenied,
        _ => null,
    };

    /// <summary>An argument refusal the writer's preflight does not name (it passed the request; something it sent was refused).</summary>
    internal const string RequestRefusedCode = "request_refused";

    /// <summary>
    /// Adds <paramref name="refused"/> to <c>sprk_findingsjson</c>'s <see cref="SignalWriteRefusalsMember"/> and, in the same update, sets
    /// <c>sprk_adminattention</c> (D-133, <see cref="AdminAttentionText"/>). Idempotent: an entry already stored (same policy, subject and
    /// code) is not added again, and nothing is written when every entry is stored. Returns the JSON written, or null when nothing was
    /// written.
    /// </summary>
    private async Task<string?> RecordSignalWriteRefusalsAsync(Guid submissionId, string findingsJson, IReadOnlyList<SignalWriteRefused> refused, CancellationToken ct)
    {
        var root = JsonNode.Parse(findingsJson)!.AsObject();
        var stored = root[SignalWriteRefusalsMember] as JsonArray ?? [];
        var keys = stored.OfType<JsonObject>()
            .Select(e => (Text(e, "policyCode"), (e["subject"] as JsonObject) is { } s ? (Text(s, "entity"), Text(s, "id")) : (null, null), Text(e, "code")))
            .ToHashSet();
        var added = 0;
        foreach (var r in refused)
        {
            var id = r.SubjectId.ToString("D");
            if (keys.Add((r.PolicyCode, (r.SubjectEntity, id), r.Code)))
            {
                stored.Add(new JsonObject
                {
                    ["policyCode"] = r.PolicyCode,
                    ["subject"] = new JsonObject { ["entity"] = r.SubjectEntity, ["id"] = id },
                    ["code"] = r.Code,
                });
                added++;
            }
        }

        if (added == 0)
        {
            return null;
        }

        root[SignalWriteRefusalsMember] = stored;
        var json = root.ToJsonString();
        var update = new Dictionary<string, object>
        {
            [InvoiceSubmissionPersister.SubmissionFindingsJson] = json,
        };

        // D-133: the same update (a plain UpdateAsync: findings writes carry no row-version guard) marks the submission for the administrator. The Signal
        // segment is a function of every refusal stored (the plan's and the writer's), so a resume that adds nothing writes nothing and never repeats it;
        // it is MERGED into the current text (R3, R5: the policy and processing-stopped segments stay), and never cleared here.
        var currentAttention = await AdminAttentionText.ReadCurrentAsync(_entities, submissionId, ct).ConfigureAwait(false);
        if (AdminAttentionText.Merge(currentAttention, root) is { } attention)
        {
            update[InvoiceSubmissionPersister.SubmissionAdminAttention] = attention;
        }

        await _entities.UpdateAsync(FinanceBaseValues.SubmissionEntity, submissionId, update, ct).ConfigureAwait(false);
        return json;
    }

    /// <summary>
    /// The option-a lifecycle (notes section 6) for one (policy, subject): the latest episode by the dedupe-key prefix, its status and its
    /// stored reason-set fingerprint decide: none → episode 1; open + same → refresh it; open + different → Superseded, then episode n+1;
    /// resolved + same → skip (a resume never re-raises what was resolved); resolved + different → episode n+1. A skipped write
    /// (<see cref="SignalWriteResult.SkippedRefusalCode"/>) is permanent when its code is in <see cref="PermanentSkipCodes"/> (F1-3: recorded,
    /// the run continues), otherwise a failure; an open Signal that changed while being superseded is a failure: retry.
    /// </summary>
    private async Task<SignalStep> WriteWithLifecycleAsync(SignalWriteRequest request, string fingerprint, Guid submissionId, CancellationToken ct)
    {
        var latest = await LatestEpisodeAsync(request, ct).ConfigureAwait(false);
        var episode = 1;
        if (latest is { } existing)
        {
            var same = string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal);
            if (existing.IsResolved)
            {
                if (same)
                {
                    return new SignalStep(false, null);
                }

                episode = existing.Episode + 1;
            }
            else if (same)
            {
                episode = existing.Episode;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(existing.RowVersion))
                {
                    _logger.LogWarning("Invoice submission {SubmissionId}: Signal {SignalId} was read without a row version; not superseded", submissionId, existing.Id);
                    return new SignalStep(false, SignalChangedCode);
                }

                var closed = await _supersedeSignal(existing.Id, existing.RowVersion, request.PolicyCode, ct).ConfigureAwait(false);
                if (closed != SignalCloseOutcome.Closed)
                {
                    return new SignalStep(false, SignalChangedCode);
                }

                episode = existing.Episode + 1;
            }
        }

        var result = await _writeSignal(request with { Episode = episode }, ct).ConfigureAwait(false);
        if (result.IsSkipped)
        {
            if (PermanentSkipCodes.Contains(result.SkippedRefusalCode!))
            {
                return new SignalStep(false, null, result.SkippedRefusalCode);
            }

            _logger.LogWarning("Invoice submission {SubmissionId}: the Signal of policy {PolicyCode} on {Subject} {SubjectId} was skipped ({Refusal})",
                submissionId, request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, result.SkippedRefusalCode);
            return new SignalStep(false, SignalWriteSkippedCode);
        }

        return new SignalStep(true, null);
    }

    private sealed record ExistingEpisode(Guid Id, int Episode, bool IsResolved, string? Fingerprint, string? RowVersion);

    private async Task<ExistingEpisode?> LatestEpisodeAsync(SignalWriteRequest request, CancellationToken ct)
    {
        var subjectId = request.SubjectId.ToString("D").ToLowerInvariant();
        var prefix = SignalWriter.DedupeKeySubjectPrefix(request.PolicyCode, request.SubjectEntityLogicalName, subjectId);
        var query = new QueryExpression(SignalEntity)
        {
            ColumnSet = new ColumnSet("sprk_dedupekey", "sprk_signalstatus", "sprk_evidencerefs", "versionnumber"),
            NoLock = true,
        };
        query.Criteria.AddCondition("sprk_dedupekey", ConditionOperator.BeginsWith, prefix);
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

        ExistingEpisode? latest = null;
        foreach (var row in rows.Entities)
        {
            if (!SignalWriter.TryParseDedupeKey(row.GetAttributeValue<string>("sprk_dedupekey"), out var code, out var entity, out var id, out var episode)
                || !string.Equals(code, request.PolicyCode, StringComparison.Ordinal)
                || !string.Equals(entity, request.SubjectEntityLogicalName, StringComparison.Ordinal) || id != request.SubjectId)
            {
                continue;
            }

            if (latest is null || episode > latest.Episode)
            {
                latest = new ExistingEpisode(
                    row.Id,
                    episode,
                    row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value == SignalWriter.SignalStatusResolved,
                    FingerprintOf(row.GetAttributeValue<string>("sprk_evidencerefs")),
                    row.RowVersion ?? (row.GetAttributeValue<long?>("versionnumber") is { } v ? v.ToString(CultureInfo.InvariantCulture) : null));
            }
        }

        return latest;
    }

    /// <summary>The <see cref="BillingFindingSignalMapper.FingerprintEvidenceKind"/> line's <c>ref</c> in a stored <c>sprk_evidencerefs</c>; null when absent or unreadable.</summary>
    internal static string? FingerprintOf(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(evidenceJson) is JsonArray lines
                ? lines.OfType<JsonObject>()
                    .Where(l => Text(l, "kind") == BillingFindingSignalMapper.FingerprintEvidenceKind)
                    .Select(l => Text(l, "ref"))
                    .FirstOrDefault()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── System Signals (D-123, D-125) ──

    /// <summary>
    /// The system findings (D-91 mismatch, refused codes, firm-code mismatches, code set needs review, no code set, coding unavailable) as
    /// Signal requests under <c>BILL-SYSTEM-SUBMISSION</c>: read app-only, once per run, and only when there is a system finding. Missing,
    /// ambiguous, disabled, of another rule type, or with no Active version in force on the received date: no system Signal, one Warning
    /// (the policy code only), and the findings stay in the findings JSON. Disabled is the administrator's choice (D-125) and stays a
    /// Warning; every other state also sets <c>sprk_adminattention</c> (R5, D-133: a misconfiguration the administrator must hear about).
    /// </summary>
    private async Task<(IReadOnlyList<(SignalWriteRequest Request, string Fingerprint)> Requests, bool WroteAttention)> SystemSignalsAsync(
        StoredFindings stored, Guid submissionId, Guid invoiceId, CancellationToken ct)
    {
        if (!stored.HasSystemFindings)
        {
            return ([], false);
        }

        var (rule, state) = await ReadSystemPolicyAsync(stored.ReceivedOn, ct).ConfigureAwait(false);
        if (rule is null)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: system billing policy {PolicyCode} is {State}; no system Signal is written, the findings stay in the findings JSON",
                submissionId, BillingRuleCatalogue.SystemPolicyCode, state);

            // R5 (D-133): D-125 accepts the skip only when an administrator DISABLED the policy on purpose. Every other state is a misconfiguration
            // the administrator must hear about; PolicyNote is null for "disabled". Merged into the current text, idempotent, a fault is swallowed.
            var wrote = AdminAttentionText.PolicyNote(BillingRuleCatalogue.SystemPolicyCode, state) is { } policyNote
                && await AdminAttentionText.NoteAsync(_entities, submissionId, policyNote, null, _logger, ct).ConfigureAwait(false);
            return ([], wrote);
        }

        var policy = new BillingSystemPolicyVersion(rule.PolicyId, rule.PolicyCode, rule.PolicyVersionId);
        var findings = stored.SystemFindings(policy);
        var lines = findings.Where(f => f.Subject.LineIndex is not null).Select(f => f.Subject.LineIndex!.Value).Distinct().ToList();
        var subjects = new BillingSignalSubjects(
            invoiceId,
            stored.InvoiceName,
            lines.ToDictionary(i => i, i => InvoiceSubmissionPersister.LineIdFor(submissionId, i)),
            lines.ToDictionary(i => i, i => InvoiceSubmissionPersister.LineNameFor(stored.TrackingNumber, i)));
        return (BillingFindingSignalMapper.MapAll(findings, new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule }, subjects)
            .Select(m => (m.Request, m.Fingerprint))
            .ToList(), false);
    }

    /// <summary>
    /// Two reads: the policy by <c>sprk_policycode</c> (billing domain), then its versions. Usable only when exactly one such policy exists,
    /// it is enabled and Active, and the version the evaluator would apply on <paramref name="receivedOn"/> exists
    /// (<see cref="InvoiceComplianceEvaluator.SelectVersion"/> over <see cref="BillingRuleCandidate.IsInForceOn"/>, the 076 K3 rule: the
    /// current version from its start, a superseded one inside its window) and is of the SubmissionIntegrity type. A version superseded
    /// after the received date (a re-seed) therefore still applies to that submission, as it does for the rule evaluation (K1, notes 13a).
    /// </summary>
    internal async Task<(BillingRuleCandidate? Rule, string State)> ReadSystemPolicyAsync(DateOnly? receivedOn, CancellationToken ct)
    {
        var query = new QueryExpression(BillingFactLoader.PolicyEntity)
        {
            ColumnSet = new ColumnSet("sprk_policycode", "sprk_enabled", "statecode", "sprk_currentversion", "sprk_lane", "sprk_severity", RuleDomains.PolicyDomainColumn),
            NoLock = true,
            TopCount = 2,
        };
        RuleDomains.AddBillingFilter(query);
        query.Criteria.AddCondition("sprk_policycode", ConditionOperator.Equal, BillingRuleCatalogue.SystemPolicyCode);

        var rows = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities
            .Where(r => RuleDomains.IsBillingRow(r)
                && string.Equals(r.GetAttributeValue<string>("sprk_policycode")?.Trim(), BillingRuleCatalogue.SystemPolicyCode, StringComparison.Ordinal))
            .ToList();
        if (rows.Count == 0)
        {
            return (null, "missing");
        }

        if (rows.Count > 1)
        {
            return (null, "ambiguous");
        }

        var policy = rows[0];
        if (policy.GetAttributeValue<bool?>("sprk_enabled") != true || policy.GetAttributeValue<OptionSetValue>("statecode")?.Value != StateActive)
        {
            return (null, "disabled");
        }

        if (receivedOn is not { } date)
        {
            return (null, "without_a_version_in_force");
        }

        var versionQuery = new QueryExpression(BillingFactLoader.VersionEntity)
        {
            ColumnSet = new ColumnSet("sprk_policy", "sprk_versionnumber", "sprk_ruletype", "sprk_rulebody", "sprk_messagetemplate", "sprk_shortheadline",
                "sprk_consequence", "sprk_inforcefrom", "sprk_inforceto"),
            NoLock = true,
        };
        versionQuery.Criteria.AddCondition("sprk_policy", ConditionOperator.Equal, policy.Id);
        var currentId = policy.GetAttributeValue<EntityReference>("sprk_currentversion")?.Id;
        var versions = (await _entities.RetrieveMultipleAsync(versionQuery, ct).ConfigureAwait(false)).Entities
            .Where(v => v.GetAttributeValue<EntityReference>("sprk_policy")?.Id == policy.Id)
            .Select(v => new BillingRuleCandidate
            {
                PolicyId = policy.Id,
                PolicyCode = BillingRuleCatalogue.SystemPolicyCode,
                PolicyVersionId = v.Id,
                // The system policy belongs to no rule set (D-125); these two are never read for it (no D-39 selection, no handler).
                RuleSetId = Guid.Empty,
                Scope = RuleSetScope.ClientDefault,
                RuleType = v.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value.ToString(CultureInfo.InvariantCulture),
                RuleBody = v.GetAttributeValue<string>("sprk_rulebody"),
                MessageTemplate = v.GetAttributeValue<string>("sprk_messagetemplate"),
                ShortHeadline = v.GetAttributeValue<string>("sprk_shortheadline"),
                InForceFrom = UtcDate(v.GetAttributeValue<DateTime?>("sprk_inforcefrom")),
                InForceTo = UtcDate(v.GetAttributeValue<DateTime?>("sprk_inforceto")),
                IsCurrent = currentId == v.Id,
                VersionNumber = v.GetAttributeValue<int?>("sprk_versionnumber") ?? 0,
                Lane = policy.GetAttributeValue<OptionSetValue>("sprk_lane")?.Value,
                Severity = policy.GetAttributeValue<OptionSetValue>("sprk_severity")?.Value,
                Consequence = v.GetAttributeValue<OptionSetValue>("sprk_consequence")?.Value,
            })
            .ToList();

        var rule = InvoiceComplianceEvaluator.SelectVersion(versions, date);
        if (rule is null)
        {
            return (null, "without_a_version_in_force");
        }

        return rule.RuleType == SubmissionIntegrityOption.ToString(CultureInfo.InvariantCulture) ? (rule, "in_force") : (null, "of_another_rule_type");
    }

    private static DateOnly? UtcDate(DateTime? value) => value is { } v
        ? DateOnly.FromDateTime(v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v)
        : null;

    // ── Decision Record (AutoRejected) ──

    /// <summary>The refusals a retry cannot change: the decision itself, its subject or its owner (anything else is a fault or a lookup: retry).</summary>
    private static readonly HashSet<string> PermanentRecordRefusals = new(StringComparer.Ordinal)
    {
        DecisionRecordRefusalReason.ReviewInvalid, DecisionRecordRefusalReason.FactSnapshotMissing, DecisionRecordRefusalReason.SubjectUnsupported,
        DecisionRecordRefusalReason.SubjectUnfileable, DecisionRecordRefusalReason.RecordClassUnderivable, DecisionRecordRefusalReason.CoreUnsupported,
        DecisionRecordRefusalReason.OwnerRefused, DecisionRecordRefusalReason.OwnerMissing,
    };

    /// <summary>
    /// The automatic rejection's decision (notes section 7, D-100, D-101), built only from the stored findings so every attempt builds the
    /// same one (074 K-5): <c>reject-invoice</c> on the invoice, the deterministic decision id, an <c>automated:</c> decider with no confirmer;
    /// Reason = the deciding reason codes; PolicyVersionId = the first deciding finding's version; Values strings only; Facts = the decision,
    /// the threshold and every merged reason of the stored, scrubbed Signal plan (D-112: never the raw findings).
    /// <para><b>ResolvedSignalIds</b> = <paramref name="resolvedSignalIds"/>: the STORED set (D-122, notes 13c), every open Signal on the
    /// invoice and its lines read once before the record; the record's hash covers it, so a resume passes the same set. Null (the preflight,
    /// which does no I/O) lists none.</para>
    /// </summary>
    internal static BillingDecision? BuildRejectionDecision(
        StoredFindings stored, Guid submissionId, Guid invoiceId, IReadOnlyList<Guid>? resolvedSignalIds = null)
    {
        var deciding = stored.DecidingIndexes
            .Select(i => stored.Findings.TryGetValue(i, out var f) ? f : null)
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();
        if (deciding.Count == 0 || deciding.Count != stored.DecidingIndexes.Count)
        {
            return null;
        }

        var codes = deciding.Select(f => f.ReasonCode).Distinct(StringComparer.Ordinal).ToList();
        var reason = string.Join(", ", codes);
        var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["decision"] = RejectActionCode,
            ["outcome"] = "Rejected",
            ["submissionId"] = submissionId.ToString("D"),
            ["trackingNumber"] = stored.TrackingNumber,
            ["reliabilityThreshold"] = stored.ReliabilityThreshold,
            ["decidingReasonCodes"] = codes,
            ["reasons"] = stored.Plan.Select(p => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["policyCode"] = p.Request.PolicyCode,
                ["policyVersionId"] = p.Request.PolicyVersionId.ToString("D"),
                ["subject"] = p.Request.SubjectEntityLogicalName,
                ["subjectId"] = p.Request.SubjectId.ToString("D"),
                ["reasonCodes"] = p.ReasonCodes,
                ["consequence"] = p.Consequence,
                ["fingerprint"] = p.Fingerprint,
                ["decision"] = p.Decision,
                ["sentence"] = p.Sentence,
            }).ToList(),
        };
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["submissionId"] = submissionId.ToString("D"),
            ["trackingNumber"] = stored.TrackingNumber,
            ["decidingReasonCodes"] = reason,
        };

        return new BillingDecision(
            RejectionDecisionIdFor(submissionId),
            RejectActionCode,
            invoiceId,
            BillingDecider.Automated(RejectDecider),
            facts,
            Outcome: "Rejected",
            Reason: reason,
            Values: values,
            PolicyVersionId: deciding[0].PolicyVersionId,
            ResolvedSignalIds: resolvedSignalIds,
            Written: [new DecisionRecordRef(InvoiceReviewService.InvoiceEntity, invoiceId)]);
    }

    // ── Indexing ──

    private static JobContract IndexingJob(Guid invoiceId, Guid documentId, string correlationId) => new()
    {
        JobId = IndexingJobIdFor(invoiceId),
        JobType = InvoiceIndexingJobHandler.JobTypeName,
        SubjectId = invoiceId.ToString(),
        CorrelationId = correlationId,
        // The extraction handler's key: Service Bus duplicate detection (MessageId) drops a resend in its window.
        IdempotencyKey = $"invoice-index-{invoiceId}",
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonDocument.Parse(JsonSerializer.Serialize(new { InvoiceId = invoiceId, DocumentId = documentId })),
    };

    // ── Helpers ──

    private static SubmissionAfterStepsResult Stop(SubmissionAfterStepsStatus status, Guid submissionId, Guid invoiceId, string code) =>
        new() { Status = status, SubmissionId = submissionId, InvoiceId = invoiceId, ReasonCode = code };

    private static bool IsRecordNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Text(JsonObject node, string name) =>
        node.TryGetPropertyValue(name, out var value) && value is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;

    // ── The stored findings JSON, as part D reads it ──

    /// <summary>One stored plan entry: the request to write and what the record's fact snapshot lists.</summary>
    internal sealed record StoredPlanEntry(
        SignalWriteRequest Request, string Fingerprint, IReadOnlyList<string> ReasonCodes, string? Consequence, string? Decision, string? Sentence);

    /// <summary>One stored billing finding (the fields the record needs).</summary>
    internal sealed record StoredFinding(string ReasonCode, Guid PolicyVersionId);

    /// <summary>
    /// <c>sprk_findingsjson</c> (<see cref="InvoiceSubmissionPersister.BuildFindingsJson"/>) read back: the kind, the decision, the
    /// findings the record needs, the Signal plan, and the pipeline and coding findings the system Signals are mapped from.
    /// </summary>
    internal sealed class StoredFindings
    {
        public required SubmissionEvaluationKind Kind { get; init; }

        /// <summary>The root <c>reasonCode</c> (the outcome's): with <see cref="Kind"/> it decides the terminal status and its reason code.</summary>
        public string? ReasonCode { get; init; }

        /// <summary>False when the stored JSON has no <c>signalPlan</c> array (G: that is not an empty plan; the run is Refused).</summary>
        public bool HasSignalPlan { get; init; }

        /// <summary>The frozen D-122 set (<see cref="ResolvedSetMember"/>); null when none is stored yet.</summary>
        public ResolvedSignalSet? ResolvedSet { get; init; }

        public required Guid InvoiceId { get; init; }
        public required string TrackingNumber { get; init; }
        public DateOnly? ReceivedOn { get; init; }
        public string? InvoiceName { get; init; }
        public decimal? ReliabilityThreshold { get; init; }
        public IReadOnlyList<int> DecidingIndexes { get; init; } = [];
        public IReadOnlyDictionary<int, StoredFinding> Findings { get; init; } = new Dictionary<int, StoredFinding>();
        public IReadOnlyList<StoredPlanEntry> Plan { get; init; } = [];
        private JsonArray PipelineFindings { get; init; } = [];
        private JsonArray CodingFindings { get; init; } = [];
        private JsonObject? CodeSet { get; init; }
        private decimal? InvoiceNumberReadConfidence { get; init; }

        /// <summary>True when any stored finding maps to a SubmissionIntegrity Signal.</summary>
        public bool HasSystemFindings =>
            PipelineFindings.OfType<JsonObject>().Any(p => Text(p, "reasonCode") is SubmissionReasonCodes.InvoiceNumberMismatch
                or SubmissionReasonCodes.CodingNoCodeSetHold or SubmissionReasonCodes.CodingUnavailableHold)
            || CodingFindings.Count > 0
            || CodeSet?["needsReview"]?.GetValue<bool>() == true;

        /// <summary>The system findings under <paramref name="policy"/> (<see cref="BillingSystemFindings"/>). A value missing on one side of
        /// a comparison is not a finding and is left out.</summary>
        public IReadOnlyList<BillingFinding> SystemFindings(BillingSystemPolicyVersion policy)
        {
            var findings = new List<BillingFinding>();
            foreach (var pipeline in PipelineFindings.OfType<JsonObject>())
            {
                switch (Text(pipeline, "reasonCode"))
                {
                    case SubmissionReasonCodes.InvoiceNumberMismatch when pipeline["values"] is JsonObject values
                        && Text(values, "typed") is { Length: > 0 } typed && !string.IsNullOrWhiteSpace(typed)
                        && Text(values, "read") is { Length: > 0 } read && !string.IsNullOrWhiteSpace(read):
                        findings.Add(BillingSystemFindings.InvoiceNumberMismatch(policy, typed, read, InvoiceNumberReadConfidence));
                        break;
                    case SubmissionReasonCodes.CodingNoCodeSetHold:
                        findings.Add(BillingSystemFindings.NoCodeSet(policy));
                        break;
                    case SubmissionReasonCodes.CodingUnavailableHold:
                        findings.Add(BillingSystemFindings.CodingUnavailable(policy));
                        break;
                }
            }

            if (CodeSet?["needsReview"]?.GetValue<bool>() == true && Text(CodeSet, "name") is { } setName && !string.IsNullOrWhiteSpace(setName))
            {
                findings.Add(BillingSystemFindings.CodeSetNeedsReview(policy, setName));
            }

            foreach (var line in CodingFindings.OfType<JsonObject>())
            {
                if (line["line"]?.GetValue<int>() is not { } index)
                {
                    continue;
                }

                foreach (var refused in (line["refused"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (Enum.TryParse<BillingCodeKind>(Text(refused, "kind"), out var kind)
                        && Enum.TryParse<CodeRefusalReason>(Text(refused, "reason"), out var why)
                        && Text(refused, "code") is { } code && !string.IsNullOrWhiteSpace(code))
                    {
                        findings.Add(BillingSystemFindings.CodeRefused(policy, index, new RefusedCode(kind, code, why)));
                    }
                }

                foreach (var (slot, kind) in new[] { ("task", BillingCodeKind.Task), ("activity", BillingCodeKind.Activity), ("expense", BillingCodeKind.Expense) })
                {
                    if (line[slot] is JsonObject value && value["mismatch"]?.GetValue<bool>() == true
                        && Text(value, "firmCode") is { } firm && !string.IsNullOrWhiteSpace(firm)
                        && Text(value, "code") is { } suggested && !string.IsNullOrWhiteSpace(suggested))
                    {
                        findings.Add(BillingSystemFindings.FirmCodeMismatch(policy, index, kind, firm, suggested));
                    }
                }
            }

            return findings;
        }

        /// <summary>Parses the stored JSON; null when it is missing, malformed, or lacks a member the steps need.</summary>
        public static StoredFindings? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                if (JsonNode.Parse(json) is not JsonObject root
                    || !Enum.TryParse<SubmissionEvaluationKind>(Text(root, "kind"), out var kind)
                    || kind is not (SubmissionEvaluationKind.Accepted or SubmissionEvaluationKind.AcceptedUncoded
                        or SubmissionEvaluationKind.AutoRejected or SubmissionEvaluationKind.Blocked)
                    || !Guid.TryParse(Text(root, "invoiceId"), out var invoiceId)
                    || Text(root, "trackingNumber") is not { Length: > 0 } tracking)
                {
                    return null;
                }

                var findings = new Dictionary<int, StoredFinding>();
                foreach (var finding in (root["findings"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (finding["index"]?.GetValue<int>() is { } index && Text(finding, "reasonCode") is { } code
                        && Guid.TryParse(Text(finding, "policyVersionId"), out var versionId))
                    {
                        findings[index] = new StoredFinding(code, versionId);
                    }
                }

                var decision = root["decision"] as JsonObject;
                var plan = root["signalPlan"] as JsonArray;
                return new StoredFindings
                {
                    Kind = kind,
                    ReasonCode = Text(root, "reasonCode"),
                    HasSignalPlan = plan is not null,
                    InvoiceId = invoiceId,
                    TrackingNumber = tracking,
                    ReceivedOn = DateOnly.TryParseExact(Text(root, "receivedOn"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on) ? on : null,
                    InvoiceName = Text(root, "invoiceName"),
                    ReliabilityThreshold = decision?["reliabilityThreshold"]?.GetValue<decimal>(),
                    DecidingIndexes = (decision?["deciding"] as JsonArray ?? []).Select(n => n!.GetValue<int>()).ToList(),
                    Findings = findings,
                    Plan = (plan ?? []).OfType<JsonObject>().Select(ParsePlanEntry).ToList(),
                    PipelineFindings = root["pipelineFindings"] as JsonArray ?? [],
                    CodingFindings = root["codingFindings"] as JsonArray ?? [],
                    CodeSet = root["codeSet"] as JsonObject,
                    InvoiceNumberReadConfidence = root["invoiceNumberReadConfidence"]?.GetValue<decimal>(),
                    ResolvedSet = root[ResolvedSetMember] switch
                    {
                        null => null,
                        JsonObject set => ResolvedSignalSet.Parse(set),
                        _ => throw new FormatException("The stored resolved set is not an object."),
                    },
                };
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
            {
                return null;
            }
        }

        private static StoredPlanEntry ParsePlanEntry(JsonObject entry)
        {
            var facts = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (name, value) in entry["facts"] as JsonObject ?? [])
            {
                facts[name] = value?.GetValue<string>();
            }

            var evidence = (entry["evidence"] as JsonArray ?? []).OfType<JsonObject>().Select(e =>
                new SignalEvidenceRef(Text(e, "kind")!, Text(e, "ref")!, Text(e, "tier")!, e["confidence"]?.GetValue<double>())
                {
                    Text = Text(e, "text"),
                    Source = Text(e, "source"),
                    AsOf = Text(e, "asOf"),
                    Clause = Text(e, "clause"),
                }).ToList();

            var request = new SignalWriteRequest(
                Guid.Parse(Text(entry, "policyId")!),
                Text(entry, "policyCode") ?? throw new FormatException("A plan entry has no policy code."),
                Guid.Parse(Text(entry, "policyVersionId")!),
                Text(entry, "subjectEntity") ?? throw new FormatException("A plan entry has no subject."),
                Guid.Parse(Text(entry, "subjectId")!),
                Text(entry, "subjectName"),
                Text(entry, "headline") ?? throw new FormatException("A plan entry has no headline."),
                entry["lane"]!.GetValue<int>(),
                entry["severity"]?.GetValue<int>(),
                Text(entry, "template") ?? throw new FormatException("A plan entry has no template."),
                facts,
                evidence);
            return new StoredPlanEntry(
                request,
                Text(entry, "fingerprint") ?? throw new FormatException("A plan entry has no fingerprint."),
                (entry["reasonCodes"] as JsonArray ?? []).Select(n => n!.GetValue<string>()).ToList(),
                Text(entry, "consequence"),
                Text(entry, "decision"),
                Text(entry, "sentence"));
        }
    }
}
