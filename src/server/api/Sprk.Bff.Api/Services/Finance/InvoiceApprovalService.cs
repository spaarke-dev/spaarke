// ebilling-r1 task 103 (FR-35, FR-11, FR-30, I-2; D-40, D-72, D-78, D-100; notes 103-approval-worklist.md).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceLifecycleService moves the status (014) but checks no approver; BillingDecisionRecorder records a decision (074) but
//       decides nothing; InvoiceLineDecisionService decides lines (100); SubmissionOverrideService decides a held submission (105). Nothing
//       approves an invoice, checks I-2 or freezes the approved total on the portion (grep: no approve route, no sprk_approvedamount writer).
//   (2) Extension — each of those owns one invariant (ADR-002); approve must run them in one order under one lock (claim → transition →
//       portion → record) with a recovery for each partial state. Folding that into the lifecycle service would make it the owner of
//       eligibility and the ledger; folding it into the line service would give a line-scoped service the invoice decision.
//   (3) Cost of doing nothing — no invoice can be approved (FR-35), so nothing reaches Sent to AP and success criterion 4 fails.
//
// Placement (bff-extensions.md): BFF, Services/Finance (B1 caller identity, B4 live request). Scoped like its scoped dependencies.

using System.Data;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>The outcome class of an approve call. The endpoint maps each to one HTTP status and one stable code.</summary>
public enum ApprovalOutcome
{
    /// <summary>This call approved the invoice (or completed this caller's own interrupted approval).</summary>
    Approved,

    /// <summary>The invoice was already approved: nothing was written by this call (a second approve is a no-op, FR-35).</summary>
    AlreadyApproved,

    InvalidRequest,
    InvoiceNotFound,
    NotEligible,
    LinesOpen,
    Held,
    NotInReview,
    Conflict,
    Busy,
    Forbidden,

    /// <summary>Nothing was changed by this call (a read, the lock or the first write failed). Try again.</summary>
    Unavailable,

    /// <summary>Something was written and a later step did not finish. The same caller repeating the approve completes it.</summary>
    Incomplete,

    /// <summary>A server-side invariant failed (no single core record, a decision the writer would refuse); nothing was changed.</summary>
    Failed,
}

/// <summary>Stable ProblemDetails codes of the approval routes (ADR-019).</summary>
public static class InvoiceApprovalCodes
{
    public const string NotEligible = "ebilling.approver.not_eligible";
    public const string LinesOpen = "ebilling.approve.lines_open";
    public const string Held = "ebilling.approve.held";
    public const string NotInReview = LineDecisionCodes.NotInReview;
    public const string PortionMissing = "ebilling.approval.missing";
    public const string PortionAmbiguous = "ebilling.approval.ambiguous";
    public const string AlreadyDecided = "ebilling.approval.already_decided";
    public const string Changed = "ebilling.approval.changed";
    public const string TransitionRefused = "ebilling.approve.refused";
    public const string TotalsRefused = LineDecisionCodes.TotalsRefused;
    public const string Busy = LineDecisionCodes.Busy;
    public const string Unavailable = "ebilling.approval.unavailable";
    public const string Incomplete = "ebilling.approval.incomplete";
    public const string CoreUnresolved = "ebilling.approval.core_unresolved";
    public const string DecisionInvalid = LineDecisionCodes.DecisionInvalid;
    public const string DeciderUnresolved = LineDecisionCodes.DeciderUnresolved;
}

/// <summary>Open-line counts (I-2).</summary>
public sealed record ApprovalOpenLinesDto(int Pending, int Flagged)
{
    public int Total => Pending + Flagged;
}

/// <summary>The approval state the Invoice Review page shows (GET, and the body of a 200 approve).</summary>
/// <param name="Status"><c>Pending</c>, <c>Approved</c>, <c>Rejected</c>, <c>Returned</c>, or <c>None</c> when the invoice has no portion.</param>
/// <param name="EligibleApprovers">The display names of the people who may approve (live), e.g. "Awaiting: J. Smith, A. Lee".</param>
/// <param name="NoEligibleApprover">Nobody may approve: the core record's roles name no assigned person linked to a user (FR-14).</param>
/// <param name="EligibilityUnavailable">The live resolution failed; <paramref name="CallerEligible"/> is false until it succeeds.</param>
/// <param name="CanApprove">The caller is eligible, the invoice is In Review and not held, no line is Pending or Flagged, the portion is Pending.</param>
/// <param name="UndecidedCleanLines">Clean lines an approve accepts in bulk (D-72).</param>
public sealed record InvoiceApprovalStateDto(
    Guid InvoiceId,
    Guid? ApprovalId,
    string Status,
    IReadOnlyList<string> EligibleApprovers,
    bool NoEligibleApprover,
    bool EligibilityUnavailable,
    bool CallerEligible,
    bool CanApprove,
    ApprovalOpenLinesDto OpenLines,
    int UndecidedCleanLines,
    bool Held,
    decimal? ApprovedAmount,
    string? DecidedBy,
    DateTime? DecidedOn,
    Guid? DecisionRecordId,
    DateTime? EligibleResolvedOn);

public sealed record ApprovalResult(
    ApprovalOutcome Outcome,
    string Code,
    string? Detail = null,
    ApprovalOpenLinesDto? OpenLines = null,
    Guid? DecisionId = null,
    Guid? DecisionRecordId = null,
    string? FailedStep = null,
    IReadOnlyList<Guid>? CommittedLineIds = null,
    Guid? LineDecisionId = null);

/// <summary>
/// Approves an invoice (FR-35): any one eligible approver (D-40), I-2 enforced, undecided Clean lines accepted in bulk (D-72), the status moved to
/// Approved through 014, the approved total frozen on the portion, and exactly one Decision Record naming who decided (074, D-100).
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> The route has already proven Write on the invoice as the caller (<c>ebilling.invoice.decide</c>). Eligibility is
/// resolved LIVE from the core record on every call (<see cref="EligibleApproverResolver.ResolveAsync"/>); the stored N:N never grants. A caller who
/// is not eligible gets <see cref="ApprovalOutcome.NotEligible"/> before anything is written. The decider is the caller's <c>systemuserid</c> from
/// the authenticated principal; no contact id, Signal id or policy version is taken from the request.</para>
/// <para><b>Exactly once (074 K-1 / K-4), three layers.</b> (1) Two processing locks, the invoice-decision lock shared with 104 / 105
/// (<see cref="SubmissionOverrideService.LockKey"/>) and the line-decision lock (<see cref="InvoiceLineDecisionService.LockKeyFor"/>), so one
/// decision runs per invoice. (2) The CLAIM: before the transition, the portion's decider, time and approved amount are written conditional on
/// the portion's row version as read; a concurrent approve that read the same version gets 409 <c>ebilling.approval.changed</c> and writes
/// nothing else. (3) Only the call whose 014 transition answered Transitioned writes the portion as Approved and records; AlreadyInTarget is a
/// no-op. The decision id is DERIVED from the portion (<see cref="DecisionIdFor"/>), so every attempt by anyone uses one id and the writer's
/// same-id check refuses a second, different record.</para>
/// <para><b>Order and recovery (074 K-3).</b> Every permanent refusal the writer can give is checked before the first write: the subject has one
/// core record, the decision passes <see cref="BillingDecisionRecorder.Preflight"/>, and approve resolves no Signal and names no policy version.
/// The remaining record refusals are transient (a read fault). So the order is: accept Clean → Unreviewed rows → totals → claim → transition →
/// portion Approved → record → link. A failure after the transition answers 503 <c>ebilling.approval.incomplete</c>; the CLAIMANT repeating the
/// approve finds the invoice Approved and completes the portion, the record (same id, same content: the hash ignores facts and time) and the link.
/// Anyone else gets a 200 no-op and no record.</para>
/// </remarks>
public sealed class InvoiceApprovalService
{
    public const string ApproverRole = "invoice-approver";
    internal const string ApproveActionCode = "approve-invoice";

    internal const int PortionPending = InvoiceSubmissionPersister.ApprovalStatusPending;
    internal const int PortionApproved = 100000001;
    internal const int PortionRejected = 100000002;
    internal const int PortionReturned = 100000003;

    internal const string PortionAmount = "sprk_approvedamount";
    internal const string PortionDecidedOn = "sprk_decidedon";
    internal const string PortionDecidedBy = "sprk_decidedby";
    internal const string PortionRecord = "sprk_decisionrecord";

    /// <summary>Long enough for the accept-Clean, the totals and the record; a lock that outlives a crash expires by itself.</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private static readonly string[] InvoiceColumns =
    [
        "statuscode", InvoiceTotalsService.InvoiceBilledTotal, "sprk_matter", "sprk_project", "sprk_regardingworkassignment",
    ];

    private readonly IGenericEntityService _entities;
    private readonly EligibleApproverResolver _resolver;
    private readonly InvoiceLineDecisionService _lines;
    private readonly IInvoiceTotalsService _totals;
    private readonly IInvoiceLifecycleService _lifecycle;
    private readonly BillingDecisionRecorder _recorder;
    private readonly IIdempotencyService _locks;
    private readonly ILogger<InvoiceApprovalService> _logger;
    private readonly TimeProvider _time;

    public InvoiceApprovalService(
        IGenericEntityService entities,
        EligibleApproverResolver resolver,
        InvoiceLineDecisionService lines,
        IInvoiceTotalsService totals,
        IInvoiceLifecycleService lifecycle,
        BillingDecisionRecorder recorder,
        IIdempotencyService locks,
        ILogger<InvoiceApprovalService> logger)
        : this(entities, resolver, lines, totals, lifecycle, recorder, locks, logger, null)
    {
    }

    internal InvoiceApprovalService(
        IGenericEntityService entities,
        EligibleApproverResolver resolver,
        InvoiceLineDecisionService lines,
        IInvoiceTotalsService totals,
        IInvoiceLifecycleService lifecycle,
        BillingDecisionRecorder recorder,
        IIdempotencyService locks,
        ILogger<InvoiceApprovalService> logger,
        TimeProvider? time)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _lines = lines ?? throw new ArgumentNullException(nameof(lines));
        _totals = totals ?? throw new ArgumentNullException(nameof(totals));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The approval's one decision id: derived from the portion, so every attempt by every approver uses the same id (074: "103: from the
    /// approval portion").</summary>
    public static Guid DecisionIdFor(Guid approvalId) => InvoiceSubmissionPersister.IdOf($"ebilling-approve-{approvalId:D}");

    /// <summary>The decision id of the bulk accept an approve runs over the undecided Clean lines (D-72).</summary>
    public static Guid AcceptCleanDecisionIdFor(Guid approvalId) => InvoiceSubmissionPersister.IdOf($"ebilling-approve-accept-clean-{approvalId:D}");

    // ── GET ──

    /// <summary>
    /// The approval state for the caller, with the D-78 recheck: eligibility is resolved live and, while the portion is Pending on an invoice In
    /// Review, mirrored onto the stored set (best effort). Null when the invoice does not exist (uniform 404). Throws on a read fault.
    /// </summary>
    public async Task<InvoiceApprovalStateDto?> GetAsync(Guid invoiceId, Guid? callerSystemUserId, CancellationToken ct)
    {
        var invoice = await ReadInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice is null)
        {
            return null;
        }

        var portions = await _resolver.ReadPortionsAsync(invoiceId, ct).ConfigureAwait(false);
        var portion = portions.Count == 1 ? portions[0] : null;
        var lines = await ReadLinesAsync(invoiceId, ct).ConfigureAwait(false);
        var held = await IsHeldAsync(invoiceId, ct).ConfigureAwait(false);

        EligibleApproverSet? set = null;
        if (InvoiceLineDecisionService.CoreOf(invoice) is { } core)
        {
            try
            {
                set = await _resolver.ResolveAsync(core.Entity, core.Id, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Approval state of invoice {InvoiceId}: eligibility could not be resolved ({ExceptionType}).", invoiceId, ex.GetType().Name);
            }
        }

        var resolvedOn = portion?.GetAttributeValue<DateTime?>(EligibleApproverResolver.ResolvedOnColumn);
        if (set is not null && portion is not null && StatusOf(portion) == PortionPending && InvoiceStatus(invoice) == InvoiceStatusCodes.InReview)
        {
            try
            {
                var sync = await _resolver.SyncAsync(portion.Id, set, resolvedOn, ct).ConfigureAwait(false);
                if (sync.Stamped)
                {
                    resolvedOn = _time.GetUtcNow().UtcDateTime;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The stored set only feeds the view; the next GET or refresh run retries. Eligibility here is the live set.
                _logger.LogWarning("Approval state of invoice {InvoiceId}: the eligible approvers could not be stored ({ExceptionType}).", invoiceId, ex.GetType().Name);
            }
        }

        return State(invoice, portion, lines, held, set, eligibilityUnavailable: set is null, callerSystemUserId, resolvedOn);
    }

    // ── POST approve ──

    /// <summary>Approves the invoice as the caller. See the class remarks for the order and the exactly-once rule.</summary>
    public async Task<ApprovalResult> ApproveAsync(Guid invoiceId, Guid callerSystemUserId, string? callerBearerToken, CancellationToken ct)
    {
        if (invoiceId == Guid.Empty || callerSystemUserId == Guid.Empty)
        {
            return new ApprovalResult(ApprovalOutcome.InvalidRequest, LineDecisionCodes.InvalidRequest, "An invoice and a caller are required.");
        }

        var owner = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var decisionKey = SubmissionOverrideService.LockKey(invoiceId);
        var lineKey = InvoiceLineDecisionService.LockKeyFor(invoiceId);

        if (await AcquireAsync(decisionKey, owner, invoiceId, ct).ConfigureAwait(false) is { } first)
        {
            return first;
        }

        try
        {
            if (await AcquireAsync(lineKey, owner, invoiceId, ct).ConfigureAwait(false) is { } second)
            {
                return second;
            }

            try
            {
                return await ApproveHeldAsync(invoiceId, callerSystemUserId, callerBearerToken, ct).ConfigureAwait(false);
            }
            finally
            {
                await ReleaseAsync(lineKey, owner, invoiceId).ConfigureAwait(false);
            }
        }
        finally
        {
            await ReleaseAsync(decisionKey, owner, invoiceId).ConfigureAwait(false);
        }
    }

    private async Task<ApprovalResult> ApproveHeldAsync(Guid invoiceId, Guid caller, string? callerBearerToken, CancellationToken ct)
    {
        Entity? invoice;
        IReadOnlyList<Entity> portions;
        try
        {
            invoice = await ReadInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
            if (invoice is null)
            {
                return new ApprovalResult(ApprovalOutcome.InvoiceNotFound, "ebilling.invoice.not_found");
            }

            portions = await _resolver.ReadPortionsAsync(invoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "read", ex);
        }

        if (portions.Count != 1)
        {
            _logger.LogWarning("Approve of invoice {InvoiceId}: {Count} approval portions; nothing changed.", invoiceId, portions.Count);
            return portions.Count == 0
                ? new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.PortionMissing, "The invoice has no approval record.")
                : new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.PortionAmbiguous, "The invoice has more than one approval record.");
        }

        var portion = portions[0];
        var approvalId = portion.Id;
        var decisionId = DecisionIdFor(approvalId);
        var portionStatus = StatusOf(portion);
        var invoiceStatus = InvoiceStatus(invoice);

        // ── Already approved (a second approve, or the claimant completing an interrupted one). ──
        if (portionStatus == PortionApproved || invoiceStatus == InvoiceStatusCodes.Approved)
        {
            return await CompleteOrNoOpAsync(invoice, portion, caller, decisionId, ct).ConfigureAwait(false);
        }

        if (portionStatus is PortionRejected or PortionReturned)
        {
            return new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.AlreadyDecided, "The invoice was already decided.");
        }

        if (invoiceStatus != InvoiceStatusCodes.InReview)
        {
            return new ApprovalResult(ApprovalOutcome.NotInReview, InvoiceApprovalCodes.NotInReview, "Only an invoice In Review can be approved.");
        }

        // ── 074 K-3: the subject has one core record (the writer's filing check), before anything is written. ──
        if (InvoiceLineDecisionService.CoreOf(invoice) is not { } core)
        {
            _logger.LogError("Approve of invoice {InvoiceId}: the invoice names no core record; nothing changed.", invoiceId);
            return new ApprovalResult(ApprovalOutcome.Failed, InvoiceApprovalCodes.CoreUnresolved, "The invoice names no core record.");
        }

        // ── FR-35: eligibility, LIVE from the core record. The stored N:N never grants. ──
        EligibleApproverSet set;
        try
        {
            set = await _resolver.ResolveAsync(core.Entity, core.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "eligibility", ex);
        }

        if (set.For(caller) is not { } approver)
        {
            _logger.LogWarning("Approve of invoice {InvoiceId}: the caller is not an eligible approver ({Eligible} eligible); nothing changed.",
                invoiceId, set.Approvers.Count);
            return new ApprovalResult(ApprovalOutcome.NotEligible, InvoiceApprovalCodes.NotEligible, "You are not an eligible approver of this invoice.");
        }

        // ── FR-20: a blocked submission holds the invoice from approval until it is decided. ──
        List<Entity> lineRows;
        try
        {
            if (await IsHeldAsync(invoiceId, ct).ConfigureAwait(false))
            {
                return new ApprovalResult(ApprovalOutcome.Held, InvoiceApprovalCodes.Held, "The invoice is held until its blocked submission is decided.");
            }

            lineRows = await ReadLinesAsync(invoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "read", ex);
        }

        // ── I-2: no line Pending or Flagged (a line with no recognised status counts as Pending: fail closed). ──
        var open = OpenLines(lineRows);
        if (open.Total > 0)
        {
            return new ApprovalResult(ApprovalOutcome.LinesOpen, InvoiceApprovalCodes.LinesOpen,
                $"{open.Pending} lines are still being processed and {open.Flagged} are flagged; decide them before approving.", OpenLines: open);
        }

        // ── D-72 (F-2): undecided Clean lines are accepted in bulk ("Accepted in bulk" rows, not labels). The caller holds the line lock. ──
        if (CleanCount(lineRows) > 0)
        {
            var lineDecisionId = AcceptCleanDecisionIdFor(approvalId);
            var accepted = await _lines.AcceptCleanHeldAsync(invoiceId, lineDecisionId, caller, callerBearerToken, ct).ConfigureAwait(false);
            if (!accepted.IsApplied)
            {
                return FromLineResult(accepted, invoiceId, "accept-clean", lineDecisionId);
            }
        }

        var decidedOn = Seconds(_time.GetUtcNow());

        // ── D-72: Unreviewed rows for the AI-sourced fields of individually decided lines that no reviewer touched. ──
        var unreviewed = await _lines.WriteUnreviewedAsync(invoiceId, approvalId, caller, ApproverRole, decidedOn.UtcDateTime, ct).ConfigureAwait(false);
        if (!unreviewed.IsApplied)
        {
            return FromLineResult(unreviewed, invoiceId, "unreviewed", lineDecisionId: null);
        }

        // ── The approved total to freeze (016 recomputes it from the decided lines). ──
        InvoiceTotalsCalculation totals;
        try
        {
            totals = await _totals.RecalculateAsync(invoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "totals", ex);
        }

        if (totals.IsRefused)
        {
            return new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.TotalsRefused, totals.Refusal!.Code);
        }

        var approvedAmount = totals.ApprovedTotal;
        var decision = BuildDecision(decisionId, invoice, approvalId, caller, approvedAmount, decidedOn);
        try
        {
            _recorder.Preflight(decision);
        }
        catch (DecisionRecordRefusedException ex)
        {
            _logger.LogError("Approve of invoice {InvoiceId}: the Decision Record would be refused ({Reason}); nothing changed.", invoiceId, ex.Reason);
            return new ApprovalResult(ApprovalOutcome.Failed, InvoiceApprovalCodes.DecisionInvalid, ex.Reason.ToString());
        }

        // ── The CLAIM: decider, time and frozen amount, conditional on the portion as read. ──
        var version = portion.TryGetAttributeValue<long>("versionnumber", out var v) ? v : 0L;
        if (version <= 0)
        {
            return new ApprovalResult(ApprovalOutcome.Unavailable, InvoiceApprovalCodes.Unavailable, "The approval version could not be read; nothing was changed.");
        }

        try
        {
            await _entities.UpdateIfRowVersionMatchesAsync(
                EligibleApproverResolver.ApprovalEntity, approvalId, version.ToString(CultureInfo.InvariantCulture),
                ClaimFields(approver.ContactId, decidedOn, approvedAmount), ct).ConfigureAwait(false);
        }
        catch (DBConcurrencyException)
        {
            return new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.Changed, "The approval changed while it was prepared. Reload and try again.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "claim", ex);
        }

        // ── 014: the only status writer. Only Transitioned records. ──
        InvoiceTransitionResult transition;
        try
        {
            transition = await _lifecycle.TransitionAsync(invoiceId, InvoiceLifecycleStatus.Approved, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The outcome is unknown (a timeout can land after the commit): the claimant's repeat completes it either way.
            return Incomplete(invoiceId, decisionId, "transition", ex.GetType().Name);
        }

        switch (transition.Outcome)
        {
            case InvoiceTransitionOutcome.Refused:
                _logger.LogWarning("Approve of invoice {InvoiceId}: the transition was refused ({Reason}).", invoiceId, transition.Reason);
                return new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.TransitionRefused, transition.Reason);
            case InvoiceTransitionOutcome.AlreadyInTarget:
                // Another approve moved it first (only possible when the lock was defeated): it records, this call does not.
                _logger.LogWarning("Approve of invoice {InvoiceId}: already Approved by another call; nothing recorded.", invoiceId);
                return new ApprovalResult(ApprovalOutcome.AlreadyApproved, "ok", DecisionId: decisionId);
        }

        return await FinishAsync(invoiceId, approvalId, decision, PortionPending, recordId: null, approver.ContactId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The invoice is already Approved (or the portion is). The claimant (the person the portion names, by the user linked to that contact)
    /// completes what is missing: the portion status, the record (same id and content) and the link. Anyone else gets a no-op, and at most the
    /// portion's status is aligned (no attribution changes).
    /// </summary>
    private async Task<ApprovalResult> CompleteOrNoOpAsync(Entity invoice, Entity portion, Guid caller, Guid decisionId, CancellationToken ct)
    {
        var invoiceId = invoice.Id;
        var decidedBy = portion.GetAttributeValue<EntityReference>(PortionDecidedBy);
        var recordId = portion.GetAttributeValue<EntityReference>(PortionRecord)?.Id;
        var portionStatus = StatusOf(portion);

        bool claimant;
        try
        {
            claimant = decidedBy is not null && decidedBy.Id != Guid.Empty && await UserOfContactAsync(decidedBy.Id, ct).ConfigureAwait(false) == caller;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "read", ex);
        }

        var complete = portionStatus == PortionApproved && recordId is { } r && r != Guid.Empty;
        if (complete || !claimant || InvoiceStatus(invoice) != InvoiceStatusCodes.Approved)
        {
            if (!complete && !claimant && portionStatus == PortionPending && decidedBy is not null && InvoiceStatus(invoice) == InvoiceStatusCodes.Approved)
            {
                // The claimant's call stopped after the transition: align the portion's status for the view; the record stays the claimant's.
                try
                {
                    await _entities.UpdateAsync(EligibleApproverResolver.ApprovalEntity, portion.Id,
                        new Dictionary<string, object> { [EligibleApproverResolver.ApprovalStatus] = new OptionSetValue(PortionApproved) }, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Approve of invoice {InvoiceId}: the portion status could not be aligned ({ExceptionType}).", invoiceId, ex.GetType().Name);
                }
            }

            return new ApprovalResult(ApprovalOutcome.AlreadyApproved, "ok", DecisionId: decisionId, DecisionRecordId: recordId);
        }

        // The claimant's repeat: rebuild the decision from what the claim froze (the hash ignores facts and time, so it matches a first write).
        var decidedOn = portion.GetAttributeValue<DateTime?>(PortionDecidedOn) is { } on
            ? new DateTimeOffset(DateTime.SpecifyKind(on, DateTimeKind.Utc))
            : Seconds(_time.GetUtcNow());
        var amount = portion.GetAttributeValue<Money>(PortionAmount)?.Value ?? 0m;
        var decision = BuildDecision(decisionId, invoice, portion.Id, caller, amount, decidedOn);
        _logger.LogInformation("Approve of invoice {InvoiceId}: completing the caller's interrupted approval.", invoiceId);
        return await FinishAsync(invoiceId, portion.Id, decision, portionStatus, recordId, decidedBy!.Id, ct).ConfigureAwait(false);
    }

    /// <summary>After the transition: the portion as Approved (re-asserting the decider), the record, the link. Each step is idempotent.</summary>
    private async Task<ApprovalResult> FinishAsync(
        Guid invoiceId, Guid approvalId, BillingDecision decision, int portionStatus, Guid? recordId, Guid deciderContactId, CancellationToken ct)
    {
        var amount = decision.Facts["approvedAmount"] is decimal d ? d : 0m;
        if (portionStatus != PortionApproved)
        {
            try
            {
                var fields = ClaimFields(deciderContactId, decision.DecidedOn!.Value, amount);
                fields[EligibleApproverResolver.ApprovalStatus] = new OptionSetValue(PortionApproved);
                await _entities.UpdateAsync(EligibleApproverResolver.ApprovalEntity, approvalId, fields, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Incomplete(invoiceId, decision.DecisionId, "portion", ex.GetType().Name);
            }
        }

        if (recordId is { } existing && existing != Guid.Empty)
        {
            return Approved(invoiceId, decision.DecisionId, existing);
        }

        BillingDecisionRecordResult recorded;
        try
        {
            recorded = await _recorder.RecordAsync(decision, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Incomplete(invoiceId, decision.DecisionId, "record", (ex as DecisionRecordRefusedException)?.Reason.ToString() ?? ex.GetType().Name);
        }

        try
        {
            await _entities.UpdateAsync(EligibleApproverResolver.ApprovalEntity, approvalId,
                new Dictionary<string, object> { [PortionRecord] = new EntityReference(InvoiceReviewReadService.DecisionRecordEntity, recorded.RecordId) }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Incomplete(invoiceId, decision.DecisionId, "link", ex.GetType().Name);
        }

        return Approved(invoiceId, decision.DecisionId, recorded.RecordId);
    }

    private ApprovalResult Approved(Guid invoiceId, Guid decisionId, Guid recordId)
    {
        _logger.LogInformation("Invoice {InvoiceId} approved; Decision Record {RecordId}.", invoiceId, recordId);
        return new ApprovalResult(ApprovalOutcome.Approved, "ok", DecisionId: decisionId, DecisionRecordId: recordId);
    }

    // ── Builders ──

    private static BillingDecision BuildDecision(Guid decisionId, Entity invoice, Guid approvalId, Guid caller, decimal approvedAmount, DateTimeOffset decidedOn)
    {
        // Ids and amounts only (ADR-015). Values null on every attempt (074 K-5); no Signal is resolved and no policy version named (K-2).
        var facts = new Dictionary<string, object?>
        {
            ["invoiceId"] = invoice.Id,
            ["approvalId"] = approvalId,
            ["approvedAmount"] = approvedAmount,
            ["billedAmount"] = invoice.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceBilledTotal)?.Value,
        };
        return new BillingDecision(
            decisionId,
            ApproveActionCode,
            invoice.Id,
            BillingDecider.Human(caller, ApproverRole),
            facts,
            "Approved",
            Written: [new DecisionRecordRef(InvoiceReviewReadService.InvoiceEntity, invoice.Id), new DecisionRecordRef(EligibleApproverResolver.ApprovalEntity, approvalId)],
            DecidedOn: decidedOn);
    }

    private static Dictionary<string, object> ClaimFields(Guid contactId, DateTimeOffset decidedOn, decimal approvedAmount) => new()
    {
        [PortionDecidedBy] = new EntityReference(EligibleApproverResolver.ContactEntity, contactId),
        [PortionDecidedOn] = decidedOn.UtcDateTime,
        [PortionAmount] = new Money(approvedAmount),
    };

    private static DateTimeOffset Seconds(DateTimeOffset now) => new(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    private InvoiceApprovalStateDto State(
        Entity invoice, Entity? portion, IReadOnlyList<Entity> lines, bool held, EligibleApproverSet? set, bool eligibilityUnavailable,
        Guid? caller, DateTime? resolvedOn)
    {
        var status = portion is null ? "None" : StatusOf(portion) switch
        {
            PortionPending => "Pending",
            PortionApproved => "Approved",
            PortionRejected => "Rejected",
            PortionReturned => "Returned",
            _ => "Unknown",
        };
        var open = OpenLines(lines);
        var callerEligible = set is not null && caller is { } user && set.For(user) is not null;
        var decided = portion is not null && StatusOf(portion) != PortionPending;
        return new InvoiceApprovalStateDto(
            invoice.Id,
            portion?.Id,
            status,
            set?.Approvers.Select(a => a.Name ?? string.Empty).Where(n => n.Length > 0).ToList() ?? [],
            NoEligibleApprover: set is not null && set.NoEligibleApprover,
            EligibilityUnavailable: eligibilityUnavailable,
            CallerEligible: callerEligible,
            CanApprove: callerEligible && portion is not null && StatusOf(portion) == PortionPending
                && InvoiceStatus(invoice) == InvoiceStatusCodes.InReview && !held && open.Total == 0,
            OpenLines: open,
            UndecidedCleanLines: CleanCount(lines),
            Held: held,
            ApprovedAmount: decided ? portion!.GetAttributeValue<Money>(PortionAmount)?.Value : null,
            DecidedBy: decided ? portion!.GetAttributeValue<EntityReference>(PortionDecidedBy)?.Name : null,
            DecidedOn: decided ? portion!.GetAttributeValue<DateTime?>(PortionDecidedOn) : null,
            DecisionRecordId: portion?.GetAttributeValue<EntityReference>(PortionRecord)?.Id,
            EligibleResolvedOn: resolvedOn);
    }

    private static ApprovalOpenLinesDto OpenLines(IEnumerable<Entity> lines)
    {
        int pending = 0, flagged = 0;
        foreach (var line in lines)
        {
            var status = line.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value;
            if (status == (int)InvoiceLineStatus.Flagged)
            {
                flagged++;
            }
            else if (status is null || status == (int)InvoiceLineStatus.Pending || !Enum.IsDefined(typeof(InvoiceLineStatus), status.Value))
            {
                pending++;
            }
        }

        return new ApprovalOpenLinesDto(pending, flagged);
    }

    private static int CleanCount(IEnumerable<Entity> lines) => lines.Count(l =>
        l.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value == (int)InvoiceLineStatus.Clean
        && l.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value is (int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense);

    private static int StatusOf(Entity portion) =>
        portion.GetAttributeValue<OptionSetValue>(EligibleApproverResolver.ApprovalStatus)?.Value ?? PortionPending;

    private static int? InvoiceStatus(Entity invoice) => invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value;

    // ── Results ──

    private ApprovalResult Unavailable(Guid invoiceId, string step, Exception ex)
    {
        _logger.LogWarning("Approve of invoice {InvoiceId}: the {Step} step failed ({ExceptionType}); nothing was changed by this step.", invoiceId, step, ex.GetType().Name);
        return new ApprovalResult(ApprovalOutcome.Unavailable, InvoiceApprovalCodes.Unavailable, "The approval could not be completed; nothing was changed. Try again.", FailedStep: step);
    }

    private ApprovalResult Incomplete(Guid invoiceId, Guid decisionId, string step, string detail)
    {
        _logger.LogError("Approve of invoice {InvoiceId}: the {Step} step failed after the status moved ({Detail}); the approver repeats the approve to finish it.",
            invoiceId, step, detail);
        return new ApprovalResult(ApprovalOutcome.Incomplete, InvoiceApprovalCodes.Incomplete,
            "The invoice was approved but a later step did not finish. Approve again to complete it.", DecisionId: decisionId, FailedStep: step);
    }

    /// <summary>A refusal of the bulk accept or the Unreviewed rows: nothing was approved.</summary>
    private static ApprovalResult FromLineResult(LineDecisionResult result, Guid invoiceId, string step, Guid? lineDecisionId) => result.Status switch
    {
        LineDecisionStatus.Forbidden => new(ApprovalOutcome.Forbidden, result.Code, result.Detail, FailedStep: step),
        LineDecisionStatus.Busy or LineDecisionStatus.Changed or LineDecisionStatus.TotalsRefused or LineDecisionStatus.NotInReview
            or LineDecisionStatus.LineNotDecidable => new(ApprovalOutcome.Conflict, result.Code, result.Detail, FailedStep: step),
        LineDecisionStatus.InvoiceNotFound => new(ApprovalOutcome.InvoiceNotFound, result.Code),
        LineDecisionStatus.OwnerUnresolved or LineDecisionStatus.InvalidRequest => new(ApprovalOutcome.Failed, result.Code, result.Detail, FailedStep: step),
        // The lines were written and a later line step failed: the caller completes the line decision (lines/decisions with these ids and
        // this decision id), then approves again. The invoice is still In Review.
        LineDecisionStatus.Incomplete => new(ApprovalOutcome.Incomplete, result.Code, result.Detail, FailedStep: step,
            CommittedLineIds: result.CommittedLineIds, LineDecisionId: lineDecisionId),
        _ => new(ApprovalOutcome.Unavailable, InvoiceApprovalCodes.Unavailable, result.Detail, FailedStep: step),
    };

    // ── Locks ──

    private async Task<ApprovalResult?> AcquireAsync(string key, string owner, Guid invoiceId, CancellationToken ct)
    {
        bool acquired;
        try
        {
            acquired = await _locks.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Fail closed: without the lock the one-at-a-time guarantee is gone.
            return Unavailable(invoiceId, "lock", ex);
        }

        return acquired
            ? null
            : new ApprovalResult(ApprovalOutcome.Busy, InvoiceApprovalCodes.Busy, "Another decision on this invoice is in progress. Try again.");
    }

    private async Task ReleaseAsync(string key, string owner, Guid invoiceId)
    {
        try
        {
            await _locks.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Approve of invoice {InvoiceId}: a processing lock could not be released ({ExceptionType}); it expires.", invoiceId, ex.GetType().Name);
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

    private Task<List<Entity>> ReadLinesAsync(Guid invoiceId, CancellationToken ct) =>
        InvoiceReviewReadService.ReadPagedAsync(_entities, InvoiceReviewReadService.LineQuery(invoiceId), ct);

    /// <summary>FR-20: held while any linked submission is Processing or Rejected with the Blocked reason (<see cref="FinanceBaseValues.HeldSubmissionConditions"/>).</summary>
    private async Task<bool> IsHeldAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(FinanceBaseValues.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition(FinanceBaseValues.SubmissionInvoiceLookup, ConditionOperator.Equal, invoiceId);
        foreach (var (column, values) in FinanceBaseValues.HeldSubmissionConditions)
        {
            query.Criteria.AddCondition(column, ConditionOperator.In, values.ToArray());
        }

        return (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.Count > 0;
    }

    /// <summary>The user linked to a contact (<c>contact.sprk_systemuser</c>); empty when none.</summary>
    private async Task<Guid> UserOfContactAsync(Guid contactId, CancellationToken ct)
    {
        var query = new QueryExpression(EligibleApproverResolver.ContactEntity)
        {
            ColumnSet = new ColumnSet(EligibleApproverResolver.ContactSystemUser),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition(EligibleApproverResolver.ContactEntity + "id", ConditionOperator.Equal, contactId);
        var contact = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        return contact?.GetAttributeValue<EntityReference>(EligibleApproverResolver.ContactSystemUser)?.Id ?? Guid.Empty;
    }
}
