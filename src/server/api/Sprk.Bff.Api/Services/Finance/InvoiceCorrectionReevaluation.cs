using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using static Sprk.Bff.Api.Services.Finance.InvoiceFindingsDiff;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>A named step of the correction follow-through failed (B2b-1): the correction service answers 503 Incomplete with this <c>failedStep</c>.</summary>
internal sealed class CorrectionStepException(string step, Exception inner)
    : Exception($"The correction step '{step}' did not finish.", inner)
{
    public string Step { get; } = step;
}

/// <summary>The totals step found the invoice's totals refused (fix round, part 2: a concurrent adjustment changed them after the pre-write dry-run):
/// the correction service answers 409 <see cref="CorrectionCodes.TotalsRefused"/>, not the 503 Incomplete that no repeat can clear.</summary>
internal sealed class CorrectionTotalsRefusedException(string refusalCode)
    : Exception($"The invoice totals were refused ({refusalCode}).")
{
    public string RefusalCode { get; } = refusalCode;
}

/// <summary>Task 095c-4: the re-evaluation found the invoice without a firm (the firm's rules cannot be re-run). The correction service answers 409
/// <see cref="CorrectionCodes.FirmMissing"/>, not the 503 Incomplete: no repeat clears it until the firm is set (095d). Replaces the former
/// <see cref="InvalidOperationException"/> "The invoice names no firm.".</summary>
internal sealed class CorrectionFirmMissingException()
    : Exception("The invoice names no firm, so its billing rules cannot be re-run.");

/// <summary>082c C12 (F1-low): the corrected invoice has a Procedure gap its stored findings do not (a correction moved work onto a date with no
/// terms in force on the record). The correction service answers 409 <see cref="CorrectionCodes.ProcedureGap"/> with a fixed sentence, not the 503
/// Incomplete whose advice (repeat the same request) can never clear a gap. Carries no id and no message to show: the text is fixed.</summary>
internal sealed class CorrectionProcedureGapException()
    : Exception("The corrected invoice has a Procedure gap its stored findings do not; the findings are not patched.");

/// <summary>082d fix round 2 (K): the patched findings are over <c>sprk_findingsjson</c>'s cap even without the Procedure snapshot. The correction service
/// answers 409 <see cref="CorrectionCodes.FindingsTooLarge"/> (the value is saved, the findings are not patched; the correction stays unapplied and holds
/// approval), not the 503 Incomplete whose advice (repeat) can never clear it. Carries no message to show.</summary>
internal sealed class CorrectionFindingsTooLargeException()
    : Exception("The patched findings are over the findings column; nothing is written.");

/// <summary>
/// The follow-through of a saved correction (ebilling-r1 task 101 part B2b-1; FR-25, FR-31, D-126): re-runs 072's billing rules in memory on the
/// invoice before and after the correction, patches the stored findings (<c>sprk_invoicesubmission.sprk_findingsjson</c>) with the difference, and
/// re-derives the status of the lines the difference touched. Wired into <see cref="InvoiceCorrectionService"/>'s seam (<c>onValueWritten</c>).
/// </summary>
/// <remarks>
/// <para><b>Deterministic diff.</b> The facts are loaded once (<see cref="BillingFactLoader.LoadAsync"/>) and the evaluator, which is pure, runs twice:
/// on the request rebuilt from the stored rows with the corrected field at its PRE-correction value, and with its final value. The pre-correction value
/// is the <c>sprk_finalvalue</c> of the last Corrected / Confirmed row of the field whose findings patch landed (listed in
/// <see cref="PatchedCorrectionsMember"/>; fix round, part 3), else the first row's <c>sprk_aivalue</c>. A field that has a
/// Corrected or Confirmed row counts as verified (confidence 1.0) on the side where the reviewer has seen it. Both sides are rebuilt the same way, so a
/// fact the stored rows do not keep (a timekeeper's unmatched reason, an observation's quoted text) is the same on both sides and cannot make a
/// difference; only findings that depend on the corrected field change.</para>
/// <para><b>Patch (findings step).</b> <c>findings[]</c>: per (version, subject, line, reason) the entries that differ are replaced (same
/// <c>index</c> kept, 073's decision members kept), added (next free index) or removed. <c>signalPlan[]</c>: per (policy, subject) the entry is
/// replaced, added or removed when the mapped group differs. A code correction to an allowed code removes that line's refused-code and
/// firm-code-mismatch entries of the corrected kind from <c>codingFindings[]</c> (the entry goes when nothing is left) and a <c>coding.not_returned</c>
/// entry from <c>lineFindings[]</c>; a narrative that is no longer blank removes <c>line.no_description</c>; <c>rules.not_checked</c> is never removed.
/// A line a reviewer has decided (Accepted, Reduced, Disallowed) is never touched: its entries are left as stored. The Signals are not written here
/// (B2b-2): what B2b-2 must close or raise is recorded in <see cref="PendingSignalChangesMember"/>.</para>
/// <para><b>Status step.</b> Each touched, undecided line, and each line an earlier landed patch left waiting (<see cref="PendingStatusLinesMember"/>),
/// takes <see cref="InvoiceSubmissionPersister.LineStatusFor"/> over the PATCHED findings: Flagged when any <c>findings[]</c>, <c>lineFindings[]</c> or
/// <c>codingFindings[]</c> entry names it, else Clean when it has a code set, else Pending.</para>
/// <para><b>Retry.</b> Every step is idempotent: the diff depends on the stored correction rows only, a patch already applied produces the same JSON
/// (no write; a plan entry already in place records no Signal change again), and a status already set is not written again. Each write is conditioned on
/// the row version read in the step, so a concurrent change fails the step and the caller's repeat converges. A failure is
/// <see cref="CorrectionStepException"/> naming the step.</para>
/// <para><b>Followed through (fix round, part 3).</b> After the last step (findings, status, Signals, totals, as the field has them), a final
/// row-version-conditional write lists the field's Corrected / Confirmed rows in <see cref="AppliedCorrectionsMember"/> and takes the lines its status step
/// re-derived off <see cref="PendingStatusLinesMember"/>; a failure there is <see cref="CorrectionCodes.ReevaluationStep"/>. A row not listed holds the
/// approval (<see cref="InvoiceApprovalService"/>) and the decision of its line (<see cref="InvoiceLineDecisionService"/>) until the correction is
/// repeated.</para>
/// <para><b>Signals step (B2b-2).</b> After the status step, <see cref="PendingSignalChangesMember"/> is reconciled: a <c>rule</c> entry closes the
/// open Signal whose fingerprint is <c>closeFingerprint</c> as ConditionCleared (record-free, <see cref="SignalWriter.ResolutionConditionCleared"/>)
/// and writes the current plan entry through <see cref="SignalEpisodeLifecycle"/> with <see cref="ResolvedEpisodeRule.ReopenAsNextEpisode"/> when
/// <c>raiseFingerprint</c> is set (equal fingerprints: a refresh, no close); a <c>system</c> entry closes or refreshes that line's system Signal from
/// the system findings re-mapped from the patched JSON. The D-125 coding hold clears when no undecided line lacks a code set: its
/// <c>pipelineFindings[]</c> entry goes and the invoice's system Signal is closed (or refreshed when another invoice-level system finding remains).
/// The rule reconcile is <see cref="InvoiceRuleSignalReconciler"/> (task 164a-4b: shared with Add line's Signals step, moved here unchanged).
/// The member is removed (and the hold entry with it) in one row-version-conditional write after every Signal is done, so a failure part-way leaves
/// it for the repeat; every close and write is idempotent. Then any undecided line still Pending with a recorded code set is re-derived.</para>
/// <para><b>073 presentation.</b> Both sides of the plan diff are mapped with 073's STORED decisions (each finding's <c>consequence</c> /
/// <c>rejects</c> / <c>heldBecause</c>) and the stored outcome kind, so a group 073 held keeps its Held / Blocked presentation. A stored entry
/// presented Blocked (the invoice is held) is never replaced or removed by a correction that would present it otherwise: it is kept and marked
/// <see cref="CorrectionReviewMember"/>, and no Signal change is recorded for it. A replacement the writer would refuse is recorded in
/// <see cref="InvoiceSubmissionPersister.SignalPlanRefusalsMember"/>.</para>
/// <para><b>Currency (fix round, part 2).</b> A Currency correction is re-evaluated like any other header field: the rules re-run with the new currency
/// (RequiredData's not-USD finding, and the D-20 / D-115 skip of the money rules), through the same diff. The correction service writes the new currency to
/// the undecided lines in its value transaction (the persister copies the header currency onto every line), so no line carries a currency of its own and
/// there is no per-line currency finding.</para>
/// <para><b>Not here.</b> 073 itself: a correction of an In Review invoice does not re-decide it (new entries carry the rule's consequence and do not reject).</para>
/// <para><b>The diff (task 164a-3).</b> The request builder (<see cref="InvoiceFindingsDiff.RequestBasis"/>, a correction basis here) and the findings and
/// plan patch (<see cref="InvoiceFindingsDiff.PatchFindings"/>, <see cref="InvoiceFindingsDiff.PatchPlan"/>, <see cref="InvoiceFindingsDiff.SubjectsOf"/>)
/// live in <see cref="InvoiceFindingsDiff"/>, shared with Add line; this class reads, sequences the steps and writes.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="InvoiceSubmissionPersister"/> builds the findings JSON for a
/// whole submission and <see cref="InvoiceSubmissionAfterSteps"/> writes its Signals; neither patches a stored copy. Extension: the persister's
/// per-entry builders and status rule are reused (B2a); the patch is a different write (one invoice's stored copy after a reviewer's change), so it
/// is not folded into the 049 pipeline, whose suites stay unchanged. Cost of doing nothing: after a correction the findings and the line status stay
/// as first evaluated, so a fixed value still shows its finding and the line stays Flagged (FR-25, prototype answer 3).</para>
/// </remarks>
public sealed class InvoiceCorrectionReevaluation
{
    /// <summary>
    /// The findings JSON member that tells B2b-2 which Signals to close and which to raise: <c>{ "rule": [ { policyCode, subjectEntity, subjectId,
    /// closeFingerprint, raiseFingerprint } ], "system": [ { line, kind, reason } ] }</c>. A <c>rule</c> entry exists per (policy, subject) whose
    /// <c>signalPlan[]</c> entry a correction changed since B2b-2 last ran: <c>closeFingerprint</c> is the fingerprint of the plan entry stored before
    /// the FIRST such correction (null when none was stored), <c>raiseFingerprint</c> the fingerprint now in the plan (null when the group no longer
    /// fires). A <c>system</c> entry names a system finding removed from <c>codingFindings[]</c> (<c>reason</c> <c>refused</c> or
    /// <c>firmCodeMismatch</c>, <c>kind</c> Task / Activity / Expense). No value is stored. B2b-2 clears the member when it has reconciled.
    /// </summary>
    public const string PendingSignalChangesMember = "pendingSignalChanges";

    /// <summary>B2b-2: set to <c>true</c> on a stored <c>signalPlan[]</c> entry presented Blocked that a correction would have presented otherwise; the
    /// entry (and its Signal) is kept as stored, so the hold stays visible until the blocked submission is overridden or returned.</summary>
    public const string CorrectionReviewMember = "correctionReview";

    /// <summary>
    /// B2c: the member of a fee line's extraction JSON written by its latest timekeeper or date correction:
    /// <c>{ field, value, before: { timekeeperId, approvedRate }, original: { timekeeperId, approvedRate }, after: { timekeeperReason, rateStatus, rateReason } }</c>
    /// (<c>value</c> is the correction's final value; <c>original</c>, fix round part 3, is the pair before the line's FIRST timekeeper or date correction). The
    /// stored columns hold the new roster match and rate but no reason, so the facts are rebuilt from this: every rebuild of the line reads the reasons in
    /// <c>after</c>, and the "before" side of a timekeeper or date correction's diff starts from <see cref="AppliedResolutionsMember"/>, else <c>original</c>
    /// with the reasons the first evaluation stored.
    /// </summary>
    internal const string ResolutionChangeMember = "resolutionChange";

    /// <summary>
    /// Fix round, part 3 (F-2): the findings JSON member listing the correction rows (ids, <c>D</c> format) whose WHOLE follow-through has finished
    /// (findings, status, Signals and totals, as the field has them): <c>[ "id", ... ]</c>. A final row-version-conditional write after the last step adds every
    /// Corrected / Confirmed row of the corrected field (on its line); a repeat adds nothing. A Corrected / Confirmed row of a followed-through field that is not
    /// listed has not been followed through: <see cref="InvoiceApprovalService"/> refuses to approve the invoice, and <see cref="InvoiceLineDecisionService"/>
    /// to decide its line, until the correction is repeated (<see cref="ReadUnappliedAsync"/>).
    /// </summary>
    public const string AppliedCorrectionsMember = "appliedCorrections";

    /// <summary>
    /// Fix round, part 3 (F-2): the findings JSON member listing the correction rows whose findings PATCH has landed: <c>[ "id", ... ]</c>, written in the same
    /// row-version-conditional write as the patch. It says which value the stored findings reflect (the diff's "before" side); it is not the guard's list
    /// (<see cref="AppliedCorrectionsMember"/>), because a patch can land while a later step fails.
    /// </summary>
    internal const string PatchedCorrectionsMember = "patchedCorrections";

    /// <summary>
    /// Fix round, part 3 (F-2): the findings JSON member listing the line indexes (<c>[ 2, 5 ]</c>) a landed findings patch touched whose status has not been
    /// re-derived since. The patch adds its touched lines when it changes the findings; the status step of any later correction re-derives them as well, and
    /// the final write of a correction removes the lines its status step re-derived. A correction diffed from a newer value than the one whose status step
    /// failed would otherwise never re-derive the lines that earlier change touched.
    /// </summary>
    internal const string PendingStatusLinesMember = "pendingStatusLines";

    /// <summary>
    /// Fix round, part 3 (F-2): the findings JSON member keeping, per fee line (key: the line index), the roster match, approved rate and reasons the stored
    /// findings were last patched with by a timekeeper or date correction: <c>{ "2": { timekeeperId, approvedRate, timekeeperReason, rateStatus, rateReason } }</c>.
    /// Written with the patch (like <see cref="PatchedCorrectionsMember"/>, it says what the stored findings reflect). The "before" side of the next timekeeper
    /// or date correction of that line starts from it, so a correction whose findings step never ran is still diffed.
    /// </summary>
    internal const string AppliedResolutionsMember = "appliedResolutions";

    /// <summary>A Corrected / Confirmed correction row whose follow-through has not finished (fix round, part 3).</summary>
    internal sealed record UnappliedCorrection(Guid RowId, Guid? LineId, string Field);

    private static readonly HashSet<string> Reevaluated = new(StringComparer.Ordinal)
    {
        ResolutionFieldNames.Hours, ResolutionFieldNames.Rate, ResolutionFieldNames.Amount, BillingFieldNames.Narrative,
        BillingFieldNames.TaskCode, BillingFieldNames.ActivityCode, BillingFieldNames.ExpenseCode,
        BillingFieldNames.FirmInvoiceNumber, BillingFieldNames.BillingPeriodStart, BillingFieldNames.BillingPeriodEnd,
        ResolutionFieldNames.Date, ResolutionFieldNames.TimekeeperAsBilled, BillingFieldNames.InvoiceDate, BillingFieldNames.Currency,
    };

    /// <summary>B2c: a correction of one of these changes a billed figure, so the invoice totals are recomputed (016).</summary>
    private static readonly HashSet<string> TotalsFields = new(StringComparer.Ordinal)
    {
        ResolutionFieldNames.Amount, ResolutionFieldNames.Hours, ResolutionFieldNames.Rate, ResolutionFieldNames.TotalDue,
    };

    private static readonly Dictionary<string, (string Member, string Kind)> CodeFields = new(StringComparer.Ordinal)
    {
        [BillingFieldNames.TaskCode] = ("task", "Task"),
        [BillingFieldNames.ActivityCode] = ("activity", "Activity"),
        [BillingFieldNames.ExpenseCode] = ("expense", "Expense"),
    };

    private static readonly string[] InvoiceColumns =
    [
        InvoiceSubmissionPersister.InvoiceVendorOrg, InvoiceReviewService.InvInvoiceNumber, InvoiceReviewService.InvInvoiceDate,
        InvoiceSubmissionPersister.InvoiceBillingPeriodStart, InvoiceSubmissionPersister.InvoiceBillingPeriodEnd, InvoiceSubmissionPersister.InvoiceCurrency,
        "sprk_matter", "sprk_project", "sprk_regardingworkassignment",
    ];

    private readonly IGenericEntityService _entities;
    private readonly Func<BillingEvaluationRequest, CancellationToken, Task<BillingLoadedFacts>> _load;
    private readonly Func<BillingEvaluationRequest, BillingLoadedFacts, BillingEvaluationResult> _evaluate;
    private readonly ILogger<InvoiceCorrectionReevaluation> _logger;
    private readonly SignalEpisodeLifecycle _lifecycle;
    private readonly Func<Guid, string, int, string, CancellationToken, Task<SignalCloseOutcome>> _closeSignal;
    private readonly Func<DateOnly?, CancellationToken, Task<(BillingRuleCandidate? Rule, string State)>> _systemPolicy;
    private readonly Func<Guid, CancellationToken, Task<InvoiceTotalsCalculation>>? _recalculate;
    private readonly JobSubmissionService? _jobs;

    public InvoiceCorrectionReevaluation(
        IGenericEntityService entities, BillingFactLoader loader, InvoiceComplianceEvaluator evaluator, SignalWriter signals,
        IInvoiceTotalsService totals, ILogger<InvoiceCorrectionReevaluation> logger, JobSubmissionService? jobs = null)
        : this(
            entities,
            (loader ?? throw new ArgumentNullException(nameof(loader))).LoadAsync,
            (evaluator ?? throw new ArgumentNullException(nameof(evaluator))).Evaluate,
            (signals ?? throw new ArgumentNullException(nameof(signals))).WriteAsync,
            (id, rowVersion, resolution, policyCode, ct) => signals.CloseAsync(id, rowVersion, resolution, policyCode, ct),
            logger,
            recalculate: (totals ?? throw new ArgumentNullException(nameof(totals))).RecalculateAsync,
            jobs: jobs)
    {
    }

    /// <summary>The unit-test seam: the fact load, the in-memory evaluation and the Signal writer's write and system close are passed as calls;
    /// <paramref name="systemPolicy"/> defaults to 049's read (<see cref="InvoiceSubmissionAfterSteps.ReadSystemPolicyAsync(IGenericEntityService, DateOnly?, CancellationToken)"/>).</summary>
    internal InvoiceCorrectionReevaluation(
        IGenericEntityService entities,
        Func<BillingEvaluationRequest, CancellationToken, Task<BillingLoadedFacts>> load,
        Func<BillingEvaluationRequest, BillingLoadedFacts, BillingEvaluationResult> evaluate,
        Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>> writeSignal,
        Func<Guid, string, int, string, CancellationToken, Task<SignalCloseOutcome>> closeSignal,
        ILogger<InvoiceCorrectionReevaluation> logger,
        Func<DateOnly?, CancellationToken, Task<(BillingRuleCandidate? Rule, string State)>>? systemPolicy = null,
        Func<Guid, CancellationToken, Task<InvoiceTotalsCalculation>>? recalculate = null,
        JobSubmissionService? jobs = null)
    {
        _jobs = jobs; // null (the test seam only): no re-index is queued
        _recalculate = recalculate; // null (the test seam only): the totals step does not run
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _closeSignal = closeSignal ?? throw new ArgumentNullException(nameof(closeSignal));
        ArgumentNullException.ThrowIfNull(writeSignal);
        _lifecycle = new SignalEpisodeLifecycle(
            entities, writeSignal, (id, rowVersion, policyCode, ct) => closeSignal(id, rowVersion, SignalWriter.ResolutionSuperseded, policyCode, ct), logger);
        _systemPolicy = systemPolicy ?? ((receivedOn, ct) => InvoiceSubmissionAfterSteps.ReadSystemPolicyAsync(entities, receivedOn, ct));
    }

    /// <summary>
    /// The seam body: the findings step, the status step, the Signals step, the totals step (as the field has them), then the final write that records the
    /// correction as followed through (<see cref="AppliedCorrectionsMember"/>). Throws <see cref="CorrectionStepException"/> naming the step that failed (the
    /// final write is <see cref="CorrectionCodes.ReevaluationStep"/>).
    /// </summary>
    internal async Task ApplyAsync(CorrectionApplied applied, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(applied);
        var reevaluate = Reevaluated.Contains(applied.Field);
        var totals = TotalsFields.Contains(applied.Field);
        if (!reevaluate && !totals)
        {
            _logger.LogInformation("Correction of {Field} on invoice {InvoiceId}: nothing follows it.", applied.Field, applied.InvoiceId);
            return;
        }

        IReadOnlyList<Guid>? followed = null; // the field's rows this run follows through, as read BEFORE its steps
        IReadOnlySet<int>? derived = null;    // the lines the status step re-derived
        if (reevaluate)
        {
            Patched patched;
            try
            {
                patched = await PatchFindingsAsync(applied, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is CorrectionFirmMissingException or CorrectionProcedureGapException or CorrectionFindingsTooLargeException)
            {
                throw; // a refusal, not a failed step (409, not 503)
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                throw new CorrectionStepException(CorrectionCodes.FindingsStep, ex);
            }

            followed = patched.Followed;
            try
            {
                derived = await DeriveStatusAsync(applied, patched, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                throw new CorrectionStepException(CorrectionCodes.StatusStep, ex);
            }
        }

        if (reevaluate)
        {
            try
            {
                await ReconcileSignalsAsync(applied, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                throw new CorrectionStepException(CorrectionCodes.SignalsStep, ex);
            }
        }

        if (totals)
        {
            try
            {
                // A field with no findings step (TotalDue): its rows are read before the totals are recomputed, so a correction saved after this read is
                // never recorded by this run.
                followed ??= await ReadFollowedAsync(applied, ct).ConfigureAwait(false);
                await RecalculateTotalsAsync(applied, ct).ConfigureAwait(false);
            }
            catch (CorrectionTotalsRefusedException)
            {
                throw; // a refusal is not a failed step: no repeat clears it (409, not 503)
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                throw new CorrectionStepException(CorrectionCodes.TotalsStep, ex);
            }
        }

        long submissionVersion;
        try
        {
            submissionVersion = await RecordFollowedThroughAsync(applied, followed!, derived, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new CorrectionStepException(CorrectionCodes.ReevaluationStep, ex);
        }

        // Task 152 (FR-43): the correction is followed through - its findings, status, Signals and totals are final - so the invoice's index
        // document is rebuilt. The change is the submission row version the final write replaced (or, on a repeat that wrote nothing, the version it
        // found): the write is conditional on it, so each followed-through correction has its own key. Best effort; never fails the correction.
        await InvoiceIndexingJobHandler.TryEnqueueAsync(
            _jobs, _logger, applied.InvoiceId, $"c{submissionVersion.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
    }

    // ── The final write (fix round, part 3) ──

    /// <summary>The ids of the field's Corrected / Confirmed rows (on its line), for a field with no findings step.</summary>
    private async Task<IReadOnlyList<Guid>> ReadFollowedAsync(CorrectionApplied applied, CancellationToken ct) =>
        (await ReadCorrectionsAsync(_entities, applied.InvoiceId, ct).ConfigureAwait(false))
            .Where(r => r.IsLabel && r.Field == applied.Field && r.LineId == applied.LineId)
            .Select(r => r.Id)
            .ToList();

    /// <summary>
    /// After the last step: lists <paramref name="followed"/> in <see cref="AppliedCorrectionsMember"/> and removes the lines the status step re-derived
    /// (<paramref name="derived"/>) from <see cref="PendingStatusLinesMember"/>, in one row-version-conditional write of the invoice's own submission. Nothing is
    /// written when nothing changes (a repeat). Throws when the submission or its findings cannot be read (fail closed: the rows stay unapplied).
    /// </summary>
    private async Task<long> RecordFollowedThroughAsync(CorrectionApplied applied, IReadOnlyList<Guid> followed, IReadOnlySet<int>? derived, CancellationToken ct)
    {
        var submission = await ReadSubmissionAsync(applied.InvoiceId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The invoice has no submission.");
        if (!submission.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var version) || version <= 0)
        {
            throw new InvalidOperationException("The submission version could not be read.");
        }

        var json = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject root)
        {
            throw new InvalidOperationException("The submission has no stored findings.");
        }

        var original = root.ToJsonString();
        MarkListed(root, AppliedCorrectionsMember, followed);
        if (derived is { Count: > 0 } && root[PendingStatusLinesMember] is JsonArray pendingLines)
        {
            foreach (var node in pendingLines.Where(n => IntOf(n) is { } index && derived.Contains(index)).ToList())
            {
                pendingLines.Remove(node);
            }

            if (pendingLines.Count == 0)
            {
                root.Remove(PendingStatusLinesMember);
            }
        }

        if (string.Equals(original, root.ToJsonString(), StringComparison.Ordinal))
        {
            return version;
        }

        var update = new Entity(InvoiceReviewReadService.SubmissionEntity, submission.Id)
        {
            RowVersion = version.ToString(CultureInfo.InvariantCulture),
            [InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString(),
        };
        await _entities.ExecuteTransactionAsync(
            [new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches }], ct).ConfigureAwait(false);
        _logger.LogInformation("Correction of {Field} on invoice {InvoiceId}: followed through ({Count} correction rows recorded).",
            applied.Field, applied.InvoiceId, followed.Count);
        return version;
    }

    // ── The totals step (B2c) ──

    /// <summary>
    /// Recomputes the invoice's totals through 016 (<see cref="IInvoiceTotalsService.RecalculateAsync"/>, the one owner of the totals). It re-reads the
    /// invoice and its lines, so it is idempotent: a repeat after a failure computes and stores the same figures. A refused calculation (for example an
    /// approved total below zero) writes nothing and throws <see cref="CorrectionTotalsRefusedException"/> (409): the correction service dry-runs the
    /// totals before it writes, so this is reached only when something else (a concurrent adjustment) changed the inputs since.
    /// </summary>
    private async Task RecalculateTotalsAsync(CorrectionApplied applied, CancellationToken ct)
    {
        if (_recalculate is null)
        {
            return;
        }

        var calculation = await _recalculate(applied.InvoiceId, ct).ConfigureAwait(false);
        if (calculation.IsRefused)
        {
            throw new CorrectionTotalsRefusedException(calculation.Refusal!.Code);
        }

        _logger.LogInformation("Correction of {Field} on invoice {InvoiceId}: totals recomputed.", applied.Field, applied.InvoiceId);
    }

    /// <summary>
    /// Sets <c>sprk_adminattention</c> on <paramref name="update"/> from the refusals stored in <paramref name="root"/> (a plan entry the writer would refuse, a
    /// permanent write refusal), exactly as the persister and 049 do (<see cref="AdminAttentionText.Merge"/>: only the Signal segment is replaced, the other
    /// segments and any administrator text stay). Nothing is set when no refusal is stored or the text would not change.
    /// </summary>
    internal static void SetAdminAttention(Entity update, string? currentAttention, JsonObject root)
    {
        if (AdminAttentionText.FromFindings(root) is null)
        {
            return;
        }

        var merged = AdminAttentionText.Merge(currentAttention, root);
        if (merged is not null && !string.Equals(merged, currentAttention, StringComparison.Ordinal))
        {
            update[InvoiceSubmissionPersister.SubmissionAdminAttention] = merged;
        }
    }

    // ── The findings step ──

    private sealed record Patched(JsonObject Root, IReadOnlyDictionary<int, StoredLine> Lines, IReadOnlySet<int> Touched, IReadOnlyList<Guid> Followed);

    private async Task<Patched> PatchFindingsAsync(CorrectionApplied applied, CancellationToken ct)
    {
        // The submission and its version are read FIRST. A concurrent correction whose findings patch lands after this read bumps the version, so our
        // conditional write fails and is retried; read in the other order, lines older than the version could be paired with a version that already
        // includes the other correction's patch, and the write would succeed on stale lines.
        var submission = await ReadSubmissionAsync(applied.InvoiceId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The invoice has no submission.");
        var invoice = await ReadInvoiceAsync(applied.InvoiceId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The invoice was not found.");

        // Task 095c-4: with no firm the rules cannot be re-run (095c-3 ran none); refuse before anything is diffed or patched.
        if (InvoiceFirmHold.HasNoFirm(invoice))
        {
            throw new CorrectionFirmMissingException();
        }

        var lines = await ReadLinesAsync(_entities, applied.InvoiceId, ct).ConfigureAwait(false);
        var rows = await ReadCorrectionsAsync(_entities, applied.InvoiceId, ct).ConfigureAwait(false);
        var json = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject root)
        {
            throw new InvalidOperationException("The submission has no stored findings.");
        }

        if (!submission.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var submissionVersion) || submissionVersion <= 0)
        {
            throw new InvalidOperationException("The submission version could not be read.");
        }

        int? correctedIndex = null;
        if (applied.LineId is { } lineId)
        {
            correctedIndex = lines.Values.SingleOrDefault(l => l.Row.Id == lineId)?.Index
                ?? throw new InvalidOperationException("The corrected line is not a fee or expense line of the invoice.");
        }

        // The corrected field: its final value (the latest Corrected / Confirmed row) and its value as the stored findings last saw it. Fix round, part 3
        // (F-2): that is the final value of the last row whose findings patch LANDED (listed in patchedCorrections), not simply the previous row: a row
        // saved without its patch would otherwise be the "before" and its change would never be diffed. It is not the last FULLY followed-through row
        // (appliedCorrections): when a patch landed and a later step failed, the stored findings reflect that patch, and a diff from an older value would
        // leave the entries that only the newer value changed.
        var fieldRows = rows.Where(r => r.Field == applied.Field && r.LineId == applied.LineId).ToList();
        var labels = fieldRows.Where(r => r.IsLabel).ToList();
        if (labels.Count == 0)
        {
            throw new InvalidOperationException("The correction row of the field was not found.");
        }

        var patchedIds = ListedOf(root, PatchedCorrectionsMember);
        var latest = labels[^1];
        var previous = labels.Take(labels.Count - 1).LastOrDefault(r => patchedIds.Contains(r.Id));
        var preValue = previous is not null ? previous.Final : fieldRows[0].Ai;
        var verified = VerifiedOf(rows);

        // A timekeeper or date correction of a fee line also diffs the roster match and the rate: from the pair the findings were last patched with.
        StoredLine? correctedLine = correctedIndex is { } at ? lines[at] : null;
        var resolutionField = applied.Field is ResolutionFieldNames.Date or ResolutionFieldNames.TimekeeperAsBilled
            && correctedLine?.Row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value == (int)InvoiceLineCostType.Fee;
        var resolutionBefore = resolutionField ? ResolutionBefore(root, correctedLine!) : null;

        var provenance = ExtractionProvenance.ParseSubmission(json);
        var core = InvoiceLineDecisionService.SingleCoreOf(invoice) ?? throw new InvalidOperationException("The invoice names no single core record.");
        var basis = RequestBasis.ForCorrection(applied, invoice, lines, provenance, verified, submission.Id, root, core, resolutionBefore);
        var before = basis.Build(preValue, preVerified: previous is not null, before: true);
        var after = basis.Build(latest.Final, preVerified: true, before: false);

        var loaded = await _load(after, ct).ConfigureAwait(false);
        var beforeResult = _evaluate(before, loaded);
        var afterResult = _evaluate(after, loaded);

        // 082c C12 (interim; a new gap hold through a correction is 082d / 101). A resolution fault on either side has already thrown (the load,
        // or RulesOn for a date only one side has). A Procedure gap the corrected value brings in (a date moved into a gap) means some of the
        // corrected invoice's rules judged nothing: the patch is refused, so the correction stays unapplied, which already holds approval.
        // Compared PER SUBJECT (F2): a line moved onto a date that already has a gap for another subject brings in no new (Procedure, date, kind)
        // key, but its own rules judged nothing. The refusal is a fixed 409, never the generic Incomplete (F1-low): repeating never clears a gap.
        var gapsBefore = GapSubjects(beforeResult);
        if (GapSubjects(afterResult).Any(gap => !gapsBefore.Contains(gap)))
        {
            throw new CorrectionProcedureGapException();
        }

        var original = root.ToJsonString();
        var touched = new HashSet<int>();
        if (correctedIndex is { } corrected)
        {
            touched.Add(corrected);
        }

        var decided = lines.Values.Where(l => l.Decided).Select(l => l.Index).ToHashSet();
        var storedFindings = (root["findings"] as JsonArray)?.DeepClone() as JsonArray ?? [];
        PatchFindings(root, beforeResult, afterResult, decided, touched);
        PatchPlan(root, beforeResult, afterResult, storedFindings, SubjectsOf(applied.InvoiceId, root, lines), decided, touched);
        if (correctedIndex is { } index && !decided.Contains(index))
        {
            ClearLineFindings(root, applied.Field, index, latest.Final);
        }

        // Fix round, part 3 (F-2): what the stored findings now reflect: this field's rows (patchedCorrections) and, for a timekeeper or date correction,
        // the pair. When the patch changed the findings, the lines it touched wait for their status (pendingStatusLines) until a status step re-derives
        // them. All ride in the same conditional write as the patch; a repeat whose patch changes nothing adds nothing and writes nothing.
        if (!string.Equals(original, root.ToJsonString(), StringComparison.Ordinal))
        {
            MarkPendingStatus(root, touched);
        }

        if (resolutionField && ResolutionNow(correctedLine!) is { } now)
        {
            if (root[AppliedResolutionsMember] is not JsonObject resolutions)
            {
                root[AppliedResolutionsMember] = resolutions = new JsonObject();
            }

            resolutions[correctedLine!.Index.ToString(CultureInfo.InvariantCulture)] = now;
        }

        MarkListed(root, PatchedCorrectionsMember, labels.Select(l => l.Id));

        if (!string.Equals(original, root.ToJsonString(), StringComparison.Ordinal))
        {
            // 082d fix rounds 1 and 2 (F4): the stored Procedure snapshot is replaced by the re-run's only when it still describes what the stored findings
            // were judged with (see ProcedureSnapshot.ForPatchedFindings); else JSON null; an absent member (a row stored before 082d) stays absent.
            ProcedureSnapshot.SetForPatchedFindings(root, beforeResult, afterResult, decided);
            var findingsText = ProcedureSnapshot.SerializeWithinCap(root, InvoiceSubmissionPersister.FindingsJsonMaxLength, out var snapshotDropped);
            if (findingsText is null)
            {
                _logger.LogWarning("Correction of {Field} on invoice {InvoiceId}: the patched findings are over the column even without the Procedure snapshot; nothing written.",
                    applied.Field, applied.InvoiceId);
                throw new CorrectionFindingsTooLargeException();
            }

            if (snapshotDropped)
            {
                _logger.LogWarning("Correction of {Field} on invoice {InvoiceId}: the patched findings are over the column; the Procedure snapshot is left out (null).",
                    applied.Field, applied.InvoiceId);
            }

            var update = new Entity(InvoiceReviewReadService.SubmissionEntity, submission.Id)
            {
                RowVersion = submissionVersion.ToString(CultureInfo.InvariantCulture),
                [InvoiceSubmissionPersister.SubmissionFindingsJson] = findingsText,
            };
            SetAdminAttention(update, submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention), root);
            await _entities.ExecuteTransactionAsync(
                [new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches }], ct).ConfigureAwait(false);
            _logger.LogInformation("Correction of {Field} on invoice {InvoiceId}: findings patched ({Before} before, {After} after the correction; {Touched} lines touched).",
                applied.Field, applied.InvoiceId, beforeResult.Findings.Count, afterResult.Findings.Count, touched.Count);
        }

        return new Patched(root, lines, touched, labels.Select(l => l.Id).ToList());
    }

    /// <summary>The correction row ids listed in <paramref name="member"/> (<see cref="AppliedCorrectionsMember"/> or <see cref="PatchedCorrectionsMember"/>;
    /// an entry that is not an id is ignored).</summary>
    private static HashSet<Guid> ListedOf(JsonObject root, string member)
    {
        var ids = new HashSet<Guid>();
        foreach (var node in root[member] as JsonArray ?? [])
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>Adds the ids not yet listed to <paramref name="member"/>, in order; nothing changes when all are listed (a repeat).</summary>
    private static void MarkListed(JsonObject root, string member, IEnumerable<Guid> ids)
    {
        var listed = ListedOf(root, member);
        foreach (var id in ids)
        {
            if (!listed.Add(id))
            {
                continue;
            }

            if (root[member] is not JsonArray array)
            {
                root[member] = array = [];
            }

            array.Add(id.ToString("D"));
        }
    }

    /// <summary>Adds the lines not yet listed to <see cref="PendingStatusLinesMember"/>; nothing changes when all are listed.</summary>
    private static void MarkPendingStatus(JsonObject root, IEnumerable<int> lines)
    {
        var listed = PendingStatusOf(root);
        foreach (var line in lines.Order())
        {
            if (!listed.Add(line))
            {
                continue;
            }

            if (root[PendingStatusLinesMember] is not JsonArray array)
            {
                root[PendingStatusLinesMember] = array = [];
            }

            array.Add(line);
        }
    }

    /// <summary>The line indexes listed in <see cref="PendingStatusLinesMember"/>.</summary>
    private static HashSet<int> PendingStatusOf(JsonObject root) =>
        (root[PendingStatusLinesMember] as JsonArray ?? []).Select(IntOf).Where(i => i is not null).Select(i => i!.Value).ToHashSet();

    /// <summary>
    /// The pair the stored findings reflect for a fee line (fix round, part 3): the one recorded in <see cref="AppliedResolutionsMember"/> by the last
    /// timekeeper or date correction whose findings step finished; else, when a correction changed the pair, the pair the line had before its FIRST
    /// timekeeper or date correction (<c>resolutionChange.original</c>, or <c>before</c> on a line written before <c>original</c> existed) with the reasons the
    /// first evaluation stored; else null (the pair was never changed: the columns are what the findings saw).
    /// </summary>
    private static ResolutionFacts? ResolutionBefore(JsonObject root, StoredLine line)
    {
        if ((root[AppliedResolutionsMember] as JsonObject)?[line.Index.ToString(CultureInfo.InvariantCulture)] is JsonObject recorded)
        {
            return new ResolutionFacts(
                Guid.TryParse(Text(recorded, "timekeeperId"), out var id) ? id : null,
                recorded["approvedRate"] is JsonValue rate && rate.TryGetValue<decimal>(out var approved) ? approved : null,
                Enum.TryParse<TimekeeperUnmatchedReason>(Text(recorded, "timekeeperReason"), out var tr) ? tr : TimekeeperUnmatchedReason.None,
                Enum.TryParse<RateResolutionStatus>(Text(recorded, "rateStatus"), out var rs) ? rs : RateResolutionStatus.NotFound,
                Enum.TryParse<RateNotFoundReason>(Text(recorded, "rateReason"), out var rr) ? rr : RateNotFoundReason.None);
        }

        if (line.Extraction?[ResolutionChangeMember] is not JsonObject change)
        {
            return null;
        }

        var was = change["original"] as JsonObject ?? change["before"] as JsonObject;
        var (timekeeperReason, rateStatus, rateReason) = RequestBasis.StoredReasons(root, line.Index);
        return new ResolutionFacts(
            was is not null && Guid.TryParse(Text(was, "timekeeperId"), out var wasId) ? wasId : null,
            was?["approvedRate"] is JsonValue wasRate && wasRate.TryGetValue<decimal>(out var wasApproved) ? wasApproved : null,
            timekeeperReason, rateStatus, rateReason);
    }

    /// <summary>The pair a fee line holds now (its columns) with the reasons its latest timekeeper or date correction stored; null when no correction changed it.</summary>
    private static JsonObject? ResolutionNow(StoredLine line)
    {
        if (line.Extraction?[ResolutionChangeMember] is not JsonObject change)
        {
            return null;
        }

        var after = change["after"] as JsonObject;
        return new JsonObject
        {
            ["timekeeperId"] = line.Row.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.LineResolvedTimekeeper)?.Id.ToString("D"),
            ["approvedRate"] = line.Row.GetAttributeValue<Money>(InvoiceSubmissionPersister.LineApprovedRate)?.Value,
            ["timekeeperReason"] = after is null ? null : Text(after, "timekeeperReason"),
            ["rateStatus"] = after is null ? null : Text(after, "rateStatus"),
            ["rateReason"] = after is null ? null : Text(after, "rateReason"),
        };
    }

    private static void RecordSystemChange(JsonObject root, int line, string kind, string reason)
    {
        var system = (JsonArray)Pending(root)["system"]!;
        if (!system.OfType<JsonObject>().Any(e => IntOf(e["line"]) == line && Text(e, "kind") == kind && Text(e, "reason") == reason))
        {
            system.Add(new JsonObject { ["line"] = line, ["kind"] = kind, ["reason"] = reason });
        }
    }

    /// <summary>The clears a correction makes on its own line: the coding findings of the corrected code kind, <c>coding.not_returned</c>, <c>line.no_description</c>.</summary>
    private static void ClearLineFindings(JsonObject root, string field, int line, string? finalValue)
    {
        var lineFindings = root[InvoiceSubmissionPersister.LineFindingsMember] as JsonArray;
        if (CodeFields.TryGetValue(field, out var code))
        {
            if (root["codingFindings"] is JsonArray coding)
            {
                foreach (var entry in coding.OfType<JsonObject>().Where(e => IntOf(e["line"]) == line).ToList())
                {
                    if (entry["refused"] is JsonArray refused)
                    {
                        foreach (var item in refused.OfType<JsonObject>().Where(r => Text(r, "kind") == code.Kind).ToList())
                        {
                            refused.Remove(item);
                            RecordSystemChange(root, line, code.Kind, "refused");
                        }
                    }

                    if (entry[code.Member] is JsonObject slot && slot["mismatch"] is JsonValue mismatch && mismatch.TryGetValue<bool>(out var flagged) && flagged)
                    {
                        slot["mismatch"] = false;
                        RecordSystemChange(root, line, code.Kind, "firmCodeMismatch");
                    }

                    var anyRefused = entry["refused"] is JsonArray left && left.Count > 0;
                    var anyMismatch = new[] { "task", "activity", "expense" }.Any(m => entry[m] is JsonObject s && s["mismatch"] is JsonValue v && v.TryGetValue<bool>(out var b) && b);
                    if (!anyRefused && !anyMismatch)
                    {
                        coding.Remove(entry);
                    }
                }
            }

            // EB-D-175: a coding.not_returned line must be "accepted or corrected" by a reviewer, so a code correction resolves it (it is removed here).
            RemoveLineFinding(lineFindings, line, InvoiceSubmissionPersister.LineFindingNotReturned);
        }
        else if (field == BillingFieldNames.Narrative && !string.IsNullOrWhiteSpace(finalValue))
        {
            RemoveLineFinding(lineFindings, line, InvoiceSubmissionPersister.LineFindingNoDescription);
        }

        // rules.not_checked (F2) is never removed by a correction: the refused rule still did not run.
    }

    private static void RemoveLineFinding(JsonArray? lineFindings, int line, string code)
    {
        foreach (var entry in (lineFindings ?? []).OfType<JsonObject>().Where(e => IntOf(e["line"]) == line && Text(e, "code") == code).ToList())
        {
            lineFindings!.Remove(entry);
        }
    }

    // ── The status step ──

    /// <summary>
    /// Re-derives the lines this patch touched and the lines an earlier landed patch left waiting (<see cref="PendingStatusLinesMember"/>); returns every
    /// line it considered, so the final write can take them off the waiting list.
    /// </summary>
    private async Task<IReadOnlySet<int>> DeriveStatusAsync(CorrectionApplied applied, Patched patched, CancellationToken ct)
    {
        var requests = new List<OrganizationRequest>();
        var noExtraction = 0;
        var considered = patched.Touched.Union(PendingStatusOf(patched.Root)).ToHashSet();
        foreach (var index in considered.Order())
        {
            if (!patched.Lines.TryGetValue(index, out var line) || line.Decided)
            {
                continue; // a decided line is never changed
            }

            if (line.Extraction is null)
            {
                // Fix round, part 3: "coded" is read from the extraction JSON, which this line does not have (it was over the column limit, or unreadable).
                // Re-deriving would make it Pending; its status is left as it is.
                noExtraction++;
                continue;
            }

            var current = line.Row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value;
            var status = StatusOf(patched.Root, index, line.Extraction);
            if (current == status)
            {
                continue;
            }

            if (!line.Row.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var version) || version <= 0)
            {
                throw new InvalidOperationException("The line version could not be read.");
            }

            var update = new Entity(InvoiceReviewReadService.LineEntity, line.Row.Id)
            {
                RowVersion = version.ToString(CultureInfo.InvariantCulture),
                [InvoiceTotalsService.LineStatus] = new OptionSetValue(status),
            };
            requests.Add(new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
        }

        if (noExtraction > 0)
        {
            _logger.LogWarning("Correction of {Field} on invoice {InvoiceId}: {Count} touched lines have no stored extraction; their status was left unchanged.",
                applied.Field, applied.InvoiceId, noExtraction);
        }

        if (requests.Count > 0)
        {
            await _entities.ExecuteTransactionAsync(requests, ct).ConfigureAwait(false);
            _logger.LogInformation("Correction of {Field} on invoice {InvoiceId}: {Count} line statuses re-derived.", applied.Field, applied.InvoiceId, requests.Count);
        }

        return considered;
    }

    /// <summary>The persister's rule (<see cref="InvoiceSubmissionPersister.LineStatusFor"/>) over the patched findings: any entry naming the line flags it; coded = a code set is recorded.</summary>
    internal static int StatusOf(JsonObject root, int line, JsonObject? extraction)
    {
        var flagged = (root["findings"] as JsonArray ?? []).OfType<JsonObject>().Any(e => Text(e, "subject") == nameof(BillingSubjectKind.Line) && IntOf(e["line"]) == line)
            || (root[InvoiceSubmissionPersister.LineFindingsMember] as JsonArray ?? []).OfType<JsonObject>().Any(e => IntOf(e["line"]) == line)
            || (root["codingFindings"] as JsonArray ?? []).OfType<JsonObject>().Any(e => IntOf(e["line"]) == line);
        var coded = extraction?["coding"] is JsonObject coding && Text(coding, "codeSetId") is { Length: > 0 };
        return InvoiceSubmissionPersister.LineStatusFor(flagged, coded);
    }

    // ── The Signals step (B2b-2) ──

    private static readonly HashSet<string> HoldCodes = new(StringComparer.Ordinal)
    {
        SubmissionReasonCodes.CodingNoCodeSetHold, SubmissionReasonCodes.CodingUnavailableHold,
    };

    /// <summary>
    /// Reconciles <see cref="PendingSignalChangesMember"/> and the D-125 coding hold, then removes both from the stored findings in ONE
    /// row-version-conditional write, then re-derives the undecided lines still Pending with a recorded code set. Every Signal call is idempotent (a
    /// Signal already closed is left; one already at the new episode is refreshed in place), so a repeat after any failure converges.
    /// </summary>
    private async Task ReconcileSignalsAsync(CorrectionApplied applied, CancellationToken ct)
    {
        var submission = await ReadSubmissionAsync(applied.InvoiceId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The invoice has no submission.");
        if (!submission.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var version) || version <= 0)
        {
            throw new InvalidOperationException("The submission version could not be read.");
        }

        var json = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject root)
        {
            throw new InvalidOperationException("The submission has no stored findings.");
        }

        var lines = await ReadLinesAsync(_entities, applied.InvoiceId, ct).ConfigureAwait(false);
        var pending = root[PendingSignalChangesMember] as JsonObject;
        var ruleChanges = (pending?["rule"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var systemLines = (pending?["system"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(e => IntOf(e["line"])).Where(i => i is not null).Select(i => i!.Value).Distinct().Order().ToList();

        // D-125: the hold clears when no undecided line is still without a code set (a decided line can no longer be re-coded).
        var pipeline = root["pipelineFindings"] as JsonArray ?? [];
        var holdEntries = pipeline.OfType<JsonObject>().Where(p => Text(p, "reasonCode") is { } code && HoldCodes.Contains(code)).ToList();
        var clearHold = holdEntries.Count > 0 && lines.Values.All(l => l.Decided || IsCoded(l.Extraction));

        if (pending is not null || clearHold)
        {
            foreach (var hold in clearHold ? holdEntries : [])
            {
                pipeline.Remove(hold);
            }

            var signals = new InvoiceRuleSignalReconciler(
                _lifecycle, _closeSignal, _logger, SignalLogContext.Correction, applied.InvoiceId, applied.Field);
            foreach (var change in ruleChanges)
            {
                await signals.ReconcileRuleAsync(root, change, ct).ConfigureAwait(false);
            }

            if (systemLines.Count > 0 || clearHold)
            {
                await ReconcileSystemAsync(applied, signals, root, lines, systemLines, clearHold, ct).ConfigureAwait(false);
            }

            root.Remove(PendingSignalChangesMember);
            InvoiceRuleSignalReconciler.RecordWriteRefusals(root, signals.Refused);
            var update = new Entity(InvoiceReviewReadService.SubmissionEntity, submission.Id)
            {
                RowVersion = version.ToString(CultureInfo.InvariantCulture),
                [InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString(),
            };
            SetAdminAttention(update, submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention), root);
            await _entities.ExecuteTransactionAsync(
                [new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches }], ct).ConfigureAwait(false);
            _logger.LogInformation("Correction of {Field} on invoice {InvoiceId}: Signals reconciled ({Rule} rule changes, {System} lines with system changes, coding hold cleared: {Hold}, {Refused} refused).",
                applied.Field, applied.InvoiceId, ruleChanges.Count, systemLines.Count, clearHold, signals.Refused.Count);
        }

        await RederivePendingAsync(applied, root, lines, ct).ConfigureAwait(false);
    }

    private static bool IsCoded(JsonObject? extraction) =>
        extraction?["coding"] is JsonObject coding && Text(coding, "codeSetId") is { Length: > 0 };

    /// <summary>
    /// The <c>system</c> entries and the coding hold: the system findings are re-mapped from the patched JSON (<see cref="InvoiceSubmissionAfterSteps.StoredFindings.SystemFindings"/>)
    /// under the system policy 049 reads; each named line's Signal, and the invoice's when the hold clears, is closed when none of its findings is left,
    /// else refreshed. A system policy that is not usable wrote no system Signal (049), so nothing is reconciled.
    /// </summary>
    private async Task ReconcileSystemAsync(
        CorrectionApplied applied, InvoiceRuleSignalReconciler signals, JsonObject root, IReadOnlyDictionary<int, StoredLine> lines,
        IReadOnlyList<int> systemLines, bool clearHold, CancellationToken ct)
    {
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(root.ToJsonString())
            ?? throw new InvalidOperationException("The stored findings could not be read back.");
        var (rule, state) = await _systemPolicy(stored.ReceivedOn, ct).ConfigureAwait(false);
        if (rule is null)
        {
            _logger.LogInformation("Correction of {Field} on invoice {InvoiceId}: system billing policy {PolicyCode} is {State}; no system Signal to reconcile.",
                applied.Field, applied.InvoiceId, BillingRuleCatalogue.SystemPolicyCode, state);
            return;
        }

        var findings = stored.SystemFindings(new BillingSystemPolicyVersion(rule.PolicyId, rule.PolicyCode, rule.PolicyVersionId));
        var subjects = SubjectsOf(applied.InvoiceId, root, lines);
        var rules = new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule };
        var targets = systemLines.Where(lines.ContainsKey).Select(i => (int?)i).ToList();
        if (clearHold)
        {
            targets.Add(null);
        }

        foreach (var line in targets)
        {
            var (entity, subjectId) = line is { } index
                ? (BillingFindingSignalMapper.LineEntity, lines[index].Row.Id)
                : (InvoiceReviewReadService.InvoiceEntity, applied.InvoiceId);
            var left = findings.Where(f => f.Subject.LineIndex == line).ToList();
            if (left.Count == 0)
            {
                await signals.CloseClearedAsync(rule.PolicyCode, entity, subjectId, fingerprint: null, ct).ConfigureAwait(false);
                continue;
            }

            var mapping = BillingFindingSignalMapper.MapAll(left, rules, subjects).Single();
            await signals.WriteAsync(mapping.Request, mapping.Fingerprint, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The lines the D-125 hold kept Pending: an undecided line still Pending that now records a code set takes the persister's rule over <paramref name="root"/>.</summary>
    private async Task RederivePendingAsync(CorrectionApplied applied, JsonObject root, IReadOnlyDictionary<int, StoredLine> lines, CancellationToken ct)
    {
        var requests = new List<OrganizationRequest>();
        foreach (var line in lines.Values.OrderBy(l => l.Index))
        {
            if (line.Decided || line.Row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value != (int)InvoiceLineStatus.Pending
                || !IsCoded(line.Extraction))
            {
                continue;
            }

            if (!line.Row.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var version) || version <= 0)
            {
                throw new InvalidOperationException("The line version could not be read.");
            }

            var update = new Entity(InvoiceReviewReadService.LineEntity, line.Row.Id)
            {
                RowVersion = version.ToString(CultureInfo.InvariantCulture),
                [InvoiceTotalsService.LineStatus] = new OptionSetValue(StatusOf(root, line.Index, line.Extraction)),
            };
            requests.Add(new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
        }

        if (requests.Count > 0)
        {
            await _entities.ExecuteTransactionAsync(requests, ct).ConfigureAwait(false);
            _logger.LogInformation("Correction of {Field} on invoice {InvoiceId}: {Count} Pending lines with a code set re-derived.", applied.Field, applied.InvoiceId, requests.Count);
        }
    }

    // ── Reads ──

    private async Task<Entity?> ReadInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.InvoiceEntity)
        {
            ColumnSet = new ColumnSet(InvoiceColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition($"{InvoiceReviewReadService.InvoiceEntity}id", ConditionOperator.Equal, invoiceId);
        return (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
    }

    private Task<Entity?> ReadSubmissionAsync(Guid invoiceId, CancellationToken ct) => ReadSubmissionAsync(_entities, invoiceId, ct);

    private static readonly string[] SubmissionReadColumns =
        [InvoiceSubmissionPersister.SubmissionFindingsJson, InvoiceTotalsService.VersionNumber, InvoiceSubmissionPersister.SubmissionAdminAttention];

    /// <summary>
    /// The invoice's OWN submission (its findings JSON, version and administrator text), read app-only through the shared selection
    /// <see cref="InvoiceBlockedHold.ReadOwnSubmissionAsync"/> (task 165a): among the submissions linked to the invoice, the one whose id derives the
    /// invoice's (<see cref="InvoiceSubmissionPersister.InvoiceIdFor"/>), never another linked row, however recent (164a fix round F1). Null when it is
    /// not linked: every caller (the correction's follow-through, Add line's steps 1 and 9, the unapplied-correction read) then writes nothing and fails
    /// closed, and the approval and line decisions refuse.
    /// </summary>
    internal static Task<Entity?> ReadSubmissionAsync(IGenericEntityService entities, Guid invoiceId, CancellationToken ct) =>
        InvoiceBlockedHold.ReadOwnSubmissionAsync(entities, invoiceId, SubmissionReadColumns, ct);

    /// <summary>
    /// Fix round, part 3 (F-2): the Corrected / Confirmed correction rows of the invoice, of a field this follow-through handles (re-evaluated, or one that
    /// feeds the totals, TotalDue included), that are not listed in the invoice's own submission's <see cref="AppliedCorrectionsMember"/>: corrections whose value
    /// was saved but whose follow-through (findings, status, Signals, totals) has not finished (the client stopped after a 503 Incomplete). A row of a line that
    /// <paramref name="lines"/> shows a reviewer has decided (Accepted, Reduced, Disallowed) is not listed: <see cref="InvoiceLineDecisionService"/> refuses to
    /// decide a line while one of its corrections is listed here, and the correction service refuses a decided line, so such a row was followed through before
    /// the decision (or predates this rule). The submission is read only when a row is left; with no findings JSON every such row is unapplied. A read fault
    /// throws (the caller refuses: fail closed). App-only (<see cref="IGenericEntityService"/>), as every read of the findings JSON.
    /// </summary>
    internal static async Task<IReadOnlyList<UnappliedCorrection>> ReadUnappliedAsync(
        IGenericEntityService entities, Guid invoiceId, IEnumerable<Entity> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(lines);
        var decidedLines = lines
            .Where(l => l.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value
                is (int)InvoiceLineStatus.Accepted or (int)InvoiceLineStatus.Reduced or (int)InvoiceLineStatus.Disallowed)
            .Select(l => l.Id)
            .ToHashSet();
        var query = new QueryExpression(InvoiceLineDecisionService.CorrectionEntity)
        {
            ColumnSet = new ColumnSet("sprk_fieldname", "sprk_invoiceline", "sprk_outcometype"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_invoice", ConditionOperator.Equal, invoiceId);
        query.Criteria.AddCondition("sprk_outcometype", ConditionOperator.In, InvoiceCorrectionService.OutcomeCorrected, InvoiceCorrectionService.OutcomeConfirmed);
        query.AddOrder($"{InvoiceLineDecisionService.CorrectionEntity}id", OrderType.Ascending);
        var rows = (await InvoiceReviewReadService.ReadPagedAsync(entities, query, ct).ConfigureAwait(false))
            .Where(r => r.GetAttributeValue<OptionSetValue>("sprk_outcometype")?.Value is InvoiceCorrectionService.OutcomeCorrected or InvoiceCorrectionService.OutcomeConfirmed
                && r.GetAttributeValue<string>("sprk_fieldname") is { } field && (Reevaluated.Contains(field) || TotalsFields.Contains(field))
                && !(r.GetAttributeValue<EntityReference>("sprk_invoiceline")?.Id is { } line && decidedLines.Contains(line)))
            .ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var submission = await ReadSubmissionAsync(entities, invoiceId, ct).ConfigureAwait(false);
        var json = submission?.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        var appliedIds = !string.IsNullOrWhiteSpace(json) && JsonNode.Parse(json) is JsonObject root ? ListedOf(root, AppliedCorrectionsMember) : [];
        return rows
            .Where(r => !appliedIds.Contains(r.Id))
            .Select(r => new UnappliedCorrection(r.Id, r.GetAttributeValue<EntityReference>("sprk_invoiceline")?.Id, r.GetAttributeValue<string>("sprk_fieldname")!))
            .ToList();
    }

    /// <summary>
    /// The reviewer-facing refusal while corrections are unapplied (the approval and the line decisions): each correction by its field and where it is (the
    /// line number, or the invoice), then what to do; no ids. <paramref name="then"/> ends the text, for example "approve again".
    /// </summary>
    internal static string UnappliedText(IReadOnlyList<UnappliedCorrection> unapplied, IEnumerable<Entity> lines, string then)
    {
        var sequence = lines.ToDictionary(l => l.Id, l => l.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence));
        var named = unapplied
            .Select(u => u.LineId is not { } lineId ? $"{u.Field} (invoice)"
                : sequence.TryGetValue(lineId, out var number) && number is { } n ? $"{u.Field} (line {n.ToString(CultureInfo.InvariantCulture)})"
                : $"{u.Field} (a line)")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return $"These corrections were saved but did not finish: {string.Join("; ", named)}. Repeat each correction, then {then}.";
    }
}
