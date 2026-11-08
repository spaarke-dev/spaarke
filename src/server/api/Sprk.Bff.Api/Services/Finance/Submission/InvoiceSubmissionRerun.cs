// ebilling-r1 task 095d-2a-2 (EB-D-185..187, EB-D-189, EB-D-195): the rules re-run after Set firm, its evaluation and its ONE commit.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — the persister writes an intake once (its linked path writes nothing by contract); InvoiceCorrectionReevaluation patches one
//       corrected field's diff; Set firm (InvoiceAssignFirmService) applies the firm and leaves the three firm markers and the rerun-pending
//       marker in place for this re-run.
//   (2) Extension — none of them can take the job: the persister's "linked is never re-evaluated" invariant protects every committed intake,
//       the correction path judges one field against an unchanged rule set, and Set firm's 2-minute lock cannot hold a pipeline run. So this
//       composes them: StoredSubmissionRead (rebuild), InvoiceSubmissionProcessor.EvaluateFromReadAsync (steps 2-5), RerunLineCoding, and the
//       persister's builders (BuildFindingsJson, BuildLines, TerminalFor). It copies none of them.
//   (3) Cost of doing nothing — after Set firm the invoice stays held for ever: nothing else clears the firm markers or the rerun-pending
//       marker (095d-0), so no line decision, Override, approval or Reject can be made on it (EB-D-185).
//
// Placement (bff-extensions.md): BFF, Services/Finance/Submission (a service over the app-only IGenericEntityService; no endpoint, no package).
// The queued job, its handler and its DI registration are 095d-2b (Services/Ai/Jobs, uac-r2 Q7: the re-run can call AI coding).

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>What one re-run did (task 095d-2a-2). The 2b job maps it: <see cref="Refused"/> is permanent (Poisoned, admin attention);
/// <see cref="Busy"/>, <see cref="Changed"/> and <see cref="Failed"/> are transient (retry, the 503 "incomplete" style).</summary>
public enum InvoiceSubmissionRerunStatus
{
    /// <summary>This run committed: the new outcome, the reset lines, the review detail, the cleared marker and <c>rulesRerun</c> = completed.</summary>
    Completed,

    /// <summary>An earlier run committed (<c>rulesRerun</c> is completed): nothing was written (idempotent retry).</summary>
    AlreadyCompleted,

    /// <summary>Permanent: nothing was written, the hold and the marker stay, and the log carries <c>Permanent=true</c> (an operator matter).</summary>
    Refused,

    /// <summary>Another decision holds the invoice's locks: nothing was written; retry.</summary>
    Busy,

    /// <summary>A row changed between the evaluation and the commit, twice: nothing was written; retry.</summary>
    Changed,

    /// <summary>A read, the evaluation or the commit faulted (or the Procedures could not be resolved): nothing was written; retry.</summary>
    Failed,
}

/// <summary>The stable codes of <see cref="InvoiceSubmissionRerunResult.Code"/> (logs and the 2b job; never shown to a partner).</summary>
public static class InvoiceSubmissionRerunCodes
{
    public const string Completed = "ebilling.rerun.completed";
    public const string AlreadyCompleted = "ebilling.rerun.already_completed";

    /// <summary>The submission carries no stored Set-firm decision (<c>assignFirm</c>): a re-run without a decision is never run.</summary>
    public const string StoredDecisionMissing = "ebilling.rerun.stored_decision_missing";

    /// <summary>The decision named is not the one Set firm derives for this submission (<see cref="InvoiceAssignFirmService.DecisionIdFor"/>).</summary>
    public const string DecisionForeign = "ebilling.rerun.decision_foreign";

    /// <summary>The stored findings, or their <c>assignFirm</c> member, cannot be read.</summary>
    public const string FindingsUnreadable = "ebilling.rerun.findings_unreadable";

    /// <summary>The submission or the invoice does not name the firm the stored decision set.</summary>
    public const string FirmMismatch = "ebilling.rerun.firm_mismatch";

    /// <summary>The submission does not exist, or its invoice lookup is not the invoice it derives (a lookup to any other invoice never reaches a marker).</summary>
    public const string NotLinked = "ebilling.rerun.not_linked";

    /// <summary>The invoice does not carry the rerun-pending marker (<see cref="InvoiceFirmHold.InvoiceRerunPendingValue"/>).</summary>
    public const string MarkerMissing = "ebilling.rerun.marker_missing";

    /// <summary>A fee or expense line of the invoice is not one this submission's intake created
    /// (<see cref="InvoiceSubmissionPersister.LineIdFor"/>): the re-run never writes a row it does not own.</summary>
    public const string LineNotOwned = "ebilling.rerun.line_not_owned";

    /// <summary>The invoice's one core record is not the submission's (<see cref="EligibleApproverResolver.SingleCoreOf"/>).</summary>
    public const string CoreMismatch = "ebilling.rerun.core_mismatch";

    /// <summary>The submission row cannot be read as a submission (<see cref="SubmissionSnapshot.Read"/>).</summary>
    public const string SubmissionUnreadable = "ebilling.rerun.submission_unreadable";

    /// <summary>The commit does not fit one transaction, or the findings do not fit the column.</summary>
    public const string TooLarge = "ebilling.rerun.too_large";

    public const string Busy = "ebilling.rerun.busy";

    /// <summary>A row version changed under the commit after the one re-evaluation (fail closed: retry).</summary>
    public const string Changed = "ebilling.rerun.changed";

    public const string Unavailable = "ebilling.rerun.unavailable";

    /// <summary>The commit's outcome is unknown (no fault returned); the retry reads <c>rulesRerun</c> and never commits twice.</summary>
    public const string OutcomeUnknown = "ebilling.rerun.outcome_unknown";

    /// <summary>The evaluation faulted or gave no persistable outcome (an unresolved Procedure before the final delivery included).</summary>
    public const string EvaluationFailed = "ebilling.rerun.evaluation_failed";

    /// <summary>The persister's builders found the outcome inconsistent with the stored rows (a fault, never a partial commit).</summary>
    public const string OutcomeInconsistent = "ebilling.rerun.outcome_inconsistent";
}

/// <summary>The typed outcome of <see cref="InvoiceSubmissionRerun.RunAsync"/>.</summary>
/// <param name="Permanent">True for a refusal (logged <c>Permanent=true</c>): retrying gives the same answer.</param>
/// <param name="Kind">The new outcome's kind (Completed only; a would-be rejection is <see cref="SubmissionEvaluationKind.Blocked"/>).</param>
/// <param name="ReasonCode">The new stored reason code (Completed only).</param>
/// <param name="ResetLines">The lines whose earlier decision was reset to the re-run's derivation (EB-D-187).</param>
/// <param name="RecodedLines">The lines sent to AI coding because their code set changed (EB-D-186).</param>
/// <param name="ReviewDetail">The review detail written, or null (blank, or the column does not exist yet).</param>
/// <param name="RerunState">The stored <c>assignFirmRerun.state</c> (<see cref="InvoiceSubmissionRerun.StateCommitted"/> after this slice's
/// commit; 2b's after-commit steps move it on). Set on Completed and AlreadyCompleted.</param>
public sealed record InvoiceSubmissionRerunResult(
    InvoiceSubmissionRerunStatus Status,
    string Code,
    bool Permanent,
    Guid SubmissionId,
    SubmissionEvaluationKind? Kind = null,
    string? ReasonCode = null,
    int ResetLines = 0,
    int RecodedLines = 0,
    int? ReviewDetail = null,
    string? RerunState = null);

/// <summary>
/// Task 095d-2a-2: ONE re-run of a submission after Set firm. It reads the stored rows under their row versions, rebuilds the read
/// (<see cref="StoredSubmissionRead"/>), evaluates it with the firm through the pipeline's own steps 2-5
/// (<see cref="InvoiceSubmissionProcessor.EvaluateFromReadAsync"/>; AI coding only on lines whose code set changes, <see cref="RerunLineCoding"/>),
/// and commits the new outcome in ONE conditional transaction.
/// </summary>
/// <remarks>
/// <para><b>The commit</b> (every row <c>IfRowVersionMatches</c>, inside the invoice-decision lock and the line lock):</para>
/// <list type="bullet">
/// <item>the submission: the new reason code and terminal status (<see cref="InvoiceSubmissionPersister.TerminalFor(SubmissionEvaluationOutcome)"/>)
/// and the findings rebuilt by <see cref="InvoiceSubmissionPersister.BuildFindingsJson(SubmissionEvaluationOutcome, Guid)"/>: so the three firm
/// markers (the reason, the pipeline finding, every line cause) are REPLACED by the new outcome's state, never deleted in place, and
/// <c>rules.not_checked</c> is recomputed from the new refusals. Kept from the stored copy: <c>invoiceName</c> (the rebuilt header has no firm
/// name), <c>provenance</c> (what the analyzer read), <c>assignFirm</c> (only its <c>rulesRerun</c> becomes completed) and every member the
/// outcome does not derive (the correction members, the stored Override / Return sets). Dropped: the Override stamp (task 114, R14: a re-hold
/// removes it in the same write). Added: <see cref="StateMember"/>, the re-run's own state and the reset decisions as history;</item>
/// <item>the invoice: <see cref="InvoiceFirmHold.InvoiceHoldColumn"/> cleared (the rerun-pending marker; this commit is its ONLY clearer), and the
/// review detail derived from the holds that remain after this write (<see cref="InvoiceOpenHolds.ReasonsOf"/> over every linked submission with
/// this one's new state substituted), where the column exists and the invoice is In Review. Outside In Review (Returned to Firm, Superseded
/// while the re-run was pending) the detail is left out of the write: D-205 keeps it blank there, and a reopen derives it;</item>
/// <item>each fee and expense line: the resolution, coding and status columns <see cref="InvoiceSubmissionPersister.BuildLines"/> derives (never
/// a billed or corrected value). A line a reviewer decided before EB-D-185 is reset to that derivation (EB-D-187 / Q4): its decision columns are
/// cleared; its correction rows and Decision Records are kept as history.</item>
/// </list>
/// <para><b>Fail closed.</b> A refused, failed or partial re-run writes nothing, so the marker, the firm markers and the hold all stay
/// (uac-r2 condition 2). Refusals (facts missing, stored rows inconsistent, findings unreadable, no or a foreign stored decision, a firm or
/// marker mismatch, a line this submission does not own, a core record that differs, over the transaction ceiling) are logged
/// <c>Permanent=true</c>. A row version changed under the commit, or a line attached or removed since the read, re-evaluates once, then fails
/// for a retry. The Procedures run (uac-r2 condition 3): an unresolved Procedure before the final delivery fails the run (nothing written);
/// on the final delivery the processor's held outcome (<c>procedures_unresolved</c>, every line <c>rules.not_checked</c>) is a reviewable hold
/// and is committed like any other outcome.</para>
/// <para><b>EB-D-187.</b> A new hold (a Procedure gap, a rule finding) holds the invoice for review; a finding that would auto-reject holds it
/// (<see cref="SubmissionEvaluationKind.Blocked"/>), never rejects it. No Decision Record is written and the invoice status is never written.</para>
/// <para><b>Idempotent.</b> A run that finds <c>rulesRerun</c> completed writes nothing (<see cref="InvoiceSubmissionRerunStatus.AlreadyCompleted"/>).</para>
/// <para><b>Not here (095d-2b seams):</b> the queued job and its DI registration, the totals, the supersedes link, the rule and system Signal
/// re-map, the shared Signal's ConditionCleared close and its record link, the partner withhold, and moving <see cref="StateMember"/> on from
/// <see cref="StateCommitted"/>.</para>
/// <para><b>Logs (ADR-015):</b> ids, counts and codes only.</para>
/// </remarks>
public sealed partial class InvoiceSubmissionRerun
{
    /// <summary>The findings member that holds the re-run's own state (task 095d-2): <c>{ schema, decisionId, state, on, kind, reasonCode,
    /// recodedLines, resetLines: [{ line, lineId, previousStatus, previousApprovedAmount }] }</c>. Ids, codes and numbers only.</summary>
    internal const string StateMember = "assignFirmRerun";

    /// <summary><see cref="StateMember"/>'s state once the commit landed; 095d-2b's after-commit steps move it on.</summary>
    public const string StateCommitted = "committed";

    /// <summary>A version change under the commit re-evaluates this many times, then the run fails for a retry.</summary>
    internal const int MaxReEvaluations = 1;

    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

    /// <summary>The line columns <see cref="InvoiceSubmissionPersister.BuildLines"/> derives from the resolution, the coding and the findings
    /// (beside the extraction JSON). The commit writes each, clearing one the new derivation does not set.</summary>
    private static readonly string[] DerivedLineColumns =
    [
        InvoiceSubmissionPersister.LineResolvedTimekeeper, InvoiceSubmissionPersister.LineApprovedRate,
        InvoiceSubmissionPersister.LineCodingConfidence, InvoiceTotalsService.LineStatus,
    ];

    /// <summary>The columns a line decision writes (<c>InvoiceLineDecisionService.LineFields</c>); the reset clears them.</summary>
    private static readonly string[] DecisionLineColumns =
    [
        InvoiceTotalsService.LineApprovedAmount, InvoiceReviewReadService.LineReason, InvoiceReviewReadService.LineReviewedBy,
        InvoiceReviewReadService.LineReviewedOn,
    ];

    private static readonly string[] SubmissionColumns =
    [
        .. SubmissionSnapshot.Columns, InvoiceSubmissionPersister.SubmissionReasonCode, InvoiceSubmissionPersister.SubmissionFindingsJson,
        InvoiceTotalsService.VersionNumber,
    ];

    private static readonly string[] InvoiceColumns =
    [
        .. StoredSubmissionRead.InvoiceColumns, "statecode", "statuscode", InvoiceTotalsService.VersionNumber,
        InvoiceSubmissionPersister.InvoiceVendorOrg, InvoiceFirmHold.InvoiceHoldColumn,
        .. InvoiceFamilyCoreRecordGuard.CoreRecordLookups[FinanceBaseValues.InvoiceEntity].Select(l => l.Column),
    ];

    /// <summary>The line columns the pre-commit re-check reads: which fee and expense rows the invoice has.</summary>
    private static readonly string[] LineSetColumns = [InvoiceTotalsService.LineCostType];

    private static readonly string[] LineColumns =
    [
        .. StoredSubmissionRead.LineColumns, InvoiceTotalsService.LineStatus, InvoiceTotalsService.LineApprovedAmount, InvoiceTotalsService.VersionNumber,
    ];

    /// <summary>BuildLines' owner answer for rows that are never created here: it sets no owner (only the derived columns are taken).</summary>
    private static readonly RecordOwnerResolution DerivedOnly = new(RecordOwnerOutcome.Unchanged, null, null, null);

    private readonly IGenericEntityService _entities;
    private readonly InvoiceSubmissionProcessor _processor;
    private readonly ILineCodingService _coding;
    private readonly Func<IReadOnlyList<InvoiceLineToCode>, CancellationToken, Task<ResolvedCodeSet?>> _firmCodeSet;
    private readonly IIdempotencyService _locks;
    private readonly ILogger<InvoiceSubmissionRerun> _logger;
    private readonly TimeProvider _time;

    /// <summary>The after-commit Signal steps' writer calls (095d-2b-2, <see cref="RemapSignalsAsync"/>); null in a test that runs the commit only.</summary>
    private readonly RerunSignalSeams? _signals;

    /// <summary>Whether <see cref="InvoiceReviewDetail.Column"/> exists (the process-wide probe; a test sets its own).</summary>
    internal ReviewDetailColumnProbe ReviewDetailProbe { get; init; } = ReviewDetailColumnProbe.Shared;

    public InvoiceSubmissionRerun(
        IGenericEntityService entities,
        InvoiceSubmissionProcessor processor,
        ILineCodingService coding,
        CodeSetResolver codeSets,
        IIdempotencyService locks,
        SignalWriter signals,
        BillingDecisionRecorder recorder,
        ILogger<InvoiceSubmissionRerun> logger)
        : this(entities, processor, coding, (codeSets ?? throw new ArgumentNullException(nameof(codeSets))).ResolveFirmCodesAsync, locks, logger, null,
            RerunSignalSeams.Of(entities, signals, recorder))
    {
    }

    /// <summary>The test seam: FR-22 step 1 alone is passed as a call (<see cref="CodeSetResolver.ResolveFirmCodesAsync"/>).</summary>
    internal InvoiceSubmissionRerun(
        IGenericEntityService entities,
        InvoiceSubmissionProcessor processor,
        ILineCodingService coding,
        Func<IReadOnlyList<InvoiceLineToCode>, CancellationToken, Task<ResolvedCodeSet?>> firmCodeSet,
        IIdempotencyService locks,
        ILogger<InvoiceSubmissionRerun> logger,
        TimeProvider? time,
        RerunSignalSeams? signals = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _coding = coding ?? throw new ArgumentNullException(nameof(coding));
        _firmCodeSet = firmCodeSet ?? throw new ArgumentNullException(nameof(firmCodeSet));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
        _signals = signals;
    }

    /// <summary>
    /// Runs the re-run of <paramref name="submissionId"/> for the Set-firm decision <paramref name="decisionId"/>. See the class remarks.
    /// </summary>
    /// <param name="finalDelivery">The job's last delivery (passed to the evaluation; the Procedures' unresolved hold is still a failed re-run).</param>
    public async Task<InvoiceSubmissionRerunResult> RunAsync(Guid submissionId, Guid decisionId, bool finalDelivery, CancellationToken ct = default)
    {
        // R10: the decision is the one Set firm derives for this submission; any other id is never run (nothing is read or written for it).
        if (submissionId == Guid.Empty || decisionId == Guid.Empty || decisionId != InvoiceAssignFirmService.DecisionIdFor(submissionId))
        {
            return Refuse(submissionId, InvoiceSubmissionRerunCodes.DecisionForeign);
        }

        // The invoice is derived from the submission id, never read from the submission's lookup (which is checked against it below).
        var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(submissionId);
        for (var attempt = 0; ; attempt++)
        {
            var (plan, early) = await PrepareAsync(submissionId, invoiceId, decisionId, finalDelivery, ct).ConfigureAwait(false);
            if (early is not null)
            {
                return early;
            }

            var result = await CommitAsync(plan!, ct).ConfigureAwait(false);
            if (result.Status == InvoiceSubmissionRerunStatus.Changed && attempt < MaxReEvaluations)
            {
                _logger.LogInformation("Re-run of submission {SubmissionId}: a row changed under the commit; nothing was written, evaluating again.", submissionId);
                continue;
            }

            return result;
        }
    }

    // ── Read and evaluate (no write) ──

    /// <summary>Everything the commit sends, built before any write.</summary>
    private sealed record Plan(
        Guid SubmissionId, Guid InvoiceId, IReadOnlyList<OrganizationRequest> Requests, SubmissionEvaluationKind Kind, string? ReasonCode,
        int ResetLines, int RecodedLines, int? ReviewDetail, IReadOnlySet<Guid> LineIds);

    private sealed record State(
        Entity? Submission, Entity? Invoice, IReadOnlyList<Entity> Lines, IReadOnlySet<(Guid? LineId, string Field)> Verified,
        IReadOnlyList<Entity> OtherSubmissions);

    private async Task<(Plan? Plan, InvoiceSubmissionRerunResult? Result)> PrepareAsync(
        Guid submissionId, Guid invoiceId, Guid decisionId, bool finalDelivery, CancellationToken ct)
    {
        State state;
        try
        {
            state = await ReadAsync(submissionId, invoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (null, Transient(submissionId, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.Unavailable, "read", ex));
        }

        // ── The gate: the stored decision, the firm and the marker. Each refusal writes nothing. ──
        if (state.Submission is not { } submission
            || submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id is not { } link
            || !InvoiceBlockedHold.IsOwnSubmission(link, submissionId))
        {
            return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.NotLinked));
        }

        var json = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        JsonObject root;
        AssignFirmStoredDecision? stored;
        try
        {
            root = (string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject) ?? throw new FormatException("The findings are not an object.");
            stored = AssignFirmStoredDecision.Read(root);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.FindingsUnreadable));
        }

        if (stored is null)
        {
            return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.StoredDecisionMissing));
        }

        if (stored.DecisionId != decisionId)
        {
            return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.DecisionForeign));
        }

        if (string.Equals(stored.RulesRerun, InvoiceAssignFirmService.RulesRerunCompleted, StringComparison.Ordinal))
        {
            var rerunState = StoredStateOf(root);
            _logger.LogInformation("Re-run of submission {SubmissionId}: already completed ({State}); nothing written.", submissionId,
                KnownStates.Contains(rerunState ?? string.Empty) ? rerunState : "unknown");
            return (null, new InvoiceSubmissionRerunResult(
                InvoiceSubmissionRerunStatus.AlreadyCompleted, InvoiceSubmissionRerunCodes.AlreadyCompleted, false, submissionId, RerunState: rerunState));
        }

        if (!string.Equals(stored.RulesRerun, InvoiceAssignFirmService.RulesRerunPending, StringComparison.Ordinal))
        {
            return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.FindingsUnreadable));
        }

        if (submission.GetAttributeValue<EntityReference>(TrackingNumberAllocator.FirmColumn)?.Id != stored.FirmId)
        {
            return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.FirmMismatch));
        }

        if (state.Invoice is not { } invoice)
        {
            return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.NotLinked));
        }

        if (invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)?.Id != stored.FirmId)
        {
            return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.FirmMismatch));
        }

        if (InvoiceFirmHold.StateOf(invoice) != InvoiceFirmHold.InvoiceHoldState.RerunPending)
        {
            return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.MarkerMissing));
        }

        // Outside In Review (Returned to Firm, Superseded while the re-run was pending) the re-run still commits; only the review detail is
        // left out (D-205: blank outside In Review; a reopen derives it).
        var inReview = invoice.GetAttributeValue<OptionSetValue>("statecode")?.Value == InvoiceStatusCodes.StateActive
            && invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value == InvoiceStatusCodes.InReview;

        // Task 165a fix round 2: an invoice already Returned to Firm keeps its submission's Return stamp through the re-run (FindingsAfter).
        var returnedToFirm = invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value == InvoiceStatusCodes.ReturnedToFirm;

        if (VersionOf(submission) is not { } submissionVersion || VersionOf(invoice) is not { } invoiceVersion)
        {
            return (null, Transient(submissionId, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.Unavailable, "version", null));
        }

        var (snapshot, snapshotFailure) = SubmissionSnapshot.Read(submission);
        if (snapshot is null || snapshot.FirmId != stored.FirmId)
        {
            _logger.LogError("Re-run of submission {SubmissionId}: the submission cannot be read as a submission ({Reason}).", submissionId, snapshotFailure);
            return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.SubmissionUnreadable));
        }

        // A2: the invoice's ONE core record is the submission's. The lines are written for that record; another record is never reached.
        if (EligibleApproverResolver.SingleCoreOf(FinanceBaseValues.InvoiceEntity, invoice) is not { } core
            || core.Entity != snapshot.CoreRecord.EntityLogicalName || core.Id != snapshot.CoreRecord.RecordId)
        {
            return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.CoreMismatch, FinanceBaseValues.InvoiceEntity, invoiceId));
        }

        // A1: every fee and expense row is one this submission's intake created (its id derives from the submission and its sequence). A row
        // attached by anything else is never written; the rebuild's count check would miss one swapped in at the same sequence.
        foreach (var line in state.Lines)
        {
            if (line.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value is (int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense
                && (line.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) is not { } sequence || sequence < 1
                    || line.Id != InvoiceSubmissionPersister.LineIdFor(submissionId, sequence - 1)))
            {
                return (null, Refuse(submissionId, InvoiceSubmissionRerunCodes.LineNotOwned, InvoiceSubmissionPersister.LineEntity, line.Id));
            }
        }

        // ── Rebuild (EB-D-189: no document read) and evaluate with the firm (steps 2-5, the pipeline's own). ──
        var rebuilt = StoredSubmissionRead.Rebuild(submissionId, json, invoice, state.Lines, state.Verified, _logger);
        if (rebuilt.RefusalReason is { } refusal)
        {
            // Rebuild logged it Permanent=true with the reason code; nothing is written and the hold stays.
            return (null, new InvoiceSubmissionRerunResult(InvoiceSubmissionRerunStatus.Refused, refusal, true, submissionId));
        }

        var coding = new RerunLineCoding(_coding, _firmCodeSet, rebuilt.Coding!, _logger);
        SubmissionEvaluationOutcome outcome;
        try
        {
            outcome = await _processor.EvaluateFromReadAsync(snapshot, rebuilt.Read!, finalDelivery, coding, rebuilt.Verified, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (null, Transient(submissionId, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.EvaluationFailed, "evaluate", ex));
        }

        // Before the final delivery an unresolved Procedure propagates (caught above: nothing written). On the final delivery the processor
        // returns its held outcome (procedures_unresolved, no rule result, every line rules.not_checked): a reviewable hold, committed as the
        // re-run's outcome (uac-r2 condition 2: procedures_unresolved through InvoiceOpenHolds), so it alone has no evaluation or decision.
        if (outcome.Kind is not (SubmissionEvaluationKind.Accepted or SubmissionEvaluationKind.AcceptedUncoded
                or SubmissionEvaluationKind.Blocked or SubmissionEvaluationKind.AutoRejected)
            || outcome.RulesSkippedForNoFirm
            || (!outcome.RulesSkippedForUnresolvedProcedures && (outcome.Evaluation is null || outcome.Decision is null)))
        {
            return (null, Transient(submissionId, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.EvaluationFailed, outcome.ReasonCode ?? outcome.Kind.ToString(), null));
        }

        // EB-D-187 (Q5): a finding that would auto-reject holds the invoice for review instead (the D-101 held set); nothing is rejected here.
        outcome = HeldInsteadOfRejected(outcome);

        var writeDetail = inReview && await ReviewDetailProbe.IsPresentAsync(_entities, _logger, ct).ConfigureAwait(false);
        var input = new PlanInput(
            submissionId, invoiceId, root, stored, outcome, state, submissionVersion, invoiceVersion, coding.RecodedLineCount, writeDetail, returnedToFirm);
        try
        {
            return BuildPlan(input);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            // K3: the builders found the outcome inconsistent with the stored rows. A typed failure, nothing written.
            return (null, Transient(submissionId, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.OutcomeInconsistent, "plan", ex));
        }
    }

    /// <summary>EB-D-187: a would-be automatic rejection is held for review (Blocked), as the Internal channel already classifies it.</summary>
    internal static SubmissionEvaluationOutcome HeldInsteadOfRejected(SubmissionEvaluationOutcome outcome) =>
        outcome.Kind == SubmissionEvaluationKind.AutoRejected
            ? outcome with { Kind = SubmissionEvaluationKind.Blocked, ReasonCode = SubmissionReasonCodes.Blocked }
            : outcome;

    private sealed record PlanInput(
        Guid SubmissionId, Guid InvoiceId, JsonObject Root, AssignFirmStoredDecision Stored, SubmissionEvaluationOutcome Outcome, State State,
        string SubmissionVersion, string InvoiceVersion, int RecodedLines, bool WriteReviewDetail, bool InvoiceReturnedToFirm);

    // ── The commit (built before any write) ──

    private (Plan? Plan, InvoiceSubmissionRerunResult? Result) BuildPlan(PlanInput input)
    {
        var outcome = input.Outcome;
        var terminal = InvoiceSubmissionPersister.TerminalFor(outcome);

        // The lines: what BuildLines derives for the new outcome, matched to the stored rows by their sequence (the rebuild checked 1..N).
        var stored = input.State.Lines
            .Where(l => l.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value is (int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense)
            .ToDictionary(l => l.GetAttributeValue<int>(InvoiceSubmissionPersister.LineSequence) - 1);
        var derived = InvoiceSubmissionPersister.BuildLines(
            outcome, input.InvoiceId, outcome.Submission!.CoreRecord, new Dictionary<string, object>(), DerivedOnly, correlationId: null);
        if (derived.Count != stored.Count || stored.Values.Any(l => VersionOf(l) is null))
        {
            return (null, Transient(input.SubmissionId, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.Unavailable, "lines", null));
        }

        // The ceiling: one transaction or none (the submission, the invoice and every line), refused before anything is sent.
        if (2 + derived.Count > InvoiceSubmissionPersister.MaxRequestsPerTransaction)
        {
            return (null, Refuse(input.SubmissionId, InvoiceSubmissionRerunCodes.TooLarge));
        }

        var lineRequests = new List<OrganizationRequest>(derived.Count);
        var reset = new JsonArray();
        for (var index = 0; index < derived.Count; index++)
        {
            var (request, resetEntry) = LineUpdate(index, derived[index], stored[index]);
            lineRequests.Add(request);
            if (resetEntry is not null)
            {
                reset.Add(resetEntry);
            }
        }

        var findings = FindingsAfter(input, reset).ToJsonString();
        if (findings.Length > InvoiceSubmissionPersister.FindingsJsonMaxLength)
        {
            return (null, Refuse(input.SubmissionId, InvoiceSubmissionRerunCodes.TooLarge));
        }

        var submission = new Entity(FinanceBaseValues.SubmissionEntity, input.SubmissionId)
        {
            RowVersion = input.SubmissionVersion,
            [InvoiceSubmissionPersister.SubmissionReasonCode] = terminal.ReasonCode,
            [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)terminal.Status),
            [InvoiceSubmissionPersister.SubmissionFindingsJson] = findings,
        };

        var (invoice, detail) = InvoiceAfter(input, terminal.ReasonCode, findings);
        List<OrganizationRequest> requests =
        [
            new UpdateRequest { Target = submission, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches },
            new UpdateRequest { Target = invoice, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches },
            .. lineRequests,
        ];

        return (new Plan(
            input.SubmissionId, input.InvoiceId, requests, outcome.Kind, terminal.ReasonCode, reset.Count, input.RecodedLines, detail,
            stored.Values.Select(l => l.Id).ToHashSet()), null);
    }

    /// <summary>
    /// The invoice in the commit: the rerun-pending marker cleared (the ONLY place it is cleared, in the same transaction that writes
    /// <c>rulesRerun</c> = completed), and the review detail derived from the holds that remain after this write (ontology D-205 writer 4).
    /// The derivation is <see cref="InvoiceOpenHolds.ReasonsOf"/> over the state this write leaves: the firm as read, the invoice's own hold
    /// cleared by this write, this submission's new reason code and findings, and every other linked submission as read (not
    /// <see cref="InvoiceOpenHolds.ReasonsAfterAsync"/>, which re-reads the invoice's own hold: this write clears it).
    /// </summary>
    private static (Entity Invoice, int? Detail) InvoiceAfter(PlanInput input, string? reasonCode, string findings)
    {
        var invoice = new Entity(FinanceBaseValues.InvoiceEntity, input.InvoiceId)
        {
            RowVersion = input.InvoiceVersion,
            [InvoiceFirmHold.InvoiceHoldColumn] = null,
        };

        int? detail = null;
        if (input.WriteReviewDetail)
        {
            detail = InvoiceReviewDetail.ReviewDetailOf(InvoiceOpenHolds.ReasonsOf(
                InvoiceFirmHold.HasNoFirm(input.State.Invoice!),
                invoiceHoldSet: false,
                [
                    new InvoiceOpenHolds.StoredSubmission(reasonCode, findings),
                    .. input.State.OtherSubmissions.Select(s => new InvoiceOpenHolds.StoredSubmission(
                        s.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode),
                        s.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))),
                ]));
            invoice[InvoiceReviewDetail.Column] = InvoiceReviewDetail.ValueOf(detail);
        }

        return (invoice, detail);
    }

    /// <summary>
    /// One line's update: the derived columns (cleared where the new derivation sets none), its extraction JSON as the new derivation writes it
    /// (with the exact numbers intake stored kept), and, for a line a reviewer decided, the decision columns cleared (EB-D-187 / Q4). Conditioned
    /// on the version read.
    /// </summary>
    private static (OrganizationRequest Request, JsonObject? Reset) LineUpdate(int index, Entity derived, Entity stored)
    {
        var update = new Entity(InvoiceSubmissionPersister.LineEntity, stored.Id) { RowVersion = VersionOf(stored) };
        foreach (var column in DerivedLineColumns)
        {
            update[column] = derived.Attributes.TryGetValue(column, out var value) ? value : null;
        }

        // BuildLines leaves the column out when the JSON is over its 100,000 characters; then the stored copy is not replaced either.
        if (derived.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExtractionConfidence) is { } extraction)
        {
            update[InvoiceSubmissionPersister.LineExtractionConfidence] =
                WithStoredExactNumbers(extraction, stored.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExtractionConfidence));
        }

        JsonObject? reset = null;
        var status = stored.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value;
        if (status is (int)InvoiceLineStatus.Accepted or (int)InvoiceLineStatus.Reduced or (int)InvoiceLineStatus.Disallowed)
        {
            foreach (var column in DecisionLineColumns)
            {
                update[column] = null;
            }

            reset = new JsonObject
            {
                ["line"] = index,
                ["lineId"] = stored.Id.ToString("D"),
                ["previousStatus"] = ((InvoiceLineStatus)status.Value).ToString(),
                ["previousApprovedAmount"] = stored.GetAttributeValue<Money>(InvoiceTotalsService.LineApprovedAmount)?.Value,
            };
        }

        return (new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches }, reset);
    }

    /// <summary>The new extraction JSON with the exact numbers intake stored (<see cref="ExtractionProvenance.ExactMember"/>) kept: the new
    /// derivation has a firm, so it would not write them, and they are what this re-run judged.</summary>
    private static string WithStoredExactNumbers(string derived, string? stored)
    {
        var next = JsonNode.Parse(derived)!.AsObject();
        if (string.IsNullOrWhiteSpace(stored) || JsonNode.Parse(stored) is not JsonObject previous
            || previous["fields"] is not JsonObject before || next["fields"] is not JsonObject after)
        {
            return derived;
        }

        foreach (var name in ExtractionProvenance.ExactLineFieldNames)
        {
            if (before[name] is JsonObject was && was.ContainsKey(ExtractionProvenance.ExactMember) && after[name] is JsonObject now)
            {
                now[ExtractionProvenance.ExactMember] = was[ExtractionProvenance.ExactMember]?.DeepClone();
            }
        }

        return next.ToJsonString();
    }

    /// <summary>
    /// The submission's findings after the re-run: the members the new outcome derives (<see cref="InvoiceSubmissionPersister.BuildFindingsJson(SubmissionEvaluationOutcome, Guid)"/>)
    /// replace the stored ones, so the three firm markers are replaced by the new state; the Override stamp is dropped, and the Return stamp too unless
    /// the invoice is already Returned to Firm; every other member is kept, with <c>assignFirm.rulesRerun</c> = completed and the re-run's own state added.
    /// </summary>
    private JsonObject FindingsAfter(PlanInput input, JsonArray reset)
    {
        var fresh = JsonNode.Parse(InvoiceSubmissionPersister.BuildFindingsJson(input.Outcome, input.InvoiceId, out var planRefusals))!.AsObject();
        if (planRefusals.Count > 0)
        {
            _logger.LogWarning("Re-run of submission {SubmissionId}: {Count} Signal plan entries the writer would refuse are left out of the plan.",
                input.SubmissionId, planRefusals.Count);
        }

        var root = (JsonObject)input.Root.DeepClone();
        var invoiceName = root["invoiceName"]?.DeepClone();
        foreach (var (name, value) in fresh)
        {
            // Kept as stored: the invoice name (the rebuilt header has no firm name) and what the analyzer read.
            if (name is "invoiceName" or ExtractionProvenance.Member)
            {
                continue;
            }

            root[name] = value?.DeepClone();
        }

        if (!fresh.ContainsKey(InvoiceSubmissionPersister.SignalPlanRefusalsMember))
        {
            root.Remove(InvoiceSubmissionPersister.SignalPlanRefusalsMember);
        }

        // Task 114 (R14): a re-hold removes the Override stamp in the same write; with the stamp gone InvoiceProcedureHold.IsReleased is false.
        // Task 165a: the Return stamp too, so the recorded outcome this commit writes is the only release InvoiceBlockedHold reads. Fix round 2:
        // except on an invoice already Returned to Firm, where the Return stands (the invoice stays returned), so its stamp is kept and the
        // submission still reads Returned.
        root.Remove(SubmissionOverrideService.OverrideMember);
        if (!input.InvoiceReturnedToFirm)
        {
            root.Remove(SubmissionOverrideService.ReturnMember);
        }

        // The invoice-level Signal plan entries name the invoice as intake named it (the stored invoice name), not the rebuilt header's.
        if (invoiceName is JsonValue storedName && storedName.TryGetValue<string>(out var display))
        {
            foreach (var entry in (root["signalPlan"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(e => StoredSubmissionRead.Text(e, "subjectEntity") == FinanceBaseValues.InvoiceEntity))
            {
                entry["subjectName"] = display;
            }
        }

        // EB-D-187 (Q8): the only change to the stored decision; BuildDecision's hashed values are unchanged.
        ((JsonObject)root[InvoiceAssignFirmService.StoredDecisionMember]!)["rulesRerun"] = InvoiceAssignFirmService.RulesRerunCompleted;
        root[StateMember] = new JsonObject
        {
            ["schema"] = 1,
            ["decisionId"] = input.Stored.DecisionId.ToString("D"),
            ["state"] = StateCommitted,
            ["on"] = _time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["kind"] = input.Outcome.Kind.ToString(),
            ["reasonCode"] = input.Outcome.ReasonCode,
            ["recodedLines"] = input.RecodedLines,
            ["resetLines"] = reset,
        };
        return root;
    }

    // ── Send ──

    private async Task<InvoiceSubmissionRerunResult> CommitAsync(Plan plan, CancellationToken ct)
    {
        var owner = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var decisionKey = SubmissionOverrideService.LockKey(plan.InvoiceId);
        var lineKey = InvoiceLineDecisionService.LockKeyFor(plan.InvoiceId);
        if (await AcquireAsync(decisionKey, owner, plan.SubmissionId, ct).ConfigureAwait(false) is { } first)
        {
            return first;
        }

        try
        {
            if (await AcquireAsync(lineKey, owner, plan.SubmissionId, ct).ConfigureAwait(false) is { } second)
            {
                return second;
            }

            try
            {
                // K1: inside the locks, the invoice still has exactly the fee and expense rows the plan was built from. A row attached or
                // removed since the read (the row versions do not see either) re-evaluates; the marker is never cleared over it.
                if (!(await LineIdsAsync(plan.InvoiceId, ct).ConfigureAwait(false)).SetEquals(plan.LineIds))
                {
                    return Transient(plan.SubmissionId, InvoiceSubmissionRerunStatus.Changed, InvoiceSubmissionRerunCodes.Changed, "lines", null);
                }

                await _entities.ExecuteTransactionAsync(plan.Requests, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsVersionConflict(ex))
            {
                return Transient(plan.SubmissionId, InvoiceSubmissionRerunStatus.Changed, InvoiceSubmissionRerunCodes.Changed, "commit", null);
            }
            catch (DataverseTransactionException ex) when (ex.OutcomeUnknown)
            {
                return Transient(plan.SubmissionId, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.OutcomeUnknown, "commit", ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Transient(plan.SubmissionId, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.Unavailable, "commit", ex);
            }
            finally
            {
                await ReleaseAsync(lineKey, owner, plan.SubmissionId).ConfigureAwait(false);
            }
        }
        finally
        {
            await ReleaseAsync(decisionKey, owner, plan.SubmissionId).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Re-run of submission {SubmissionId}: committed ({Kind}, {ReasonCode}); {LineCount} lines, {ResetCount} decisions reset, {RecodedCount} re-coded; marker cleared, rulesRerun completed.",
            plan.SubmissionId, plan.Kind, plan.ReasonCode, plan.Requests.Count - 2, plan.ResetLines, plan.RecodedLines);
        return new InvoiceSubmissionRerunResult(
            InvoiceSubmissionRerunStatus.Completed, InvoiceSubmissionRerunCodes.Completed, false, plan.SubmissionId, plan.Kind, plan.ReasonCode,
            plan.ResetLines, plan.RecodedLines, plan.ReviewDetail, StateCommitted);
    }

    private async Task<InvoiceSubmissionRerunResult?> AcquireAsync(string key, string owner, Guid submissionId, CancellationToken ct)
    {
        bool acquired;
        try
        {
            acquired = await _locks.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Transient(submissionId, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.Unavailable, "lock", ex);
        }

        return acquired ? null : Transient(submissionId, InvoiceSubmissionRerunStatus.Busy, InvoiceSubmissionRerunCodes.Busy, "lock", null);
    }

    private async Task ReleaseAsync(string key, string owner, Guid submissionId)
    {
        try
        {
            await _locks.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Re-run of submission {SubmissionId}: a processing lock could not be released ({ExceptionType}); it expires.", submissionId, ex.GetType().Name);
        }
    }

    // ── Reads (app-only, IGenericEntityService) ──

    private async Task<State> ReadAsync(Guid submissionId, Guid invoiceId, CancellationToken ct)
    {
        var submissionQuery = new QueryExpression(FinanceBaseValues.SubmissionEntity) { ColumnSet = new ColumnSet(SubmissionColumns), TopCount = 1 };
        submissionQuery.Criteria.AddCondition($"{FinanceBaseValues.SubmissionEntity}id", ConditionOperator.Equal, submissionId);
        var submission = (await _entities.RetrieveMultipleAsync(submissionQuery, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        if (submission is null)
        {
            return new State(null, null, [], new HashSet<(Guid?, string)>(), []);
        }

        var invoiceQuery = new QueryExpression(FinanceBaseValues.InvoiceEntity) { ColumnSet = new ColumnSet(InvoiceColumns), TopCount = 1 };
        invoiceQuery.Criteria.AddCondition(FinanceBaseValues.InvoiceKeyColumn, ConditionOperator.Equal, invoiceId);
        var invoice = (await _entities.RetrieveMultipleAsync(invoiceQuery, ct).ConfigureAwait(false)).Entities.FirstOrDefault();

        var lineQuery = new QueryExpression(InvoiceSubmissionPersister.LineEntity)
        {
            ColumnSet = new ColumnSet(LineColumns),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 },
        };
        lineQuery.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
        lineQuery.AddOrder($"{InvoiceSubmissionPersister.LineEntity}id", OrderType.Ascending);
        var lines = await InvoiceReviewReadService.ReadPagedAsync(_entities, lineQuery, ct).ConfigureAwait(false);

        // The fields a reviewer corrected or confirmed, as InvoiceCorrectionReevaluation reads them (every Corrected or Confirmed row).
        var correctionQuery = new QueryExpression(InvoiceLineDecisionService.CorrectionEntity)
        {
            ColumnSet = new ColumnSet("sprk_invoiceline", "sprk_fieldname", "sprk_outcometype"),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 },
        };
        correctionQuery.Criteria.AddCondition("sprk_invoice", ConditionOperator.Equal, invoiceId);
        correctionQuery.Criteria.AddCondition("sprk_outcometype", ConditionOperator.In, InvoiceCorrectionService.OutcomeCorrected, InvoiceCorrectionService.OutcomeConfirmed);
        correctionQuery.AddOrder($"{InvoiceLineDecisionService.CorrectionEntity}id", OrderType.Ascending);
        var verified = (await InvoiceReviewReadService.ReadPagedAsync(_entities, correctionQuery, ct).ConfigureAwait(false))
            .Where(r => r.GetAttributeValue<OptionSetValue>("sprk_outcometype")?.Value is InvoiceCorrectionService.OutcomeCorrected or InvoiceCorrectionService.OutcomeConfirmed
                && !string.IsNullOrEmpty(r.GetAttributeValue<string>("sprk_fieldname")))
            .Select(r => (r.GetAttributeValue<EntityReference>("sprk_invoiceline")?.Id, r.GetAttributeValue<string>("sprk_fieldname")))
            .ToHashSet();

        // Every other submission linked to the invoice: the review detail derives from all of them, as approval's holds do.
        var othersQuery = new QueryExpression(FinanceBaseValues.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(InvoiceSubmissionPersister.SubmissionInvoice, InvoiceSubmissionPersister.SubmissionReasonCode, InvoiceSubmissionPersister.SubmissionFindingsJson),
        };
        othersQuery.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.Equal, invoiceId);
        var others = (await _entities.RetrieveMultipleAsync(othersQuery, ct).ConfigureAwait(false)).Entities.Where(s => s.Id != submissionId).ToList();

        return new State(submission, invoice, lines, verified, others);
    }

    /// <summary>The ids of the invoice's fee and expense rows as stored now (the pre-commit re-check, K1).</summary>
    private async Task<HashSet<Guid>> LineIdsAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceSubmissionPersister.LineEntity)
        {
            ColumnSet = new ColumnSet(LineSetColumns),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 },
        };
        query.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
        query.AddOrder($"{InvoiceSubmissionPersister.LineEntity}id", OrderType.Ascending);
        return (await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false))
            .Where(l => l.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value is (int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense)
            .Select(l => l.Id)
            .ToHashSet();
    }

    // ── Helpers ──

    /// <summary>The <see cref="StateMember"/> states this code knows; any other stored value is logged as "unknown" (it is row data).</summary>
    private static readonly HashSet<string> KnownStates = new(StringComparer.Ordinal) { StateCommitted, StateDone };

    /// <summary>The stored re-run state (<see cref="StateMember"/>'s <c>state</c>), or null when absent.</summary>
    private static string? StoredStateOf(JsonObject root) => StoredSubmissionRead.Text(root[StateMember] as JsonObject, "state");

    /// <summary>The row version as the persister reads it: the <c>versionnumber</c> column, else <see cref="Entity.RowVersion"/>.</summary>
    private static string? VersionOf(Entity row) =>
        row.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var v) && v > 0 ? v.ToString(CultureInfo.InvariantCulture)
        : string.IsNullOrWhiteSpace(row.RowVersion) ? null : row.RowVersion;

    private static bool IsVersionConflict(Exception ex) =>
        ex is System.Data.DBConcurrencyException || (ex is not OperationCanceledException && DataverseServiceClientImpl.IsRowVersionMismatch(ex));

    /// <summary>A permanent refusal: nothing written, the hold and the marker stay, logged <c>Permanent=true</c> (operator, not retry).</summary>
    private InvoiceSubmissionRerunResult Refuse(Guid submissionId, string code)
    {
        _logger.LogError("Re-run of submission {SubmissionId}: refused ({Code}); nothing is written and the hold stays (Permanent={Permanent}).",
            submissionId, code, true);
        return new InvoiceSubmissionRerunResult(InvoiceSubmissionRerunStatus.Refused, code, true, submissionId);
    }

    /// <summary>A permanent refusal that names the row it is about: the table and the row id only (never a person or partner text).</summary>
    private InvoiceSubmissionRerunResult Refuse(Guid submissionId, string code, string table, Guid rowId)
    {
        _logger.LogError("Re-run of submission {SubmissionId}: refused ({Code}) at {Table} {RowId}; nothing is written and the hold stays (Permanent={Permanent}).",
            submissionId, code, table, rowId, true);
        return new InvoiceSubmissionRerunResult(InvoiceSubmissionRerunStatus.Refused, code, true, submissionId);
    }

    /// <summary>A transient failure: nothing written, the hold and the marker stay; the job retries. The log names the fault's type only.</summary>
    private InvoiceSubmissionRerunResult Transient(Guid submissionId, InvoiceSubmissionRerunStatus status, string code, string step, Exception? fault)
    {
        _logger.LogWarning("Re-run of submission {SubmissionId}: {Status} at {Step} ({Code}, {FaultType}); nothing is written and the hold stays.",
            submissionId, status, step, code, fault?.GetType().Name);
        return new InvoiceSubmissionRerunResult(status, code, false, submissionId);
    }
}
