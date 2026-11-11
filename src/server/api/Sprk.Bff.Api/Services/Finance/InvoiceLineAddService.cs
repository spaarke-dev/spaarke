using System.Data;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using static Sprk.Bff.Api.Services.Finance.InvoiceFindingsDiff;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// Request to add one Fee or Expense line a firm did not bill (ebilling-r1 task 164, EB-D-188). The body never carries a total, a status, an owner or a
/// person: the decider is the caller's <c>systemuserid</c>, resolved by the endpoint from the caller's own token.
/// </summary>
/// <param name="RequestId">The client's id for this add: a retry carries the same id. The decision id and the line id are derived from it and the invoice.</param>
/// <param name="CostType"><c>fee</c> or <c>expense</c>.</param>
/// <param name="Timekeeper">A Fee line's timekeeper as the reviewer types it (resolved against the firm's roster, as intake resolves a billed name).</param>
/// <param name="Hours">A Fee line's hours (with <paramref name="Rate"/>), or none when only an amount is given.</param>
/// <param name="Amount">The line amount. For a Fee with hours and rate it may be left out (it is hours times rate, to the cent) or must equal that.</param>
public sealed record AddLineCommand(
    Guid InvoiceId,
    Guid RequestId,
    string? CostType,
    DateOnly? Date,
    string? Timekeeper,
    decimal? Hours,
    decimal? Rate,
    decimal? Amount,
    string? TaskCode,
    string? ActivityCode,
    string? ExpenseCode,
    string? Description,
    string? Reason,
    Guid DeciderSystemUserId);

/// <summary>Outcome class of an Add line attempt. The endpoint maps each to one HTTP status and one stable code.</summary>
public enum AddLineStatus
{
    Applied,
    InvalidRequest,
    InvoiceNotFound,
    NotInReview,

    /// <summary>EB-D-185 / EB-D-197 (<see cref="InvoiceFirmGate"/>): no firm, held for one, or the Set-firm re-run pending. 409, nothing written.</summary>
    FirmMissing,

    /// <summary>A correction of the invoice was saved but not followed through (<see cref="InvoiceCorrectionReevaluation.ReadUnappliedAsync"/>).</summary>
    CorrectionsPending,

    /// <summary>No code set resolves for the invoice (FR-22): no typed code can be checked. 409, nothing written.</summary>
    CodeSetUnavailable,

    /// <summary>The added line would bring in a Procedure gap the stored findings do not have (R15). 409, nothing written.</summary>
    ProcedureGap,
    TotalsRefused,

    /// <summary>The request id was used for a line with other content. 409, nothing written.</summary>
    IdReused,
    Busy,

    /// <summary>The submission, the invoice or a stored line changed between the read and the commit. 409, nothing written.</summary>
    Changed,
    OwnerUnresolved,

    /// <summary>A read, the lock or the commit failed and NOTHING is known to have changed (or a repeat of the same request is safe).</summary>
    Unavailable,

    /// <summary>The line was committed but a later step (totals, record or Signals) did not finish: 503 with <see cref="AddLineResult.FailedStep"/>.
    /// A repeat of the same request completes it (one line, one record).</summary>
    Incomplete,

    /// <summary>082d fix round 2: the patched findings would exceed <c>sprk_findingsjson</c>'s column even without the Procedure snapshot. 409, nothing written.
    /// Last in the enum so every earlier member keeps its numeric value.</summary>
    TooLarge,
}

/// <summary>Stable ProblemDetails codes of Add line (ADR-019). New codes are <c>ebilling.line_add.*</c>; the rest reuse the line decision codes.</summary>
public static class AddLineCodes
{
    public const string Invalid = "ebilling.line_add.invalid";
    public const string AmountMismatch = "ebilling.line_add.amount_mismatch";
    public const string CodeNotAllowed = "ebilling.line_add.code_not_allowed";
    public const string CodeSetUnavailable = "ebilling.line_add.code_set_unavailable";
    public const string ProcedureGap = "ebilling.line_add.procedure_gap";
    public const string IdReused = "ebilling.line_add.id_reused";
    public const string Changed = "ebilling.line_add.changed";
    public const string FirmMissing = "ebilling.line_add.firm_missing";
    public const string TooLarge = "ebilling.line_add.too_large";

    public const string ReasonRequired = LineDecisionCodes.ReasonRequired;
    public const string ReasonTooLong = LineDecisionCodes.ReasonTooLong;
    public const string NotInReview = LineDecisionCodes.NotInReview;
    public const string CorrectionsPending = LineDecisionCodes.CorrectionsPending;
    public const string TotalsRefused = LineDecisionCodes.TotalsRefused;
    public const string Busy = LineDecisionCodes.Busy;
    public const string OwnerUnresolved = LineDecisionCodes.OwnerUnresolved;
    public const string Unavailable = LineDecisionCodes.Unavailable;
    public const string DecisionInvalid = LineDecisionCodes.DecisionInvalid;
    public const string DeciderUnresolved = LineDecisionCodes.DeciderUnresolved;
    public const string Incomplete = LineDecisionCodes.Incomplete;

    /// <summary>The sentence with <see cref="FirmMissing"/> for <paramref name="gate"/>.</summary>
    public static string FirmDetailFor(InvoiceFirmGateResult gate) => gate switch
    {
        InvoiceFirmGateResult.HeldForFirm => "This invoice is waiting for its firm to be assigned. A line can be added once the firm is assigned; nothing was changed.",
        InvoiceFirmGateResult.RerunPending => "This invoice's firm was just assigned and its billing rules are being checked again. Try again when the check finishes; nothing was changed.",
        _ => "This invoice has no firm. Add the firm before adding a line; nothing was changed.",
    };
}

/// <summary>The outcome of an Add line attempt. <see cref="FailedStep"/> names the step of an <see cref="AddLineStatus.Incomplete"/> add
/// (<c>totals</c>, <c>record</c> or <c>signals</c>).</summary>
public sealed record AddLineResult(
    AddLineStatus Status,
    string Code,
    string? Detail = null,
    Guid? LineId = null,
    Guid? DecisionId = null,
    string? LineStatus = null,
    bool AlreadyApplied = false,
    ReviewTotalsDto? Totals = null,
    string? FailedStep = null)
{
    public bool IsApplied => Status == AddLineStatus.Applied;
}

/// <summary>
/// Add line (ebilling-r1 task 164, EB-D-188; brief <c>notes/add-line-brief.md</c> section 3): a reviewer adds one Fee or Expense line to an invoice that is
/// In Review and has a firm. Steps 0 to 6 (164a-4a): the shape checks, the lock and the reads, the resolution of the codes and the timekeeper,
/// the evaluation through the shared before/after diff, the totals dry run and the Decision Record preflight, then ONE commit. Steps 7 to 9 (164a-4b):
/// the stored totals (EB-D-199) and the re-index, the <c>add-line</c> Decision Record, and the Signals.
/// </summary>
/// <remarks>
/// <para><b>Rights.</b> The route asks the caller's Write on the invoice as the caller (<c>ebilling.invoice.decide</c>, D-77) before this runs; the line is
/// written by the app identity (person roles hold no Create on <c>sprk_billingevent</c>), and the caller is stamped as <c>sprk_createdbyperson</c>.</para>
/// <para><b>Order.</b> Step 0, the shape (400), reads nothing. Step 1 takes the per-invoice lock (<see cref="InvoiceLineDecisionService.LockKeyFor"/>, so
/// an add, a line decision, an adjustment, approve and the Set-firm re-run never interleave) and reads app-only: the invoice, then the derived row (a
/// repeat of the same content by the same caller answers <c>alreadyApplied</c>; other content or another caller is <c>id_reused</c>), then, for a first
/// add only, In Review, the single core record, the firm gate (<see cref="InvoiceFirmGate"/>: no firm, held for one, or the re-run pending is 409) and an
/// unapplied correction (409). A repeat is looked up before those refusals so that a committed add can still finish its record (164a fix round). Steps 2 to 5
/// write nothing. Step 6 is one transaction: the line create, the submission's findings (conditioned on its row version), the status of each stored line
/// the add touched (conditioned on its version) and a version-only update of the invoice (conditioned on its version). From the commit on the request's
/// cancellation no longer applies.</para>
/// <para><b>The id contract (164a-2, EB-D-198).</b> The decision id is derived from (invoice, request id); the row id is
/// <see cref="ExtractionProvenance.AddedLineIdFor"/> of the invoice and the decision id, and the row's <c>sprk_extractionconfidence</c> carries
/// <see cref="ExtractionProvenance.ForReviewerAddedLine"/> of the same id, so <see cref="ExtractionProvenance.IsReviewerAdded"/> reads it as reviewer-added
/// and a retry addresses the same row (one line, one record).</para>
/// <para><b>The rules.</b> Only through <see cref="InvoiceFindingsDiff"/> (an added-line <see cref="InvoiceFindingsDiff.RequestBasis"/>) and the pure
/// evaluator: the facts are loaded once, the evaluator runs on the stored lines and on the stored lines plus the new one, and the stored findings and plan
/// are patched with the difference. Decided lines and Blocked plan entries are kept as stored; no 073 runs; a new Procedure gap is 409. The added row is
/// built so that every later rebuild reads it the same way: its as-billed codes are the typed codes (D-117), and its extraction JSON holds the field
/// confidence (1.0, reviewer-typed), the code set and the timekeeper's unmatched and rate reasons (<c>resolutionChange.after</c>).</para>
/// <para><b>After the commit (164a-4b), with the request's cancellation no longer applying.</b> Step 7: the totals through 016
/// (<see cref="IInvoiceTotalsService.RecalculateAsync"/>, EB-D-199), then the re-index (best effort). Step 8: ONE <c>add-line</c> record through
/// <see cref="BillingDecisionRecorder.RecordAsync"/>, idempotent on the decision id. Step 9: the plan changes the commit recorded in
/// <see cref="InvoiceCorrectionReevaluation.PendingSignalChangesMember"/> (<c>rule</c>, as a correction records them) are reconciled through
/// <see cref="InvoiceRuleSignalReconciler"/>, the correction's own reconcile (a new group: episode 1; the same fingerprint: a refresh; another:
/// Superseded, record-free, then n+1; a group that is gone: ConditionCleared, record-free), then ONE row-version-conditional write removes them and
/// moves <see cref="AddLineMember"/> to <see cref="StateDone"/>. The member stays where the correction's Signals step reads it, so whichever runs first
/// reconciles an entry and removes it; the other finds it gone. A correction takes no processing lock, so the two can overlap: a Signal write is unique on
/// <c>sprk_dedupekey</c>, a close and the member's removal are conditional on the row version, and the run that loses a condition fails its step (503
/// Incomplete) and converges on its repeat. No hold link is written
/// (EB-D-198 Q7): Add line never clears a hold reason, and a stored Blocked entry is kept by the diff. A failure at step 7, 8 or 9 is
/// <see cref="AddLineStatus.Incomplete"/> naming the step.</para>
/// <para><b>The resume.</b> A repeat finds its row (the derived id) with the same content and answers <c>alreadyApplied</c>. Unless
/// <see cref="AddLineMember"/> names this decision at <see cref="StateDone"/>, steps 7 to 9 run again (each is idempotent), so a repeat after an
/// Incomplete add finishes it with one record. <see cref="AddLineMember"/> is a single member: a later add overwrites it, and then a repeat of the
/// earlier add cannot tell whether it finished, so it runs the steps again. Step 8 runs on EVERY repeat, and on a repeat whose invoice is no longer In
/// Review it is the only step that runs (steps 7 and 9 need the invoice In Review). Step 7 failing does not skip step 8. The recorder refuses the same
/// decision id with other content (another reason, which the row does not store); only that refusal is answered <c>id_reused</c>.</para>
/// <para><b>Logging</b> carries ids, counts and codes only (ADR-015): never a description, a name, an amount or the reason.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="InvoiceAdjustmentService"/> (creates Adjustment lines),
/// <see cref="InvoiceLineDecisionService"/> (decides lines), <see cref="InvoiceCorrectionReevaluation"/> (re-runs the rules after a correction), the
/// submission persister (creates intake lines). Extension: none adds a Fee or Expense line after intake, and giving one of them that write gives it a second
/// invariant (ADR-002); this composes their parts (the lock, the diff, the totals calculator, the owner resolver, the recorder's preflight) and copies
/// none. Cost of doing nothing: a reviewer cannot add a line the firm did not bill (EB-D-188).</para>
/// </remarks>
public sealed class InvoiceLineAddService
{
    public const int ReasonMaxLength = InvoiceLineDecisionService.ReasonMaxLength;

    /// <summary>The <c>sprk_description</c> column's maximum length.</summary>
    public const int DescriptionMaxLength = 2000;

    /// <summary>The longest code a reviewer may type (the UTBMS codes are four characters).</summary>
    public const int CodeMaxLength = 20;

    /// <summary>The longest timekeeper name a reviewer may type (the <c>sprk_timekeeper</c> text column).</summary>
    public const int TimekeeperMaxLength = 200;

    /// <summary>The largest amount, rate or hours value accepted (the money columns' range).</summary>
    public const decimal MaxValue = InvoiceAdjustmentService.MaxValue;

    public const string ActionCode = "add-line";
    public const string OutcomeAdded = "Added";
    public const string DeciderRole = InvoiceLineDecisionService.DeciderRole;

    /// <summary>The <c>lineFindings[]</c> code of a reviewer-added line (R10): it flags the line, so it is decided like any other.</summary>
    public const string LineFindingReviewerAdded = "line.reviewer_added";

    internal const string LineFindingReviewerAddedMessage = "A reviewer added this line; it was not billed by the firm.";

    /// <summary>
    /// The findings JSON member the commit writes: <c>{ decisionId, lineId, state }</c>. <c>state</c> is <see cref="StateCommitted"/> after step 6;
    /// task 164a-4b's later steps move it on. Ids and a state word only.
    /// </summary>
    public const string AddLineMember = "addLine";

    public const string StateCommitted = "committed";

    /// <summary>Steps 7 to 9 finished for the add named in <see cref="AddLineMember"/>.</summary>
    public const string StateDone = "done";

    /// <summary>The <see cref="AddLineResult.FailedStep"/> values.</summary>
    public const string TotalsStep = "totals";
    public const string RecordStep = "record";
    public const string SignalsStep = "signals";

    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

    private static readonly string[] InvoiceColumns =
    [
        "statuscode", InvoiceTotalsService.VersionNumber, InvoiceTotalsService.InvoiceBilledTotal, InvoiceSubmissionPersister.InvoiceVendorOrg,
        InvoiceReviewService.InvInvoiceNumber, InvoiceReviewService.InvInvoiceDate, InvoiceReviewService.InvTrackingNumber,
        InvoiceSubmissionPersister.InvoiceBillingPeriodStart, InvoiceSubmissionPersister.InvoiceBillingPeriodEnd, InvoiceSubmissionPersister.InvoiceCurrency,
        "sprk_matter", "sprk_project", "sprk_regardingworkassignment",
        CoreAncestorResolver.RegardingRecordTypeColumn, CoreAncestorResolver.RegardingRecordIdColumn,
        "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber",
    ];

    private static readonly string[] ExistingColumns =
    [
        InvoiceTotalsService.LineInvoiceLookup, InvoiceTotalsService.LineCostType, InvoiceTotalsService.LineStatus, InvoiceTotalsService.LineAmount,
        InvoiceSubmissionPersister.LineQuantity, InvoiceSubmissionPersister.LineRate, InvoiceSubmissionPersister.LineEventDate,
        InvoiceSubmissionPersister.LineTimekeeper, InvoiceSubmissionPersister.LineDescription, InvoiceSubmissionPersister.LineTaskCodeAsBilled,
        InvoiceSubmissionPersister.LineActivityCodeAsBilled, InvoiceSubmissionPersister.LineExpenseCodeAsBilled,
        InvoiceSubmissionPersister.LineExtractionConfidence, InvoiceSubmissionPersister.LineSequence, InvoiceSubmissionPersister.LineResolvedTimekeeper,
        RecordCreatorPerson.Column, // the caller who added the line: a repeat by another caller is id_reused (164a fix round K2)
    ];

    private readonly IGenericEntityService _entities;
    private readonly CodeSetResolver _codeSets;
    private readonly Func<InvoiceResolutionInput, CancellationToken, Task<InvoiceResolutionResult>> _resolve;
    private readonly Func<BillingEvaluationRequest, CancellationToken, Task<BillingLoadedFacts>> _load;
    private readonly Func<BillingEvaluationRequest, BillingLoadedFacts, BillingEvaluationResult> _evaluate;
    private readonly BillingDecisionRecorder _recorder;
    private readonly IIdempotencyService _locks;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly Func<Guid, CancellationToken, Task<InvoiceTotalsCalculation>> _recalculate;
    private readonly SignalEpisodeLifecycle _lifecycle;
    private readonly Func<Guid, string, int, string, CancellationToken, Task<SignalCloseOutcome>> _closeSignal;
    private readonly JobSubmissionService? _jobs;
    private readonly ILogger<InvoiceLineAddService> _logger;
    private readonly TimeProvider _time;

    public InvoiceLineAddService(
        IGenericEntityService entities,
        CodeSetResolver codeSets,
        IInvoiceResolutionService resolution,
        BillingFactLoader loader,
        InvoiceComplianceEvaluator evaluator,
        BillingDecisionRecorder recorder,
        IIdempotencyService locks,
        IRecordOwnershipResolver ownership,
        IInvoiceTotalsService totals,
        SignalWriter signals,
        ILogger<InvoiceLineAddService> logger,
        JobSubmissionService? jobs = null)
        : this(
            entities, codeSets,
            (resolution ?? throw new ArgumentNullException(nameof(resolution))).ResolveAsync,
            (loader ?? throw new ArgumentNullException(nameof(loader))).LoadAsync,
            (evaluator ?? throw new ArgumentNullException(nameof(evaluator))).Evaluate,
            recorder, locks, ownership,
            (totals ?? throw new ArgumentNullException(nameof(totals))).RecalculateAsync,
            (signals ?? throw new ArgumentNullException(nameof(signals))).WriteAsync,
            (id, rowVersion, resolution, policyCode, ct) => signals.CloseAsync(id, rowVersion, resolution, policyCode, ct),
            logger, time: null, jobs: jobs)
    {
    }

    /// <summary>The unit-test seam: the resolution, the fact load, the pure evaluation, the totals and the Signal writer's write and close are passed as
    /// calls (the correction re-evaluation's pattern).</summary>
    internal InvoiceLineAddService(
        IGenericEntityService entities,
        CodeSetResolver codeSets,
        Func<InvoiceResolutionInput, CancellationToken, Task<InvoiceResolutionResult>> resolve,
        Func<BillingEvaluationRequest, CancellationToken, Task<BillingLoadedFacts>> load,
        Func<BillingEvaluationRequest, BillingLoadedFacts, BillingEvaluationResult> evaluate,
        BillingDecisionRecorder recorder,
        IIdempotencyService locks,
        IRecordOwnershipResolver ownership,
        Func<Guid, CancellationToken, Task<InvoiceTotalsCalculation>> recalculate,
        Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>> writeSignal,
        Func<Guid, string, int, string, CancellationToken, Task<SignalCloseOutcome>> closeSignal,
        ILogger<InvoiceLineAddService> logger,
        TimeProvider? time = null,
        JobSubmissionService? jobs = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _codeSets = codeSets ?? throw new ArgumentNullException(nameof(codeSets));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _recalculate = recalculate ?? throw new ArgumentNullException(nameof(recalculate));
        _closeSignal = closeSignal ?? throw new ArgumentNullException(nameof(closeSignal));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(writeSignal);
        _lifecycle = new SignalEpisodeLifecycle(
            entities, writeSignal, (id, rowVersion, policyCode, ct) => closeSignal(id, rowVersion, SignalWriter.ResolutionSuperseded, policyCode, ct), logger);
        _jobs = jobs; // null when no queue is configured: no re-index is queued
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The decision id of an add: the client's request id hashed with the invoice, so another invoice's request id can never collide with it.</summary>
    internal static Guid DecisionIdFor(Guid invoiceId, Guid requestId) =>
        InvoiceLineDecisionService.IdOf($"ebilling-add-line-{invoiceId:D}-{requestId:D}");

    /// <summary>The checked request (step 0): trimmed text, the cost type, and the amount (hours times rate when only those were given).</summary>
    internal sealed record Shape(
        InvoiceLineCostType CostType, DateOnly Date, string? Timekeeper, decimal? Hours, decimal? Rate, decimal Amount,
        string? TaskCode, string? ActivityCode, string? ExpenseCode, string Description, string Reason);

    public async Task<AddLineResult> AddAsync(AddLineCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (ShapeError(command, out var shape) is { } invalid)
        {
            return invalid;
        }

        if (command.DeciderSystemUserId == Guid.Empty)
        {
            return new AddLineResult(AddLineStatus.InvalidRequest, AddLineCodes.DeciderUnresolved, "The decider could not be identified; nothing was changed.");
        }

        var key = InvoiceLineDecisionService.LockKeyFor(command.InvoiceId);
        var owner = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        bool acquired;
        try
        {
            acquired = await _locks.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Add line on invoice {InvoiceId}: the processing lock could not be taken ({ExceptionType}); nothing changed.", command.InvoiceId, ex.GetType().Name);
            return new AddLineResult(AddLineStatus.Unavailable, AddLineCodes.Unavailable, "The line could not be added; nothing was changed. Try again.");
        }

        if (!acquired)
        {
            return new AddLineResult(AddLineStatus.Busy, AddLineCodes.Busy, "Another change to this invoice is in progress. Try again.");
        }

        try
        {
            return await ApplyAsync(command, shape!, ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _locks.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Add line on invoice {InvoiceId}: the processing lock could not be released ({ExceptionType}); it expires.", command.InvoiceId, ex.GetType().Name);
            }
        }
    }

    // ── Step 0: the shape (400), no I/O ──

    /// <summary>The request-shape rules that need no data (400). Null when the request is well formed; <paramref name="shape"/> is then set.</summary>
    internal static AddLineResult? ShapeError(AddLineCommand command, out Shape? shape)
    {
        static AddLineResult Bad(string code, string detail) => new(AddLineStatus.InvalidRequest, code, detail);

        shape = null;
        if (command.InvoiceId == Guid.Empty || command.RequestId == Guid.Empty)
        {
            return Bad(AddLineCodes.Invalid, "An invoice id and a request id are required.");
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Bad(AddLineCodes.ReasonRequired, "A reason is required to add a line.");
        }

        if (reason.Length > ReasonMaxLength)
        {
            return Bad(AddLineCodes.ReasonTooLong, $"The reason is at most {ReasonMaxLength} characters.");
        }

        var description = command.Description?.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            return Bad(AddLineCodes.Invalid, "A description is required.");
        }

        if (description.Length > DescriptionMaxLength)
        {
            return Bad(AddLineCodes.Invalid, $"The description is at most {DescriptionMaxLength} characters.");
        }

        InvoiceLineCostType type;
        switch (command.CostType?.Trim().ToLowerInvariant())
        {
            case "fee":
                type = InvoiceLineCostType.Fee;
                break;
            case "expense":
                type = InvoiceLineCostType.Expense;
                break;
            default:
                return Bad(AddLineCodes.Invalid, "The cost type is fee or expense.");
        }

        if (command.Date is not { } date)
        {
            return Bad(AddLineCodes.Invalid, "The line date is required.");
        }

        var task = Code(command.TaskCode);
        var activity = Code(command.ActivityCode);
        var expense = Code(command.ExpenseCode);
        if (new[] { task, activity, expense }.Any(c => c is { Length: > CodeMaxLength }))
        {
            return Bad(AddLineCodes.Invalid, $"A code is at most {CodeMaxLength} characters.");
        }

        var timekeeper = command.Timekeeper?.Trim();
        decimal amount;
        if (type == InvoiceLineCostType.Fee)
        {
            if (string.IsNullOrWhiteSpace(timekeeper))
            {
                return Bad(AddLineCodes.Invalid, "A fee line needs its timekeeper.");
            }

            if (timekeeper.Length > TimekeeperMaxLength)
            {
                return Bad(AddLineCodes.Invalid, $"The timekeeper is at most {TimekeeperMaxLength} characters.");
            }

            if (expense is not null)
            {
                return Bad(AddLineCodes.Invalid, "A fee line takes a task and an activity code, not an expense code.");
            }

            if (command.Hours.HasValue != command.Rate.HasValue)
            {
                return Bad(AddLineCodes.Invalid, "A fee line has hours and a rate together, or an amount.");
            }

            if (command.Hours is { } hours && command.Rate is { } rate)
            {
                if (hours <= 0m || hours > MaxValue || decimal.Round(hours, 2) != hours)
                {
                    return Bad(AddLineCodes.Invalid, "The hours are above 0, with at most 2 decimals.");
                }

                if (rate <= 0m || rate > MaxValue || decimal.Round(rate, 2) != rate)
                {
                    return Bad(AddLineCodes.Invalid, "The rate is above 0, with at most 2 decimals.");
                }

                var computed = decimal.Round(hours * rate, 2, MidpointRounding.AwayFromZero);
                if (command.Amount is { } given && given != computed)
                {
                    return Bad(AddLineCodes.AmountMismatch, "The amount is not the hours times the rate.");
                }

                amount = computed;
            }
            else if (command.Amount is { } only)
            {
                amount = only;
            }
            else
            {
                return Bad(AddLineCodes.Invalid, "A fee line has hours and a rate, or an amount.");
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(timekeeper) || command.Hours is not null || command.Rate is not null)
            {
                return Bad(AddLineCodes.Invalid, "An expense line has an amount; it takes no timekeeper, hours or rate.");
            }

            if (task is not null || activity is not null)
            {
                return Bad(AddLineCodes.Invalid, "An expense line takes an expense code, not a task or an activity code.");
            }

            if (command.Amount is not { } given)
            {
                return Bad(AddLineCodes.Invalid, "An expense line needs its amount.");
            }

            amount = given;
            timekeeper = null;
        }

        if (amount == 0m || Math.Abs(amount) > MaxValue || decimal.Round(amount, 2) != amount)
        {
            return Bad(AddLineCodes.Invalid, "The amount is not zero, with at most 2 decimals.");
        }

        shape = new Shape(
            type, date, timekeeper, type == InvoiceLineCostType.Fee ? command.Hours : null, type == InvoiceLineCostType.Fee ? command.Rate : null, amount,
            task, activity, expense, description, reason);
        return null;

        static string? Code(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    // ── Steps 1 to 6 ──

    private sealed record Reads(
        Entity Invoice, (string Entity, Guid Id) Core, List<Entity> AllRows, IReadOnlyDictionary<int, StoredLine> Lines, Entity Submission, JsonObject Root,
        long InvoiceVersion, long SubmissionVersion, IReadOnlySet<(Guid? LineId, string Field)> Verified);

    private async Task<AddLineResult> ApplyAsync(AddLineCommand command, Shape shape, CancellationToken ct)
    {
        var decisionId = DecisionIdFor(command.InvoiceId, command.RequestId);
        var lineId = ExtractionProvenance.AddedLineIdFor(command.InvoiceId, decisionId);

        // ── Step 1: the reads, app-only. Each refusal writes nothing. ──
        Reads reads;
        Entity? repeat = null;
        try
        {
            var invoice = await ReadInvoiceAsync(command.InvoiceId, ct).ConfigureAwait(false);
            if (invoice is null)
            {
                return new AddLineResult(AddLineStatus.InvoiceNotFound, "ebilling.invoice.not_found");
            }

            // A repeat of the same request: the row exists. It is looked up FIRST (164a fix round F2), before the status, firm and correction
            // refusals, so a repeat whose first attempt committed the line still finishes its record however the invoice moved on since (the resume).
            // Other content, or another caller than the one stamped on the row, is refused.
            if (await ReadExistingAsync(lineId, ct).ConfigureAwait(false) is { } existing)
            {
                if (!IsSameLine(existing, command.InvoiceId, shape) || !IsSameCreator(existing, command.DeciderSystemUserId))
                {
                    return IdReused();
                }

                repeat = existing;
            }

            // The refusals of a first attempt. A repeat skips them: its line is committed; the resume runs steps 7 and 9 only while In Review.
            var core = InvoiceLineDecisionService.SingleCoreOf(invoice);
            if (repeat is null)
            {
                if (invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value != InvoiceStatusCodes.InReview)
                {
                    return new AddLineResult(AddLineStatus.NotInReview, AddLineCodes.NotInReview, "A line can be added only while the invoice is In Review.");
                }

                if (core is null)
                {
                    return new AddLineResult(AddLineStatus.OwnerUnresolved, AddLineCodes.OwnerUnresolved, "The invoice names no single core record; nothing was changed.");
                }

                // EB-D-185 / EB-D-197: a firm comes first. Read once, app-only; a fault is Unavailable below (fail closed), never Open.
                if (await InvoiceFirmGate.CheckAsync(_entities, invoice, ct).ConfigureAwait(false) is not InvoiceFirmGateResult.Open and var gate)
                {
                    _logger.LogInformation("Add line on invoice {InvoiceId}: refused by the firm gate ({Gate}); nothing changed.", command.InvoiceId, gate);
                    return new AddLineResult(AddLineStatus.FirmMissing, AddLineCodes.FirmMissing, AddLineCodes.FirmDetailFor(gate));
                }
            }

            var allRows = await InvoiceReviewReadService.ReadPagedAsync(_entities, InvoiceReviewReadService.LineQuery(command.InvoiceId), ct).ConfigureAwait(false);
            if (repeat is null)
            {
                var unapplied = await InvoiceCorrectionReevaluation.ReadUnappliedAsync(_entities, command.InvoiceId, allRows, ct).ConfigureAwait(false);
                if (unapplied.Count > 0)
                {
                    _logger.LogWarning("Add line on invoice {InvoiceId}: {Count} corrections have not been followed through; nothing changed.", command.InvoiceId, unapplied.Count);
                    return new AddLineResult(
                        AddLineStatus.CorrectionsPending, AddLineCodes.CorrectionsPending,
                        InvoiceCorrectionReevaluation.UnappliedText(unapplied, allRows, "add the line again"));
                }
            }

            var submission = await InvoiceCorrectionReevaluation.ReadSubmissionAsync(_entities, command.InvoiceId, ct).ConfigureAwait(false);
            var json = submission?.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
            if (submission is null || string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject storedRoot)
            {
                throw new InvalidOperationException("The invoice has no stored findings.");
            }

            if (!submission.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var submissionVersion) || submissionVersion <= 0
                || !invoice.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var invoiceVersion) || invoiceVersion <= 0)
            {
                throw new InvalidOperationException("A row version could not be read.");
            }

            var lines = await ReadLinesAsync(_entities, command.InvoiceId, ct).ConfigureAwait(false);
            var verified = VerifiedOf(await ReadCorrectionsAsync(_entities, command.InvoiceId, ct).ConfigureAwait(false));
            // A repeat on an invoice with no single core record reads no core: it is used only by a first attempt's steps 2 to 6.
            reads = new Reads(invoice, core ?? default, allRows, lines, submission, storedRoot, invoiceVersion, submissionVersion, verified);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command.InvoiceId, ex);
        }

        if (repeat is not null)
        {
            return await ResumeAsync(command, shape, decisionId, lineId, repeat, reads).ConfigureAwait(false);
        }

        // ── Step 2: the codes (FR-22: only from the invoice's resolved set) and the timekeeper. ──
        ResolvedCodeSet codeSet;
        try
        {
            var codingLines = reads.AllRows
                .Where(r => r.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value is (int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense)
                .Select(InvoiceCorrectionService.CodingLineOf)
                .ToList();
            codeSet = await _codeSets.ResolveAsync(new CodingCoreRecord(reads.Core.Entity, reads.Core.Id), codingLines, ct).ConfigureAwait(false);
        }
        catch (LineCodingException ex) when (ex.ReasonCode == LineCodingException.NoCodeSet)
        {
            _logger.LogWarning("Add line on invoice {InvoiceId}: no code set resolves for the invoice; nothing changed.", command.InvoiceId);
            return new AddLineResult(AddLineStatus.CodeSetUnavailable, AddLineCodes.CodeSetUnavailable, "No code set can be resolved for this invoice, so the codes cannot be checked.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command.InvoiceId, ex);
        }

        var codes = new Dictionary<BillingCodeKind, string?>();
        foreach (var (kind, typed) in new[] { (BillingCodeKind.Task, shape.TaskCode), (BillingCodeKind.Activity, shape.ActivityCode), (BillingCodeKind.Expense, shape.ExpenseCode) })
        {
            if (typed is null)
            {
                codes[kind] = null;
                continue;
            }

            if (!codeSet.TryGetCode(kind, typed, out var canonical))
            {
                return new AddLineResult(
                    AddLineStatus.InvalidRequest, AddLineCodes.CodeNotAllowed,
                    $"The {kind.ToString().ToLowerInvariant()} code is not in this invoice's code set; nothing was changed.");
            }

            codes[kind] = canonical;
        }

        ResolvedInvoiceLine? resolved = null;
        if (shape.CostType == InvoiceLineCostType.Fee)
        {
            try
            {
                var result = await _resolve(ResolutionInputFor(reads, shape), ct).ConfigureAwait(false);
                resolved = result.Lines.Count == 1 ? result.Lines[0] : throw new InvalidOperationException("The resolution returned no result for the line.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return ReadFailed(command.InvoiceId, ex);
            }
        }

        var index = reads.AllRows.Select(r => r.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) ?? 0)
            .Concat(reads.Lines.Keys.Select(k => k + 1))
            .DefaultIfEmpty(0).Max();
        var sequence = index + 1;
        var extraction = ExtractionFor(decisionId, shape, codes, codeSet, resolved);
        var row = BuildRow(command, lineId, shape, codes, resolved, sequence, extraction, reads);

        RecordOwnerResolution owner;
        try
        {
            // UAC 194b R3b-2: the line's question is built from the row itself, as the secure-child reconciler asks the written row.
            owner = await _ownership.ResolveOwnerAsync(RecordOwnershipContext.ForChild(row), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command.InvoiceId, ex);
        }

        if (!owner.IsOwned)
        {
            _logger.LogWarning("Add line on invoice {InvoiceId}: no owner for the new line ({Code}); nothing changed.", command.InvoiceId, owner.RefusalCode);
            return new AddLineResult(AddLineStatus.OwnerUnresolved, AddLineCodes.OwnerUnresolved, "The new line has no owner; nothing was changed.");
        }

        owner.ApplyTo(row);
        RecordCreatorPerson.Stamp(row, command.DeciderSystemUserId);
        if (InvoiceFamilyCoreRecordGuard.Check(row) is { IsRefused: true } refused)
        {
            throw new InvalidOperationException($"The added line names no single core record ({refused.RefusalCode}).");
        }

        // ── Step 3: the rules, through the shared before/after diff. ──
        var root = reads.Root;
        var added = new StoredLine(index, row, extraction, Decided: false);
        BillingEvaluationResult beforeResult;
        BillingEvaluationResult afterResult;
        RequestBasis basis;
        try
        {
            basis = RequestBasis.ForAddedLine(
                added, reads.Invoice, reads.Lines, ExtractionProvenance.ParseSubmission(root.ToJsonString()), reads.Verified, reads.Submission.Id, root, reads.Core);
            var before = basis.Build(before: true);
            var after = basis.Build(before: false);
            var loaded = await _load(after, ct).ConfigureAwait(false);
            beforeResult = _evaluate(before, loaded);
            afterResult = _evaluate(after, loaded);
        }
        catch (CorrectionFirmMissingException)
        {
            return new AddLineResult(AddLineStatus.FirmMissing, AddLineCodes.FirmMissing, AddLineCodes.FirmDetailFor(InvoiceFirmGateResult.NoFirm));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command.InvoiceId, ex);
        }

        var gapsBefore = GapSubjects(beforeResult);
        if (GapSubjects(afterResult).Any(gap => !gapsBefore.Contains(gap)))
        {
            _logger.LogInformation("Add line on invoice {InvoiceId}: the line brings in a Procedure gap; nothing changed.", command.InvoiceId);
            return new AddLineResult(
                AddLineStatus.ProcedureGap, AddLineCodes.ProcedureGap,
                "No billing terms are in force for this line's date, so its rules cannot be checked. Nothing was changed.");
        }

        var decided = reads.Lines.Values.Where(l => l.Decided).Select(l => l.Index).ToHashSet();
        var touched = new HashSet<int>();
        var storedFindings = (root["findings"] as JsonArray)?.DeepClone() as JsonArray ?? [];
        PatchFindings(root, beforeResult, afterResult, decided, touched);
        PatchPlan(root, beforeResult, afterResult, storedFindings, SubjectsOf(command.InvoiceId, root, basis.LinesAfter), decided, touched);
        AddLineFindings(root, index, afterResult);

        // 082d fix rounds 1 and 2 (F4): the stored Procedure snapshot is replaced by the re-run's only when it still describes what the stored findings
        // were judged with (see ProcedureSnapshot.ForPatchedFindings); else JSON null; an absent member (a row stored before 082d) stays absent.
        ProcedureSnapshot.SetForPatchedFindings(root, beforeResult, afterResult, decided);
        root[AddLineMember] = new JsonObject
        {
            ["decisionId"] = decisionId.ToString("D"),
            ["lineId"] = lineId.ToString("D"),
            ["state"] = StateCommitted,
        };

        var findingsText = ProcedureSnapshot.SerializeWithinCap(root, InvoiceSubmissionPersister.FindingsJsonMaxLength, out var snapshotDropped);
        if (findingsText is null)
        {
            _logger.LogWarning("Add line on invoice {InvoiceId}: the patched findings are over the column even without the Procedure snapshot; nothing changed.", command.InvoiceId);
            return new AddLineResult(
                AddLineStatus.TooLarge, AddLineCodes.TooLarge, "This invoice's stored findings are too large to add a line. Nothing was changed. Contact support.");
        }

        if (snapshotDropped)
        {
            _logger.LogWarning("Add line on invoice {InvoiceId}: the patched findings are over the column; the Procedure snapshot is left out (null).", command.InvoiceId);
        }

        // The status step: every stored, undecided line the patch touched takes the persister's rule over the patched findings. The added line is
        // created Flagged (R10) and is skipped; a stored line with no extraction keeps its status (as the correction's status step does).
        var statusUpdates = new List<OrganizationRequest>();
        foreach (var touchedIndex in touched.Where(i => i != index).Order())
        {
            if (!reads.Lines.TryGetValue(touchedIndex, out var line) || line.Decided || line.Extraction is null)
            {
                continue;
            }

            var status = InvoiceCorrectionReevaluation.StatusOf(root, touchedIndex, line.Extraction);
            if (line.Row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value == status)
            {
                continue;
            }

            if (!line.Row.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var version) || version <= 0)
            {
                return ReadFailed(command.InvoiceId, new InvalidOperationException("A line version could not be read."));
            }

            var target = VersionedTarget(InvoiceReviewReadService.LineEntity, line.Row.Id, version);
            target[InvoiceTotalsService.LineStatus] = new OptionSetValue(status);
            statusUpdates.Add(new UpdateRequest { Target = target, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
        }

        // ── Step 4: the totals dry run (EB-D-199) and the Decision Record's no-I/O refusals, before any write. ──
        var inputs = reads.AllRows.Select(InvoiceAdjustmentService.ToInput).ToList();
        inputs.Add(new InvoiceLineInput(lineId, shape.CostType, shape.Amount, null, InvoiceLineStatus.Flagged, null, null, ReviewerAdded: true));
        var dryRun = InvoiceTotalsCalculator.Calculate(reads.Invoice.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceBilledTotal)?.Value, inputs);
        if (dryRun.IsRefused)
        {
            _logger.LogInformation("Add line on invoice {InvoiceId}: the totals would be refused ({ReasonCode}); nothing changed.", command.InvoiceId, dryRun.Refusal!.Code);
            return new AddLineResult(AddLineStatus.TotalsRefused, AddLineCodes.TotalsRefused, dryRun.Refusal.Code);
        }

        var decision = BuildDecision(
            command.InvoiceId, decisionId, lineId, sequence, shape, codes,
            resolved?.Timekeeper is { IsMatched: true, TimekeeperId: { } matched } ? matched : null,
            afterResult.Findings.Where(f => f.Subject.Kind == BillingSubjectKind.Line && f.Subject.LineIndex == index).Select(f => f.ReasonCode),
            command.DeciderSystemUserId, _time.GetUtcNow());
        try
        {
            _recorder.Preflight(decision);
        }
        catch (DecisionRecordRefusedException ex)
        {
            _logger.LogWarning("Add line on invoice {InvoiceId}: the Decision Record would be refused ({Reason}); nothing changed.", command.InvoiceId, ex.Reason);
            return new AddLineResult(AddLineStatus.InvalidRequest, AddLineCodes.DecisionInvalid, ex.Reason);
        }

        // (Step 5, the hold link: none in R1. Add line cannot clear the last hold reason; EB-D-198 Q7.)

        // ── Step 6: ONE transaction. From here the request's cancellation no longer applies. ──
        var submissionUpdate = VersionedTarget(InvoiceReviewReadService.SubmissionEntity, reads.Submission.Id, reads.SubmissionVersion);
        submissionUpdate[InvoiceSubmissionPersister.SubmissionFindingsJson] = findingsText;
        InvoiceCorrectionReevaluation.SetAdminAttention(submissionUpdate, reads.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention), root);
        var requests = new List<OrganizationRequest>
        {
            new CreateRequest { Target = row },
            new UpdateRequest { Target = submissionUpdate, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches },
        };
        requests.AddRange(statusUpdates);

        // The invoice: a version-only conditional update (no column), so a change to the invoice since the read (a status, a firm, a total) fails the commit.
        requests.Add(new UpdateRequest
        {
            Target = VersionedTarget(InvoiceReviewReadService.InvoiceEntity, command.InvoiceId, reads.InvoiceVersion),
            ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
        });

        try
        {
            await _entities.ExecuteTransactionAsync(requests, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DBConcurrencyException || DataverseServiceClientImpl.IsRowVersionMismatch(ex))
        {
            _logger.LogInformation("Add line on invoice {InvoiceId}: the invoice changed while the line was prepared; nothing changed.", command.InvoiceId);
            return new AddLineResult(AddLineStatus.Changed, AddLineCodes.Changed, "The invoice changed while the line was prepared; nothing was changed. Reload and try again.");
        }
        catch (Exception ex)
        {
            // A timeout can land after the commit (outcome unknown): a repeat of the same request is safe (the derived row id finds it).
            _logger.LogError("Add line on invoice {InvoiceId}: the commit failed ({ExceptionType}); the outcome may be unknown.", command.InvoiceId, ex.GetType().Name);
            return new AddLineResult(AddLineStatus.Unavailable, AddLineCodes.Unavailable, "The line could not be saved. Repeat the same request to be sure.");
        }

        _logger.LogInformation(
            "Add line on invoice {InvoiceId}: line {LineId} committed (decision {DecisionId}; {Before} findings before, {After} after; {Touched} stored lines touched, {Statuses} statuses changed).",
            command.InvoiceId, lineId, decisionId, beforeResult.Findings.Count, afterResult.Findings.Count, touched.Count(i => i != index), statusUpdates.Count);

        return await FinishAsync(command, decisionId, lineId, decision, reads, nameof(InvoiceLineStatus.Flagged), alreadyApplied: false, finished: false)
            .ConfigureAwait(false);
    }

    // ── The resume (a repeat) and steps 7 to 9 ──

    /// <summary>
    /// A repeat whose row has the same content: the decision is rebuilt from the row (its hashed members, the code, subject, decider, role, outcome,
    /// reason and the empty resolved set, are what they were; the facts are ids and codes read back from the row and the stored findings), and steps 7
    /// to 9 run unless <see cref="AddLineMember"/> names this decision at <see cref="StateDone"/> or the invoice is no longer In Review (164a fix round
    /// F2: then only step 8 runs). Step 8 runs always (the recorder's hash catches a repeat with another reason).
    /// </summary>
    private Task<AddLineResult> ResumeAsync(AddLineCommand command, Shape shape, Guid decisionId, Guid lineId, Entity existing, Reads reads)
    {
        var sequence = existing.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) ?? 0;
        var index = sequence - 1;
        var codes = new Dictionary<BillingCodeKind, string?>
        {
            [BillingCodeKind.Task] = Stored(existing, InvoiceSubmissionPersister.LineTaskCodeAsBilled),
            [BillingCodeKind.Activity] = Stored(existing, InvoiceSubmissionPersister.LineActivityCodeAsBilled),
            [BillingCodeKind.Expense] = Stored(existing, InvoiceSubmissionPersister.LineExpenseCodeAsBilled),
        };
        var reasonCodes = (reads.Root["findings"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(f => IntOf(f["line"]) == index && Text(f, "reasonCode") is { Length: > 0 })
            .Select(f => Text(f, "reasonCode")!);
        var decision = BuildDecision(
            command.InvoiceId, decisionId, lineId, sequence, shape, codes,
            existing.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.LineResolvedTimekeeper)?.Id, reasonCodes,
            command.DeciderSystemUserId, _time.GetUtcNow());
        var finished = reads.Root[AddLineMember] is JsonObject member
            && string.Equals(Text(member, "decisionId"), decisionId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            && Text(member, "state") == StateDone;

        // 164a fix round F2: steps 7 and 9 run only while the invoice is In Review; step 8 (the record) runs on every repeat, whatever the status.
        if (!finished && reads.Invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value != InvoiceStatusCodes.InReview)
        {
            _logger.LogInformation(
                "Add line {DecisionId} on invoice {InvoiceId}: the invoice is no longer In Review; the repeat runs the record step only.", decisionId, command.InvoiceId);
            finished = true;
        }

        return FinishAsync(
            command, decisionId, lineId, decision, reads, StatusText(existing.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value),
            alreadyApplied: true, finished);

        static string? Stored(Entity row, string column) => row.GetAttributeValue<string>(column) is { Length: > 0 } code ? code : null;
    }

    /// <summary>
    /// Steps 7 to 9 after the commit (or on a repeat), with <see cref="CancellationToken.None"/>. <paramref name="finished"/>: this add's later steps are
    /// known done, or the invoice is no longer In Review, so the totals are only computed from the rows read (no write) and the Signals step is skipped;
    /// the record step still runs. Step 8 runs even when step 7 fails (164a fix round F2: the record does not depend on the totals); the answer then
    /// names the totals step, and step 9 waits for the repeat.
    /// </summary>
    private async Task<AddLineResult> FinishAsync(
        AddLineCommand command, Guid decisionId, Guid lineId, BillingDecision decision, Reads reads, string? lineStatus, bool alreadyApplied, bool finished)
    {
        var billed = reads.Invoice.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceBilledTotal)?.Value;

        // ── Step 7: the stored totals (016, EB-D-199), then the re-index (task 152, best effort). ──
        InvoiceTotalsCalculation? calculation = null;
        string? totalsFailure = null;
        if (finished)
        {
            calculation = InvoiceTotalsCalculator.Calculate(billed, reads.AllRows.Select(InvoiceAdjustmentService.ToInput).ToList());
        }
        else
        {
            try
            {
                calculation = await _recalculate(command.InvoiceId, CancellationToken.None).ConfigureAwait(false);
                if (calculation.IsRefused)
                {
                    totalsFailure = calculation.Refusal!.Code;
                }
            }
            catch (Exception ex)
            {
                totalsFailure = ex.GetType().Name;
            }

            if (totalsFailure is null)
            {
                await InvoiceIndexingJobHandler.TryEnqueueAsync(
                    _jobs, _logger, command.InvoiceId, $"a{decisionId.ToString("N", CultureInfo.InvariantCulture)}").ConfigureAwait(false);
            }
        }

        // ── Step 8: ONE add-line record (R8), only through the recorder and after the line write; idempotent on the decision id. ──
        try
        {
            await _recorder.RecordAsync(decision, CancellationToken.None).ConfigureAwait(false);
        }
        catch (DecisionRecordRefusedException ex) when (alreadyApplied && IsOtherContent(ex))
        {
            // The decision id is recorded with the same code and other (readable) content: the repeat carries another reason or decider than the add
            // it repeats. Only this refusal is id_reused (164a fix round K2); any other refusal of a repeat is the record step's Incomplete below.
            _logger.LogInformation("Add line on invoice {InvoiceId}: decision {DecisionId} is recorded with other content; the repeat is refused.", command.InvoiceId, decisionId);
            return IdReused();
        }
        catch (Exception ex)
        {
            var detail = (ex as DecisionRecordRefusedException)?.Reason ?? ex.GetType().Name;
            return totalsFailure is not null
                ? Incomplete(command.InvoiceId, decisionId, lineId, TotalsStep, totalsFailure)
                : Incomplete(command.InvoiceId, decisionId, lineId, RecordStep, detail);
        }

        if (totalsFailure is not null)
        {
            return Incomplete(command.InvoiceId, decisionId, lineId, TotalsStep, totalsFailure);
        }

        // ── Step 9: the Signals of the plan changes (none on a repeat whose steps finished). ──
        if (!finished)
        {
            try
            {
                await ReconcileSignalsAsync(command.InvoiceId, decisionId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Incomplete(command.InvoiceId, decisionId, lineId, SignalsStep, ex.GetType().Name);
            }
        }

        var totals = calculation!; // set: either computed from the rows (finished) or stored by step 7 (no totals failure)
        return new AddLineResult(
            AddLineStatus.Applied, "ok", LineId: lineId, DecisionId: decisionId, LineStatus: lineStatus, AlreadyApplied: alreadyApplied,
            Totals: new ReviewTotalsDto(
                billed, totals.FeesTotal, totals.ExpensesTotal, totals.LineAdjustmentsTotal, totals.InvoiceAdjustmentsTotal, totals.ApprovedTotal));
    }

    /// <summary>
    /// The writer's "same decision id, same billing action, other content" refusal (164a fix round K2): the only refusal of a repeat's record step that
    /// is answered <c>id_reused</c>. Another <see cref="DecisionRecordRefusalReason.ReviewInvalid"/> (a recorded content that cannot be read, another
    /// billing action under the id, more than one record under the key) is not proof that the caller changed its request.
    /// </summary>
    private static bool IsOtherContent(DecisionRecordRefusedException ex) =>
        ex.Reason == DecisionRecordRefusalReason.ReviewInvalid
        && string.Equals(ex.Message, DecisionRecordWriter.BillingOtherContentRefusal, StringComparison.Ordinal);

    /// <summary>
    /// 164a fix round K2: a repeat is the same add only when it comes from the caller stamped on the row (<see cref="RecordCreatorPerson.Column"/>).
    /// Another caller's repeat is <c>id_reused</c> before any step runs, so a record the first attempt did not write is never written naming
    /// another decider.
    /// </summary>
    private static bool IsSameCreator(Entity existing, Guid decider) =>
        existing.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id is { } creator && creator != Guid.Empty && creator == decider;

    /// <summary>
    /// Step 9: reconciles the <c>rule</c> entries of <see cref="InvoiceCorrectionReevaluation.PendingSignalChangesMember"/> through the correction's own
    /// reconcile, then removes them and moves <see cref="AddLineMember"/> (when it names this decision) to <see cref="StateDone"/>, in ONE
    /// row-version-conditional write of the invoice's own submission. A <c>system</c> entry is left for the correction's Signals step (only a correction
    /// writes one; a first add is refused while a correction is unapplied, and a repeat leaves it). Nothing is written when nothing changes.
    /// </summary>
    private async Task ReconcileSignalsAsync(Guid invoiceId, Guid decisionId, CancellationToken ct)
    {
        var submission = await InvoiceCorrectionReevaluation.ReadSubmissionAsync(_entities, invoiceId, ct).ConfigureAwait(false)
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
        var pending = root[InvoiceCorrectionReevaluation.PendingSignalChangesMember] as JsonObject;
        var ruleChanges = (pending?["rule"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var signals = new InvoiceRuleSignalReconciler(_lifecycle, _closeSignal, _logger, SignalLogContext.AddLine, invoiceId, field: null);
        foreach (var change in ruleChanges)
        {
            await signals.ReconcileRuleAsync(root, change, ct).ConfigureAwait(false);
        }

        if (pending is not null)
        {
            if (pending["system"] is JsonArray { Count: > 0 })
            {
                pending.Remove("rule");
            }
            else
            {
                root.Remove(InvoiceCorrectionReevaluation.PendingSignalChangesMember);
            }
        }

        InvoiceRuleSignalReconciler.RecordWriteRefusals(root, signals.Refused);
        if (root[AddLineMember] is JsonObject member && string.Equals(Text(member, "decisionId"), decisionId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            member["state"] = StateDone;
        }

        if (string.Equals(original, root.ToJsonString(), StringComparison.Ordinal))
        {
            return;
        }

        var update = VersionedTarget(InvoiceReviewReadService.SubmissionEntity, submission.Id, version);
        update[InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString();
        InvoiceCorrectionReevaluation.SetAdminAttention(update, submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention), root);
        await _entities.ExecuteTransactionAsync(
            [new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches }], ct).ConfigureAwait(false);
        _logger.LogInformation("Add line on invoice {InvoiceId}: Signals reconciled ({Rule} rule changes, {Refused} refused).", invoiceId, ruleChanges.Count, signals.Refused.Count);
    }

    private static AddLineResult IdReused() =>
        new(AddLineStatus.IdReused, AddLineCodes.IdReused, "This request id was used for a different line or reason. Use a new request id.");

    private AddLineResult Incomplete(Guid invoiceId, Guid decisionId, Guid lineId, string step, string detail)
    {
        _logger.LogError(
            "Add line {DecisionId} on invoice {InvoiceId}: line {LineId} was committed but the {Step} step failed ({Detail}); the caller repeats the request to finish it.",
            decisionId, invoiceId, lineId, step, detail);
        return new AddLineResult(
            AddLineStatus.Incomplete, AddLineCodes.Incomplete,
            "The line was added but a later step did not finish. Repeat the same request (same request id) to complete it.",
            LineId: lineId, DecisionId: decisionId, FailedStep: step);
    }

    // ── The row, its extraction JSON and its findings ──

    private static InvoiceResolutionInput ResolutionInputFor(Reads reads, Shape shape) => new()
    {
        FirmId = reads.Invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)!.Id,
        CoreRecord = new CoreRecordReference(
            reads.Core.Entity switch { "sprk_matter" => CoreRecordKind.Matter, "sprk_project" => CoreRecordKind.Project, _ => CoreRecordKind.WorkAssignment },
            reads.Core.Id),
        Header = new InvoiceResolutionHeader(),
        Lines =
        [
            new InvoiceResolutionLine
            {
                LineType = InvoiceLineType.Fee,
                Date = new ResolutionField<DateOnly?>(shape.Date),
                TimekeeperAsBilled = new ResolutionField<string?>(shape.Timekeeper),
                Hours = new ResolutionField<decimal?>(shape.Hours),
                Rate = new ResolutionField<decimal?>(shape.Rate),
                Amount = new ResolutionField<decimal?>(shape.Amount),
            },
        ],
    };

    /// <summary>
    /// The added line's <c>sprk_extractionconfidence</c>: <see cref="ExtractionProvenance.ForReviewerAddedLine"/> (the marker, unchanged) plus what every
    /// later rebuild reads (164a-3 open points 2 and 3): each reviewer-typed field at combined confidence 1.0, the code set and the typed codes
    /// (<c>recoded</c>: no AI coded the line), and for a Fee line the roster match's and the rate's reasons in <c>resolutionChange.after</c>. Numbers,
    /// flags, codes and ids only (EB-D-189 conditions).
    /// </summary>
    internal static JsonObject ExtractionFor(Guid decisionId, Shape shape, IReadOnlyDictionary<BillingCodeKind, string?> codes, ResolvedCodeSet codeSet, ResolvedInvoiceLine? resolved)
    {
        var json = (JsonObject)JsonNode.Parse(ExtractionProvenance.ForReviewerAddedLine(decisionId))!;
        var isFee = shape.CostType == InvoiceLineCostType.Fee;
        var fields = new JsonObject();
        foreach (var name in isFee
            ? new[] { ResolutionFieldNames.Date, ResolutionFieldNames.TimekeeperAsBilled, ResolutionFieldNames.Hours, ResolutionFieldNames.Rate, ResolutionFieldNames.Amount, BillingFieldNames.Narrative }
            : [ResolutionFieldNames.Date, ResolutionFieldNames.Amount, BillingFieldNames.Narrative])
        {
            fields[name] = new JsonObject { ["combined"] = 1m };
        }

        json["fields"] = fields;
        json["coding"] = new JsonObject
        {
            ["status"] = nameof(LineCodingStatus.Coded),
            ["recoded"] = true,
            ["codeSetId"] = codeSet.CodeSetId.ToString("D"),
            ["codeSetName"] = codeSet.CodeSetName,
            ["codeSetSource"] = codeSet.Source.ToString(),
            ["needsReview"] = codeSet.NeedsReview,
            ["confidence"] = 1m,
            ["task"] = CodeJson(codes[BillingCodeKind.Task]),
            ["activity"] = CodeJson(codes[BillingCodeKind.Activity]),
            ["expense"] = CodeJson(codes[BillingCodeKind.Expense]),
            ["firmCodeMismatch"] = false,
            ["refused"] = new JsonArray(),
            ["observations"] = new JsonArray(),
        };
        json[ExtractionProvenance.LineDateMissingMember] = false;
        if (isFee && resolved is not null)
        {
            json[InvoiceCorrectionReevaluation.ResolutionChangeMember] = new JsonObject
            {
                ["after"] = new JsonObject
                {
                    ["timekeeperReason"] = (resolved.Timekeeper?.Reason ?? TimekeeperUnmatchedReason.None).ToString(),
                    ["rateStatus"] = resolved.Rate.Status.ToString(),
                    ["rateReason"] = resolved.Rate.Reason.ToString(),
                },
            };
        }

        return json;

        // The typed code is also the as-billed code (D-117), so it is its own firm code and never a mismatch.
        static JsonObject CodeJson(string? code) => new() { ["code"] = code, ["firmCode"] = code, ["mismatch"] = false };
    }

    private static Entity BuildRow(
        AddLineCommand command, Guid lineId, Shape shape, IReadOnlyDictionary<BillingCodeKind, string?> codes, ResolvedInvoiceLine? resolved, int sequence,
        JsonObject extraction, Reads reads)
    {
        var invoice = reads.Invoice;
        var tracking = invoice.GetAttributeValue<string>(InvoiceReviewService.InvTrackingNumber) ?? Text(reads.Root, "trackingNumber");
        var row = new Entity(InvoiceReviewReadService.LineEntity, lineId)
        {
            [InvoiceFamilyCoreRecordGuard.LineInvoiceColumn] = new EntityReference(InvoiceReviewService.InvoiceEntity, command.InvoiceId),
            [InvoiceSubmissionPersister.LineName] = tracking is { Length: > 0 }
                ? InvoiceSubmissionPersister.LineNameFor(tracking, sequence - 1)
                : $"Added line #{sequence.ToString(CultureInfo.InvariantCulture)}",
            [InvoiceSubmissionPersister.LineSequence] = sequence,
            [InvoiceTotalsService.LineCostType] = new OptionSetValue((int)shape.CostType),
            [InvoiceTotalsService.LineAmount] = new Money(shape.Amount),
            [InvoiceSubmissionPersister.LineEventDate] = shape.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            [InvoiceSubmissionPersister.LineDescription] = shape.Description,
            // R10: created Flagged (its line finding is line.reviewer_added), so it is decided like any line before approval (I-2).
            [InvoiceTotalsService.LineStatus] = new OptionSetValue((int)InvoiceLineStatus.Flagged),
            [InvoiceSubmissionPersister.LineExtractionConfidence] = extraction.ToJsonString(),
        };

        if (shape.CostType == InvoiceLineCostType.Fee)
        {
            row[InvoiceSubmissionPersister.LineTimekeeper] = shape.Timekeeper;
            if (shape.Hours is { } hours)
            {
                row[InvoiceSubmissionPersister.LineQuantity] = hours;
            }

            if (shape.Rate is { } rate)
            {
                row[InvoiceSubmissionPersister.LineRate] = new Money(rate);
            }

            if (resolved?.Timekeeper is { IsMatched: true, TimekeeperId: { } timekeeperId } && timekeeperId != Guid.Empty)
            {
                row[InvoiceSubmissionPersister.LineResolvedTimekeeper] = new EntityReference("sprk_timekeeper", timekeeperId);
            }

            if (resolved?.Rate is { Status: RateResolutionStatus.Found, ApprovedRate: { } approvedRate })
            {
                row[InvoiceSubmissionPersister.LineApprovedRate] = new Money(approvedRate);
            }
        }

        // D-117 (164a-3 open point 1): the rules judge the BILLED code, so the typed codes are the line's as-billed codes too.
        SetCode(row, InvoiceSubmissionPersister.LineTaskCodeAsBilled, codes[BillingCodeKind.Task]);
        SetCode(row, InvoiceSubmissionPersister.LineActivityCodeAsBilled, codes[BillingCodeKind.Activity]);
        SetCode(row, InvoiceSubmissionPersister.LineExpenseCodeAsBilled, codes[BillingCodeKind.Expense]);

        // The firm gate proved the invoice's firm is a confirmed one (not held for one), so it is copied onto the line, with the invoice's currency.
        if (invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg) is { } firm && firm.Id != Guid.Empty)
        {
            row[InvoiceSubmissionPersister.LineVendorOrg] = new EntityReference("sprk_organization", firm.Id);
        }

        if (invoice.GetAttributeValue<string>(InvoiceSubmissionPersister.InvoiceCurrency) is { Length: > 0 } currency)
        {
            row[InvoiceSubmissionPersister.LineCurrency] = currency;
        }

        // 013: exactly one core-record lookup, plus the resolver fields, copied from the invoice (a line shares its invoice's core record).
        row[InvoiceFamilyCoreRecordGuard.CoreRecordLookups[InvoiceReviewReadService.LineEntity].Single(l => l.CoreEntity == reads.Core.Entity).Column] =
            new EntityReference(reads.Core.Entity, reads.Core.Id);
        foreach (var column in new[]
        {
            CoreAncestorResolver.RegardingRecordTypeColumn, CoreAncestorResolver.RegardingRecordIdColumn,
            "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber",
        })
        {
            if (invoice.Contains(column) && invoice[column] is { } carried)
            {
                row[column] = carried;
            }
        }

        return row;

        static void SetCode(Entity row, string column, string? code)
        {
            if (code is not null)
            {
                row[column] = code;
            }
        }
    }

    /// <summary>The added line's <c>lineFindings[]</c>: <see cref="LineFindingReviewerAdded"/> always, and <c>rules.not_checked</c> when a refused rule
    /// version should have checked it (the persister's rule).</summary>
    private static void AddLineFindings(JsonObject root, int index, BillingEvaluationResult after)
    {
        if (root[InvoiceSubmissionPersister.LineFindingsMember] is not JsonArray lineFindings)
        {
            root[InvoiceSubmissionPersister.LineFindingsMember] = lineFindings = [];
        }

        void Add(string code, string message)
        {
            if (!lineFindings.OfType<JsonObject>().Any(e => IntOf(e["line"]) == index && Text(e, "code") == code))
            {
                lineFindings.Add(InvoiceSubmissionPersister.LineFindingEntryJson(index, code, message));
            }
        }

        Add(LineFindingReviewerAdded, LineFindingReviewerAddedMessage);
        if (InvoiceSubmissionPersister.RulesNotChecked(after, index + 1).TryGetValue(index, out var notChecked))
        {
            Add(InvoiceSubmissionPersister.LineFindingRulesNotChecked, notChecked);
        }
    }

    /// <summary>
    /// The Add line Decision Record (R8; ontology O3): code <see cref="ActionCode"/>, subject the new line, the caller as decider, the reason, no resolved
    /// Signals. Facts are ids and codes only: the invoice and line ids, the sequence, the cost type, the codes, the matched timekeeper's id and the reason
    /// codes of the rules that found something on the line. Its hashed members (code, subject, decider, role, outcome, reason, empty resolved set) are the
    /// same on every attempt, so a repeat's record is the same record (<see cref="ResumeAsync"/> rebuilds the facts from the row).
    /// </summary>
    internal static BillingDecision BuildDecision(
        Guid invoiceId, Guid decisionId, Guid lineId, int sequence, Shape shape, IReadOnlyDictionary<BillingCodeKind, string?> codes,
        Guid? timekeeperId, IEnumerable<string> ruleReasonCodes, Guid decider, DateTimeOffset decidedOn)
    {
        var facts = new Dictionary<string, object?>
        {
            ["invoiceId"] = invoiceId,
            ["lineId"] = lineId,
            ["lineSequence"] = sequence,
            ["costType"] = shape.CostType.ToString(),
            ["taskCode"] = codes[BillingCodeKind.Task],
            ["activityCode"] = codes[BillingCodeKind.Activity],
            ["expenseCode"] = codes[BillingCodeKind.Expense],
            ["timekeeperId"] = timekeeperId is { } id && id != Guid.Empty ? id : null,
            ["ruleReasonCodes"] = ruleReasonCodes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
        };

        return new BillingDecision(
            decisionId, ActionCode, lineId, BillingDecider.Human(decider, DeciderRole), facts, OutcomeAdded,
            Reason: shape.Reason, ResolvedSignalIds: null, DecidedOn: decidedOn);
    }

    /// <summary>A repeat's row is the same line when it is this invoice's reviewer-added line with the requested values.</summary>
    private static bool IsSameLine(Entity existing, Guid invoiceId, Shape shape)
    {
        static bool SameText(string? stored, string? requested) =>
            string.Equals(stored?.Trim() is { Length: > 0 } s ? s : null, requested, StringComparison.OrdinalIgnoreCase);

        var isFee = shape.CostType == InvoiceLineCostType.Fee;
        return ExtractionProvenance.IsReviewerAdded(existing)
            && existing.GetAttributeValue<EntityReference>(InvoiceTotalsService.LineInvoiceLookup)?.Id == invoiceId
            && existing.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value == (int)shape.CostType
            && existing.GetAttributeValue<Money>(InvoiceTotalsService.LineAmount)?.Value == shape.Amount
            && existing.GetAttributeValue<DateTime?>(InvoiceSubmissionPersister.LineEventDate) is { } date && DateOnly.FromDateTime(date) == shape.Date
            && string.Equals(existing.GetAttributeValue<string>(InvoiceSubmissionPersister.LineDescription), shape.Description, StringComparison.Ordinal)
            && (!isFee || (string.Equals(existing.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTimekeeper), shape.Timekeeper, StringComparison.Ordinal)
                && existing.GetAttributeValue<decimal?>(InvoiceSubmissionPersister.LineQuantity) == shape.Hours
                && existing.GetAttributeValue<Money>(InvoiceSubmissionPersister.LineRate)?.Value == shape.Rate))
            && SameText(existing.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTaskCodeAsBilled), shape.TaskCode)
            && SameText(existing.GetAttributeValue<string>(InvoiceSubmissionPersister.LineActivityCodeAsBilled), shape.ActivityCode)
            && SameText(existing.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExpenseCodeAsBilled), shape.ExpenseCode);
    }

    /// <summary>The target of a row-version conditional update of an EXISTING row (never a create): the commit's submission, invoice and line status
    /// updates. Built here, apart from the step that hands the new line to its <c>CreateRequest</c>.</summary>
    private static Entity VersionedTarget(string entity, Guid id, long version) =>
        new(entity, id) { RowVersion = version.ToString(CultureInfo.InvariantCulture) };

    private static string? StatusText(int? status) =>
        status is { } s && Enum.IsDefined(typeof(InvoiceLineStatus), s) ? ((InvoiceLineStatus)s).ToString() : null;

    private AddLineResult ReadFailed(Guid invoiceId, Exception ex)
    {
        _logger.LogWarning("Add line on invoice {InvoiceId}: a read failed ({ExceptionType}); nothing changed.", invoiceId, ex.GetType().Name);
        return new AddLineResult(AddLineStatus.Unavailable, AddLineCodes.Unavailable, "The invoice could not be read; nothing was changed. Try again.");
    }

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

    /// <summary>The derived row in ANY state (a repeat must see its own line however it was decided since).</summary>
    private async Task<Entity?> ReadExistingAsync(Guid lineId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.LineEntity)
        {
            ColumnSet = new ColumnSet(ExistingColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition($"{InvoiceReviewReadService.LineEntity}id", ConditionOperator.Equal, lineId);
        return (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
    }
}
