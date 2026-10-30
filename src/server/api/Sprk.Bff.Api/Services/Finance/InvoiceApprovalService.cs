// ebilling-r1 task 103 (FR-35, FR-11, FR-30, I-2; D-40, D-72, D-78, D-100, D-122; EB-D-171, EB-D-172; notes 103-approval-worklist.md).
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
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;
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

    /// <summary>409 (K1): another person's approve claimed the portion and has not finished; only that person's repeat completes it.</summary>
    public const string Claimed = "ebilling.approval.claimed";

    /// <summary>409 (K4): the portion's status is none of Pending, Approved, Rejected, Returned; it is never treated as approvable.</summary>
    public const string StatusUnknown = "ebilling.approval.status_unknown";

    /// <summary>409 (F2): a line was decided by another person whose decision is not recorded yet; they repeat it, then approve.</summary>
    public const string LineDecisionIncomplete = "ebilling.approve.decision_incomplete";

    /// <summary>409 (101 fix round, part 3): a correction's value was saved but its follow-through (findings, status, Signals, totals) did not finish; the reviewer repeats it.</summary>
    public const string CorrectionsPending = "ebilling.approval.corrections_pending";

    /// <summary>403 (EB-D-172): closing the invoice's Signals needs the caller's rights, and the caller does not hold them.</summary>
    public const string NotAuthorized = LineDecisionCodes.NotAuthorized;

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
/// <param name="Status"><c>Pending</c>, <c>Approved</c>, <c>Rejected</c>, <c>Returned</c>, <c>Unknown</c>, or <c>None</c> when the invoice has no portion.</param>
/// <param name="EligibleApprovers">The display names of the people who may approve (live), e.g. "Awaiting: J. Smith, A. Lee".</param>
/// <param name="NoEligibleApprover">Nobody may approve: the core record's roles name no assigned person linked to a user (FR-14).</param>
/// <param name="EligibilityUnavailable">The live resolution failed; <paramref name="CallerEligible"/> is false until it succeeds.</param>
/// <param name="CallerEligible">The caller's OWN contact (EB-D-171) is one of the eligible approvers.</param>
/// <param name="CanApprove">The caller is eligible and holds Write on the invoice, the invoice is In Review and not held, no line is Pending or
/// Flagged, no correction is waiting for its follow-through (101), the portion is Pending.</param>
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
/// Approved through 014, the approved total frozen on the portion, the invoice's open Signals closed (D-122, EB-D-172), and exactly one Decision
/// Record naming who decided (074, D-100).
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> The route has already proven Write on the invoice as the caller (<c>ebilling.invoice.decide</c>). Eligibility is
/// resolved LIVE from the core record on every call (<see cref="EligibleApproverResolver.ResolveAsync"/>); the stored N:N never grants. The caller
/// is eligible only when the caller's OWN contact (<see cref="IIdentityNormalizationService.ResolveAsync"/>, the recorder's rule) is one of the
/// eligible assignment contacts (EB-D-171): a contact's <c>sprk_systemuser</c> column is editable and never grants. A caller with no resolvable
/// contact gets <see cref="ApprovalOutcome.NotEligible"/>, a caller whose identity could not be read <see cref="ApprovalOutcome.Unavailable"/>,
/// both before anything is written. The decider is the caller's <c>systemuserid</c> from the authenticated principal; no contact id, Signal id
/// or policy version is taken from the request.</para>
/// <para><b>Exactly once (074 K-1 / K-4), three layers.</b> (1) Two processing locks, the invoice-decision lock shared with 104 / 105
/// (<see cref="SubmissionOverrideService.LockKey"/>) and the line-decision lock (<see cref="InvoiceLineDecisionService.LockKeyFor"/>, the
/// per-invoice <see cref="InvoiceLineDecisionService.LockKeyPrefix"/> key every line decision takes), so one decision runs per invoice. (2) The
/// CLAIM: before the transition, the portion's decider, time and approved amount are written conditional on the portion's row version as read,
/// and only when the portion names no decider or the caller's own contact (K1); a concurrent approve that read the same version gets 409
/// <c>ebilling.approval.changed</c>, another person's unfinished claim 409 <c>ebilling.approval.claimed</c>, and nothing else is written. A
/// refused transition clears the claim again (K2). (3) Only the call whose 014 transition answered Transitioned writes the portion as Approved
/// and records; AlreadyInTarget is a no-op. The decision id is DERIVED from the portion (<see cref="DecisionIdFor"/>), so every attempt by
/// anyone uses one id and the writer's same-id check refuses a second, different record.</para>
/// <para><b>Signals (D-122, EB-D-172).</b> The decision resolves every Open or Acknowledged Signal whose subject is the INVOICE, created at or
/// before the decision time (the line Signals are their lines' decisions'). The set is derived server-side, the caller's close rights are
/// asked as the caller BEFORE any write (<see cref="HumanDecisionSignalClose.AuthorizeAsync"/>: AppendTo on the core record, Write on the
/// invoice), and the set is stored WITH the record (<c>ResolvedSignalIds</c> and the read time and policy codes in the facts). After the record
/// the set is closed as Acted naming the approver, before the link; a close that failed (a fault or a change since the read) answers 503
/// <c>ebilling.approval.incomplete</c> at step <c>signals</c>, and the claimant's repeat finishes the remaining closes under the same record (no
/// second record), then links. A repeat closes the set stored with the record
/// (<see cref="BillingDecisionRecorder.FindStoredResolvedSetAsync"/>), never a fresh read; before the record exists it re-derives the set with
/// the decision time frozen by the claim as the bound, so a Signal raised after the decision is never added.</para>
/// <para><b>Order and recovery (074 K-3).</b> Every permanent refusal the writer can give is checked before the first write: the invoice names
/// exactly one core record (<see cref="InvoiceFamilyCoreRecordGuard.CoreRecordLookups"/>) and the recorder's filing read derives exactly that one
/// (<see cref="CoreAncestorResolver.ResolveStampsAsync"/>), the Signal close is authorized, and the decision passes
/// <see cref="BillingDecisionRecorder.Preflight"/>. The order is: complete an earlier attempt's bulk accept (F2) → accept Clean → Unreviewed rows
/// → totals → claim → transition → portion Approved → record → Signals → link. A failure after the transition answers 503
/// <c>ebilling.approval.incomplete</c>; the CLAIMANT repeating the approve finds the invoice Approved and completes the portion, the record (same
/// id, same content: the hash ignores facts and time), the Signals and the link. Anyone else gets a 200 no-op and no record.</para>
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

    /// <summary>The line-decision codes whose record proves a line's decision is recorded (F2).</summary>
    private static readonly string[] LineActionCodes = ["accept-line", "reduce-line", "disallow-line"];

    /// <summary>Long enough for the accept-Clean, the totals and the record; a lock that outlives a crash expires by itself.</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private static readonly string[] InvoiceColumns =
    [
        "statuscode", InvoiceTotalsService.InvoiceBilledTotal, .. EligibleApproverResolver.CoreLookupColumns(InvoiceReviewReadService.InvoiceEntity),
    ];

    private readonly IGenericEntityService _entities;
    private readonly EligibleApproverResolver _resolver;
    private readonly InvoiceLineDecisionService _lines;
    private readonly IInvoiceTotalsService _totals;
    private readonly IInvoiceLifecycleService _lifecycle;
    private readonly BillingDecisionRecorder _recorder;
    private readonly IIdempotencyService _locks;
    private readonly IIdentityNormalizationService _identity;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly CallerRecordAccessProbe _probe;
    private readonly HumanDecisionSignalClose.CloseActed _closeActed;
    private readonly Func<InvoiceContextService.InvoiceCaller, Guid, CancellationToken, Task<bool>> _canDecide;
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
        IIdentityNormalizationService identity,
        CoreAncestorResolver coreAncestors,
        CallerRecordAccessProbe probe,
        SignalWriter signals,
        AuthorizationService authorization,
        ILogger<InvoiceApprovalService> logger)
        : this(
            entities, resolver, lines, totals, lifecycle, recorder, locks, identity, coreAncestors, probe,
            (signals ?? throw new ArgumentNullException(nameof(signals))).CloseActedByDecisionAsync,
            DecideCheck(authorization ?? throw new ArgumentNullException(nameof(authorization)), logger),
            logger,
            null)
    {
    }

    /// <summary>The seam the unit tests use: the Signal writer's close and the caller's decide right are passed as calls (100's pattern).</summary>
    internal InvoiceApprovalService(
        IGenericEntityService entities,
        EligibleApproverResolver resolver,
        InvoiceLineDecisionService lines,
        IInvoiceTotalsService totals,
        IInvoiceLifecycleService lifecycle,
        BillingDecisionRecorder recorder,
        IIdempotencyService locks,
        IIdentityNormalizationService identity,
        CoreAncestorResolver coreAncestors,
        CallerRecordAccessProbe probe,
        HumanDecisionSignalClose.CloseActed closeActed,
        Func<InvoiceContextService.InvoiceCaller, Guid, CancellationToken, Task<bool>> canDecide,
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
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _closeActed = closeActed ?? throw new ArgumentNullException(nameof(closeActed));
        _canDecide = canDecide ?? throw new ArgumentNullException(nameof(canDecide));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>F4: the decide right (Write on the invoice) asked of Dataverse as the caller, as the review read's <c>canDecide</c>.</summary>
    internal static Func<InvoiceContextService.InvoiceCaller, Guid, CancellationToken, Task<bool>> DecideCheck(AuthorizationService authorization, ILogger logger) =>
        async (caller, invoiceId, ct) => await CallerReadCheck.CheckAsync(
            authorization, caller, FinanceAuthorizationFilter.InvoiceEntitySet, "ebilling.invoice.decide", invoiceId, logger, "Invoice approval", ct)
            .ConfigureAwait(false) == CallerReadOutcome.Readable;

    /// <summary>The approval's one decision id: derived from the portion, so every attempt by every approver uses the same id (074: "103: from the
    /// approval portion").</summary>
    public static Guid DecisionIdFor(Guid approvalId) => InvoiceSubmissionPersister.IdOf($"ebilling-approve-{approvalId:D}");

    /// <summary>The decision id of the bulk accept an approve runs over the undecided Clean lines (D-72).</summary>
    public static Guid AcceptCleanDecisionIdFor(Guid approvalId) => InvoiceSubmissionPersister.IdOf($"ebilling-approve-accept-clean-{approvalId:D}");

    // ── GET ──

    /// <summary>
    /// The approval state for the caller, with the D-78 recheck: eligibility is resolved live and, while the portion is Pending on an invoice In
    /// Review, mirrored onto the stored set (best effort). <paramref name="caller"/> is asked for the decide right (F4); without it
    /// <c>canApprove</c> is false. Null when the invoice does not exist (uniform 404). Throws on a read fault.
    /// </summary>
    public async Task<InvoiceApprovalStateDto?> GetAsync(
        Guid invoiceId, Guid? callerSystemUserId, InvoiceContextService.InvoiceCaller? caller, CancellationToken ct)
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
        var correctionsPending = (await InvoiceCorrectionReevaluation.ReadUnappliedAsync(_entities, invoiceId, lines, ct).ConfigureAwait(false)).Count > 0;

        EligibleApproverSet? set = null;
        if (EligibleApproverResolver.SingleCoreOf(InvoiceReviewReadService.InvoiceEntity, invoice) is { } core)
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

        // EB-D-171: eligible only by the caller's own contact. Best effort here: a read fault shows "not eligible"; approve refuses it.
        var callerEligible = false;
        if (set is not null && callerSystemUserId is { } user && user != Guid.Empty)
        {
            var contact = await CallerContactAsync(invoiceId, user, ct).ConfigureAwait(false);
            callerEligible = contact.Failure is null && set.ForContact(contact.ContactId) is not null;
        }

        // F4: canApprove also needs the decide right (Write on the invoice), asked as the caller; "could not answer" is no.
        var canDecide = false;
        if (callerEligible && caller is not null)
        {
            try
            {
                canDecide = await _canDecide(caller, invoiceId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Approval state of invoice {InvoiceId}: the caller's decide right could not be read ({ExceptionType}).", invoiceId, ex.GetType().Name);
            }
        }

        return State(invoice, portion, lines, held, correctionsPending, set, eligibilityUnavailable: set is null, callerEligible, canDecide, resolvedOn);
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
            return await CompleteOrNoOpAsync(invoice, portion, caller, callerBearerToken, decisionId, ct).ConfigureAwait(false);
        }

        if (portionStatus is PortionRejected or PortionReturned)
        {
            return new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.AlreadyDecided, "The invoice was already decided.");
        }

        // ── K4: only a Pending portion is approvable; an unknown (or missing) status is refused, never assumed Pending. ──
        if (portionStatus != PortionPending)
        {
            _logger.LogWarning("Approve of invoice {InvoiceId}: the approval status {Status} is not recognised; nothing changed.", invoiceId, portionStatus);
            return new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.StatusUnknown, "The approval record's status is not recognised.");
        }

        // Task 097: a Superseded invoice (closed by its correction) is refused here like every other status but In Review.
        if (invoiceStatus != InvoiceStatusCodes.InReview)
        {
            return new ApprovalResult(ApprovalOutcome.NotInReview, InvoiceApprovalCodes.NotInReview, "Only an invoice In Review can be approved.");
        }

        // ── F3 / 074 K-3: the invoice names exactly one core record (013's lookups), before anything is written. ──
        if (EligibleApproverResolver.SingleCoreOf(InvoiceReviewReadService.InvoiceEntity, invoice) is not { } core)
        {
            _logger.LogError("Approve of invoice {InvoiceId}: the invoice names no single core record; nothing changed.", invoiceId);
            return new ApprovalResult(ApprovalOutcome.Failed, InvoiceApprovalCodes.CoreUnresolved, "The invoice names no single core record.");
        }

        // ── FR-35, EB-D-171: eligibility, LIVE from the core record, by the caller's OWN contact. The stored N:N never grants. ──
        EligibleApproverSet set;
        try
        {
            set = await _resolver.ResolveAsync(core.Entity, core.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "eligibility", ex);
        }

        var callerContact = await CallerContactAsync(invoiceId, caller, ct).ConfigureAwait(false);
        if (callerContact.Failure is { } identityFailure)
        {
            return identityFailure;
        }

        if (set.ForContact(callerContact.ContactId) is not { } approver)
        {
            _logger.LogWarning("Approve of invoice {InvoiceId}: the caller is not an eligible approver ({Eligible} eligible, caller contact {Resolved}); nothing changed.",
                invoiceId, set.Approvers.Count, callerContact.ContactId is null ? "unresolved" : "resolved");
            return new ApprovalResult(ApprovalOutcome.NotEligible, InvoiceApprovalCodes.NotEligible, "You are not an eligible approver of this invoice.");
        }

        // ── K1: another person's unfinished claim is theirs to complete; nothing is written over it. ──
        if (portion.GetAttributeValue<EntityReference>(PortionDecidedBy) is { } claimedBy && claimedBy.Id != Guid.Empty && claimedBy.Id != approver.ContactId)
        {
            _logger.LogWarning("Approve of invoice {InvoiceId}: the approval is claimed by another approver's unfinished approve; nothing changed.", invoiceId);
            return new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.Claimed,
                "Another approver's approval of this invoice has not finished. They complete it by approving again.");
        }

        // ── FR-20: a blocked submission holds the invoice from approval until it is decided. ──
        // ── 101 fix round, part 3 (F-2): a correction whose follow-through did not finish holds it until repeated. A read fault refuses (fail closed). ──
        List<Entity> lineRows;
        IReadOnlyList<InvoiceCorrectionReevaluation.UnappliedCorrection> unapplied;
        try
        {
            if (await IsHeldAsync(invoiceId, ct).ConfigureAwait(false))
            {
                return new ApprovalResult(ApprovalOutcome.Held, InvoiceApprovalCodes.Held, "The invoice is held until its blocked submission is decided.");
            }

            lineRows = await ReadLinesAsync(invoiceId, ct).ConfigureAwait(false);
            unapplied = await InvoiceCorrectionReevaluation.ReadUnappliedAsync(_entities, invoiceId, lineRows, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "read", ex);
        }

        if (unapplied.Count > 0)
        {
            _logger.LogWarning("Approve of invoice {InvoiceId}: {Count} corrections have not been followed through; nothing changed.", invoiceId, unapplied.Count);
            return new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.CorrectionsPending, InvoiceCorrectionReevaluation.UnappliedText(unapplied, lineRows, "approve again"));
        }

        // ── I-2: no line Pending or Flagged (a line with no recognised status counts as Pending: fail closed). ──
        var open = OpenLines(lineRows);
        if (open.Total > 0)
        {
            return new ApprovalResult(ApprovalOutcome.LinesOpen, InvoiceApprovalCodes.LinesOpen,
                $"{open.Pending} lines are still being processed and {open.Flagged} are flagged; decide them before approving.", OpenLines: open);
        }

        var decidedOn = Seconds(_time.GetUtcNow());

        // ── EB-D-172: the invoice's open Signals created by the decision time, derived server-side; the close authorized BEFORE any write. ──
        var signals = await ReadInvoiceSignalsAsync(invoiceId, decidedOn.UtcDateTime, ct).ConfigureAwait(false);
        if (signals.Failure is { } signalFailure)
        {
            return signalFailure;
        }

        if (await AuthorizeCloseAsync(invoiceId, core, signals.Set, callerBearerToken, ct).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        // ── F3: the recorder files the record by the invoice's derived core stamp; it must be exactly the core above, before any write. ──
        if (await CheckFilingAsync(invoiceId, core, ct).ConfigureAwait(false) is { } filing)
        {
            return filing;
        }

        // ── F2: an earlier attempt's bulk accept whose lines were written but not recorded is completed first (never skipped). ──
        if (await CompleteEarlierBulkAcceptAsync(invoiceId, approvalId, caller, callerBearerToken, lineRows, ct).ConfigureAwait(false) is { } bulkIncomplete)
        {
            return bulkIncomplete;
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
        var decision = BuildDecision(decisionId, invoice, approvalId, caller, approvedAmount, decidedOn, signals.Set);
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
                await ReleaseClaimAsync(invoiceId, approvalId, approver.ContactId, decidedOn, ct).ConfigureAwait(false);
                return new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.TransitionRefused, transition.Reason);
            case InvoiceTransitionOutcome.AlreadyInTarget:
                // Another approve moved it first (only possible when the lock was defeated): it records, this call does not.
                _logger.LogWarning("Approve of invoice {InvoiceId}: already Approved by another call; nothing recorded.", invoiceId);
                return new ApprovalResult(ApprovalOutcome.AlreadyApproved, "ok", DecisionId: decisionId);
        }

        return await FinishAsync(invoiceId, approvalId, decision, PortionPending, recordId: null, approver.ContactId, signals.Set, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The invoice is already Approved (or the portion is). The claimant (the person the portion names, matched by the caller's OWN contact,
    /// EB-D-171) completes what is missing: the portion status, the record (same id and content), the Signals and the link. Anyone else gets a
    /// no-op, and at most the portion's status is aligned (no attribution changes).
    /// </summary>
    private async Task<ApprovalResult> CompleteOrNoOpAsync(
        Entity invoice, Entity portion, Guid caller, string? callerBearerToken, Guid decisionId, CancellationToken ct)
    {
        var invoiceId = invoice.Id;
        var decidedBy = portion.GetAttributeValue<EntityReference>(PortionDecidedBy);
        var recordId = portion.GetAttributeValue<EntityReference>(PortionRecord)?.Id;
        var portionStatus = StatusOf(portion);
        var complete = portionStatus == PortionApproved && recordId is { } r && r != Guid.Empty;

        var claimant = false;
        if (!complete && decidedBy is not null && decidedBy.Id != Guid.Empty)
        {
            var callerContact = await CallerContactAsync(invoiceId, caller, ct).ConfigureAwait(false);
            if (callerContact.Failure is { } failure)
            {
                return failure;
            }

            claimant = callerContact.ContactId == decidedBy.Id;
        }

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
        if (EligibleApproverResolver.SingleCoreOf(InvoiceReviewReadService.InvoiceEntity, invoice) is not { } core)
        {
            _logger.LogError("Approve of invoice {InvoiceId}: the invoice names no single core record; the approval cannot be completed.", invoiceId);
            return new ApprovalResult(ApprovalOutcome.Failed, InvoiceApprovalCodes.CoreUnresolved, "The invoice names no single core record.");
        }

        var decidedOn = portion.GetAttributeValue<DateTime?>(PortionDecidedOn) is { } on
            ? new DateTimeOffset(DateTime.SpecifyKind(on, DateTimeKind.Utc))
            : Seconds(_time.GetUtcNow());

        // EB-D-172: the set stored with the record when it exists; otherwise re-derived with the frozen decision time as the bound (never widened).
        var signals = await ReplaySignalsAsync(invoiceId, decisionId, decidedOn.UtcDateTime, ct).ConfigureAwait(false);
        if (signals.Failure is { } signalFailure)
        {
            return signalFailure;
        }

        if (await AuthorizeCloseAsync(invoiceId, core, signals.Set, callerBearerToken, ct).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        var amount = portion.GetAttributeValue<Money>(PortionAmount)?.Value ?? 0m;
        var decision = BuildDecision(decisionId, invoice, portion.Id, caller, amount, decidedOn, signals.Set);
        _logger.LogInformation("Approve of invoice {InvoiceId}: completing the caller's interrupted approval.", invoiceId);
        return await FinishAsync(invoiceId, portion.Id, decision, portionStatus, recordId, decidedBy!.Id, signals.Set, ct).ConfigureAwait(false);
    }

    /// <summary>After the transition: the portion as Approved (re-asserting the decider), the record, the Signals, the link. Each step is idempotent.</summary>
    private async Task<ApprovalResult> FinishAsync(
        Guid invoiceId, Guid approvalId, BillingDecision decision, int? portionStatus, Guid? recordId, Guid deciderContactId,
        InvoiceSubmissionAfterSteps.ResolvedSignalSet signals, CancellationToken ct)
    {
        var amount = decision.Facts["approvedAmount"] is decimal d ? d : 0m;
        var decider = decision.Decider.SystemUserId ?? Guid.Empty;
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
            var notClosed = await HumanDecisionSignalClose.CloseSetAsync(_closeActed, signals, existing, decider, _logger, ct).ConfigureAwait(false);
            return notClosed > 0
                ? Incomplete(invoiceId, decision.DecisionId, "signals", $"{notClosed} Signals not closed")
                : Approved(invoiceId, decision.DecisionId, existing);
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

        // ── D-122: the stored set, closed as Acted by the record (a repeat: AlreadyClosedByThisRecord). A close that failed (a fault or a
        //    change since the read) leaves the approve Incomplete at "signals" BEFORE the link, so the claimant's repeat comes back through
        //    here: the record answers AlreadyRecorded (no second record) and the remaining closes run over the same stored set. ──
        var failedCloses = await HumanDecisionSignalClose.CloseSetAsync(_closeActed, signals, recorded.RecordId, decider, _logger, ct).ConfigureAwait(false);
        if (failedCloses > 0)
        {
            return Incomplete(invoiceId, decision.DecisionId, "signals", $"{failedCloses} Signals not closed");
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

    /// <summary>
    /// K2: a refused transition leaves no claim behind. The portion is re-read and, while it still holds THIS call's claim (our contact and
    /// our decision time), the claim fields are cleared conditional on the version just read, so a later writer's change is never undone.
    /// Best effort: a failure only warns (the claimant's own repeat writes the claim again).
    /// </summary>
    private async Task ReleaseClaimAsync(Guid invoiceId, Guid approvalId, Guid contactId, DateTimeOffset decidedOn, CancellationToken ct)
    {
        try
        {
            var current = (await _resolver.ReadPortionsAsync(invoiceId, ct).ConfigureAwait(false)).FirstOrDefault(p => p.Id == approvalId);
            var ours = current is not null
                && current.GetAttributeValue<EntityReference>(PortionDecidedBy)?.Id == contactId
                && current.GetAttributeValue<DateTime?>(PortionDecidedOn) is { } on
                && DateTime.SpecifyKind(on, DateTimeKind.Utc) == decidedOn.UtcDateTime
                && StatusOf(current) == PortionPending;
            if (!ours || !current!.TryGetAttributeValue<long>("versionnumber", out var version) || version <= 0)
            {
                return;
            }

            await _entities.UpdateIfRowVersionMatchesAsync(
                EligibleApproverResolver.ApprovalEntity, approvalId, version.ToString(CultureInfo.InvariantCulture),
                new Dictionary<string, object> { [PortionDecidedBy] = DBNull.Value, [PortionDecidedOn] = DBNull.Value, [PortionAmount] = DBNull.Value },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Approve of invoice {InvoiceId}: the claim of a refused approval could not be cleared ({ExceptionType}).", invoiceId, ex.GetType().Name);
        }
    }

    // ── EB-D-171: the caller's own contact ──

    private sealed record CallerContact(Guid? ContactId, ApprovalResult? Failure);

    /// <summary>
    /// The caller's OWN linked contact, by the recorder's rule (<see cref="IIdentityNormalizationService.ResolveAsync"/>: the user's primary
    /// contact, else the contact bound to their Entra oid). Null contact: the caller has none (not eligible). A failed read is Unavailable:
    /// the answer is unknown, so nothing is written either way.
    /// </summary>
    private async Task<CallerContact> CallerContactAsync(Guid invoiceId, Guid caller, CancellationToken ct)
    {
        try
        {
            var identity = await _identity.ResolveAsync(caller, ct).ConfigureAwait(false);
            if (identity.ContactId is { } contact && contact != Guid.Empty)
            {
                return new CallerContact(contact, null);
            }

            if (identity.ContactUnreadable)
            {
                _logger.LogWarning("Approve of invoice {InvoiceId}: the caller's linked contact could not be read ({Faults}); nothing was changed.", invoiceId, identity.Faults);
                return new CallerContact(null, new ApprovalResult(ApprovalOutcome.Unavailable, InvoiceApprovalCodes.Unavailable,
                    "Your eligibility could not be checked; nothing was changed. Try again.", FailedStep: "identity"));
            }

            return new CallerContact(null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new CallerContact(null, Unavailable(invoiceId, "identity", ex));
        }
    }

    // ── EB-D-172: the invoice's Signals ──

    private sealed record SignalRead(InvoiceSubmissionAfterSteps.ResolvedSignalSet Set, ApprovalResult? Failure);

    /// <summary>The invoice-level Open or Acknowledged Signals created at or before <paramref name="decidedOnUtc"/>, ordered by id (the record's hash).</summary>
    private async Task<SignalRead> ReadInvoiceSignalsAsync(Guid invoiceId, DateTime decidedOnUtc, CancellationToken ct)
    {
        var setReadAt = _time.GetUtcNow().UtcDateTime;
        try
        {
            var open = await InvoiceSubmissionAfterSteps.ReadOpenSignalsAsync(
                _entities, invoiceId, [], _logger, ct, createdOnOrBefore: decidedOnUtc).ConfigureAwait(false);
            var signals = open
                .Where(s => s.SubjectEntity == InvoiceReviewReadService.InvoiceEntity && s.SubjectId == invoiceId)
                .Select(s => s.Signal).DistinctBy(s => s.Id).OrderBy(s => s.Id).ToList();
            return new SignalRead(new InvoiceSubmissionAfterSteps.ResolvedSignalSet(setReadAt, signals), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new SignalRead(new InvoiceSubmissionAfterSteps.ResolvedSignalSet(DateTime.MinValue, []), Unavailable(invoiceId, "signals", ex));
        }
    }

    /// <summary>The claimant's repeat: the set stored with the record (never re-read); with no record yet, re-derived under the frozen bound.</summary>
    private async Task<SignalRead> ReplaySignalsAsync(Guid invoiceId, Guid decisionId, DateTime decidedOnUtc, CancellationToken ct)
    {
        DecisionRecordWriter.BillingStoredResolvedSet? stored;
        try
        {
            stored = await _recorder.FindStoredResolvedSetAsync(ApproveActionCode, invoiceId, decisionId, ct).ConfigureAwait(false);
        }
        catch (DecisionRecordRefusedException ex)
        {
            _logger.LogWarning("Approve of invoice {InvoiceId}: the stored Signal set could not be read ({Reason}); nothing changed.", invoiceId, ex.Reason);
            return new SignalRead(
                new InvoiceSubmissionAfterSteps.ResolvedSignalSet(DateTime.MinValue, []),
                ex.Reason == DecisionRecordRefusalReason.ReviewInvalid
                    ? new ApprovalResult(ApprovalOutcome.Failed, InvoiceApprovalCodes.DecisionInvalid, ex.Reason.ToString())
                    : new ApprovalResult(ApprovalOutcome.Unavailable, InvoiceApprovalCodes.Unavailable, "The approval could not be checked; nothing was changed. Try again.", FailedStep: "signals"));
        }

        if (stored is not null)
        {
            return new SignalRead(
                new InvoiceSubmissionAfterSteps.ResolvedSignalSet(
                    stored.SetReadAtUtc ?? DateTime.MinValue,
                    stored.Signals.Select(s => new InvoiceSubmissionAfterSteps.ResolvedSignal(s.SignalId, s.PolicyCode)).ToList()),
                null);
        }

        return await ReadInvoiceSignalsAsync(invoiceId, decidedOnUtc, ct).ConfigureAwait(false);
    }

    /// <summary>FR-51 when the approval closes at least one Signal: AppendTo on the core record and Write on the invoice, asked as the caller.</summary>
    private async Task<ApprovalResult?> AuthorizeCloseAsync(
        Guid invoiceId, (string Entity, Guid Id) core, InvoiceSubmissionAfterSteps.ResolvedSignalSet set, string? callerBearerToken, CancellationToken ct)
    {
        if (set.Signals.Count == 0)
        {
            return null;
        }

        DecisionCallerRefusal refusal;
        try
        {
            refusal = await HumanDecisionSignalClose.AuthorizeAsync(
                _entities, _probe, callerBearerToken, new DecisionCoreRecord(core.Entity, core.Id), invoiceId,
                [(InvoiceReviewReadService.InvoiceEntity, invoiceId)], _logger, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "signal-rights", ex);
        }

        switch (refusal)
        {
            case DecisionCallerRefusal.None:
                return null;
            case DecisionCallerRefusal.Unavailable:
                return new ApprovalResult(ApprovalOutcome.Unavailable, InvoiceApprovalCodes.Unavailable,
                    "The approval could not be checked; nothing was changed. Try again.", FailedStep: "signal-rights");
            default:
                _logger.LogWarning("Approve of invoice {InvoiceId}: the caller may not close its {Count} Signals ({Refusal}); nothing changed.",
                    invoiceId, set.Signals.Count, refusal);
                return new ApprovalResult(ApprovalOutcome.Forbidden, InvoiceApprovalCodes.NotAuthorized,
                    "You do not have permission to record a decision that resolves this invoice's findings.");
        }
    }

    // ── F3: the recorder's filing read ──

    /// <summary>
    /// The writer files the record by the invoice's derived core stamp and refuses any answer but exactly one. Asked here, before any write, so
    /// a refusal leaves nothing behind: a fault is Unavailable, any other answer (none, several, or a different core) is Failed.
    /// </summary>
    private async Task<ApprovalResult?> CheckFilingAsync(Guid invoiceId, (string Entity, Guid Id) core, CancellationToken ct)
    {
        CoreAncestorResult derived;
        try
        {
            derived = await _coreAncestors.ResolveStampsAsync(InvoiceReviewReadService.InvoiceEntity, invoiceId, ct, CoreAncestorIntent.ReadOnly).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "filing", ex);
        }

        if (!derived.Succeeded && !derived.IsPermanent)
        {
            _logger.LogWarning("Approve of invoice {InvoiceId}: the invoice's core record could not be derived ({Error}); nothing changed.", invoiceId, derived.Error);
            return new ApprovalResult(ApprovalOutcome.Unavailable, InvoiceApprovalCodes.Unavailable, "The approval could not be completed; nothing was changed. Try again.", FailedStep: "filing");
        }

        if (derived.Stamps is [var own] && string.Equals(own.EntityType, core.Entity, StringComparison.OrdinalIgnoreCase) && own.RecordId == core.Id)
        {
            return null;
        }

        _logger.LogError("Approve of invoice {InvoiceId}: the invoice's derived core record ({Status}, {Count} stamps) is not its one core lookup; nothing changed.",
            invoiceId, derived.Status, derived.Stamps.Count);
        return new ApprovalResult(ApprovalOutcome.Failed, InvoiceApprovalCodes.CoreUnresolved, "The invoice names no single core record.");
    }

    // ── F2: an earlier attempt's bulk accept ──

    /// <summary>
    /// F2: an approve whose bulk accept wrote its lines and then failed (totals, corrections or records) leaves Accepted lines with no line
    /// Decision Record. Those the CALLER decided (<c>sprk_reviewedby</c>) are completed now under the approval's bulk decision id
    /// (<see cref="AcceptCleanDecisionIdFor"/>): same values, the "Accepted in bulk" correction rows, the missing records. One decided by
    /// another person is theirs to repeat: 409 until it is recorded. A line with no reviewer is not a decision this service can complete
    /// and is left alone. Null when nothing is outstanding.
    /// </summary>
    private async Task<ApprovalResult?> CompleteEarlierBulkAcceptAsync(
        Guid invoiceId, Guid approvalId, Guid caller, string? callerBearerToken, IReadOnlyList<Entity> lineRows, CancellationToken ct)
    {
        var accepted = lineRows
            .Where(l => l.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value == (int)InvoiceLineStatus.Accepted
                && l.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value is (int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense
                && l.GetAttributeValue<EntityReference>(InvoiceReviewReadService.LineReviewedBy) is { } by && by.Id != Guid.Empty)
            .ToList();
        if (accepted.Count == 0)
        {
            return null;
        }

        HashSet<Guid> recorded;
        try
        {
            recorded = await ReadRecordedLinesAsync(accepted.Select(l => l.Id).ToList(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "read", ex);
        }

        var unrecorded = accepted.Where(l => !recorded.Contains(l.Id)).ToList();
        if (unrecorded.Count == 0)
        {
            return null;
        }

        var others = unrecorded.Where(l => l.GetAttributeValue<EntityReference>(InvoiceReviewReadService.LineReviewedBy).Id != caller).Select(l => l.Id).ToList();
        if (others.Count > 0)
        {
            _logger.LogWarning("Approve of invoice {InvoiceId}: {Count} lines were decided by another person and not recorded; nothing changed.", invoiceId, others.Count);
            return new ApprovalResult(ApprovalOutcome.Conflict, InvoiceApprovalCodes.LineDecisionIncomplete,
                "A line decision on this invoice has not finished. Its reviewer repeats it, then the invoice can be approved.", CommittedLineIds: others);
        }

        var lineDecisionId = AcceptCleanDecisionIdFor(approvalId);
        var ids = unrecorded.Select(l => l.Id).ToList();
        _logger.LogInformation("Approve of invoice {InvoiceId}: completing the earlier bulk accept over {Count} lines.", invoiceId, ids.Count);
        for (var offset = 0; offset < ids.Count; offset += InvoiceLineDecisionService.MaxLinesPerRequest)
        {
            var chunk = ids.Skip(offset).Take(InvoiceLineDecisionService.MaxLinesPerRequest).ToList();
            var result = await _lines.CompleteBulkAcceptHeldAsync(invoiceId, lineDecisionId, chunk, caller, callerBearerToken, ct).ConfigureAwait(false);
            if (!result.IsApplied)
            {
                return FromLineResult(result, invoiceId, "accept-clean", lineDecisionId);
            }
        }

        return null;
    }

    /// <summary>The lines among <paramref name="lineIds"/> that have a line-decision record (accept, reduce or disallow; any decision id).</summary>
    private async Task<HashSet<Guid>> ReadRecordedLinesAsync(IReadOnlyList<Guid> lineIds, CancellationToken ct)
    {
        var found = new HashSet<Guid>();
        for (var offset = 0; offset < lineIds.Count; offset += InvoiceLineDecisionService.MaxLinesPerRequest)
        {
            var query = new QueryExpression(InvoiceReviewReadService.DecisionRecordEntity)
            {
                ColumnSet = new ColumnSet("sprk_regardinginvoiceline"),
                Criteria = new FilterExpression(LogicalOperator.And),
                PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 },
                NoLock = true,
            };
            query.Criteria.AddCondition("sprk_regardinginvoiceline", ConditionOperator.In,
                lineIds.Skip(offset).Take(InvoiceLineDecisionService.MaxLinesPerRequest).Cast<object>().ToArray());
            query.Criteria.AddCondition("sprk_actioncode", ConditionOperator.In, LineActionCodes.Cast<object>().ToArray());
            query.AddOrder(InvoiceReviewReadService.DecisionRecordEntity + "id", OrderType.Ascending);
            foreach (var row in await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false))
            {
                if (row.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline") is { } line && line.Id != Guid.Empty)
                {
                    found.Add(line.Id);
                }
            }
        }

        return found;
    }

    // ── Builders ──

    private static BillingDecision BuildDecision(
        Guid decisionId, Entity invoice, Guid approvalId, Guid caller, decimal approvedAmount, DateTimeOffset decidedOn,
        InvoiceSubmissionAfterSteps.ResolvedSignalSet signals)
    {
        // Ids and amounts only (ADR-015). K3: the approved amount is in Values, so the record's hash covers what was approved (074 K-5: the
        // same key and a normalised decimal on every attempt). No policy version is named (K-2).
        var facts = new Dictionary<string, object?>
        {
            ["invoiceId"] = invoice.Id,
            ["approvalId"] = approvalId,
            ["approvedAmount"] = approvedAmount,
            ["billedAmount"] = invoice.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceBilledTotal)?.Value,
        };
        if (signals.SetReadAtUtc != DateTime.MinValue)
        {
            // D-122: what the close needs beside the ids (the record's ResolvedSignalIds): when the set was read and each policy code.
            facts[DecisionRecordWriter.BillingResolvedSetFact] = InvoiceLineDecisionService.SetFact(signals);
        }

        return new BillingDecision(
            decisionId,
            ApproveActionCode,
            invoice.Id,
            BillingDecider.Human(caller, ApproverRole),
            facts,
            "Approved",
            Values: new Dictionary<string, object?> { ["approvedAmount"] = approvedAmount },
            ResolvedSignalIds: signals.Signals.Count > 0 ? signals.Signals.Select(s => s.Id).ToList() : null,
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

    private static InvoiceApprovalStateDto State(
        Entity invoice, Entity? portion, IReadOnlyList<Entity> lines, bool held, bool correctionsPending, EligibleApproverSet? set, bool eligibilityUnavailable,
        bool callerEligible, bool canDecide, DateTime? resolvedOn)
    {
        var portionStatus = portion is null ? null : StatusOf(portion);
        var status = portion is null ? "None" : portionStatus switch
        {
            PortionPending => "Pending",
            PortionApproved => "Approved",
            PortionRejected => "Rejected",
            PortionReturned => "Returned",
            _ => "Unknown",
        };
        var open = OpenLines(lines);
        var decided = portionStatus is PortionApproved or PortionRejected or PortionReturned;
        return new InvoiceApprovalStateDto(
            invoice.Id,
            portion?.Id,
            status,
            set?.Approvers.Select(a => a.Name ?? string.Empty).Where(n => n.Length > 0).ToList() ?? [],
            NoEligibleApprover: set is not null && set.NoEligibleApprover,
            EligibilityUnavailable: eligibilityUnavailable,
            CallerEligible: callerEligible,
            CanApprove: callerEligible && canDecide && portionStatus == PortionPending
                && InvoiceStatus(invoice) == InvoiceStatusCodes.InReview && !held && !correctionsPending && open.Total == 0,
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

    /// <summary>The portion's status; null when it has none (K4: never read as Pending).</summary>
    private static int? StatusOf(Entity portion) => portion.GetAttributeValue<OptionSetValue>(EligibleApproverResolver.ApprovalStatus)?.Value;

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
        LineDecisionStatus.InvoiceNotFound or LineDecisionStatus.LineNotFound => new(ApprovalOutcome.InvoiceNotFound, result.Code),
        LineDecisionStatus.OwnerUnresolved or LineDecisionStatus.InvalidRequest => new(ApprovalOutcome.Failed, result.Code, result.Detail, FailedStep: step),
        // The lines were written and a later line step failed: the caller's next approve completes the bulk accept (F2) before anything
        // else. The invoice is still In Review.
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
}
