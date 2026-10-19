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
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// <see cref="IInvoiceSubmissionAfterSteps"/>: steps 7 to 9 of the Processing Pipeline, in this order (D-130 a, D-122):
/// <list type="number">
/// <item><b>Signals.</b> The rule Signals from the stored plan (<see cref="InvoiceSubmissionPersister.SignalPlanJson"/>) and the system
/// Signals (D-123 / D-125) mapped from the stored pipeline and coding findings under <c>BILL-SYSTEM-SUBMISSION</c>, each through the
/// option-a lifecycle (notes section 6).</item>
/// <item><b>Decision Record</b>, AutoRejected only, after the Signals (D-122 moved it there).</item>
/// <item><b>Indexing</b> (Accepted, AcceptedUncoded, Blocked).</item>
/// <item><b>Notification</b>, once per submission (an idempotency marker).</item>
/// <item><b>Terminal status</b>, last: the persister's <see cref="SubmissionPersistResult.TerminalStatus"/> and reason code.</item>
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
    private const string VersionAlias = "v";

    internal static readonly string[] SubmissionColumns =
    [
        InvoiceSubmissionPersister.SubmissionFindingsJson, InvoiceSubmissionPersister.SubmissionInvoice, "sprk_channel", "sprk_document",
        InvoiceSubmissionPersister.SubmissionCorrelationId,
    ];

    // The "indexing needs DocumentIntelligence:Enabled" note (map row 9) is logged once per process.
    private static int _indexingNoteLogged;

    private readonly IGenericEntityService _entities;
    private readonly IIdempotencyService _idempotency;
    private readonly IEbillingNotificationSink _sink;
    private readonly ILogger<InvoiceSubmissionAfterSteps> _logger;
    private readonly Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>> _writeSignal;
    private readonly Func<Guid, string, string, CancellationToken, Task<SignalCloseOutcome>> _supersedeSignal;
    private readonly Func<BillingDecision, CancellationToken, Task<BillingDecisionRecordResult>> _record;
    private readonly Action<BillingDecision> _preflight;
    private readonly Func<JobContract, CancellationToken, Task> _submitJob;

    public InvoiceSubmissionAfterSteps(
        IGenericEntityService entities,
        SignalWriter signals,
        BillingDecisionRecorder recorder,
        JobSubmissionService jobs,
        IIdempotencyService idempotency,
        IEbillingNotificationSink sink,
        ILogger<InvoiceSubmissionAfterSteps> logger)
        : this(
            entities,
            idempotency,
            sink,
            logger,
            (signals ?? throw new ArgumentNullException(nameof(signals))).WriteAsync,
            (id, rowVersion, policyCode, ct) => signals.CloseAsync(id, rowVersion, SignalWriter.ResolutionSuperseded, policyCode, ct),
            (recorder ?? throw new ArgumentNullException(nameof(recorder))).RecordAsync,
            recorder.Preflight,
            (jobs ?? throw new ArgumentNullException(nameof(jobs))).SubmitJobAsync)
    {
    }

    /// <summary>The seam the unit tests use: the ontology-owned writers and the recorder are concrete, so they are passed as their calls.</summary>
    internal InvoiceSubmissionAfterSteps(
        IGenericEntityService entities,
        IIdempotencyService idempotency,
        IEbillingNotificationSink sink,
        ILogger<InvoiceSubmissionAfterSteps> logger,
        Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>> writeSignal,
        Func<Guid, string, string, CancellationToken, Task<SignalCloseOutcome>> supersedeSignal,
        Func<BillingDecision, CancellationToken, Task<BillingDecisionRecordResult>> record,
        Action<BillingDecision> preflight,
        Func<JobContract, CancellationToken, Task> submitJob)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _writeSignal = writeSignal ?? throw new ArgumentNullException(nameof(writeSignal));
        _supersedeSignal = supersedeSignal ?? throw new ArgumentNullException(nameof(supersedeSignal));
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
        _submitJob = submitJob ?? throw new ArgumentNullException(nameof(submitJob));
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
            || persisted.InvoiceId is not { } invoiceId || invoiceId == Guid.Empty || persisted.TerminalStatus is not { } terminal)
        {
            throw new ArgumentException($"A {persisted.Status} result carries no committed invoice; the after-commit steps need one.", nameof(persisted));
        }

        var submissionId = persisted.SubmissionId;

        // ── The stored state: the resume marker (the link) and the findings it was committed with ──
        Entity? submission;
        try
        {
            submission = await _entities.RetrieveAsync(FinanceBaseValues.SubmissionEntity, submissionId, SubmissionColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecordNotFound(ex))
        {
            submission = null;
        }

        if (submission is null)
        {
            _logger.LogInformation("Invoice submission {SubmissionId}: the row no longer exists; no after-commit step ran", submissionId);
            return new SubmissionAfterStepsResult { Status = SubmissionAfterStepsStatus.SubmissionMissing, SubmissionId = submissionId };
        }

        if (submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id != invoiceId)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: it does not link invoice {InvoiceId}; no after-commit step ran", submissionId, invoiceId);
            return Stop(SubmissionAfterStepsStatus.Refused, submissionId, invoiceId, InvoiceLinkMismatchCode);
        }

        var stored = StoredFindings.Parse(submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson));
        if (stored is null || stored.InvoiceId != invoiceId)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: its findings JSON is missing, unreadable or names another invoice; no after-commit step ran",
                submissionId);
            return Stop(SubmissionAfterStepsStatus.Refused, submissionId, invoiceId, FindingsUnreadableCode);
        }

        // ── 1. Signals ──
        var requests = new List<(SignalWriteRequest Request, string Fingerprint)>();
        foreach (var entry in stored.Plan)
        {
            requests.Add((entry.Request, entry.Fingerprint));
        }

        requests.AddRange(await SystemSignalsAsync(stored, submissionId, invoiceId, ct).ConfigureAwait(false));

        var written = 0;
        string? signalFailure = null;
        foreach (var (request, fingerprint) in requests)
        {
            var step = await WriteWithLifecycleAsync(request, fingerprint, submissionId, ct).ConfigureAwait(false);
            if (step.Wrote)
            {
                written++;
            }

            // The rest are still written (each is idempotent); the run stops before the Decision Record.
            signalFailure ??= step.FailureCode;
        }

        if (signalFailure is not null)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: {Failed} of {Count} Signals did not complete ({Code}); it stays Processing for a retry",
                submissionId, requests.Count - written, requests.Count, signalFailure);
            return Stop(SubmissionAfterStepsStatus.Retry, submissionId, invoiceId, signalFailure) with { SignalsWritten = written };
        }

        // ── 2. Decision Record: the automatic rejection only (Blocked and Accepted get none; 105's Override / Return record those) ──
        Guid? recordId = null;
        if (stored.Kind == SubmissionEvaluationKind.AutoRejected)
        {
            var decision = BuildRejectionDecision(stored, submissionId, invoiceId);
            if (decision is null)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: an automatic rejection with no deciding finding in its findings JSON", submissionId);
                return Stop(SubmissionAfterStepsStatus.Refused, submissionId, invoiceId, FindingsUnreadableCode) with { SignalsWritten = written };
            }

            try
            {
                var recorded = await _record(decision, ct).ConfigureAwait(false);
                recordId = recorded.RecordId;
                _logger.LogInformation("Invoice submission {SubmissionId}: automatic rejection of invoice {InvoiceId} recorded as {RecordId} (already recorded: {Already})",
                    submissionId, invoiceId, recorded.RecordId, recorded.AlreadyRecorded);
            }
            catch (DecisionRecordRefusedException ex)
            {
                var permanent = PermanentRecordRefusals.Contains(ex.Reason);
                _logger.LogWarning("Invoice submission {SubmissionId}: the automatic rejection's Decision Record was refused ({Reason}); {Next}",
                    submissionId, ex.Reason, permanent ? "a retry cannot succeed" : "it stays Processing for a retry");
                return Stop(permanent ? SubmissionAfterStepsStatus.Refused : SubmissionAfterStepsStatus.Retry, submissionId, invoiceId, ex.Reason)
                    with { SignalsWritten = written };
            }
        }

        if (totalsRetry)
        {
            // The totals failed after the commit: the Signals and the record are written; the redelivery re-runs the totals, then the rest.
            return Stop(SubmissionAfterStepsStatus.Retry, submissionId, invoiceId, persisted.ReasonCode ?? InvoiceSubmissionPersister.TotalsFailedCode)
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
        await NotifyOnceAsync(submission, submissionId, invoiceId, terminal, persisted.TerminalReasonCode, ct).ConfigureAwait(false);

        // ── 5. Terminal status, LAST (D-130 a): Accepted / Rejected means complete ──
        await _entities.UpdateAsync(FinanceBaseValues.SubmissionEntity, submissionId, new Dictionary<string, object>
        {
            [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)terminal),
            [InvoiceSubmissionPersister.SubmissionReasonCode] = persisted.TerminalReasonCode!,
        }, ct).ConfigureAwait(false);

        _logger.LogInformation("Invoice submission {SubmissionId}: after-commit steps complete for invoice {InvoiceId} ({Kind}): {Written} Signals, status {Status} ({ReasonCode})",
            submissionId, invoiceId, stored.Kind, written, terminal, persisted.TerminalReasonCode);
        return new SubmissionAfterStepsResult
        {
            Status = SubmissionAfterStepsStatus.Completed,
            SubmissionId = submissionId,
            InvoiceId = invoiceId,
            TerminalStatus = terminal,
            DecisionRecordId = recordId,
            SignalsWritten = written,
        };
    }

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

    private readonly record struct SignalStep(bool Wrote, string? FailureCode);

    /// <summary>
    /// The option-a lifecycle (notes section 6) for one (policy, subject): the latest episode by the dedupe-key prefix, its status and its
    /// stored reason-set fingerprint decide: none → episode 1; open + same → refresh it; open + different → Superseded, then episode n+1;
    /// resolved + same → skip (a resume never re-raises what was resolved); resolved + different → episode n+1. A skipped write
    /// (<see cref="SignalWriteResult.SkippedRefusalCode"/>) or an open Signal that changed while being superseded is a failure: retry.
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
    /// disabled, or with no Active version in force on the received date: no system Signal, one Warning (the policy code only), and the
    /// findings stay in the findings JSON (D-125: an administrator may turn the policy off).
    /// </summary>
    private async Task<IReadOnlyList<(SignalWriteRequest Request, string Fingerprint)>> SystemSignalsAsync(
        StoredFindings stored, Guid submissionId, Guid invoiceId, CancellationToken ct)
    {
        if (!stored.HasSystemFindings)
        {
            return [];
        }

        var (rule, state) = await ReadSystemPolicyAsync(stored.ReceivedOn, ct).ConfigureAwait(false);
        if (rule is null)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: system billing policy {PolicyCode} is {State}; no system Signal is written, the findings stay in the findings JSON",
                submissionId, BillingRuleCatalogue.SystemPolicyCode, state);
            return [];
        }

        var policy = new BillingSystemPolicyVersion(rule.PolicyId, rule.PolicyCode, rule.PolicyVersionId);
        var findings = stored.SystemFindings(policy);
        var lines = findings.Where(f => f.Subject.LineIndex is not null).Select(f => f.Subject.LineIndex!.Value).Distinct().ToList();
        var subjects = new BillingSignalSubjects(
            invoiceId,
            stored.InvoiceName,
            lines.ToDictionary(i => i, i => InvoiceSubmissionPersister.LineIdFor(submissionId, i)),
            lines.ToDictionary(i => i, i => InvoiceSubmissionPersister.LineNameFor(stored.TrackingNumber, i)));
        return BillingFindingSignalMapper.MapAll(findings, new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule }, subjects)
            .Select(m => (m.Request, m.Fingerprint))
            .ToList();
    }

    /// <summary>
    /// One read: the policy by <c>sprk_policycode</c> (billing domain) with its current version joined. Usable only when exactly one such
    /// policy exists, it is enabled and Active, and its current version is Active, of the SubmissionIntegrity type and in force on
    /// <paramref name="receivedOn"/> (<see cref="BillingRuleCandidate.IsInForceOn"/>, the 076 K3 rule the evaluator applies).
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
        var version = query.AddLink("sprk_policyversion", "sprk_currentversion", "sprk_policyversionid", JoinOperator.LeftOuter);
        version.EntityAlias = VersionAlias;
        version.Columns = new ColumnSet("statecode", "sprk_ruletype", "sprk_rulebody", "sprk_messagetemplate", "sprk_shortheadline", "sprk_consequence",
            "sprk_inforcefrom", "sprk_inforceto", "sprk_versionnumber");

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

        if (policy.GetAttributeValue<EntityReference>("sprk_currentversion") is not { } current || current.Id == Guid.Empty
            || Aliased<OptionSetValue>(policy, "statecode")?.Value != StateActive
            || Aliased<OptionSetValue>(policy, "sprk_ruletype")?.Value != SubmissionIntegrityOption)
        {
            return (null, "without_an_active_version");
        }

        var rule = new BillingRuleCandidate
        {
            PolicyId = policy.Id,
            PolicyCode = BillingRuleCatalogue.SystemPolicyCode,
            PolicyVersionId = current.Id,
            // The system policy belongs to no rule set (D-125); these two are never read for it (no D-39 selection, no handler).
            RuleSetId = Guid.Empty,
            Scope = RuleSetScope.ClientDefault,
            RuleType = SubmissionIntegrityOption.ToString(CultureInfo.InvariantCulture),
            RuleBody = Aliased<string>(policy, "sprk_rulebody"),
            MessageTemplate = Aliased<string>(policy, "sprk_messagetemplate"),
            ShortHeadline = Aliased<string>(policy, "sprk_shortheadline"),
            InForceFrom = UtcDate(Aliased<DateTime?>(policy, "sprk_inforcefrom")),
            InForceTo = UtcDate(Aliased<DateTime?>(policy, "sprk_inforceto")),
            IsCurrent = true,
            VersionNumber = Aliased<int?>(policy, "sprk_versionnumber") ?? 0,
            Lane = policy.GetAttributeValue<OptionSetValue>("sprk_lane")?.Value,
            Severity = policy.GetAttributeValue<OptionSetValue>("sprk_severity")?.Value,
            Consequence = Aliased<OptionSetValue>(policy, "sprk_consequence")?.Value,
        };

        return receivedOn is { } date && rule.IsInForceOn(date) ? (rule, "in_force") : (null, "without_a_version_in_force");
    }

    private static T? Aliased<T>(Entity row, string column) =>
        row.GetAttributeValue<AliasedValue>($"{VersionAlias}.{column}")?.Value is T value ? value : default;

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
    /// <para><b>ResolvedSignalIds is empty.</b> D-122 (resolve every open Signal on the rejected invoice) is NOT implemented: no path exists to
    /// close a Signal as resolved by a billing decision (SignalWriter.CloseAsync takes system resolutions only; the commit route's close is
    /// private; the recorder leaves closing to its caller), and listing Signals the record cannot close would half-resolve them. Notes
    /// section 13, open item 1.</para>
    /// </summary>
    internal static BillingDecision? BuildRejectionDecision(StoredFindings stored, Guid submissionId, Guid invoiceId)
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
            ResolvedSignalIds: null,
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
                return new StoredFindings
                {
                    Kind = kind,
                    InvoiceId = invoiceId,
                    TrackingNumber = tracking,
                    ReceivedOn = DateOnly.TryParseExact(Text(root, "receivedOn"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on) ? on : null,
                    InvoiceName = Text(root, "invoiceName"),
                    ReliabilityThreshold = decision?["reliabilityThreshold"]?.GetValue<decimal>(),
                    DecidingIndexes = (decision?["deciding"] as JsonArray ?? []).Select(n => n!.GetValue<int>()).ToList(),
                    Findings = findings,
                    Plan = (root["signalPlan"] as JsonArray ?? []).OfType<JsonObject>().Select(ParsePlanEntry).ToList(),
                    PipelineFindings = root["pipelineFindings"] as JsonArray ?? [],
                    CodingFindings = root["codingFindings"] as JsonArray ?? [],
                    CodeSet = root["codeSet"] as JsonObject,
                    InvoiceNumberReadConfidence = root["invoiceNumberReadConfidence"]?.GetValue<decimal>(),
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
