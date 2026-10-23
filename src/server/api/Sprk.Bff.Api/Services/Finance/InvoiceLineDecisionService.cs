using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>What a reviewer does with a line (FR-31).</summary>
public enum LineDecisionAction
{
    /// <summary>Approved amount = billed amount.</summary>
    Accept,

    /// <summary>Approved amount below the billed amount (above it for a credit), reason required.</summary>
    Reduce,

    /// <summary>Approved amount 0, reason required.</summary>
    Disallow,
}

/// <summary>One line of a decision request. <see cref="ApprovedAmount"/> is for a Reduce only.</summary>
public sealed record LineDecisionLine(Guid LineId, decimal? ApprovedAmount = null);

/// <summary>
/// A line decision request. The decider is the authenticated caller's <c>systemuserid</c>, resolved by the endpoint from the caller's own
/// token and never read from the body (074 obligation 2).
/// </summary>
/// <param name="DecisionId">The client's decision id: a retry of the same request carries the same id and records nothing twice (FR-30).</param>
/// <param name="Bulk">The request came through the multi-line or Accept all Clean route: each Accepted line then writes the "Accepted in bulk"
/// correction rows (D-64). The single-line route writes none.</param>
/// <param name="CallerBearerToken">The caller's own token, from the request: the rights to close the lines' Signals (AppendTo on the core
/// record, Write on each line) are asked of Dataverse AS THE CALLER with it. Null answers every rights question "no".</param>
public sealed record LineDecisionCommand(
    Guid InvoiceId,
    Guid DecisionId,
    LineDecisionAction Action,
    string? Reason,
    IReadOnlyList<LineDecisionLine> Lines,
    Guid DeciderSystemUserId,
    bool Bulk,
    string? CallerBearerToken = null);

/// <summary>Outcome class of a decision attempt. The endpoint maps each to one HTTP status and one stable code.</summary>
public enum LineDecisionStatus
{
    Applied,
    InvalidRequest,
    InvoiceNotFound,
    LineNotFound,
    NotInReview,
    LineNotDecidable,
    TotalsRefused,
    Busy,
    Changed,
    OwnerUnresolved,

    /// <summary>The decision was refused or could not be carried out and NOTHING was changed (a read or the lock failed, or the first write failed).</summary>
    Unavailable,

    /// <summary>The lines were changed but a later step failed; <see cref="LineDecisionResult.CommittedLineIds"/> names them. Retrying the same decision id
    /// through the multi-line route completes it (every step is idempotent).</summary>
    Incomplete,

    /// <summary>The decision closes Signals and the caller lacks AppendTo on the core record or Write on a decided line (FR-51, the commit
    /// route's rule); NOTHING was changed.</summary>
    Forbidden,
}

/// <summary>Stable ProblemDetails codes (ADR-019). Callers branch on these, never on a message.</summary>
public static class LineDecisionCodes
{
    public const string InvalidRequest = "ebilling.request.invalid";
    public const string ReasonRequired = "ebilling.line.reason_required";
    public const string ReasonTooLong = "ebilling.line.reason_too_long";
    public const string AmountRequired = "ebilling.line.amount_required";
    public const string AmountOutOfRange = "ebilling.line.amount_out_of_range";
    public const string AmountNotAllowed = "ebilling.line.amount_not_allowed";
    public const string TooManyLines = "ebilling.request.too_many_lines";
    public const string DecisionInvalid = "ebilling.decision.invalid";
    public const string NotInReview = "ebilling.invoice.not_in_review";
    public const string LinePending = "ebilling.line.pending";
    public const string LineNotDecidable = "ebilling.line.not_decidable";
    public const string TotalsRefused = "ebilling.invoice.totals_refused";
    public const string Busy = "ebilling.invoice.busy";
    public const string LineChanged = "ebilling.line.changed";
    public const string OwnerUnresolved = "ebilling.decision.owner_unresolved";
    public const string Unavailable = "ebilling.decision.unavailable";
    public const string Incomplete = "ebilling.decision.incomplete";
    public const string DeciderUnresolved = "ebilling.decider.unresolved";
    public const string NotAuthorized = "ebilling.decision.not_authorized";
}

/// <summary>A decided line as the caller needs it to update the screen without a second read.</summary>
public sealed record DecidedLineDto(
    Guid LineId, string Status, decimal ApprovedAmount, string? Reason, DateTimeOffset ReviewedOn, bool AlreadyRecorded);

public sealed record LineDecisionResult(
    LineDecisionStatus Status,
    string Code,
    string? Detail = null,
    IReadOnlyList<DecidedLineDto>? Lines = null,
    ReviewTotalsDto? Totals = null,
    IReadOnlyList<Guid>? CommittedLineIds = null,
    string? FailedStep = null)
{
    public bool IsApplied => Status == LineDecisionStatus.Applied;
}

/// <summary>
/// The Finance-side owner of an invoice-line decision (ebilling-r1 task 100; FR-31, FR-07, FR-30, D-64, D-75, D-77, D-128, D-129): validates it,
/// writes the line, has 016 recompute the totals, writes the bulk correction rows and records the Decision Record(s) through 074.
/// </summary>
/// <remarks>
/// <para><b>Serialisation (074 K-4, obligation 1), two layers.</b> (1) A per-invoice processing lock (<see cref="IIdempotencyService"/>, key
/// <c>ebilling-line-decision:{invoice}</c>): one decision at a time per invoice, so two concurrent calls with the same decision id cannot both reach
/// the recorder; the loser gets 409 <c>ebilling.invoice.busy</c> and retries. (2) Every line write is conditional on the row version read in the same
/// request (<see cref="IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync"/>, all-or-nothing), so a lock that expired mid-request, or a writer
/// that does not take the lock, makes the write fail (409 <c>ebilling.line.changed</c>) before anything is recorded. The Decision Record is written only
/// AFTER the line write succeeded, so a refused or conflicting decision leaves no record.</para>
/// <para><b>Order and recovery.</b> Validate everything (all lines, the amount rules, the dry-run totals, the recorder's no-I/O preflight, the owner of
/// the correction rows) before the first write, then: lines (one conditional transaction) -> totals (016) -> correction rows (deterministic-id upserts) ->
/// Decision Records (one per line, idempotent by decision id). A failure after the lines were written answers 503 <c>ebilling.decision.incomplete</c>
/// with the committed line ids; the caller repeats the decision with the same decision id through the multi-line route
/// (<c>POST .../lines/decisions</c> with those line ids), which re-applies the same values, recomputes, upserts the same correction rows and records
/// only what is missing. Accept all Clean cannot be repeated by itself after its lines left Clean, which is why the committed ids are returned.</para>
/// <para><b>Identity.</b> The decider is the caller's <c>systemuserid</c> from the authenticated principal. The linked contact is looked up by the
/// recorder. Lines are written by the application (D-90: the caller's role cannot write <c>sprk_billingevent</c>); the endpoint filter has already
/// proven Write on the invoice as the caller (D-77).</para>
/// <para><b>Signals (D-122, the human-decision close agreed with ontology-r1).</b> Each line's decision resolves every Open or Acknowledged
/// Signal whose subject is that line, of every policy. The set is read BEFORE the record and stored WITH it (<c>ResolvedSignalIds</c>, and the
/// read time and policy codes in the facts); a retry finds the recorded set (<see cref="BillingDecisionRecorder.FindStoredResolvedSetAsync"/>)
/// and closes exactly it, never a fresh read. When a decision closes any Signal, the caller must hold AppendTo on the core record and Write
/// for each decided line that has one, asked ON THE PARENT INVOICE (ontology-r1 2026-10-20: line subject → Write on the parent invoice,
/// D-90), as the caller (<see cref="HumanDecisionSignalClose.AuthorizeAsync"/>, the commit route's rule with that one mapping);
/// otherwise 403 and nothing is written. After each line's record, its Signals are closed as Acted by the app-identity writer naming the
/// decider; a failed close only warns.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="InvoiceTotalsService"/> (totals only),
/// <see cref="BillingDecisionRecorder"/> (the record only), <see cref="InvoiceSubmissionPersister"/> (creates lines); nothing changes a line's decision.
/// Extension: those owners stay single-purpose (ADR-002 one owner per invariant); this service orchestrates them in the one order that keeps I-1 and
/// the ledger consistent. Cost of doing nothing: no line can be decided, so no invoice can be approved (FR-31, FR-35).</para>
/// </remarks>
public sealed class InvoiceLineDecisionService
{
    public const int MaxLinesPerRequest = 1000;
    public const int ReasonMaxLength = 2000;
    public const string DeciderRole = "invoice-reviewer";

    internal const string LockKeyPrefix = "ebilling-line-decision:";
    internal const string CorrectionEntity = "sprk_invoicecorrection";
    internal const string SystemUserEntity = "systemuser";
    internal const string AcceptedInBulkLabel = "Accepted in bulk";

    private const int OutcomeAcceptedInBulk = 100000002;
    private const int CorrectionChunk = 400;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

    private static readonly string[] InvoiceColumns =
    [
        "statuscode", InvoiceTotalsService.InvoiceBilledTotal,
        "sprk_matter", "sprk_project", "sprk_regardingworkassignment",
        CoreAncestorResolver.RegardingRecordTypeColumn, CoreAncestorResolver.RegardingRecordIdColumn,
        "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber",
    ];

    /// <summary>The extracted line fields a bulk accept records, with the line column holding the value as read (the AI value).</summary>
    internal static readonly IReadOnlyDictionary<string, string> AiFieldColumns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ResolutionFieldNames.Date] = InvoiceSubmissionPersister.LineEventDate,
        [ResolutionFieldNames.TimekeeperAsBilled] = InvoiceSubmissionPersister.LineTimekeeper,
        [ResolutionFieldNames.Hours] = InvoiceSubmissionPersister.LineQuantity,
        [ResolutionFieldNames.Quantity] = InvoiceSubmissionPersister.LineQuantity,
        [ResolutionFieldNames.Rate] = InvoiceSubmissionPersister.LineRate,
        [ResolutionFieldNames.UnitCost] = InvoiceSubmissionPersister.LineRate,
        [ResolutionFieldNames.Amount] = InvoiceTotalsService.LineAmount,
        [BillingFieldNames.Narrative] = InvoiceSubmissionPersister.LineDescription,
        [BillingFieldNames.BilledTaskCode] = InvoiceSubmissionPersister.LineTaskCodeAsBilled,
        [BillingFieldNames.BilledActivityCode] = InvoiceSubmissionPersister.LineActivityCodeAsBilled,
        [BillingFieldNames.BilledExpenseCode] = InvoiceSubmissionPersister.LineExpenseCodeAsBilled,
    };

    private readonly IGenericEntityService _entities;
    private readonly IInvoiceTotalsService _totals;
    private readonly BillingDecisionRecorder _recorder;
    private readonly IIdempotencyService _locks;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly CallerRecordAccessProbe _probe;
    private readonly HumanDecisionSignalClose.CloseActed _closeActed;
    private readonly ILogger<InvoiceLineDecisionService> _logger;
    private readonly TimeProvider _time;

    public InvoiceLineDecisionService(
        IGenericEntityService entities,
        IInvoiceTotalsService totals,
        BillingDecisionRecorder recorder,
        IIdempotencyService locks,
        IRecordOwnershipResolver ownership,
        SignalWriter signals,
        CallerRecordAccessProbe probe,
        ILogger<InvoiceLineDecisionService> logger)
        : this(
            entities, totals, recorder, locks, ownership, probe,
            (signals ?? throw new ArgumentNullException(nameof(signals))).CloseActedByDecisionAsync,
            logger)
    {
    }

    /// <summary>The seam the unit tests use: the ontology-owned writer is concrete, so its close is passed as its call (049's pattern).</summary>
    internal InvoiceLineDecisionService(
        IGenericEntityService entities,
        IInvoiceTotalsService totals,
        BillingDecisionRecorder recorder,
        IIdempotencyService locks,
        IRecordOwnershipResolver ownership,
        CallerRecordAccessProbe probe,
        HumanDecisionSignalClose.CloseActed closeActed,
        ILogger<InvoiceLineDecisionService> logger,
        TimeProvider? time = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _totals = totals ?? throw new ArgumentNullException(nameof(totals));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _closeActed = closeActed ?? throw new ArgumentNullException(nameof(closeActed));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Accept all Clean: accepts every Clean Fee and Expense line of the invoice at its billed amount, as one bulk decision (D-64).</summary>
    public Task<LineDecisionResult> AcceptCleanAsync(
        Guid invoiceId, Guid decisionId, Guid deciderSystemUserId, CancellationToken ct, string? callerBearerToken = null) =>
        RunAsync(
            new LineDecisionCommand(invoiceId, decisionId, LineDecisionAction.Accept, null, [], deciderSystemUserId, Bulk: true, callerBearerToken),
            acceptClean: true,
            ct);

    /// <summary>One decision on one or more lines of the invoice (the single-line route has Bulk false).</summary>
    public Task<LineDecisionResult> DecideAsync(LineDecisionCommand command, CancellationToken ct) =>
        RunAsync(command ?? throw new ArgumentNullException(nameof(command)), acceptClean: false, ct);

    private async Task<LineDecisionResult> RunAsync(LineDecisionCommand command, bool acceptClean, CancellationToken ct)
    {
        if (ShapeError(command, acceptClean) is { } invalid)
        {
            return invalid;
        }

        var key = LockKeyPrefix + command.InvoiceId.ToString("N", CultureInfo.InvariantCulture);
        var owner = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        bool acquired;
        try
        {
            acquired = await _locks.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Fail closed: without the lock the one-at-a-time guarantee is gone.
            _logger.LogWarning("Line decision on invoice {InvoiceId}: the processing lock could not be taken ({ExceptionType}); nothing changed.",
                command.InvoiceId, ex.GetType().Name);
            return new LineDecisionResult(LineDecisionStatus.Unavailable, LineDecisionCodes.Unavailable, "The decision could not be started; nothing was changed. Try again.");
        }

        if (!acquired)
        {
            return new LineDecisionResult(LineDecisionStatus.Busy, LineDecisionCodes.Busy, "Another decision on this invoice is in progress. Try again.");
        }

        try
        {
            return await ApplyAsync(command, acceptClean, ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _locks.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The lock expires by itself (LockDuration); a failed release must not hide the decision's own outcome.
                _logger.LogWarning("Line decision on invoice {InvoiceId}: the processing lock could not be released ({ExceptionType}); it expires.",
                    command.InvoiceId, ex.GetType().Name);
            }
        }
    }

    /// <summary>The request-shape rules that need no data (400). Reduce / Disallow need a reason; only Reduce takes an amount.</summary>
    internal static LineDecisionResult? ShapeError(LineDecisionCommand command, bool acceptClean)
    {
        static LineDecisionResult Bad(string code, string detail) => new(LineDecisionStatus.InvalidRequest, code, detail);

        if (command.InvoiceId == Guid.Empty || command.DecisionId == Guid.Empty || command.DeciderSystemUserId == Guid.Empty)
        {
            return Bad(LineDecisionCodes.InvalidRequest, "An invoice id and a decision id are required.");
        }

        if (acceptClean)
        {
            return null;
        }

        if (command.Lines is null || command.Lines.Count == 0)
        {
            return Bad(LineDecisionCodes.InvalidRequest, "At least one line is required.");
        }

        if (command.Lines.Count > MaxLinesPerRequest)
        {
            return Bad(LineDecisionCodes.TooManyLines, $"A decision covers at most {MaxLinesPerRequest} lines.");
        }

        if (command.Lines.Any(l => l.LineId == Guid.Empty) || command.Lines.Select(l => l.LineId).Distinct().Count() != command.Lines.Count)
        {
            return Bad(LineDecisionCodes.InvalidRequest, "Every line id must be a distinct, non-empty id.");
        }

        var reason = command.Reason?.Trim();
        if (command.Action is LineDecisionAction.Reduce or LineDecisionAction.Disallow)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Bad(LineDecisionCodes.ReasonRequired, "A reason is required to reduce or disallow a line.");
            }

            if (reason.Length > ReasonMaxLength)
            {
                return Bad(LineDecisionCodes.ReasonTooLong, $"The reason is at most {ReasonMaxLength} characters.");
            }
        }

        if (command.Action == LineDecisionAction.Reduce)
        {
            if (command.Lines.Any(l => l.ApprovedAmount is null))
            {
                return Bad(LineDecisionCodes.AmountRequired, "A reduction needs an approved amount for every line.");
            }
        }
        else if (command.Lines.Any(l => l.ApprovedAmount is not null))
        {
            return Bad(LineDecisionCodes.AmountNotAllowed, "Only a reduction takes an approved amount.");
        }

        return null;
    }

    // ── The decision ──

    private sealed record Target(Entity Row, int Sequence, InvoiceLineCostType? Type, InvoiceLineStatus? Status, decimal Billed, decimal? PreviousApproved, long Version)
    {
        public Guid Id => Row.Id;
    }

    private sealed record Proposal(Target Line, InvoiceLineStatus Status, decimal Approved, string? Reason, string Action, string Outcome);

    private async Task<LineDecisionResult> ApplyAsync(LineDecisionCommand command, bool acceptClean, CancellationToken ct)
    {
        Entity? invoice;
        List<Entity> lineRows;
        try
        {
            invoice = await ReadInvoiceAsync(command.InvoiceId, ct).ConfigureAwait(false);
            if (invoice is null)
            {
                return new LineDecisionResult(LineDecisionStatus.InvoiceNotFound, "ebilling.invoice.not_found");
            }

            if (invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value != InvoiceStatusCodes.InReview)
            {
                return new LineDecisionResult(LineDecisionStatus.NotInReview, LineDecisionCodes.NotInReview, "Lines can be decided only while the invoice is In Review.");
            }

            lineRows = await InvoiceReviewReadService.ReadPagedAsync(_entities, InvoiceReviewReadService.LineQuery(command.InvoiceId), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command, ex);
        }

        var all = new Dictionary<Guid, Target>(lineRows.Count);
        foreach (var row in lineRows)
        {
            all[row.Id] = ToTarget(row);
        }

        // ── Choose the lines. Every id must be one of THIS invoice's lines (uniform 404 otherwise), checked before anything is written. ──
        List<LineDecisionLine> chosen;
        if (acceptClean)
        {
            chosen = all.Values
                .Where(t => t.Status == InvoiceLineStatus.Clean && t.Type is InvoiceLineCostType.Fee or InvoiceLineCostType.Expense)
                .OrderBy(t => t.Sequence).ThenBy(t => t.Id)
                .Select(t => new LineDecisionLine(t.Id))
                .ToList();
        }
        else
        {
            if (command.Lines.Any(l => !all.ContainsKey(l.LineId)))
            {
                return new LineDecisionResult(LineDecisionStatus.LineNotFound, "ebilling.line.not_found");
            }

            chosen = command.Lines.ToList();
        }

        if (chosen.Count == 0)
        {
            // Nothing is Clean: nothing to do, nothing written, no totals touched.
            return new LineDecisionResult(LineDecisionStatus.Applied, "ok", Lines: [], Totals: null);
        }

        // ── Validate every line (all or nothing) and build the proposals. ──
        var reason = command.Action == LineDecisionAction.Accept ? null : command.Reason!.Trim();
        var proposals = new List<Proposal>(chosen.Count);
        foreach (var line in chosen)
        {
            var target = all[line.LineId];
            if (target.Type is not (InvoiceLineCostType.Fee or InvoiceLineCostType.Expense))
            {
                return new LineDecisionResult(LineDecisionStatus.LineNotDecidable, LineDecisionCodes.LineNotDecidable, "An Adjustment line is computed, not decided.");
            }

            if (target.Status == InvoiceLineStatus.Pending)
            {
                return new LineDecisionResult(LineDecisionStatus.LineNotDecidable, LineDecisionCodes.LinePending, "A line still being processed cannot be decided.");
            }

            if (target.Status is null)
            {
                return new LineDecisionResult(LineDecisionStatus.LineNotDecidable, LineDecisionCodes.LineNotDecidable, "The line has no recognised status.");
            }

            if (target.Version <= 0)
            {
                return new LineDecisionResult(LineDecisionStatus.Unavailable, LineDecisionCodes.Unavailable, "The line version could not be read; nothing was changed.");
            }

            switch (command.Action)
            {
                case LineDecisionAction.Accept:
                    proposals.Add(new Proposal(target, InvoiceLineStatus.Accepted, target.Billed, null, "accept-line", "Accepted"));
                    break;

                case LineDecisionAction.Disallow:
                    proposals.Add(new Proposal(target, InvoiceLineStatus.Disallowed, 0m, reason, "disallow-line", "Disallowed"));
                    break;

                default:
                    var approved = Math.Round(line.ApprovedAmount!.Value, 2, MidpointRounding.AwayFromZero);
                    // D-129 (3): between 0 and the billed amount whichever sign the line has, and not the billed amount itself.
                    if (approved < Math.Min(0m, target.Billed) || approved > Math.Max(0m, target.Billed) || approved == target.Billed)
                    {
                        return new LineDecisionResult(
                            LineDecisionStatus.InvalidRequest, LineDecisionCodes.AmountOutOfRange,
                            "The approved amount must be between 0 and the line's billed amount, and lower than it.");
                    }

                    proposals.Add(new Proposal(target, InvoiceLineStatus.Reduced, approved, reason, "reduce-line", "Reduced"));
                    break;
            }
        }

        // ── Dry-run the totals (I-1 via 016's calculator): a refusal changes nothing. ──
        var billedTotal = invoice.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceBilledTotal)?.Value;
        var proposed = proposals.ToDictionary(p => p.Line.Id);
        var inputs = lineRows.Select(row =>
        {
            var t = all[row.Id];
            return proposed.TryGetValue(row.Id, out var p)
                ? new InvoiceLineInput(row.Id, t.Type, t.Billed, p.Approved, p.Status, AdjustmentMethodOf(row), row.GetAttributeValue<decimal?>(InvoiceTotalsService.LineAdjustmentValue))
                : new InvoiceLineInput(row.Id, t.Type, t.Billed, t.PreviousApproved, t.Status, AdjustmentMethodOf(row), row.GetAttributeValue<decimal?>(InvoiceTotalsService.LineAdjustmentValue));
        }).ToList();
        var dryRun = InvoiceTotalsCalculator.Calculate(billedTotal, inputs);
        if (dryRun.IsRefused)
        {
            _logger.LogInformation("Line decision on invoice {InvoiceId}: the totals would be refused ({ReasonCode}); nothing changed.", command.InvoiceId, dryRun.Refusal!.Code);
            return new LineDecisionResult(LineDecisionStatus.TotalsRefused, LineDecisionCodes.TotalsRefused, dryRun.Refusal.Code);
        }

        // ── D-122: the Signals each line's decision resolves: the set stored with its record (a retry), else read now. ──
        var signalSets = await ResolveSignalSetsAsync(command, proposals, ct).ConfigureAwait(false);
        if (signalSets.Failure is { } setFailure)
        {
            return setFailure;
        }

        // ── FR-51, before any write: closing Signals (app identity, no impersonation) needs the caller's rights, asked as the caller. ──
        if (await AuthorizeCloseAsync(command, invoice, signalSets.ByLine, ct).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        // ── The recorder's no-I/O refusals, before our first write (074 obligation: refuse before you change anything). ──
        var decidedOn = _time.GetUtcNow();
        var decisions = proposals.Select(p => BuildDecision(command, p, decidedOn, signalSets.ByLine[p.Line.Id])).ToList();
        try
        {
            foreach (var decision in decisions)
            {
                _recorder.Preflight(decision);
            }
        }
        catch (DecisionRecordRefusedException ex)
        {
            _logger.LogWarning("Line decision on invoice {InvoiceId}: the Decision Record would be refused ({Reason}); nothing changed.", command.InvoiceId, ex.Reason);
            return new LineDecisionResult(LineDecisionStatus.InvalidRequest, LineDecisionCodes.DecisionInvalid, ex.Reason);
        }

        // ── The bulk correction rows (D-64) and their owner, resolved before the first write. ──
        List<Entity> corrections = [];
        if (command.Bulk && command.Action == LineDecisionAction.Accept)
        {
            var built = await BuildCorrectionsAsync(command, invoice, proposals, decidedOn.UtcDateTime, ct).ConfigureAwait(false);
            if (built.Failure is not null)
            {
                return built.Failure;
            }

            corrections = built.Rows;
        }

        // ── 1. The lines: one conditional all-or-nothing transaction. ──
        var updates = proposals.Select(p => (
            id: p.Line.Id,
            fields: LineFields(p, command.DeciderSystemUserId, decidedOn.UtcDateTime),
            rowVersion: p.Line.Version)).ToList();
        try
        {
            await _entities.BulkUpdateIfRowVersionsMatchAsync(InvoiceReviewReadService.LineEntity, updates, ct).ConfigureAwait(false);
        }
        catch (DBConcurrencyException)
        {
            return new LineDecisionResult(LineDecisionStatus.Changed, LineDecisionCodes.LineChanged, "A line changed while the decision was prepared; nothing was changed. Reload and decide again.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A timeout can land after the commit (outcome unknown): the message says so, and a retry with the same decision id is safe.
            _logger.LogError("Line decision on invoice {InvoiceId}: the line update failed ({ExceptionType}); the outcome may be unknown.", command.InvoiceId, ex.GetType().Name);
            return new LineDecisionResult(LineDecisionStatus.Unavailable, LineDecisionCodes.Unavailable, "The lines could not be updated. Repeat the same decision to be sure.");
        }

        var committed = proposals.Select(p => p.Line.Id).ToList();

        // ── 2. Totals (016): the approved total follows the approved amounts (D-130). ──
        InvoiceTotalsCalculation calculation;
        try
        {
            calculation = await _totals.RecalculateAsync(command.InvoiceId, ct).ConfigureAwait(false);
            if (calculation.IsRefused)
            {
                return Incomplete(command, committed, "totals", calculation.Refusal!.Code);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Incomplete(command, committed, "totals", ex.GetType().Name);
        }

        // ── 3. Correction rows: deterministic-id upserts, so a repeat rewrites the same rows. ──
        try
        {
            for (var offset = 0; offset < corrections.Count; offset += CorrectionChunk)
            {
                var requests = corrections.Skip(offset).Take(CorrectionChunk)
                    .Select(row => (OrganizationRequest)new UpsertRequest { Target = row }).ToList();
                await _entities.ExecuteTransactionAsync(requests, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Incomplete(command, committed, "corrections", ex.GetType().Name);
        }

        // ── 4. Decision Records: one per line (D-75), idempotent by decision id. Every line is attempted. ──
        var decided = new List<DecidedLineDto>(proposals.Count);
        var failedStep = default(string);
        for (var i = 0; i < proposals.Count; i++)
        {
            try
            {
                var recorded = await _recorder.RecordAsync(decisions[i], ct).ConfigureAwait(false);
                decided.Add(new DecidedLineDto(
                    proposals[i].Line.Id, proposals[i].Status.ToString(), proposals[i].Approved, proposals[i].Reason, decidedOn, recorded.AlreadyRecorded));

                // ── 5. The line's Signals, closed as Acted by its record (a retry: AlreadyClosedByThisRecord). A failure only warns. ──
                await HumanDecisionSignalClose.CloseSetAsync(
                    _closeActed, signalSets.ByLine[proposals[i].Line.Id], recorded.RecordId, command.DeciderSystemUserId, _logger, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failedStep = "record:" + ((ex as DecisionRecordRefusedException)?.Reason ?? ex.GetType().Name);
                _logger.LogError("Line decision on invoice {InvoiceId}: the Decision Record of line {LineId} was not written ({Reason}).",
                    command.InvoiceId, proposals[i].Line.Id, failedStep);
            }
        }

        return failedStep is not null
            ? Incomplete(command, committed, "record", failedStep)
            : new LineDecisionResult(LineDecisionStatus.Applied, "ok", Lines: decided, Totals: TotalsOf(invoice, calculation));
    }

    private LineDecisionResult ReadFailed(LineDecisionCommand command, Exception ex)
    {
        _logger.LogWarning("Line decision on invoice {InvoiceId}: a read failed ({ExceptionType}); nothing changed.", command.InvoiceId, ex.GetType().Name);
        return new LineDecisionResult(LineDecisionStatus.Unavailable, LineDecisionCodes.Unavailable, "The invoice could not be read; nothing was changed.");
    }

    private LineDecisionResult Incomplete(LineDecisionCommand command, IReadOnlyList<Guid> committed, string step, string detail)
    {
        _logger.LogError(
            "Line decision {DecisionId} on invoice {InvoiceId}: the lines ({Count}) were written but the {Step} step failed ({Detail}); the caller repeats the decision to finish it.",
            command.DecisionId, command.InvoiceId, committed.Count, step, detail);
        return new LineDecisionResult(
            LineDecisionStatus.Incomplete, LineDecisionCodes.Incomplete,
            "The lines were updated but a later step did not finish. Repeat the same decision (same decision id, these lines) to complete it.",
            CommittedLineIds: committed, FailedStep: step);
    }

    // ── Builders ──

    private static Target ToTarget(Entity row)
    {
        var type = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value;
        var status = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value;
        return new Target(
            row,
            row.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) ?? 0,
            type is { } t && Enum.IsDefined(typeof(InvoiceLineCostType), t) ? (InvoiceLineCostType)t : null,
            status is { } s && Enum.IsDefined(typeof(InvoiceLineStatus), s) ? (InvoiceLineStatus)s : null,
            row.GetAttributeValue<Money>(InvoiceTotalsService.LineAmount)?.Value ?? 0m,
            row.GetAttributeValue<Money>(InvoiceTotalsService.LineApprovedAmount)?.Value,
            row.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var version) ? version : 0L);
    }

    private static InvoiceAdjustmentMethod? AdjustmentMethodOf(Entity row) =>
        row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineAdjustmentMethod) is { } m && Enum.IsDefined(typeof(InvoiceAdjustmentMethod), m.Value)
            ? (InvoiceAdjustmentMethod)m.Value
            : null;

    private static Dictionary<string, object> LineFields(Proposal p, Guid decider, DateTime decidedOnUtc) => new()
    {
        [InvoiceTotalsService.LineStatus] = new OptionSetValue((int)p.Status),
        [InvoiceTotalsService.LineApprovedAmount] = new Money(p.Approved),
        // An Accept clears an earlier reduction's reason (DBNull clears the column).
        [InvoiceReviewReadService.LineReason] = p.Reason is null ? DBNull.Value : p.Reason,
        [InvoiceReviewReadService.LineReviewedBy] = new EntityReference(SystemUserEntity, decider),
        [InvoiceReviewReadService.LineReviewedOn] = decidedOnUtc,
    };

    private BillingDecision BuildDecision(
        LineDecisionCommand command, Proposal p, DateTimeOffset decidedOn, InvoiceSubmissionAfterSteps.ResolvedSignalSet signals)
    {
        // Values are built the same way on every attempt (same keys, decimals normalised by the hash): 074 K-5.
        IReadOnlyDictionary<string, object?>? values = p.Status == InvoiceLineStatus.Reduced
            ? new Dictionary<string, object?> { ["approvedAmount"] = p.Approved }
            : null;

        // Ids, codes and amounts only (ADR-015): no narrative, no extracted text.
        var facts = new Dictionary<string, object?>
        {
            ["invoiceId"] = command.InvoiceId,
            ["lineId"] = p.Line.Id,
            ["lineSequence"] = p.Line.Sequence,
            ["billedAmount"] = p.Line.Billed,
            ["previousStatus"] = p.Line.Status?.ToString(),
            ["previousApprovedAmount"] = p.Line.PreviousApproved,
            ["status"] = p.Status.ToString(),
            ["approvedAmount"] = p.Approved,
            ["bulk"] = command.Bulk,
        };
        if (signals.SetReadAtUtc != DateTime.MinValue)
        {
            // D-122: what the close needs beside the ids (the record's ResolvedSignalIds): when the set was read and each policy code.
            facts[DecisionRecordWriter.BillingResolvedSetFact] = SetFact(signals);
        }

        return new BillingDecision(
            command.DecisionId,
            p.Action,
            p.Line.Id,
            BillingDecider.Human(command.DeciderSystemUserId, DeciderRole),
            facts,
            p.Outcome,
            Reason: p.Reason,
            Values: values,
            ResolvedSignalIds: signals.Signals.Count > 0 ? signals.Signals.Select(s => s.Id).ToList() : null,
            DecidedOn: decidedOn);
    }

    /// <summary>The stored set as a fact (<see cref="DecisionRecordWriter.BillingResolvedSetFact"/>): ids, read time and policy codes only.</summary>
    internal static Dictionary<string, object?> SetFact(InvoiceSubmissionAfterSteps.ResolvedSignalSet set) => new(StringComparer.Ordinal)
    {
        ["setReadAtUtc"] = set.SetReadAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ["signals"] = set.Signals
            .Select(s => new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = s.Id.ToString("D"), ["policyCode"] = s.PolicyCode })
            .ToList(),
    };

    private sealed record SignalSets(Dictionary<Guid, InvoiceSubmissionAfterSteps.ResolvedSignalSet> ByLine, LineDecisionResult? Failure);

    /// <summary>
    /// D-122 per decided line. A line whose decision is already recorded under this decision id keeps the set stored with that record (a
    /// stored set wins: never re-read or widened, so a Signal raised after the first attempt stays open). The stored sets of ALL the lines
    /// are found in ONE query (K2, inside the lock: never one lookup per line). Every other line's set is read now, in ONE query over those
    /// lines, with the clock read before it. Nothing is written; a refusal here changes nothing.
    /// </summary>
    private async Task<SignalSets> ResolveSignalSetsAsync(LineDecisionCommand command, IReadOnlyList<Proposal> proposals, CancellationToken ct)
    {
        var byLine = new Dictionary<Guid, InvoiceSubmissionAfterSteps.ResolvedSignalSet>(proposals.Count);
        var fresh = new List<Guid>();
        try
        {
            // One action per decision, so one query; grouped all the same, so a mixed list could never pair a line with another code's table.
            foreach (var group in proposals.GroupBy(p => p.Action, StringComparer.Ordinal))
            {
                var found = await _recorder.FindStoredResolvedSetsAsync(group.Key, group.Select(p => p.Line.Id).ToList(), command.DecisionId, ct).ConfigureAwait(false);
                foreach (var p in group)
                {
                    if (found.TryGetValue(p.Line.Id, out var stored) && stored is not null)
                    {
                        byLine[p.Line.Id] = new InvoiceSubmissionAfterSteps.ResolvedSignalSet(
                            stored.SetReadAtUtc ?? DateTime.MinValue,
                            stored.Signals.Select(s => new InvoiceSubmissionAfterSteps.ResolvedSignal(s.SignalId, s.PolicyCode)).ToList());
                    }
                    else
                    {
                        fresh.Add(p.Line.Id);
                    }
                }
            }
        }
        catch (DecisionRecordRefusedException ex)
        {
            _logger.LogWarning("Line decision on invoice {InvoiceId}: the stored Signal set could not be read ({Reason}); nothing changed.", command.InvoiceId, ex.Reason);
            return new SignalSets(byLine, ex.Reason == DecisionRecordRefusalReason.ReviewInvalid
                ? new LineDecisionResult(LineDecisionStatus.InvalidRequest, LineDecisionCodes.DecisionInvalid, ex.Reason)
                : new LineDecisionResult(LineDecisionStatus.Unavailable, LineDecisionCodes.Unavailable, "The decision could not be checked; nothing was changed. Try again."));
        }

        if (fresh.Count > 0)
        {
            var setReadAt = _time.GetUtcNow().UtcDateTime;
            IReadOnlyList<(InvoiceSubmissionAfterSteps.ResolvedSignal Signal, string SubjectEntity, Guid SubjectId)> open;
            try
            {
                open = await InvoiceSubmissionAfterSteps.ReadOpenSignalsAsync(_entities, invoiceId: null, fresh, _logger, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return new SignalSets(byLine, ReadFailed(command, ex));
            }

            foreach (var line in fresh)
            {
                // Ordered by id so the record's hash is stable.
                byLine[line] = new InvoiceSubmissionAfterSteps.ResolvedSignalSet(
                    setReadAt,
                    open.Where(s => s.SubjectId == line).Select(s => s.Signal).DistinctBy(s => s.Id).OrderBy(s => s.Id).ToList());
            }
        }

        return new SignalSets(byLine, null);
    }

    /// <summary>
    /// FR-51 when the decision closes at least one Signal: AppendTo on the invoice's core record and Write for every decided line that has a
    /// Signal (each Signal's subject is its line), asked as the caller. A line's Write is asked on its PARENT INVOICE (ontology-r1 2026-10-20:
    /// line subject → Write on the parent invoice, D-90; <see cref="HumanDecisionSignalClose.AuthorizeAsync"/>): every decided line was read
    /// by this invoice, so the parent is <see cref="LineDecisionCommand.InvoiceId"/>. Null when allowed or there is nothing to close.
    /// </summary>
    private async Task<LineDecisionResult?> AuthorizeCloseAsync(
        LineDecisionCommand command, Entity invoice, IReadOnlyDictionary<Guid, InvoiceSubmissionAfterSteps.ResolvedSignalSet> sets, CancellationToken ct)
    {
        var subjects = sets.Where(s => s.Value.Signals.Count > 0).Select(s => (InvoiceReviewReadService.LineEntity, s.Key)).ToList();
        if (subjects.Count == 0)
        {
            return null;
        }

        if (CoreOf(invoice) is not { } core)
        {
            return new LineDecisionResult(LineDecisionStatus.OwnerUnresolved, LineDecisionCodes.OwnerUnresolved, "The invoice names no single core record.");
        }

        var refusal = await HumanDecisionSignalClose.AuthorizeAsync(
            _entities, _probe, command.CallerBearerToken, new DecisionCoreRecord(core.Entity, core.Id), command.InvoiceId, subjects, _logger, ct).ConfigureAwait(false);
        switch (refusal)
        {
            case DecisionCallerRefusal.None:
                return null;
            case DecisionCallerRefusal.Unavailable:
                return new LineDecisionResult(LineDecisionStatus.Unavailable, LineDecisionCodes.Unavailable, "The decision could not be checked; nothing was changed. Try again.");
            default:
                _logger.LogWarning("Line decision on invoice {InvoiceId}: the caller may not close its Signals ({Refusal}); nothing changed.", command.InvoiceId, refusal);
                return new LineDecisionResult(LineDecisionStatus.Forbidden, LineDecisionCodes.NotAuthorized,
                    "You do not have permission to record a decision on these lines.");
        }
    }

    private sealed record CorrectionBuild(List<Entity> Rows, LineDecisionResult? Failure);

    private async Task<CorrectionBuild> BuildCorrectionsAsync(
        LineDecisionCommand command, Entity invoice, IReadOnlyList<Proposal> proposals, DateTime decidedOnUtc, CancellationToken ct)
    {
        var rows = new List<Entity>();
        foreach (var proposal in proposals)
        {
            rows.AddRange(CorrectionRowsFor(proposal.Line, command, invoice, decidedOnUtc));
        }

        if (rows.Count == 0)
        {
            return new CorrectionBuild(rows, null);
        }

        // The core record the correction rows are filed under (013): the invoice's own, which every one of its lines shares (D-96).
        var core = CoreOf(invoice);
        if (core is null)
        {
            return new CorrectionBuild(rows, new LineDecisionResult(LineDecisionStatus.OwnerUnresolved, LineDecisionCodes.OwnerUnresolved, "The invoice names no single core record."));
        }

        RecordOwnerResolution owner;
        try
        {
            owner = await _ownership.ResolveOwnerAsync(
                RecordOwnershipContext.ForParents(new RecordOwnershipParent?[] { new(core.Value.Entity, core.Value.Id) }), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new CorrectionBuild(rows, ReadFailed(command, ex));
        }

        if (!owner.IsOwned)
        {
            _logger.LogWarning("Line decision on invoice {InvoiceId}: no owner for the correction rows ({Code}); nothing changed.", command.InvoiceId, owner.RefusalCode);
            return new CorrectionBuild(rows, new LineDecisionResult(LineDecisionStatus.OwnerUnresolved, LineDecisionCodes.OwnerUnresolved, "The correction rows have no owner; nothing was changed."));
        }

        foreach (var row in rows)
        {
            owner.ApplyTo(row);
            RecordCreatorPerson.Stamp(row, command.DeciderSystemUserId);
            if (InvoiceFamilyCoreRecordGuard.Check(row) is { IsRefused: true } refused)
            {
                // A builder defect, never sent (013's guard decides every invoice-family row as built).
                throw new InvalidOperationException($"The correction row names no single core record ({refused.RefusalCode}).");
            }
        }

        return new CorrectionBuild(rows, null);
    }

    /// <summary>The invoice's one core record (matter, project, else work assignment); null when it names none.</summary>
    internal static (string Entity, Guid Id)? CoreOf(Entity invoice)
    {
        foreach (var (column, entity) in new[] { ("sprk_matter", "sprk_matter"), ("sprk_project", "sprk_project"), ("sprk_regardingworkassignment", "sprk_workassignment") })
        {
            if (invoice.GetAttributeValue<EntityReference>(column) is { } reference && reference.Id != Guid.Empty)
            {
                return (entity, reference.Id);
            }
        }

        return null;
    }

    /// <summary>
    /// The "Accepted in bulk" rows of one line (D-64): one per AI-sourced field, <c>sprk_islabel</c> false. A field is AI-sourced when the stored
    /// extraction JSON has a confidence for it and the line holds a value; the coded task, activity and expense codes count when the line was coded.
    /// The row id is deterministic (decision, line, field), so a repeat upserts the same row.
    /// </summary>
    private IEnumerable<Entity> CorrectionRowsFor(Target line, LineDecisionCommand command, Entity invoice, DateTime decidedOnUtc)
    {
        var extraction = line.Row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExtractionConfidence);
        if (string.IsNullOrWhiteSpace(extraction))
        {
            yield break;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(extraction);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                yield break;
            }

            if (root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in fields.EnumerateObject())
                {
                    if (!AiFieldColumns.TryGetValue(field.Name, out var column) || field.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var confidence = LineExtraction.Decimal(field.Value, "combined") ?? LineExtraction.Decimal(field.Value, "confidence");
                    if (LineExtraction.Decimal(field.Value, "confidence") is null || ValueText(line.Row, column) is not { } value)
                    {
                        continue;
                    }

                    yield return CorrectionRow(line, command, invoice, decidedOnUtc, field.Name, value, confidence, "invoice-extraction");
                }
            }

            if (root.TryGetProperty("coding", out var coding) && coding.ValueKind == JsonValueKind.Object
                && !string.Equals(LineExtraction.Text(coding, "status"), "NotCoded", StringComparison.Ordinal))
            {
                var codingConfidence = LineExtraction.Decimal(coding, "confidence");
                foreach (var (member, name) in new[] { ("task", BillingFieldNames.TaskCode), ("activity", BillingFieldNames.ActivityCode), ("expense", BillingFieldNames.ExpenseCode) })
                {
                    if (coding.TryGetProperty(member, out var code) && code.ValueKind == JsonValueKind.Object && LineExtraction.Text(code, "code") is { Length: > 0 } text)
                    {
                        yield return CorrectionRow(line, command, invoice, decidedOnUtc, name, text, codingConfidence, "invoice-line-code");
                    }
                }
            }
        }
    }

    private Entity CorrectionRow(
        Target line, LineDecisionCommand command, Entity invoice, DateTime decidedOnUtc, string fieldName, string value, decimal? confidence, string sourceCode)
    {
        var id = IdOf($"ebilling-correction-{command.DecisionId:D}-{line.Id:D}-{fieldName}");
        var row = new Entity(CorrectionEntity, id)
        {
            ["sprk_name"] = $"{fieldName} - line {line.Sequence.ToString(CultureInfo.InvariantCulture)}",
            ["sprk_fieldname"] = fieldName,
            ["sprk_aivalue"] = Cap(value),
            ["sprk_finalvalue"] = Cap(value),
            ["sprk_outcometype"] = new OptionSetValue(OutcomeAcceptedInBulk),
            ["sprk_islabel"] = false,
            ["sprk_sourcecode"] = sourceCode,
            ["sprk_reviewerrole"] = DeciderRole,
            ["sprk_reviewedon"] = decidedOnUtc,
            ["sprk_reviewedby"] = new EntityReference(SystemUserEntity, command.DeciderSystemUserId),
            ["sprk_invoice"] = new EntityReference(InvoiceReviewReadService.InvoiceEntity, command.InvoiceId),
            ["sprk_invoiceline"] = new EntityReference(InvoiceReviewReadService.LineEntity, line.Id),
        };
        if (confidence is { } c)
        {
            row["sprk_aiconfidence"] = Math.Round(c, 4, MidpointRounding.AwayFromZero);
        }

        // 013: exactly one core-record lookup, plus the resolver fields, copied from the invoice (a line shares its invoice's core record).
        var core = CoreOf(invoice)!.Value;
        row[CoreAncestorResolver.StampColumnFor(core.Entity)] = new EntityReference(core.Entity, core.Id);
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
    }

    private static string? ValueText(Entity row, string column)
    {
        if (!row.Contains(column) || row[column] is not { } raw)
        {
            return null;
        }

        var text = raw switch
        {
            Money m => m.Value.ToString("0.00", CultureInfo.InvariantCulture),
            DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString(),
        };
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string Cap(string value) => value.Length > 4000 ? value[..4000] : value;

    internal static Guid IdOf(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));

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
        var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return page.Entities.FirstOrDefault();
    }

    private static ReviewTotalsDto TotalsOf(Entity invoice, InvoiceTotalsCalculation calculation) => new(
        invoice.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceBilledTotal)?.Value,
        calculation.FeesTotal, calculation.ExpensesTotal, calculation.LineAdjustmentsTotal, calculation.InvoiceAdjustmentsTotal, calculation.ApprovedTotal);
}
