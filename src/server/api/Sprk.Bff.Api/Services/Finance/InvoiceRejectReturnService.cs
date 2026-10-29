// ebilling-r1 task 104 (FR-36, FR-30, FR-06; D-34, D-37, D-43, D-73, D-101, D-122; EB-D-171; notes 104-reject-return.md).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceApprovalService (103) approves and nothing else; InvoiceLifecycleService (014) moves the status but checks no approver;
//       SubmissionOverrideService (105) returns a BLOCKED SUBMISSION (its invoice is held), not an invoice an approver reviewed. No route
//       rejects or returns an invoice (grep: no reject-invoice writer outside 049's automatic rejection).
//   (2) Extension — approve's sequencing is copied, not called: its helpers are private to a service whose result type, codes and recovery
//       are approve's (a bulk accept, totals and a frozen amount that a reject and a return do not have). One generic method serves both
//       verbs here, so reject and return cannot drift apart.
//   (3) Cost of doing nothing — reviewers cannot refuse or send back an invoice, a resubmission is unlinked from what it replaces, and FR-36
//       and success criterion 5 fail.
//
// Placement (bff-extensions.md): BFF, Services/Finance (B1 caller identity, B4 live request). Scoped like its scoped dependencies.

using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Reject (final) or Return to Firm (the firm corrects and resubmits, D-43).</summary>
public enum InvoiceDecisionKind
{
    Reject,
    Return,
}

/// <summary>The outcome class of a reject or return call. The endpoint maps each to one HTTP status and one stable code.</summary>
public enum InvoiceDecisionOutcome
{
    /// <summary>This call decided the invoice (or completed this caller's own interrupted decision).</summary>
    Decided,

    /// <summary>The invoice already had this decision: nothing was written by this call (a repeat is a no-op, FR-36).</summary>
    AlreadyDecided,

    InvalidRequest,
    InvoiceNotFound,
    NotEligible,
    Held,
    NotInReview,
    Conflict,
    Busy,
    Forbidden,

    /// <summary>Nothing was changed by this call (a read, the lock or the first write failed). Try again.</summary>
    Unavailable,

    /// <summary>Something was written and a later step did not finish. The same caller repeating the call completes it.</summary>
    Incomplete,

    /// <summary>A server-side invariant failed (no single core record, a decision the writer would refuse); nothing was changed.</summary>
    Failed,
}

/// <summary>Stable ProblemDetails codes of the reject and return routes (ADR-019).</summary>
public static class InvoiceRejectReturnCodes
{
    /// <summary>400: a reason is required for both (FR-36). Shared with 105's override and return.</summary>
    public const string ReasonRequired = BlockedSubmissionErrors.ReasonRequired;

    public const string ReasonTooLong = BlockedSubmissionErrors.ReasonTooLong;
    public const string InvalidRequest = LineDecisionCodes.InvalidRequest;
    public const string NotEligible = InvoiceApprovalCodes.NotEligible;
    public const string NotInReview = LineDecisionCodes.NotInReview;
    public const string AlreadyDecided = InvoiceApprovalCodes.AlreadyDecided;
    public const string PortionMissing = InvoiceApprovalCodes.PortionMissing;
    public const string PortionAmbiguous = InvoiceApprovalCodes.PortionAmbiguous;
    public const string StatusUnknown = InvoiceApprovalCodes.StatusUnknown;
    public const string Changed = InvoiceApprovalCodes.Changed;
    public const string Claimed = InvoiceApprovalCodes.Claimed;
    public const string CoreUnresolved = InvoiceApprovalCodes.CoreUnresolved;
    public const string NotAuthorized = LineDecisionCodes.NotAuthorized;
    public const string DecisionInvalid = LineDecisionCodes.DecisionInvalid;
    public const string DeciderUnresolved = LineDecisionCodes.DeciderUnresolved;
    public const string Busy = LineDecisionCodes.Busy;

    /// <summary>409: a blocked submission holds the invoice (D-101); Override or Return of the SUBMISSION (105) is the way out.</summary>
    public const string Held = "ebilling.invoice.held";

    /// <summary>409: 014 refused the transition.</summary>
    public const string TransitionRefused = "ebilling.invoice.decision_refused";

    /// <summary>503: nothing was changed; try again.</summary>
    public const string Unavailable = "ebilling.invoice.decision_unavailable";

    /// <summary>503: the status moved and a later step did not finish; the same caller repeats the call.</summary>
    public const string Incomplete = "ebilling.invoice.decision_incomplete";
}

public sealed record InvoiceDecisionResult(
    InvoiceDecisionOutcome Outcome,
    string Code,
    string? Detail = null,
    Guid? DecisionId = null,
    Guid? DecisionRecordId = null,
    string? FailedStep = null);

/// <summary>
/// Rejects an invoice (final) or returns it to the firm (FR-36): any one eligible approver (D-40; the same live check as 103, EB-D-171), a
/// reason required, only from In Review, the status moved through 014, the approval portion set with decider and time, the invoice's open
/// Signals closed (D-122), and exactly one Decision Record naming who decided (074, D-100).
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> The route has already proven Write on the invoice as the caller (<c>ebilling.invoice.decide</c>). Eligibility is
/// resolved LIVE from the core record on every call and matched on the caller's OWN contact
/// (<see cref="IIdentityNormalizationService.ResolveAsync"/>, EB-D-171); a contact's <c>sprk_systemuser</c> never grants. A caller with no
/// contact gets <see cref="InvoiceDecisionOutcome.NotEligible"/>, a caller whose identity could not be read
/// <see cref="InvoiceDecisionOutcome.Unavailable"/>, both before anything is written. The decider is the caller's <c>systemuserid</c> from the
/// authenticated principal; no contact, Signal or policy version is taken from the request.</para>
/// <para><b>Exactly once (074 K-1 / K-4), three layers.</b> (1) The invoice-decision lock shared with 103 / 105
/// (<see cref="SubmissionOverrideService.LockKey"/>) and the per-invoice line lock, so one decision runs per invoice. (2) The CLAIM: before
/// the transition the portion's decider and time are written conditional on the portion's row version as read, and only when the portion
/// names no decider or the caller's own contact; a concurrent call that read the same version gets 409 <c>ebilling.approval.changed</c>,
/// another person's unfinished claim 409 <c>ebilling.approval.claimed</c>. A refused transition clears the claim again. (3) Only the call
/// whose 014 transition answered Transitioned records. The decision id is DERIVED from the portion and the verb
/// (<see cref="RejectDecisionIdFor"/>, <see cref="ReturnDecisionIdFor"/>), so every attempt by anyone uses one id.</para>
/// <para><b>Repeat.</b> A call on an invoice already in the verb's target status writes nothing for anyone but the claimant (the person the
/// portion names, matched by their OWN contact): it answers <see cref="InvoiceDecisionOutcome.AlreadyDecided"/>. The claimant's repeat
/// completes what is missing, in the order portion → record → Signals → link, each step idempotent: the record answers AlreadyRecorded (no
/// second record) and the remaining Signal closes run over the set stored with the record. A call on an invoice in ANY OTHER non-In-Review
/// status (including the other final decision) is 409 <c>ebilling.invoice.not_in_review</c>.</para>
/// <para><b>Signals (D-122; the human-decision close shared with 100, 103, 105).</b> A reject or return ends the invoice, so it resolves every
/// Open or Acknowledged Signal on the invoice and on its lines, created at or before the decision time (the set is derived server-side). The
/// caller's close rights are asked as the caller BEFORE any write (<see cref="HumanDecisionSignalClose.AuthorizeAsync"/>), the set is stored
/// with the record (<c>ResolvedSignalIds</c> and the set fact), and the closes run after the record and before the link. A close that failed
/// (a fault or a change since the read) answers 503 <see cref="InvoiceRejectReturnCodes.Incomplete"/> at step <c>signals</c> BEFORE the link, so
/// the claimant's repeat finishes the remaining closes.</para>
/// <para><b>Order and refusals (074 K-3).</b> Every permanent refusal the writer can give is checked before the first write: the invoice
/// names exactly one core record (<see cref="EligibleApproverResolver.SingleCoreOf"/>, 194b) and the recorder's filing read derives exactly
/// that one, the Signal close is authorized, and the decision passes <see cref="BillingDecisionRecorder.Preflight"/>. A held invoice (a
/// blocked submission, D-101) is refused: its way out is 105's Override or Return of the submission, which records the submission's state.</para>
/// <para><b>The reason</b> is stored in the Decision Record (<c>sprk_reason</c>), the one place a firm notice (132) reads it from; the
/// approval portion has no reason column. A Rejected or Returned invoice is not counted by the rollups
/// (<see cref="FinanceBaseValues.CountedInvoiceStatuses"/>) and is not exported (150).</para>
/// </remarks>
public sealed class InvoiceRejectReturnService
{
    internal const string RejectActionCode = "reject-invoice";
    internal const string ReturnActionCode = "return-to-firm";
    internal const int ReasonMaxLength = 2000;

    /// <summary>The same role string as approve: the deciding person acts as an invoice approver.</summary>
    internal const string DeciderRole = InvoiceApprovalService.ApproverRole;

    /// <summary>Long enough for the claim, the transition, the record and the closes; a lock that outlives a crash expires by itself.</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

    private sealed record Verb(
        InvoiceDecisionKind Kind, string ActionCode, InvoiceLifecycleStatus Target, int InvoiceStatus, int PortionStatus, string Outcome, string KeyPrefix);

    private static readonly Verb RejectVerb = new(
        InvoiceDecisionKind.Reject, RejectActionCode, InvoiceLifecycleStatus.Rejected, InvoiceStatusCodes.Rejected,
        InvoiceApprovalService.PortionRejected, "Rejected", "ebilling-reject-");

    private static readonly Verb ReturnVerb = new(
        InvoiceDecisionKind.Return, ReturnActionCode, InvoiceLifecycleStatus.ReturnedToFirm, InvoiceStatusCodes.ReturnedToFirm,
        InvoiceApprovalService.PortionReturned, "Returned", "ebilling-return-");

    private static readonly string[] InvoiceColumns =
    [
        "statuscode", InvoiceTotalsService.InvoiceBilledTotal, .. EligibleApproverResolver.CoreLookupColumns(InvoiceReviewReadService.InvoiceEntity),
    ];

    private readonly IGenericEntityService _entities;
    private readonly EligibleApproverResolver _resolver;
    private readonly IInvoiceLifecycleService _lifecycle;
    private readonly BillingDecisionRecorder _recorder;
    private readonly IIdempotencyService _locks;
    private readonly IIdentityNormalizationService _identity;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly CallerRecordAccessProbe _probe;
    private readonly HumanDecisionSignalClose.CloseActed _closeActed;
    private readonly ILogger<InvoiceRejectReturnService> _logger;
    private readonly TimeProvider _time;

    public InvoiceRejectReturnService(
        IGenericEntityService entities,
        EligibleApproverResolver resolver,
        IInvoiceLifecycleService lifecycle,
        BillingDecisionRecorder recorder,
        IIdempotencyService locks,
        IIdentityNormalizationService identity,
        CoreAncestorResolver coreAncestors,
        CallerRecordAccessProbe probe,
        SignalWriter signals,
        ILogger<InvoiceRejectReturnService> logger)
        : this(
            entities, resolver, lifecycle, recorder, locks, identity, coreAncestors, probe,
            (signals ?? throw new ArgumentNullException(nameof(signals))).CloseActedByDecisionAsync, logger, null)
    {
    }

    /// <summary>The seam the unit tests use: the Signal writer's close is passed as a call (103's pattern).</summary>
    internal InvoiceRejectReturnService(
        IGenericEntityService entities,
        EligibleApproverResolver resolver,
        IInvoiceLifecycleService lifecycle,
        BillingDecisionRecorder recorder,
        IIdempotencyService locks,
        IIdentityNormalizationService identity,
        CoreAncestorResolver coreAncestors,
        CallerRecordAccessProbe probe,
        HumanDecisionSignalClose.CloseActed closeActed,
        ILogger<InvoiceRejectReturnService> logger,
        TimeProvider? time)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _closeActed = closeActed ?? throw new ArgumentNullException(nameof(closeActed));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The reject's one decision id: derived from the portion, so every attempt by every approver uses the same id.</summary>
    public static Guid RejectDecisionIdFor(Guid approvalId) => InvoiceSubmissionPersister.IdOf($"{RejectVerb.KeyPrefix}{approvalId:D}");

    /// <summary>The return's one decision id: derived from the portion.</summary>
    public static Guid ReturnDecisionIdFor(Guid approvalId) => InvoiceSubmissionPersister.IdOf($"{ReturnVerb.KeyPrefix}{approvalId:D}");

    /// <summary>Rejects the invoice as the caller (final). See the class remarks.</summary>
    public Task<InvoiceDecisionResult> RejectAsync(Guid invoiceId, Guid callerSystemUserId, string? reason, string? callerBearerToken, CancellationToken ct) =>
        DecideAsync(RejectVerb, invoiceId, callerSystemUserId, reason, callerBearerToken, ct);

    /// <summary>Returns the invoice to the firm as the caller. See the class remarks.</summary>
    public Task<InvoiceDecisionResult> ReturnAsync(Guid invoiceId, Guid callerSystemUserId, string? reason, string? callerBearerToken, CancellationToken ct) =>
        DecideAsync(ReturnVerb, invoiceId, callerSystemUserId, reason, callerBearerToken, ct);

    private async Task<InvoiceDecisionResult> DecideAsync(
        Verb verb, Guid invoiceId, Guid callerSystemUserId, string? reason, string? callerBearerToken, CancellationToken ct)
    {
        if (invoiceId == Guid.Empty || callerSystemUserId == Guid.Empty)
        {
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.InvalidRequest, InvoiceRejectReturnCodes.InvalidRequest, "An invoice and a caller are required.");
        }

        // FR-36: a reason is required for both; checked before any lock or read, so a refused call writes nothing.
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.InvalidRequest, InvoiceRejectReturnCodes.ReasonRequired, "A reason is required.");
        }

        if (trimmed.Length > ReasonMaxLength)
        {
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.InvalidRequest, InvoiceRejectReturnCodes.ReasonTooLong,
                $"The reason may be at most {ReasonMaxLength} characters.");
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
                return await DecideHeldAsync(verb, invoiceId, callerSystemUserId, trimmed, callerBearerToken, ct).ConfigureAwait(false);
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

    private async Task<InvoiceDecisionResult> DecideHeldAsync(
        Verb verb, Guid invoiceId, Guid caller, string reason, string? callerBearerToken, CancellationToken ct)
    {
        Entity? invoice;
        IReadOnlyList<Entity> portions;
        try
        {
            invoice = await ReadInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
            if (invoice is null)
            {
                return new InvoiceDecisionResult(InvoiceDecisionOutcome.InvoiceNotFound, "ebilling.invoice.not_found");
            }

            portions = await _resolver.ReadPortionsAsync(invoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(verb, invoiceId, "read", ex);
        }

        var invoiceStatus = InvoiceStatus(invoice);

        // ── Already in this verb's target status: a repeat. The claimant completes what is missing; anyone else gets a no-op. ──
        if (invoiceStatus == verb.InvoiceStatus)
        {
            return await CompleteOrNoOpAsync(verb, invoice, portions, caller, reason, callerBearerToken, ct).ConfigureAwait(false);
        }

        // ── FR-36 / FR-06: only an invoice In Review can be rejected or returned (any other status, the other final decision included, is 409). ──
        if (invoiceStatus != InvoiceStatusCodes.InReview)
        {
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.NotInReview, InvoiceRejectReturnCodes.NotInReview,
                $"Only an invoice In Review can be {Past(verb)}.");
        }

        if (portions.Count != 1)
        {
            _logger.LogWarning("{Verb} of invoice {InvoiceId}: {Count} approval portions; nothing changed.", Label(verb), invoiceId, portions.Count);
            return portions.Count == 0
                ? new InvoiceDecisionResult(InvoiceDecisionOutcome.Conflict, InvoiceRejectReturnCodes.PortionMissing, "The invoice has no approval record.")
                : new InvoiceDecisionResult(InvoiceDecisionOutcome.Conflict, InvoiceRejectReturnCodes.PortionAmbiguous, "The invoice has more than one approval record.");
        }

        var portion = portions[0];
        var approvalId = portion.Id;
        var decisionId = DecisionIdFor(verb, approvalId);
        var portionStatus = StatusOf(portion);
        if (portionStatus is InvoiceApprovalService.PortionApproved or InvoiceApprovalService.PortionRejected or InvoiceApprovalService.PortionReturned)
        {
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.Conflict, InvoiceRejectReturnCodes.AlreadyDecided, "The invoice was already decided.");
        }

        // K4 (103): only a Pending portion is decidable; an unknown (or missing) status is refused, never assumed Pending.
        if (portionStatus != InvoiceApprovalService.PortionPending)
        {
            _logger.LogWarning("{Verb} of invoice {InvoiceId}: the approval status {Status} is not recognised; nothing changed.", Label(verb), invoiceId, portionStatus);
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.Conflict, InvoiceRejectReturnCodes.StatusUnknown, "The approval record's status is not recognised.");
        }

        // ── 194b / 074 K-3: the invoice names exactly one core record (013's lookups), before any claim or write. ──
        if (EligibleApproverResolver.SingleCoreOf(InvoiceReviewReadService.InvoiceEntity, invoice) is not { } core)
        {
            _logger.LogError("{Verb} of invoice {InvoiceId}: the invoice names no single core record; nothing changed.", Label(verb), invoiceId);
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.Failed, InvoiceRejectReturnCodes.CoreUnresolved, "The invoice names no single core record.");
        }

        // ── FR-35 / F-7, EB-D-171: eligibility, LIVE from the core record, by the caller's OWN contact. The stored N:N never grants. ──
        EligibleApproverSet set;
        try
        {
            set = await _resolver.ResolveAsync(core.Entity, core.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(verb, invoiceId, "eligibility", ex);
        }

        var callerContact = await CallerContactAsync(verb, invoiceId, caller, ct).ConfigureAwait(false);
        if (callerContact.Failure is { } identityFailure)
        {
            return identityFailure;
        }

        if (set.ForContact(callerContact.ContactId) is not { } approver)
        {
            _logger.LogWarning("{Verb} of invoice {InvoiceId}: the caller is not an eligible approver ({Eligible} eligible, caller contact {Resolved}); nothing changed.",
                Label(verb), invoiceId, set.Approvers.Count, callerContact.ContactId is null ? "unresolved" : "resolved");
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.NotEligible, InvoiceRejectReturnCodes.NotEligible, "You are not an eligible approver of this invoice.");
        }

        // ── K1 (103): another person's unfinished claim is theirs to complete; nothing is written over it. ──
        if (portion.GetAttributeValue<EntityReference>(InvoiceApprovalService.PortionDecidedBy) is { } claimedBy
            && claimedBy.Id != Guid.Empty && claimedBy.Id != approver.ContactId)
        {
            _logger.LogWarning("{Verb} of invoice {InvoiceId}: the approval is claimed by another approver's unfinished decision; nothing changed.", Label(verb), invoiceId);
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.Conflict, InvoiceRejectReturnCodes.Claimed,
                "Another approver's decision on this invoice has not finished. They complete it by repeating it.");
        }

        // ── D-101: a blocked submission holds the invoice; Override or Return of the SUBMISSION (105) is the way out, and it records the submission. ──
        try
        {
            if (await IsHeldAsync(invoiceId, ct).ConfigureAwait(false))
            {
                return new InvoiceDecisionResult(InvoiceDecisionOutcome.Held, InvoiceRejectReturnCodes.Held,
                    "The invoice is held until its blocked submission is decided (override or return the submission).");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(verb, invoiceId, "read", ex);
        }

        var decidedOn = Seconds(_time.GetUtcNow());

        // ── D-122: the invoice's and its lines' open Signals created by the decision time, derived server-side; the close authorized BEFORE any write. ──
        var signals = await ReadSignalsAsync(verb, invoiceId, decidedOn.UtcDateTime, ct).ConfigureAwait(false);
        if (signals.Failure is { } signalFailure)
        {
            return signalFailure;
        }

        if (await AuthorizeCloseAsync(verb, invoiceId, core, signals.Subjects, callerBearerToken, ct).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        // ── The recorder files the record by the invoice's derived core stamp; it must be exactly the core above, before any write. ──
        if (await CheckFilingAsync(verb, invoiceId, core, ct).ConfigureAwait(false) is { } filing)
        {
            return filing;
        }

        var decision = BuildDecision(verb, decisionId, invoice, approvalId, caller, reason, decidedOn, signals.Set);
        try
        {
            _recorder.Preflight(decision);
        }
        catch (DecisionRecordRefusedException ex)
        {
            _logger.LogError("{Verb} of invoice {InvoiceId}: the Decision Record would be refused ({Reason}); nothing changed.", Label(verb), invoiceId, ex.Reason);
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.Failed, InvoiceRejectReturnCodes.DecisionInvalid, ex.Reason.ToString());
        }

        // ── The CLAIM: decider and time, conditional on the portion as read. ──
        var version = portion.TryGetAttributeValue<long>("versionnumber", out var v) ? v : 0L;
        if (version <= 0)
        {
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.Unavailable, InvoiceRejectReturnCodes.Unavailable, "The approval version could not be read; nothing was changed.");
        }

        try
        {
            await _entities.UpdateIfRowVersionMatchesAsync(
                EligibleApproverResolver.ApprovalEntity, approvalId, version.ToString(CultureInfo.InvariantCulture),
                ClaimFields(approver.ContactId, decidedOn), ct).ConfigureAwait(false);
        }
        catch (System.Data.DBConcurrencyException)
        {
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.Conflict, InvoiceRejectReturnCodes.Changed, "The approval changed while it was prepared. Reload and try again.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(verb, invoiceId, "claim", ex);
        }

        // ── 014: the only status writer. Only Transitioned records. ──
        InvoiceTransitionResult transition;
        try
        {
            transition = await _lifecycle.TransitionAsync(invoiceId, verb.Target, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The outcome is unknown (a timeout can land after the commit): the claimant's repeat completes it either way.
            return Incomplete(verb, invoiceId, decisionId, "transition", ex.GetType().Name);
        }

        switch (transition.Outcome)
        {
            case InvoiceTransitionOutcome.Refused:
                _logger.LogWarning("{Verb} of invoice {InvoiceId}: the transition was refused ({Reason}).", Label(verb), invoiceId, transition.Reason);
                await ReleaseClaimAsync(verb, invoiceId, approvalId, approver.ContactId, decidedOn, ct).ConfigureAwait(false);
                return new InvoiceDecisionResult(InvoiceDecisionOutcome.Conflict, InvoiceRejectReturnCodes.TransitionRefused, transition.Reason);
            case InvoiceTransitionOutcome.AlreadyInTarget:
                // Another call moved it first (only possible when the lock was defeated): it records, this call does not.
                _logger.LogWarning("{Verb} of invoice {InvoiceId}: already {Past} by another call; nothing recorded.", Label(verb), invoiceId, Past(verb));
                return new InvoiceDecisionResult(InvoiceDecisionOutcome.AlreadyDecided, "ok", DecisionId: decisionId);
        }

        return await FinishAsync(verb, invoiceId, approvalId, decision, InvoiceApprovalService.PortionPending, recordId: null, approver.ContactId, signals.Set, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The invoice is already in the verb's target status. The claimant (the person the portion names, matched by the caller's OWN contact,
    /// EB-D-171) completes what is missing: the portion status, the record (same id and content), the Signals and the link. Anyone else gets a
    /// no-op, and at most the portion's status is aligned (no attribution changes).
    /// </summary>
    private async Task<InvoiceDecisionResult> CompleteOrNoOpAsync(
        Verb verb, Entity invoice, IReadOnlyList<Entity> portions, Guid caller, string reason, string? callerBearerToken, CancellationToken ct)
    {
        var invoiceId = invoice.Id;

        // A rejected invoice with no portion is 049's automatic rejection (D-101): not this decision, and nothing to complete.
        if (portions.Count != 1)
        {
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.AlreadyDecided, "ok");
        }

        var portion = portions[0];
        var decisionId = DecisionIdFor(verb, portion.Id);
        var portionStatus = StatusOf(portion);
        if (portionStatus is not (InvoiceApprovalService.PortionPending or null) && portionStatus != verb.PortionStatus)
        {
            // The portion records another decision than the invoice's status: nothing this service can complete.
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.Conflict, InvoiceRejectReturnCodes.AlreadyDecided, "The invoice was already decided.");
        }

        var decidedBy = portion.GetAttributeValue<EntityReference>(InvoiceApprovalService.PortionDecidedBy);
        var recordId = portion.GetAttributeValue<EntityReference>(InvoiceApprovalService.PortionRecord)?.Id;
        var complete = portionStatus == verb.PortionStatus && recordId is { } r && r != Guid.Empty;

        var claimant = false;
        if (!complete && decidedBy is not null && decidedBy.Id != Guid.Empty)
        {
            var callerContact = await CallerContactAsync(verb, invoiceId, caller, ct).ConfigureAwait(false);
            if (callerContact.Failure is { } failure)
            {
                return failure;
            }

            claimant = callerContact.ContactId == decidedBy.Id;
        }

        if (complete || !claimant)
        {
            if (!complete && portionStatus == InvoiceApprovalService.PortionPending && decidedBy is not null)
            {
                // The claimant's call stopped after the transition: align the portion's status for the view; the record stays the claimant's.
                try
                {
                    await _entities.UpdateAsync(EligibleApproverResolver.ApprovalEntity, portion.Id,
                        new Dictionary<string, object> { [EligibleApproverResolver.ApprovalStatus] = new OptionSetValue(verb.PortionStatus) }, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogWarning("{Verb} of invoice {InvoiceId}: the portion status could not be aligned ({ExceptionType}).", Label(verb), invoiceId, ex.GetType().Name);
                }
            }

            return new InvoiceDecisionResult(InvoiceDecisionOutcome.AlreadyDecided, "ok", DecisionId: decisionId, DecisionRecordId: recordId);
        }

        // The claimant's repeat: rebuild the decision from what the claim froze (the hash ignores facts and time, so it matches a first write).
        if (EligibleApproverResolver.SingleCoreOf(InvoiceReviewReadService.InvoiceEntity, invoice) is not { } core)
        {
            _logger.LogError("{Verb} of invoice {InvoiceId}: the invoice names no single core record; the decision cannot be completed.", Label(verb), invoiceId);
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.Failed, InvoiceRejectReturnCodes.CoreUnresolved, "The invoice names no single core record.");
        }

        var decidedOn = portion.GetAttributeValue<DateTime?>(InvoiceApprovalService.PortionDecidedOn) is { } on
            ? new DateTimeOffset(DateTime.SpecifyKind(on, DateTimeKind.Utc))
            : Seconds(_time.GetUtcNow());

        // The set stored with the record when it exists; otherwise re-derived with the frozen decision time as the bound (never widened).
        var signals = await ReplaySignalsAsync(verb, invoiceId, decisionId, decidedOn.UtcDateTime, ct).ConfigureAwait(false);
        if (signals.Failure is { } signalFailure)
        {
            return signalFailure;
        }

        if (await AuthorizeCloseAsync(verb, invoiceId, core, signals.Subjects, callerBearerToken, ct).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        var decision = BuildDecision(verb, decisionId, invoice, portion.Id, caller, reason, decidedOn, signals.Set);
        _logger.LogInformation("{Verb} of invoice {InvoiceId}: completing the caller's interrupted decision.", Label(verb), invoiceId);
        return await FinishAsync(verb, invoiceId, portion.Id, decision, portionStatus, recordId, decidedBy!.Id, signals.Set, ct).ConfigureAwait(false);
    }

    /// <summary>After the transition: the portion as decided (re-asserting the decider), the record, the Signals, the link. Each step is idempotent.</summary>
    private async Task<InvoiceDecisionResult> FinishAsync(
        Verb verb, Guid invoiceId, Guid approvalId, BillingDecision decision, int? portionStatus, Guid? recordId, Guid deciderContactId,
        InvoiceSubmissionAfterSteps.ResolvedSignalSet signals, CancellationToken ct)
    {
        var decider = decision.Decider.SystemUserId ?? Guid.Empty;
        if (portionStatus != verb.PortionStatus)
        {
            try
            {
                var fields = ClaimFields(deciderContactId, decision.DecidedOn!.Value);
                fields[EligibleApproverResolver.ApprovalStatus] = new OptionSetValue(verb.PortionStatus);
                await _entities.UpdateAsync(EligibleApproverResolver.ApprovalEntity, approvalId, fields, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Incomplete(verb, invoiceId, decision.DecisionId, "portion", ex.GetType().Name);
            }
        }

        if (recordId is { } existing && existing != Guid.Empty)
        {
            var notClosed = await HumanDecisionSignalClose.CloseSetAsync(_closeActed, signals, existing, decider, _logger, ct).ConfigureAwait(false);
            return notClosed > 0
                ? Incomplete(verb, invoiceId, decision.DecisionId, "signals", $"{notClosed} Signals not closed")
                : Decided(verb, invoiceId, decision.DecisionId, existing);
        }

        BillingDecisionRecordResult recorded;
        try
        {
            recorded = await _recorder.RecordAsync(decision, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Incomplete(verb, invoiceId, decision.DecisionId, "record", (ex as DecisionRecordRefusedException)?.Reason.ToString() ?? ex.GetType().Name);
        }

        // D-122: the stored set, closed as Acted by the record (a repeat: AlreadyClosedByThisRecord). A close that failed (a fault or a change
        // since the read) leaves the call Incomplete at "signals" BEFORE the link, so the claimant's repeat comes back through here: the record
        // answers AlreadyRecorded (no second record) and the remaining closes run over the same stored set, then the link.
        var failedCloses = await HumanDecisionSignalClose.CloseSetAsync(_closeActed, signals, recorded.RecordId, decider, _logger, ct).ConfigureAwait(false);
        if (failedCloses > 0)
        {
            return Incomplete(verb, invoiceId, decision.DecisionId, "signals", $"{failedCloses} Signals not closed");
        }

        try
        {
            await _entities.UpdateAsync(EligibleApproverResolver.ApprovalEntity, approvalId,
                new Dictionary<string, object> { [InvoiceApprovalService.PortionRecord] = new EntityReference(InvoiceReviewReadService.DecisionRecordEntity, recorded.RecordId) }, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Incomplete(verb, invoiceId, decision.DecisionId, "link", ex.GetType().Name);
        }

        return Decided(verb, invoiceId, decision.DecisionId, recorded.RecordId);
    }

    private InvoiceDecisionResult Decided(Verb verb, Guid invoiceId, Guid decisionId, Guid recordId)
    {
        _logger.LogInformation("Invoice {InvoiceId} {Past}; Decision Record {RecordId}.", invoiceId, Past(verb), recordId);
        return new InvoiceDecisionResult(InvoiceDecisionOutcome.Decided, "ok", DecisionId: decisionId, DecisionRecordId: recordId);
    }

    /// <summary>
    /// A refused transition leaves no claim behind (103 K2). The portion is re-read and, while it still holds THIS call's claim (our contact and
    /// our decision time), the claim fields are cleared conditional on the version just read, so a later writer's change is never undone.
    /// Best effort: a failure only warns (the claimant's own repeat writes the claim again).
    /// </summary>
    private async Task ReleaseClaimAsync(Verb verb, Guid invoiceId, Guid approvalId, Guid contactId, DateTimeOffset decidedOn, CancellationToken ct)
    {
        try
        {
            var current = (await _resolver.ReadPortionsAsync(invoiceId, ct).ConfigureAwait(false)).FirstOrDefault(p => p.Id == approvalId);
            var ours = current is not null
                && current.GetAttributeValue<EntityReference>(InvoiceApprovalService.PortionDecidedBy)?.Id == contactId
                && current.GetAttributeValue<DateTime?>(InvoiceApprovalService.PortionDecidedOn) is { } on
                && DateTime.SpecifyKind(on, DateTimeKind.Utc) == decidedOn.UtcDateTime
                && StatusOf(current) == InvoiceApprovalService.PortionPending;
            if (!ours || !current!.TryGetAttributeValue<long>("versionnumber", out var version) || version <= 0)
            {
                return;
            }

            await _entities.UpdateIfRowVersionMatchesAsync(
                EligibleApproverResolver.ApprovalEntity, approvalId, version.ToString(CultureInfo.InvariantCulture),
                new Dictionary<string, object>
                {
                    [InvoiceApprovalService.PortionDecidedBy] = DBNull.Value,
                    [InvoiceApprovalService.PortionDecidedOn] = DBNull.Value,
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("{Verb} of invoice {InvoiceId}: the claim of a refused decision could not be cleared ({ExceptionType}).", Label(verb), invoiceId, ex.GetType().Name);
        }
    }

    // ── EB-D-171: the caller's own contact ──

    private sealed record CallerContact(Guid? ContactId, InvoiceDecisionResult? Failure);

    /// <summary>
    /// The caller's OWN linked contact, by the recorder's rule (<see cref="IIdentityNormalizationService.ResolveAsync"/>: the user's primary
    /// contact, else the contact bound to their Entra oid). Null contact: the caller has none (not eligible). A failed read is Unavailable:
    /// the answer is unknown, so nothing is written either way.
    /// </summary>
    private async Task<CallerContact> CallerContactAsync(Verb verb, Guid invoiceId, Guid caller, CancellationToken ct)
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
                _logger.LogWarning("{Verb} of invoice {InvoiceId}: the caller's linked contact could not be read ({Faults}); nothing was changed.", Label(verb), invoiceId, identity.Faults);
                return new CallerContact(null, new InvoiceDecisionResult(InvoiceDecisionOutcome.Unavailable, InvoiceRejectReturnCodes.Unavailable,
                    "Your eligibility could not be checked; nothing was changed. Try again.", FailedStep: "identity"));
            }

            return new CallerContact(null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new CallerContact(null, Unavailable(verb, invoiceId, "identity", ex));
        }
    }

    // ── D-122: the invoice's Signals ──

    private sealed record SignalRead(
        InvoiceSubmissionAfterSteps.ResolvedSignalSet Set, IReadOnlyList<(string Entity, Guid Id)> Subjects, InvoiceDecisionResult? Failure);

    /// <summary>The Open or Acknowledged Signals on the invoice and its lines, created at or before <paramref name="decidedOnUtc"/>, ordered by id (the record's hash).</summary>
    private async Task<SignalRead> ReadSignalsAsync(Verb verb, Guid invoiceId, DateTime decidedOnUtc, CancellationToken ct)
    {
        var setReadAt = _time.GetUtcNow().UtcDateTime;
        try
        {
            // K2 (notes 13c): a single RetrieveMultiple with no paging, fine below 5,000 lines per invoice (049's resume query, 105's read).
            var lineQuery = new QueryExpression(InvoiceSubmissionPersister.LineEntity) { ColumnSet = new ColumnSet(false), NoLock = true };
            lineQuery.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
            var lineIds = (await _entities.RetrieveMultipleAsync(lineQuery, ct).ConfigureAwait(false)).Entities
                .Select(r => r.Id).Where(id => id != Guid.Empty).Distinct().ToList();

            var open = await InvoiceSubmissionAfterSteps.ReadOpenSignalsAsync(
                _entities, invoiceId, lineIds, _logger, ct, createdOnOrBefore: decidedOnUtc).ConfigureAwait(false);
            var signals = open.Select(s => s.Signal).DistinctBy(s => s.Id).OrderBy(s => s.Id).ToList();
            return new SignalRead(
                new InvoiceSubmissionAfterSteps.ResolvedSignalSet(setReadAt, signals),
                open.Select(s => (s.SubjectEntity, s.SubjectId)).Distinct().ToList(),
                null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new SignalRead(new InvoiceSubmissionAfterSteps.ResolvedSignalSet(DateTime.MinValue, []), [], Unavailable(verb, invoiceId, "signals", ex));
        }
    }

    /// <summary>The claimant's repeat: the set stored with the record (never re-read); with no record yet, re-derived under the frozen bound.</summary>
    private async Task<SignalRead> ReplaySignalsAsync(Verb verb, Guid invoiceId, Guid decisionId, DateTime decidedOnUtc, CancellationToken ct)
    {
        DecisionRecordWriter.BillingStoredResolvedSet? stored;
        try
        {
            stored = await _recorder.FindStoredResolvedSetAsync(verb.ActionCode, invoiceId, decisionId, ct).ConfigureAwait(false);
        }
        catch (DecisionRecordRefusedException ex)
        {
            _logger.LogWarning("{Verb} of invoice {InvoiceId}: the stored Signal set could not be read ({Reason}); nothing changed.", Label(verb), invoiceId, ex.Reason);
            return new SignalRead(
                new InvoiceSubmissionAfterSteps.ResolvedSignalSet(DateTime.MinValue, []),
                [],
                ex.Reason == DecisionRecordRefusalReason.ReviewInvalid
                    ? new InvoiceDecisionResult(InvoiceDecisionOutcome.Failed, InvoiceRejectReturnCodes.DecisionInvalid, ex.Reason.ToString())
                    : new InvoiceDecisionResult(InvoiceDecisionOutcome.Unavailable, InvoiceRejectReturnCodes.Unavailable,
                        "The decision could not be checked; nothing was changed. Try again.", FailedStep: "signals"));
        }

        if (stored is not null)
        {
            // The rights are asked only when the stored set closes something. The invoice is then the one subject: it covers every
            // Signal's Write (a line Signal's is asked on its parent invoice).
            return new SignalRead(
                new InvoiceSubmissionAfterSteps.ResolvedSignalSet(
                    stored.SetReadAtUtc ?? DateTime.MinValue,
                    stored.Signals.Select(s => new InvoiceSubmissionAfterSteps.ResolvedSignal(s.SignalId, s.PolicyCode)).ToList()),
                stored.Signals.Count > 0 ? [(InvoiceReviewReadService.InvoiceEntity, invoiceId)] : [],
                null);
        }

        return await ReadSignalsAsync(verb, invoiceId, decidedOnUtc, ct).ConfigureAwait(false);
    }

    /// <summary>FR-51 when the decision closes at least one Signal: AppendTo on the core record and Write on the invoice (a line subject on its parent invoice), asked as the caller.</summary>
    private async Task<InvoiceDecisionResult?> AuthorizeCloseAsync(
        Verb verb, Guid invoiceId, (string Entity, Guid Id) core, IReadOnlyList<(string Entity, Guid Id)> subjects, string? callerBearerToken, CancellationToken ct)
    {
        if (subjects.Count == 0)
        {
            return null;
        }

        DecisionCallerRefusal refusal;
        try
        {
            refusal = await HumanDecisionSignalClose.AuthorizeAsync(
                _entities, _probe, callerBearerToken, new DecisionCoreRecord(core.Entity, core.Id), invoiceId, subjects, _logger, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(verb, invoiceId, "signal-rights", ex);
        }

        switch (refusal)
        {
            case DecisionCallerRefusal.None:
                return null;
            case DecisionCallerRefusal.Unavailable:
                return new InvoiceDecisionResult(InvoiceDecisionOutcome.Unavailable, InvoiceRejectReturnCodes.Unavailable,
                    "The decision could not be checked; nothing was changed. Try again.", FailedStep: "signal-rights");
            default:
                _logger.LogWarning("{Verb} of invoice {InvoiceId}: the caller may not close its Signals ({Refusal}); nothing changed.", Label(verb), invoiceId, refusal);
                return new InvoiceDecisionResult(InvoiceDecisionOutcome.Forbidden, InvoiceRejectReturnCodes.NotAuthorized,
                    "You do not have permission to record a decision that resolves this invoice's findings.");
        }
    }

    // ── The recorder's filing read ──

    /// <summary>
    /// The writer files the record by the invoice's derived core stamp and refuses any answer but exactly one. Asked here, before any write, so
    /// a refusal leaves nothing behind: a fault is Unavailable, any other answer (none, several, or a different core) is Failed.
    /// </summary>
    private async Task<InvoiceDecisionResult?> CheckFilingAsync(Verb verb, Guid invoiceId, (string Entity, Guid Id) core, CancellationToken ct)
    {
        CoreAncestorResult derived;
        try
        {
            derived = await _coreAncestors.ResolveStampsAsync(InvoiceReviewReadService.InvoiceEntity, invoiceId, ct, CoreAncestorIntent.ReadOnly).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(verb, invoiceId, "filing", ex);
        }

        if (!derived.Succeeded && !derived.IsPermanent)
        {
            _logger.LogWarning("{Verb} of invoice {InvoiceId}: the invoice's core record could not be derived ({Error}); nothing changed.", Label(verb), invoiceId, derived.Error);
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.Unavailable, InvoiceRejectReturnCodes.Unavailable,
                "The decision could not be completed; nothing was changed. Try again.", FailedStep: "filing");
        }

        if (derived.Stamps is [var own] && string.Equals(own.EntityType, core.Entity, StringComparison.OrdinalIgnoreCase) && own.RecordId == core.Id)
        {
            return null;
        }

        _logger.LogError("{Verb} of invoice {InvoiceId}: the invoice's derived core record ({Status}, {Count} stamps) is not its one core lookup; nothing changed.",
            Label(verb), invoiceId, derived.Status, derived.Stamps.Count);
        return new InvoiceDecisionResult(InvoiceDecisionOutcome.Failed, InvoiceRejectReturnCodes.CoreUnresolved, "The invoice names no single core record.");
    }

    // ── Builders ──

    private static BillingDecision BuildDecision(
        Verb verb, Guid decisionId, Entity invoice, Guid approvalId, Guid caller, string reason, DateTimeOffset decidedOn,
        InvoiceSubmissionAfterSteps.ResolvedSignalSet signals)
    {
        // Ids and amounts only in the facts (ADR-015); the reason is the record's sprk_reason and a parameter value, so the hash covers it
        // (074 K-5: the same key and the same trimmed text on every attempt). No policy version is named (K-2).
        var facts = new Dictionary<string, object?>
        {
            ["invoiceId"] = invoice.Id,
            ["approvalId"] = approvalId,
            ["billedAmount"] = invoice.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceBilledTotal)?.Value,
        };
        if (signals.SetReadAtUtc != DateTime.MinValue)
        {
            // D-122: what the close needs beside the ids (the record's ResolvedSignalIds): when the set was read and each policy code.
            facts[DecisionRecordWriter.BillingResolvedSetFact] = InvoiceLineDecisionService.SetFact(signals);
        }

        return new BillingDecision(
            decisionId,
            verb.ActionCode,
            invoice.Id,
            BillingDecider.Human(caller, DeciderRole),
            facts,
            verb.Outcome,
            Reason: reason,
            Values: new Dictionary<string, object?> { ["reason"] = reason },
            ResolvedSignalIds: signals.Signals.Count > 0 ? signals.Signals.Select(s => s.Id).ToList() : null,
            Written: [new DecisionRecordRef(InvoiceReviewReadService.InvoiceEntity, invoice.Id), new DecisionRecordRef(EligibleApproverResolver.ApprovalEntity, approvalId)],
            DecidedOn: decidedOn);
    }

    private static Dictionary<string, object> ClaimFields(Guid contactId, DateTimeOffset decidedOn) => new()
    {
        [InvoiceApprovalService.PortionDecidedBy] = new EntityReference(EligibleApproverResolver.ContactEntity, contactId),
        [InvoiceApprovalService.PortionDecidedOn] = decidedOn.UtcDateTime,
    };

    private static Guid DecisionIdFor(Verb verb, Guid approvalId) =>
        verb.Kind == InvoiceDecisionKind.Reject ? RejectDecisionIdFor(approvalId) : ReturnDecisionIdFor(approvalId);

    private static string Label(Verb verb) => verb.Kind == InvoiceDecisionKind.Reject ? "Reject" : "Return";

    private static string Past(Verb verb) => verb.Kind == InvoiceDecisionKind.Reject ? "rejected" : "returned to the firm";

    private static DateTimeOffset Seconds(DateTimeOffset now) => new(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    /// <summary>The portion's status; null when it has none (K4: never read as Pending).</summary>
    private static int? StatusOf(Entity portion) => portion.GetAttributeValue<OptionSetValue>(EligibleApproverResolver.ApprovalStatus)?.Value;

    private static int? InvoiceStatus(Entity invoice) => invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value;

    // ── Results ──

    private InvoiceDecisionResult Unavailable(Verb verb, Guid invoiceId, string step, Exception ex)
    {
        _logger.LogWarning("{Verb} of invoice {InvoiceId}: the {Step} step failed ({ExceptionType}); nothing was changed by this step.", Label(verb), invoiceId, step, ex.GetType().Name);
        return new InvoiceDecisionResult(InvoiceDecisionOutcome.Unavailable, InvoiceRejectReturnCodes.Unavailable,
            "The decision could not be completed; nothing was changed. Try again.", FailedStep: step);
    }

    private InvoiceDecisionResult Incomplete(Verb verb, Guid invoiceId, Guid decisionId, string step, string detail)
    {
        _logger.LogError("{Verb} of invoice {InvoiceId}: the {Step} step failed after the status moved ({Detail}); the same approver repeats the call to finish it.",
            Label(verb), invoiceId, step, detail);
        return new InvoiceDecisionResult(InvoiceDecisionOutcome.Incomplete, InvoiceRejectReturnCodes.Incomplete,
            $"The invoice was {Past(verb)} but a later step did not finish. Repeat the call to complete it.", DecisionId: decisionId, FailedStep: step);
    }

    // ── Locks ──

    private async Task<InvoiceDecisionResult?> AcquireAsync(string key, string owner, Guid invoiceId, CancellationToken ct)
    {
        bool acquired;
        try
        {
            acquired = await _locks.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Fail closed: without the lock the one-at-a-time guarantee is gone.
            _logger.LogWarning("Invoice {InvoiceId}: the decision lock could not be taken ({ExceptionType}); nothing was changed.", invoiceId, ex.GetType().Name);
            return new InvoiceDecisionResult(InvoiceDecisionOutcome.Unavailable, InvoiceRejectReturnCodes.Unavailable,
                "The decision could not be completed; nothing was changed. Try again.", FailedStep: "lock");
        }

        return acquired
            ? null
            : new InvoiceDecisionResult(InvoiceDecisionOutcome.Busy, InvoiceRejectReturnCodes.Busy, "Another decision on this invoice is in progress. Try again.");
    }

    private async Task ReleaseAsync(string key, string owner, Guid invoiceId)
    {
        try
        {
            await _locks.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Invoice {InvoiceId}: a processing lock could not be released ({ExceptionType}); it expires.", invoiceId, ex.GetType().Name);
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

    /// <summary>FR-20 / D-101: held while any linked submission is Processing or Rejected with the Blocked reason (<see cref="FinanceBaseValues.HeldSubmissionConditions"/>).</summary>
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
