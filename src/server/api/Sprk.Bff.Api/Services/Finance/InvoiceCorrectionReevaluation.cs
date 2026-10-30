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

    private static readonly string[] CorrectionColumns =
        ["sprk_fieldname", "sprk_aivalue", "sprk_finalvalue", "sprk_outcometype", "sprk_invoiceline", "sprk_reviewedon", "createdon"];

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
        (await ReadCorrectionsAsync(applied.InvoiceId, ct).ConfigureAwait(false))
            .Where(r => r.IsLabel && r.Field == applied.Field && r.LineId == applied.LineId)
            .Select(r => r.Id)
            .ToList();

    /// <summary>
    /// After the last step: lists <paramref name="followed"/> in <see cref="AppliedCorrectionsMember"/> and removes the lines the status step re-derived
    /// (<paramref name="derived"/>) from <see cref="PendingStatusLinesMember"/>, in one row-version-conditional write of the latest submission. Nothing is
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
    private static void SetAdminAttention(Entity update, string? currentAttention, JsonObject root)
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

    private sealed record StoredLine(int Index, Entity Row, JsonObject? Extraction, bool Decided);

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
        var lines = await ReadLinesAsync(applied.InvoiceId, ct).ConfigureAwait(false);
        var rows = await ReadCorrectionsAsync(applied.InvoiceId, ct).ConfigureAwait(false);
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
        var verified = rows.Where(r => r.IsLabel).Select(r => (r.LineId, r.Field)).ToHashSet();

        // A timekeeper or date correction of a fee line also diffs the roster match and the rate: from the pair the findings were last patched with.
        StoredLine? correctedLine = correctedIndex is { } at ? lines[at] : null;
        var resolutionField = applied.Field is ResolutionFieldNames.Date or ResolutionFieldNames.TimekeeperAsBilled
            && correctedLine?.Row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value == (int)InvoiceLineCostType.Fee;
        var resolutionBefore = resolutionField ? ResolutionBefore(root, correctedLine!) : null;

        var provenance = ExtractionProvenance.ParseSubmission(json);
        var core = InvoiceLineDecisionService.SingleCoreOf(invoice) ?? throw new InvalidOperationException("The invoice names no single core record.");
        var basis = new RequestBasis(applied, invoice, lines, provenance, verified, submission.Id, root, core, resolutionBefore);
        var before = basis.Build(preValue, preVerified: previous is not null, before: true);
        var after = basis.Build(latest.Final, preVerified: true, before: false);

        var loaded = await _load(after, ct).ConfigureAwait(false);
        var beforeResult = _evaluate(before, loaded);
        var afterResult = _evaluate(after, loaded);

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
            var update = new Entity(InvoiceReviewReadService.SubmissionEntity, submission.Id)
            {
                RowVersion = submissionVersion.ToString(CultureInfo.InvariantCulture),
                [InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString(),
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

    /// <summary>The roster match, approved rate and reasons a timekeeper or date correction is diffed FROM.</summary>
    private sealed record ResolutionFacts(Guid? TimekeeperId, decimal? ApprovedRate, TimekeeperUnmatchedReason TimekeeperReason, RateResolutionStatus RateStatus, RateNotFoundReason RateReason);

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

    private static string KeyOf(BillingFinding f) =>
        string.Join('|', f.PolicyVersionId.ToString("D"), f.Subject.Kind.ToString(), f.Subject.LineIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, f.ReasonCode);

    private static string? KeyOf(JsonObject entry) =>
        Text(entry, "policyVersionId") is { } version && Guid.TryParse(version, out var id) && Text(entry, "subject") is { } subject && Text(entry, "reasonCode") is { } reason
            ? string.Join('|', id.ToString("D"), subject, IntOf(entry["line"])?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, reason)
            : null;

    /// <summary><c>findings[]</c>: per key, the entries that differ between the two evaluations are replaced, added or removed.</summary>
    private static void PatchFindings(JsonObject root, BillingEvaluationResult before, BillingEvaluationResult after, IReadOnlySet<int> decided, HashSet<int> touched)
    {
        if (root["findings"] is not JsonArray stored)
        {
            root["findings"] = stored = [];
        }

        var beforeByKey = before.Findings.GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var afterByKey = after.Findings.GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var withheld = after.Findings.Where(f => f.DetailWithheld).Select(f => (f.PolicyId, f.Subject)).ToHashSet();

        // A new entry takes an index no stored entry (nor 073's deciding list) has used, so a removed finding's index is never reused.
        var next = stored.OfType<JsonObject>().Select(e => IntOf(e["index"]) ?? -1)
            .Concat((root["decision"]?["deciding"] as JsonArray ?? []).Select(IntOf).Select(i => i ?? -1))
            .DefaultIfEmpty(-1).Max() + 1;
        foreach (var key in beforeByKey.Keys.Union(afterByKey.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var was = beforeByKey.GetValueOrDefault(key) ?? [];
            var now = afterByKey.GetValueOrDefault(key) ?? [];
            if (Same(was, now))
            {
                continue;
            }

            var line = (was.Count > 0 ? was[0] : now[0]).Subject.LineIndex;
            if (line is { } decidedLine && decided.Contains(decidedLine))
            {
                continue;
            }

            if (line is { } index)
            {
                touched.Add(index);
            }

            var existing = stored.OfType<JsonObject>().Where(e => KeyOf(e) == key).ToList();
            for (var i = 0; i < Math.Max(existing.Count, now.Count); i++)
            {
                if (i >= now.Count)
                {
                    stored.Remove(existing[i]);
                    continue;
                }

                var finding = now[i];
                var scrub = withheld.Contains((finding.PolicyId, finding.Subject));
                if (i < existing.Count)
                {
                    var slot = stored.IndexOf(existing[i]);
                    var decision = DecisionOf(existing[i], finding, after);
                    stored[slot] = InvoiceSubmissionPersister.FindingEntryJson(IntOf(existing[i]["index"]) ?? next++, finding, decision, scrub);
                }
                else
                {
                    stored.Add(InvoiceSubmissionPersister.FindingEntryJson(next++, finding, DecisionOf(null, finding, after), scrub));
                }
            }
        }

        static bool Same(List<BillingFinding> a, List<BillingFinding> b) =>
            a.Count == b.Count && a.Zip(b).All(p => JsonNode.DeepEquals(
                InvoiceSubmissionPersister.FindingEntryJson(0, p.First, null, scrub: false),
                InvoiceSubmissionPersister.FindingEntryJson(0, p.Second, null, scrub: false)));
    }

    /// <summary>A replaced entry keeps 073's decision members as stored; a new one carries the rule's consequence and does not reject (no 073 re-run).</summary>
    private static FindingDecision DecisionOf(JsonObject? stored, BillingFinding finding, BillingEvaluationResult after)
    {
        BillingConsequence? consequence = null;
        if (stored is not null && Enum.TryParse<BillingConsequence>(Text(stored, "consequence"), out var kept))
        {
            consequence = kept;
        }
        else if (after.RulesByVersion.TryGetValue(finding.PolicyVersionId, out var rule) && rule.Consequence is { } value
            && Enum.IsDefined(typeof(BillingConsequence), value))
        {
            consequence = (BillingConsequence)value;
        }

        return new FindingDecision
        {
            Index = 0,
            Finding = finding,
            Consequence = consequence,
            Rejects = stored?["rejects"] is JsonValue rejects && rejects.TryGetValue<bool>(out var r) && r,
            HeldBecause = stored is null ? null : Text(stored, "heldBecause"),
            ValuesRead = finding.ValuesRead,
        };
    }

    private static BillingSignalSubjects SubjectsOf(Guid invoiceId, JsonObject root, IReadOnlyDictionary<int, StoredLine> lines) => new(
        invoiceId,
        Text(root, "invoiceName"),
        lines.ToDictionary(l => l.Key, l => l.Value.Row.Id),
        lines.ToDictionary(l => l.Key, l => l.Value.Row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineName) ?? string.Empty));

    private static string PlanKey(string? policyCode, string? entity, string? subjectId) =>
        string.Join('|', policyCode, entity, Guid.TryParse(subjectId, out var id) ? id.ToString("D") : subjectId);

    /// <summary>
    /// 073's stored decision of each finding of <paramref name="result"/> (B2b-2): the n-th finding of a key takes the n-th stored entry of that key in
    /// <paramref name="stored"/> (<see cref="DecisionOf"/>: its <c>consequence</c>, <c>rejects</c>, <c>heldBecause</c>); a finding with no stored entry
    /// carries the rule's consequence and does not reject. Null when the stored findings carry no 073 decision (the persister mapped without one).
    /// </summary>
    private static IReadOnlyList<FindingDecision>? StoredDecisions(JsonObject root, BillingEvaluationResult result, JsonArray stored)
    {
        if (root["decision"] is not JsonObject)
        {
            return null;
        }

        var byKey = stored.OfType<JsonObject>().GroupBy(e => KeyOf(e) ?? string.Empty).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var decisions = new List<FindingDecision>(result.Findings.Count);
        for (var i = 0; i < result.Findings.Count; i++)
        {
            var finding = result.Findings[i];
            var key = KeyOf(finding);
            var n = seen.GetValueOrDefault(key);
            seen[key] = n + 1;
            var entry = byKey.TryGetValue(key, out var entries) && n < entries.Count ? entries[n] : null;
            decisions.Add(DecisionOf(entry, finding, result) with { Index = i });
        }

        return decisions;
    }

    /// <summary>The stored outcome kind, passed to the mapper only with 073's decisions (a Blocked outcome needs them).</summary>
    private static SubmissionEvaluationKind? StoredKind(JsonObject root) =>
        Enum.TryParse<SubmissionEvaluationKind>(Text(root, "kind"), out var kind) ? kind : null;

    /// <summary>
    /// <c>signalPlan[]</c>: per (policy, subject), the entry is replaced, added or removed when the mapped group differs; the change is recorded for B2b-2.
    /// Both sides are mapped with 073's stored decisions and the stored kind (<see cref="StoredDecisions"/>). A stored Blocked entry the after side would
    /// present otherwise is kept and marked <see cref="CorrectionReviewMember"/>; a replacement the writer would refuse is recorded in
    /// <see cref="InvoiceSubmissionPersister.SignalPlanRefusalsMember"/>.
    /// </summary>
    private static void PatchPlan(
        JsonObject root, BillingEvaluationResult before, BillingEvaluationResult after, JsonArray storedFindings, BillingSignalSubjects subjects,
        IReadOnlySet<int> decided, HashSet<int> touched)
    {
        if (root["signalPlan"] is not JsonArray plan)
        {
            root["signalPlan"] = plan = [];
        }

        var lineByRow = subjects.LineIds.ToDictionary(p => p.Value, p => p.Key);
        var beforeDecisions = StoredDecisions(root, before, storedFindings);
        var afterDecisions = StoredDecisions(root, after, root["findings"] as JsonArray ?? []);
        var kind = beforeDecisions is null ? null : StoredKind(root);
        var was = Entries(BillingFindingSignalMapper.MapAll(before, subjects, beforeDecisions, kind));
        var now = Entries(BillingFindingSignalMapper.MapAll(after, subjects, afterDecisions, kind));
        foreach (var key in was.Keys.Union(now.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var old = was.GetValueOrDefault(key);
            var current = now.GetValueOrDefault(key);
            if (old is not null && current is not null && JsonNode.DeepEquals(old.Value.Entry, current.Value.Entry))
            {
                continue;
            }

            var sample = (current ?? old)!.Value.Mapping.Request;
            if (sample.SubjectEntityLogicalName == BillingFindingSignalMapper.LineEntity && lineByRow.TryGetValue(sample.SubjectId, out var line))
            {
                if (decided.Contains(line))
                {
                    continue;
                }

                touched.Add(line);
            }

            var stored = plan.OfType<JsonObject>().FirstOrDefault(e => PlanKey(Text(e, "policyCode"), Text(e, "subjectEntity"), Text(e, "subjectId")) == key);
            if (stored is not null && current is { } already && JsonNode.DeepEquals(stored, already.Entry))
            {
                // Fix round, part 3: a repeat whose patch already landed. The plan holds the entry; its Signal change is either still recorded (the
                // Signals step has not finished) or done; recording it again would only make the Signals step write the same entry once more.
                continue;
            }

            var storedFingerprint = stored is null ? null : Text(stored, "fingerprint");

            // A correction never drops a hold: a stored entry presented Blocked stays as stored (its Signal too) when the after side would not present
            // it Blocked; it is marked for the reviewer, who leaves the hold through override or return (105).
            if (stored is not null && Text(stored, "decision") == nameof(BillingGroupDecision.Blocked)
                && current is not { Mapping.Decision: BillingGroupDecision.Blocked })
            {
                stored[CorrectionReviewMember] = true;
                continue;
            }

            string? raiseFingerprint = null;
            var refusal = current is { } candidate ? SignalWriter.PreflightRefusal(candidate.Mapping.Request) : null;
            if (refusal is not null)
            {
                RecordPlanRefusal(root, sample, refusal);
            }

            if (current is { } replacement && refusal is null)
            {
                if (stored is null)
                {
                    plan.Add(replacement.Entry.DeepClone());
                }
                else
                {
                    plan[plan.IndexOf(stored)] = replacement.Entry.DeepClone();
                }

                raiseFingerprint = replacement.Mapping.Fingerprint;
            }
            else if (stored is not null)
            {
                plan.Remove(stored);
            }

            RecordRuleChange(root, sample, storedFingerprint, raiseFingerprint);
        }

        static Dictionary<string, (BillingSignalMapping Mapping, JsonObject Entry)?> Entries(IReadOnlyList<BillingSignalMapping> mappings) =>
            mappings.ToDictionary(
                m => PlanKey(m.Request.PolicyCode, m.Request.SubjectEntityLogicalName, m.Request.SubjectId.ToString("D")),
                m => ((BillingSignalMapping, JsonObject)?)(m, InvoiceSubmissionPersister.SignalPlanEntryJson(m)),
                StringComparer.Ordinal);
    }

    /// <summary>A replacement the writer's preflight refuses is left out of the plan and recorded as the persister records one (049 F1-3), once.</summary>
    private static void RecordPlanRefusal(JsonObject root, SignalWriteRequest request, string code)
    {
        if (root[InvoiceSubmissionPersister.SignalPlanRefusalsMember] is not JsonArray refusals)
        {
            root[InvoiceSubmissionPersister.SignalPlanRefusalsMember] = refusals = [];
        }

        var entry = InvoiceSubmissionPersister.SignalPlanRefusalEntryJson(
            new InvoiceSubmissionPersister.SignalPlanRefusal(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, code));
        if (!refusals.Any(r => JsonNode.DeepEquals(r, entry)))
        {
            refusals.Add(entry);
        }
    }

    private static JsonObject Pending(JsonObject root)
    {
        if (root[PendingSignalChangesMember] is not JsonObject pending)
        {
            root[PendingSignalChangesMember] = pending = new JsonObject { ["rule"] = new JsonArray(), ["system"] = new JsonArray() };
        }

        if (pending["rule"] is not JsonArray)
        {
            pending["rule"] = new JsonArray();
        }

        if (pending["system"] is not JsonArray)
        {
            pending["system"] = new JsonArray();
        }

        return pending;
    }

    /// <summary>One <c>rule</c> entry per (policy, subject): the fingerprint to close is the one stored before the FIRST change, the one to raise the latest.</summary>
    private static void RecordRuleChange(JsonObject root, SignalWriteRequest request, string? storedFingerprint, string? raiseFingerprint)
    {
        var key = PlanKey(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId.ToString("D"));
        var entry = (root[PendingSignalChangesMember]?["rule"] as JsonArray ?? [])
            .OfType<JsonObject>().FirstOrDefault(e => PlanKey(Text(e, "policyCode"), Text(e, "subjectEntity"), Text(e, "subjectId")) == key);
        if (entry is null)
        {
            if (storedFingerprint is null && raiseFingerprint is null)
            {
                return;
            }

            ((JsonArray)Pending(root)["rule"]!).Add(new JsonObject
            {
                ["policyCode"] = request.PolicyCode,
                ["subjectEntity"] = request.SubjectEntityLogicalName,
                ["subjectId"] = request.SubjectId.ToString("D"),
                ["closeFingerprint"] = storedFingerprint,
                ["raiseFingerprint"] = raiseFingerprint,
            });
            return;
        }

        entry["raiseFingerprint"] = raiseFingerprint;
        if (Text(entry, "closeFingerprint") is null && raiseFingerprint is null)
        {
            ((JsonArray)entry.Parent!).Remove(entry);
        }
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

        var lines = await ReadLinesAsync(applied.InvoiceId, ct).ConfigureAwait(false);
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

            var refused = new List<(SignalWriteRequest Request, string Code)>();
            foreach (var change in ruleChanges)
            {
                await ReconcileRuleAsync(applied, root, change, refused, ct).ConfigureAwait(false);
            }

            if (systemLines.Count > 0 || clearHold)
            {
                await ReconcileSystemAsync(applied, root, lines, systemLines, clearHold, refused, ct).ConfigureAwait(false);
            }

            root.Remove(PendingSignalChangesMember);
            RecordWriteRefusals(root, refused);
            var update = new Entity(InvoiceReviewReadService.SubmissionEntity, submission.Id)
            {
                RowVersion = version.ToString(CultureInfo.InvariantCulture),
                [InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString(),
            };
            SetAdminAttention(update, submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention), root);
            await _entities.ExecuteTransactionAsync(
                [new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches }], ct).ConfigureAwait(false);
            _logger.LogInformation("Correction of {Field} on invoice {InvoiceId}: Signals reconciled ({Rule} rule changes, {System} lines with system changes, coding hold cleared: {Hold}, {Refused} refused).",
                applied.Field, applied.InvoiceId, ruleChanges.Count, systemLines.Count, clearHold, refused.Count);
        }

        await RederivePendingAsync(applied, root, lines, ct).ConfigureAwait(false);
    }

    private static bool IsCoded(JsonObject? extraction) =>
        extraction?["coding"] is JsonObject coding && Text(coding, "codeSetId") is { Length: > 0 };

    /// <summary>One <c>rule</c> entry: close the open Signal of <c>closeFingerprint</c> (unless it equals <c>raiseFingerprint</c>: a refresh), then write the plan entry of <c>raiseFingerprint</c>.</summary>
    private async Task ReconcileRuleAsync(
        CorrectionApplied applied, JsonObject root, JsonObject change, List<(SignalWriteRequest, string)> refused, CancellationToken ct)
    {
        if (Text(change, "policyCode") is not { Length: > 0 } policyCode || Text(change, "subjectEntity") is not { Length: > 0 } entity
            || !Guid.TryParse(Text(change, "subjectId"), out var subjectId))
        {
            _logger.LogWarning("Correction of {Field} on invoice {InvoiceId}: a pending Signal change names no policy or subject; skipped.", applied.Field, applied.InvoiceId);
            return;
        }

        var close = Text(change, "closeFingerprint");
        var raise = Text(change, "raiseFingerprint");
        if (close is not null && !string.Equals(close, raise, StringComparison.Ordinal))
        {
            // Raise null: the group is gone, so the latest open episode is closed whatever its fingerprint. A member that survived a failed clear can
            // read close fp0 / raise null while the open episode already carries fp1 (a repeat wrote it); matching fp0 would leave that episode open.
            // Raise set: the close is of the stored fingerprint only, the raise below then writes the next episode.
            await CloseClearedAsync(policyCode, entity, subjectId, raise is null ? null : close, ct).ConfigureAwait(false);
        }

        if (raise is null)
        {
            return;
        }

        var key = PlanKey(policyCode, entity, subjectId.ToString("D"));
        var entry = (root["signalPlan"] as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(e => PlanKey(Text(e, "policyCode"), Text(e, "subjectEntity"), Text(e, "subjectId")) == key);
        if (entry is null || !string.Equals(Text(entry, "fingerprint"), raise, StringComparison.Ordinal))
        {
            // By construction the plan holds the entry B2b-1 recorded; anything else is not raised (nothing to raise it from).
            _logger.LogWarning("Correction of {Field} on invoice {InvoiceId}: no plan entry of policy {PolicyCode} on {Subject} {SubjectId} matches the change; not raised.",
                applied.Field, applied.InvoiceId, policyCode, entity, subjectId);
            return;
        }

        var planned = InvoiceSubmissionAfterSteps.StoredFindings.ParsePlanEntry(entry);
        await WriteAsync(applied, planned.Request, planned.Fingerprint, refused, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The <c>system</c> entries and the coding hold: the system findings are re-mapped from the patched JSON (<see cref="InvoiceSubmissionAfterSteps.StoredFindings.SystemFindings"/>)
    /// under the system policy 049 reads; each named line's Signal, and the invoice's when the hold clears, is closed when none of its findings is left,
    /// else refreshed. A system policy that is not usable wrote no system Signal (049), so nothing is reconciled.
    /// </summary>
    private async Task ReconcileSystemAsync(
        CorrectionApplied applied, JsonObject root, IReadOnlyDictionary<int, StoredLine> lines, IReadOnlyList<int> systemLines, bool clearHold,
        List<(SignalWriteRequest, string)> refused, CancellationToken ct)
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
                await CloseClearedAsync(rule.PolicyCode, entity, subjectId, fingerprint: null, ct).ConfigureAwait(false);
                continue;
            }

            var mapping = BillingFindingSignalMapper.MapAll(left, rules, subjects).Single();
            await WriteAsync(applied, mapping.Request, mapping.Fingerprint, refused, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes the latest episode of (policy, subject) as ConditionCleared (record-free, as <c>SignalReevaluationJob</c> closes a cleared condition) when
    /// it is open and, if <paramref name="fingerprint"/> is given, carries it. Already resolved, absent, or another fingerprint: nothing is written.
    /// </summary>
    private async Task CloseClearedAsync(string policyCode, string entity, Guid subjectId, string? fingerprint, CancellationToken ct)
    {
        var latest = await _lifecycle.LatestEpisodeAsync(policyCode, entity, subjectId, ct).ConfigureAwait(false);
        if (latest is not { IsResolved: false } open
            || (fingerprint is not null && !string.Equals(open.Fingerprint, fingerprint, StringComparison.Ordinal)))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(open.RowVersion))
        {
            throw new InvalidOperationException("The open Signal was read without a row version.");
        }

        var outcome = await _closeSignal(open.Id, open.RowVersion, SignalWriter.ResolutionConditionCleared, policyCode, ct).ConfigureAwait(false);
        if (outcome != SignalCloseOutcome.Closed)
        {
            throw new InvalidOperationException("The Signal changed while it was being closed.");
        }
    }

    /// <summary>One write through the option-a lifecycle (B2b: a resolved latest episode reopens as n+1). A permanent refusal (049 F1-3) is recorded and the step goes on; a retryable failure fails the step.</summary>
    private async Task WriteAsync(
        CorrectionApplied applied, SignalWriteRequest request, string fingerprint, List<(SignalWriteRequest, string)> refused, CancellationToken ct)
    {
        SignalStep step;
        try
        {
            step = await _lifecycle.WriteAsync(request, fingerprint, SignalLogContext.Correction, applied.InvoiceId, ResolvedEpisodeRule.ReopenAsNextEpisode, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when ((ex is not OperationCanceledException || !ct.IsCancellationRequested)
            && InvoiceSubmissionAfterSteps.PermanentWriteRefusal(request, ex) is { } code)
        {
            step = new SignalStep(false, null, code);
        }

        if (step.RefusedCode is { } refusedCode)
        {
            _logger.LogError("Correction of {Field} on invoice {InvoiceId}: the Signal of policy {PolicyCode} on {Subject} {SubjectId} is refused permanently ({Code}); recorded in the findings.",
                applied.Field, applied.InvoiceId, request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, refusedCode);
            refused.Add((request, refusedCode));
        }

        if (step.FailureCode is { } failure)
        {
            throw new InvalidOperationException($"A Signal write did not complete ({failure}).");
        }
    }

    /// <summary>The writer's permanent refusals, in 049's <see cref="InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember"/> shape, once each.</summary>
    private static void RecordWriteRefusals(JsonObject root, IReadOnlyList<(SignalWriteRequest Request, string Code)> refused)
    {
        if (refused.Count == 0)
        {
            return;
        }

        if (root[InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember] is not JsonArray stored)
        {
            root[InvoiceSubmissionAfterSteps.SignalWriteRefusalsMember] = stored = [];
        }

        foreach (var (request, code) in refused)
        {
            var entry = new JsonObject
            {
                ["policyCode"] = request.PolicyCode,
                ["subject"] = new JsonObject { ["entity"] = request.SubjectEntityLogicalName, ["id"] = request.SubjectId.ToString("D") },
                ["code"] = code,
            };
            if (!stored.Any(e => JsonNode.DeepEquals(e, entry)))
            {
                stored.Add(entry);
            }
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

    // ── The request, rebuilt from the stored rows ──

    private sealed record CorrectionRow(Guid Id, Guid? LineId, string Field, string? Ai, string? Final, bool IsLabel);

    private sealed class RequestBasis(
        CorrectionApplied applied, Entity invoice, IReadOnlyDictionary<int, StoredLine> lines, ExtractionProvenance.SubmissionProvenance? provenance,
        IReadOnlySet<(Guid? LineId, string Field)> verified, Guid submissionId, JsonObject root, (string Entity, Guid Id) core, ResolutionFacts? resolutionBefore)
    {
        /// <summary>The request with the corrected field at <paramref name="value"/>; <paramref name="preVerified"/>: the field counts as verified (confidence 1.0);
        /// <paramref name="before"/>: the side of the diff before the correction (a timekeeper or date correction reads the line's pre-correction roster match and rate).</summary>
        public BillingEvaluationRequest Build(string? value, bool preVerified, bool before)
        {
            // A header field as the evaluator saw it: the corrected field at the value of this side; a field a reviewer corrected or confirmed at its
            // column (the correction wrote it there); any other at the value the analyzer read (the header provenance: the invoice number column
            // holds the number the submitter typed, not the one read), else its column.
            string? Header(string name)
            {
                if (applied.LineId is null && applied.Field == name)
                {
                    return value;
                }

                if (!verified.Contains(((Guid?)null, name)) && provenance is not null
                    && provenance.Header.TryGetValue(name, out var read) && read.Value is not null)
                {
                    return read.Value;
                }

                return ColumnText(name);
            }

            var confidence = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var (name, field) in provenance?.Header ?? new Dictionary<string, ExtractionProvenance.HeaderField>())
            {
                if (field.Combined is { } combined)
                {
                    confidence[name] = combined;
                }
            }

            foreach (var name in ExtractionProvenance.HeaderFieldNames.Where(n => IsVerified(null, n, preVerified)))
            {
                confidence[name] = 1m;
            }

            return new BillingEvaluationRequest
            {
                SubmissionId = submissionId,
                FirmId = invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)?.Id ?? throw new InvalidOperationException("The invoice names no firm."),
                CoreRecord = new CoreRecordReference(
                    core.Entity switch { "sprk_matter" => CoreRecordKind.Matter, "sprk_project" => CoreRecordKind.Project, _ => CoreRecordKind.WorkAssignment },
                    core.Id),
                TrackingNumber = Text(root, "trackingNumber"),
                FirmInvoiceNumber = Header(BillingFieldNames.FirmInvoiceNumber),
                InvoiceDate = DateOf(Header(BillingFieldNames.InvoiceDate)),
                ReceivedOn = DateOf(Text(root, "receivedOn")) ?? throw new InvalidOperationException("The stored findings have no received date."),
                BillingPeriodStart = DateOf(Header(BillingFieldNames.BillingPeriodStart)),
                BillingPeriodEnd = DateOf(Header(BillingFieldNames.BillingPeriodEnd)),
                Currency = Header(BillingFieldNames.Currency),
                HeaderConfidence = confidence,
                Lines = lines.Values.OrderBy(l => l.Index).Select(l => LineFacts(l, value, preVerified, before)).ToList(),
            };
        }

        /// <summary>A field the reviewer has seen counts as verified: any Corrected / Confirmed row, except that the corrected field before this correction counts only when an earlier row did.</summary>
        private bool IsVerified(Guid? lineId, string field, bool preVerified) =>
            applied.LineId == lineId && applied.Field == field ? preVerified : verified.Contains((lineId, field));

        private string? ColumnText(string name) => name switch
        {
            BillingFieldNames.FirmInvoiceNumber => invoice.GetAttributeValue<string>(InvoiceReviewService.InvInvoiceNumber),
            BillingFieldNames.InvoiceDate => DateText(invoice, InvoiceReviewService.InvInvoiceDate),
            BillingFieldNames.BillingPeriodStart => DateText(invoice, InvoiceSubmissionPersister.InvoiceBillingPeriodStart),
            BillingFieldNames.BillingPeriodEnd => DateText(invoice, InvoiceSubmissionPersister.InvoiceBillingPeriodEnd),
            BillingFieldNames.Currency => invoice.GetAttributeValue<string>(InvoiceSubmissionPersister.InvoiceCurrency),
            _ => null,
        };

        private BillingLineFacts LineFacts(StoredLine line, string? value, bool preVerified, bool before)
        {
            var row = line.Row;
            var isFee = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value == (int)InvoiceLineCostType.Fee;
            var target = applied.LineId == row.Id;
            string? Field(string name, string? stored) => target && applied.Field == name ? value : stored;

            var coding = line.Extraction?["coding"] as JsonObject;
            var hours = isFee ? DecimalOf(Field(ResolutionFieldNames.Hours, Canonical(row.GetAttributeValue<decimal?>(InvoiceSubmissionPersister.LineQuantity)))) : null;
            var rate = isFee ? DecimalOf(Field(ResolutionFieldNames.Rate, Canonical(row.GetAttributeValue<Money>(InvoiceSubmissionPersister.LineRate)?.Value))) : null;
            var amount = DecimalOf(Field(ResolutionFieldNames.Amount, Canonical(row.GetAttributeValue<Money>(InvoiceTotalsService.LineAmount)?.Value)));

            var confidence = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var (name, node) in line.Extraction?["fields"] as JsonObject ?? new JsonObject())
            {
                if (node is JsonObject field && field["combined"] is JsonValue combined && combined.TryGetValue<decimal>(out var c))
                {
                    confidence[name] = c;
                }
            }

            var observations = new List<InvoiceLineObservation>();
            if (coding?["confidence"] is JsonValue codingConfidence && codingConfidence.TryGetValue<decimal>(out var codingValue))
            {
                confidence[BillingFieldNames.TaskCode] = codingValue;
                confidence[BillingFieldNames.ActivityCode] = codingValue;
                confidence[BillingFieldNames.ExpenseCode] = codingValue;
            }

            foreach (var observation in (coding?["observations"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Enum.TryParse<InvoiceLineObservationType>(Text(observation, "type"), out var type)
                    && observation["confidence"] is JsonValue oc && oc.TryGetValue<decimal>(out var ov))
                {
                    // The quoted text is not stored; it is the same (empty) on both sides of the diff.
                    observations.Add(new InvoiceLineObservation { Type = type, Confidence = ov, QuotedText = string.Empty });
                    var key = BillingFieldNames.ObservationPrefix + type;
                    confidence[key] = confidence.TryGetValue(key, out var seen) ? Math.Max(seen, ov) : ov;
                }
            }

            foreach (var name in new[] { ResolutionFieldNames.Hours, ResolutionFieldNames.Rate, ResolutionFieldNames.Amount, BillingFieldNames.Narrative,
                BillingFieldNames.TaskCode, BillingFieldNames.ActivityCode, BillingFieldNames.ExpenseCode,
                ResolutionFieldNames.Date, ResolutionFieldNames.TimekeeperAsBilled })
            {
                if (IsVerified(row.Id, name, preVerified))
                {
                    confidence[name] = 1m;
                }
            }

            var timekeeperId = row.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.LineResolvedTimekeeper)?.Id;
            var approvedRate = row.GetAttributeValue<Money>(InvoiceSubmissionPersister.LineApprovedRate)?.Value;
            var timekeeperReason = TimekeeperUnmatchedReason.None;
            var rateStatus = RateResolutionStatus.NotFound;
            var rateReason = RateNotFoundReason.None;
            if (line.Extraction?[ResolutionChangeMember] is JsonObject change)
            {
                var after = change["after"] as JsonObject;
                timekeeperReason = Enum.TryParse<TimekeeperUnmatchedReason>(after is null ? null : Text(after, "timekeeperReason"), out var tr) ? tr : timekeeperReason;
                rateStatus = Enum.TryParse<RateResolutionStatus>(after is null ? null : Text(after, "rateStatus"), out var rs) ? rs : rateStatus;
                rateReason = Enum.TryParse<RateNotFoundReason>(after is null ? null : Text(after, "rateReason"), out var rr) ? rr : rateReason;
            }

            if (before && target && resolutionBefore is { } was)
            {
                // A timekeeper or date correction (fix round, part 3): the stored columns hold the pair the latest correction gave; the "before" side is
                // the pair the stored findings were last patched with (see ResolutionBefore), so a correction whose findings step never ran is diffed too.
                timekeeperId = was.TimekeeperId;
                approvedRate = was.ApprovedRate;
                (timekeeperReason, rateStatus, rateReason) = (was.TimekeeperReason, was.RateStatus, was.RateReason);
            }

            return new BillingLineFacts
            {
                Index = line.Index,
                LineType = isFee ? InvoiceLineType.Fee : InvoiceLineType.Expense,
                Date = DateOf(Field(ResolutionFieldNames.Date, DateText(row, InvoiceSubmissionPersister.LineEventDate))),
                TimekeeperName = Field(ResolutionFieldNames.TimekeeperAsBilled, row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTimekeeper)),
                Timekeeper = !isFee ? null
                    : timekeeperId is { } tk && tk != Guid.Empty
                        ? new TimekeeperResolution { TimekeeperId = tk, Stage = TimekeeperMatchStage.Exact, MatchConfidence = 1m }
                        : new TimekeeperResolution { Reason = timekeeperReason },
                ApprovedRate = !isFee ? new RateResolution { Status = RateResolutionStatus.NotApplicable }
                    : approvedRate is { } approved ? new RateResolution { Status = RateResolutionStatus.Found, ApprovedRate = approved }
                    : new RateResolution { Status = rateStatus == RateResolutionStatus.Found ? RateResolutionStatus.NotFound : rateStatus, Reason = rateReason },
                Hours = hours,
                BilledRate = rate,
                Amount = amount,
                Narrative = Field(BillingFieldNames.Narrative, row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineDescription)),
                TaskCode = Field(BillingFieldNames.TaskCode, CodeOf(coding, "task")),
                ActivityCode = Field(BillingFieldNames.ActivityCode, CodeOf(coding, "activity")),
                ExpenseCode = Field(BillingFieldNames.ExpenseCode, CodeOf(coding, "expense")),
                BilledTaskCode = row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTaskCodeAsBilled),
                BilledActivityCode = row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineActivityCodeAsBilled),
                BilledExpenseCode = row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExpenseCodeAsBilled),
                Observations = observations,
                FieldConfidence = confidence,
            };
        }

        /// <summary>The unmatched / not-found reasons of a line as the first evaluation stored them: the reason codes of its ApprovedTimekeeper and ApprovedRate findings.</summary>
        internal static (TimekeeperUnmatchedReason, RateResolutionStatus, RateNotFoundReason) StoredReasons(JsonObject root, int line)
        {
            var timekeeper = TimekeeperUnmatchedReason.None;
            var status = RateResolutionStatus.NotFound;
            var reason = RateNotFoundReason.None;
            foreach (var entry in (root["findings"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(e => Text(e, "subject") == nameof(BillingSubjectKind.Line) && IntOf(e["line"]) == line))
            {
                switch (Text(entry, "reasonCode"))
                {
                    case BillingReasonCodes.TimekeeperNameMissing: timekeeper = TimekeeperUnmatchedReason.NameMissing; break;
                    case BillingReasonCodes.TimekeeperLineDateMissing: timekeeper = TimekeeperUnmatchedReason.LineDateMissing; break;
                    case BillingReasonCodes.TimekeeperNameIncomplete: timekeeper = TimekeeperUnmatchedReason.NameIncomplete; break;
                    case BillingReasonCodes.TimekeeperNotOnRoster: timekeeper = TimekeeperUnmatchedReason.NotOnRoster; break;
                    case BillingReasonCodes.TimekeeperNotApprovedOnLineDate: timekeeper = TimekeeperUnmatchedReason.NotApprovedOnLineDate; break;
                    case BillingReasonCodes.TimekeeperAmbiguous: timekeeper = TimekeeperUnmatchedReason.Ambiguous; break;
                    case BillingReasonCodes.TimekeeperAiBelowThreshold: timekeeper = TimekeeperUnmatchedReason.AiBelowThreshold; break;
                    case BillingReasonCodes.TimekeeperAiNotClearlyAhead: timekeeper = TimekeeperUnmatchedReason.AiNotClearlyAhead; break;
                    case BillingReasonCodes.TimekeeperAiUnavailable: timekeeper = TimekeeperUnmatchedReason.AiUnavailable; break;
                    case BillingReasonCodes.RateAmbiguous: status = RateResolutionStatus.Ambiguous; break;
                    case BillingReasonCodes.RateTimekeeperUnmatched: reason = RateNotFoundReason.TimekeeperUnmatched; break;
                    case BillingReasonCodes.RateLineDateMissing: reason = RateNotFoundReason.LineDateMissing; break;
                    case BillingReasonCodes.RateNoRateInForce: reason = RateNotFoundReason.NoRateInForce; break;
                }
            }

            return (timekeeper, status, reason);
        }

        private static string? CodeOf(JsonObject? coding, string member) =>
            coding?[member] is JsonObject slot && Text(slot, "code") is { } code && !string.IsNullOrWhiteSpace(code) ? code.Trim() : null;

        private static string? Canonical(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static decimal? DecimalOf(string? text) =>
            decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;

        private static string? DateText(Entity row, string column) =>
            row.GetAttributeValue<DateTime?>(column) is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
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

    /// <summary>The invoice's fee and expense lines by index (<c>sprk_linesequence</c> - 1, the persister's key); Adjustment lines are not evaluated.</summary>
    private async Task<IReadOnlyDictionary<int, StoredLine>> ReadLinesAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.LineEntity)
        {
            ColumnSet = new ColumnSet(InvoiceReviewReadService.LineColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
        query.AddOrder(InvoiceSubmissionPersister.LineSequence, OrderType.Ascending);
        var lines = new Dictionary<int, StoredLine>();
        foreach (var row in await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false))
        {
            if (row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value is not ((int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense)
                || row.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) is not { } sequence || sequence < 1)
            {
                continue;
            }

            JsonObject? extraction = null;
            if (row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExtractionConfidence) is { Length: > 0 } text)
            {
                try
                {
                    extraction = JsonNode.Parse(text) as JsonObject;
                }
                catch (JsonException)
                {
                    extraction = null;
                }
            }

            var status = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value;
            var decided = status is (int)InvoiceLineStatus.Accepted or (int)InvoiceLineStatus.Reduced or (int)InvoiceLineStatus.Disallowed;
            if (!lines.TryAdd(sequence - 1, new StoredLine(sequence - 1, row, extraction, decided)))
            {
                throw new InvalidOperationException("Two lines of the invoice share a sequence.");
            }
        }

        return lines;
    }

    /// <summary>Every correction row of the invoice, oldest first (the order the correction service reads them in).</summary>
    private async Task<IReadOnlyList<CorrectionRow>> ReadCorrectionsAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceLineDecisionService.CorrectionEntity)
        {
            ColumnSet = new ColumnSet(CorrectionColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_invoice", ConditionOperator.Equal, invoiceId);
        query.AddOrder($"{InvoiceLineDecisionService.CorrectionEntity}id", OrderType.Ascending);
        var rows = await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false);
        return rows
            .OrderBy(r => r.GetAttributeValue<DateTime?>("sprk_reviewedon") ?? r.GetAttributeValue<DateTime?>("createdon") ?? DateTime.MinValue)
            .ThenBy(r => r.GetAttributeValue<DateTime?>("createdon"))
            .Select(r => new CorrectionRow(
                r.Id,
                r.GetAttributeValue<EntityReference>("sprk_invoiceline")?.Id,
                r.GetAttributeValue<string>("sprk_fieldname") ?? string.Empty,
                r.GetAttributeValue<string>("sprk_aivalue"),
                r.GetAttributeValue<string>("sprk_finalvalue"),
                r.GetAttributeValue<OptionSetValue>("sprk_outcometype")?.Value is InvoiceCorrectionService.OutcomeCorrected or InvoiceCorrectionService.OutcomeConfirmed))
            .ToList();
    }

    private Task<Entity?> ReadSubmissionAsync(Guid invoiceId, CancellationToken ct) => ReadSubmissionAsync(_entities, invoiceId, ct);

    /// <summary>The invoice's latest submission (its findings JSON, version and administrator text), read app-only.</summary>
    private static async Task<Entity?> ReadSubmissionAsync(IGenericEntityService entities, Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(InvoiceSubmissionPersister.SubmissionFindingsJson, InvoiceTotalsService.VersionNumber, InvoiceSubmissionPersister.SubmissionAdminAttention, "createdon"),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.Equal, invoiceId);
        query.AddOrder("createdon", OrderType.Descending);
        return (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
    }

    /// <summary>
    /// Fix round, part 3 (F-2): the Corrected / Confirmed correction rows of the invoice, of a field this follow-through handles (re-evaluated, or one that
    /// feeds the totals, TotalDue included), that are not listed in the latest submission's <see cref="AppliedCorrectionsMember"/>: corrections whose value
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

    // ── JSON helpers ──

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? IntOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static DateOnly? DateOf(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
