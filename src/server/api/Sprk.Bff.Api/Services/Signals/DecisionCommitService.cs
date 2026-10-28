using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

// =====================================================================================================================
// The decision commit route's service (ontology platform R1 task 043; spec FR-18, FR-22, FR-36, FR-51, FR-52, NFR-10;
// D-17, D-19, D-29, D-34, D-35, D-52, D-119).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: the executors (044), the inquiry (070), the work-assignment create (046), the plan resolver (036), the
//     Decision Record writer (040) and the Signal access check (036). None sequences several writes and an append-only
//     record; the ADR-039 gate resolve route is chat-session-only.
//   Extension: this class only orders and records; every write is one of those components' (it adds no Dataverse write of
//     its own except closing the resolved Signals, which no component owns: 040's writer leaves the link to this route).
//   Cost of doing nothing: one click in the wizard cannot both act and record; FR-18's "every resolution writes one record"
//     is unenforceable and follow-on ids cannot be listed on an append-only record.
// =====================================================================================================================

/// <summary>One review the decision wizard posts (task 043). Anything else in the body is ignored: in particular the core
/// record and the item subject are derived on the server from the Signals (uac-r2 condition on #1600), never read from here.</summary>
public sealed record DecisionReview
{
    /// <summary>The client's id for this review. A repeat returns the first outcome and writes nothing.</summary>
    public Guid ReviewId { get; init; }

    /// <summary>True only when the wizard's Confirm step ran (D-52). An unconfirmed review is refused before anything else.</summary>
    public bool Confirmed { get; init; }

    /// <summary>Other Decide-lane Signals on the same core record this decision also resolves (R-7, D-34).</summary>
    public IReadOnlyList<Guid>? AlsoResolve { get; init; }

    /// <summary>Every action the plan offered, each exactly once, taken or skipped.</summary>
    public IReadOnlyList<DecisionReviewAction>? Actions { get; init; }

    /// <summary>The Next steps to create; each code is one of the plan's Next steps (a code may repeat).</summary>
    public IReadOnlyList<DecisionReviewNextStep>? NextSteps { get; init; }

    /// <summary>The dismissal reason, when nothing is taken and no Next step is created (Z-7).</summary>
    public DecisionReviewDismissal? Dismissal { get; init; }

    /// <summary>The decider's "because" text, recorded in the fact snapshot.</summary>
    public string? Because { get; init; }

    /// <summary>Resume a review whose first attempt FAILED before its record: the steps that landed are not run again.</summary>
    public bool Retry { get; init; }
}

/// <summary>One offered action: taken or skipped, with the wizard's values (keyed by the catalog's parameter codes).</summary>
public sealed record DecisionReviewAction
{
    public string? Code { get; init; }

    public bool Taken { get; init; }

    public IReadOnlyDictionary<string, JsonElement>? Values { get; init; }
}

/// <summary>One Next step to create.</summary>
public sealed record DecisionReviewNextStep
{
    public string? Code { get; init; }

    public IReadOnlyDictionary<string, JsonElement>? Values { get; init; }
}

/// <summary>A dismissal reason from the lane's closed list; <see cref="Detail"/> is required for <c>other</c>.</summary>
public sealed record DecisionReviewDismissal
{
    public string? Code { get; init; }

    public string? Detail { get; init; }
}

/// <summary>How a commit ended. The endpoint maps each to one HTTP status.</summary>
public enum DecisionCommitStatus
{
    /// <summary>The record was written and the Signals closed (or warnings say which could not be).</summary>
    Recorded,

    /// <summary>Refused before any write: the review is invalid, or an action's preflight refused it.</summary>
    Refused,

    /// <summary>Refused before any write: the caller lacks a right an action needs.</summary>
    Forbidden,

    /// <summary>A Signal or its core record is not readable by the caller (the uniform 404).</summary>
    NotFound,

    /// <summary>The caller could not be resolved to a Dataverse user (the single 403, D-29).</summary>
    CallerUnresolved,

    /// <summary>A Signal is no longer open (resolved by a person, or by the system before the review started).</summary>
    Conflict,

    /// <summary>A step before the record failed: NO record, NO Signal changed; the result lists what landed.</summary>
    Failed,

    /// <summary>Another request with this review id is running.</summary>
    InProgress,

    /// <summary>The idempotency store or a read the route needs is unavailable; nothing was written.</summary>
    Unavailable,
}

/// <summary>A record a step wrote.</summary>
public sealed record DecisionCommitRef(string Entity, Guid Id);

/// <summary>One executed step.</summary>
/// <param name="Key">Stable within the review (<c>action:revise-budget</c>, <c>next-step:add-todo#0</c>), used to resume.</param>
/// <param name="Code">The catalog code.</param>
/// <param name="Phase">D-17 step: 2 Dataverse writes of taken actions, 3 Next-step creators, 4 messages (the inquiry last).</param>
/// <param name="Status">Done, Refused or Failed (<see cref="DecisionActionStatus"/>).</param>
public sealed record DecisionCommitStep(
    string Key,
    string Code,
    int Phase,
    string Status,
    IReadOnlyList<DecisionCommitRef> Written,
    IReadOnlyList<DecisionCommitRef> PossiblyWritten,
    string? ReasonCode,
    string? Detail);

/// <summary>What happened to one Signal at step 6.</summary>
/// <param name="State">Acted, Dismissed, InquirySent (D-119: left open, Acknowledged, linked to the record) or Unchanged.</param>
public sealed record DecisionCommitSignal(Guid SignalId, string State, string? Warning);

/// <summary>The outcome of one commit. Serializable: it is what a repeated review id gets back.</summary>
public sealed record DecisionCommitResult
{
    public DecisionCommitStatus Status { get; init; }

    public Guid ReviewId { get; init; }

    public string? ReasonCode { get; init; }

    public string? Detail { get; init; }

    /// <summary>The action the refusal or failure is about.</summary>
    public string? ActionCode { get; init; }

    public Guid? DecisionRecordId { get; init; }

    public string? RecordClass { get; init; }

    public string? GateTier { get; init; }

    public IReadOnlyList<DecisionCommitStep> Steps { get; init; } = [];

    public IReadOnlyList<DecisionCommitRef> FollowOns { get; init; } = [];

    public IReadOnlyList<DecisionCommitSignal> Signals { get; init; } = [];

    /// <summary>On failure: the D-17 step that failed (2, 3, 4 or 5).</summary>
    public int? FailedStep { get; init; }

    /// <summary>On failure: every write confirmed to have landed, in order.</summary>
    public IReadOnlyList<DecisionCommitRef> Landed { get; init; } = [];

    /// <summary>On failure: writes sent without a clear answer; they may have landed.</summary>
    public IReadOnlyList<DecisionCommitRef> PossiblyLanded { get; init; } = [];

    /// <summary>On a step-5 failure that did not get a clear answer: the record may exist.</summary>
    public bool RecordMayExist { get; init; }

    /// <summary>A message went out and the record was not written: the escalation case (task 043 escalation trigger 1).</summary>
    public bool SentButNotRecorded { get; init; }

    /// <summary>A failed first attempt that <see cref="DecisionReview.Retry"/> may resume.</summary>
    public bool Resumable { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True when this is the stored first outcome of a repeated review id.</summary>
    public bool Replayed { get; init; }
}

/// <summary>Stable reason codes the commit route returns.</summary>
public static class DecisionCommitReasons
{
    public const string ReviewInvalid = "ontology.decision.review_invalid";
    public const string NotConfirmed = "ontology.decision.not_confirmed";
    public const string PlanRefused = "ontology.decisionplan.refused";
    public const string ActionsDoNotMatchPlan = "ontology.decision.actions_do_not_match_plan";
    public const string UnresolvedAction = "ontology.decision.unresolved_action";
    public const string GateUndeclared = "ontology.decision.gate_undeclared";
    public const string InvalidParameter = "ontology.decision.invalid_parameter";
    public const string ActionsExclude = "ontology.decision.actions_exclude";
    public const string DismissalReasonRequired = "ontology.decision.dismissal_reason_required";
    public const string DismissalReasonUnknown = "ontology.decision.dismissal_reason_unknown";
    public const string DismissalDetailRequired = "ontology.decision.dismissal_detail_required";
    public const string DismissalWithActions = "ontology.decision.dismissal_with_actions";
    public const string AlsoResolveNotAllowed = "ontology.decision.also_resolve_not_allowed";

    /// <summary>Task 142 fix round 2 (K3): the secure families of the review's items could not be read (a fault or a short answer).
    /// Unavailable (503), never a "not allowed" reason: a retry may succeed. A family that WAS read and differs stays
    /// <see cref="AlsoResolveNotAllowed"/> (a refusal).</summary>
    public const string SecureFamilyUnavailable = "ontology.decision.secure_family_unavailable";
    public const string SignalNotOpen = "ontology.decision.signal_not_open";
    public const string SignalUnreadable = "ontology.decision.signal_unreadable";
    public const string CoreUnsupported = "ontology.decision.core_unsupported";
    public const string ItemOwnerUnsupported = "ontology.decision.item_owner_unsupported";
    public const string RecordRefused = "ontology.decision.record_refused";
    public const string StepFailed = "ontology.decision.step_failed";
    public const string RecordWriteFailed = "ontology.decision.record_write_failed";
    public const string ReviewInProgress = "ontology.decision.review_in_progress";
    public const string IdempotencyUnavailable = "ontology.decision.idempotency_unavailable";
    public const string ReviewChanged = "ontology.decision.review_changed";
    public const string NotResumable = "ontology.decision.not_resumable";
    public const string NotAuthorized = "ontology.decision.not_authorized";
    public const string CoreChanged = "ontology.decision.core_changed";

    /// <summary>Task 154 (D-111): Mark complete was taken on an inquiry's "Record the outcome" To Do (409). It is completed by recording
    /// the inquiry's disposition (<c>POST /api/v1/inquiries/{id}/disposition</c>); the code is that route's family.</summary>
    public const string InquiryDispositionRequired = "inquiry.disposition_required";

    /// <summary>Task 141 fix round 2 (K2): a Signal names a subject (its pair id is set) but the subject's table cannot be
    /// derived, for example the subject was deleted and its RemoveLink cascade cleared the lookup. Refused exactly as before
    /// (Forbidden, fail closed, no new allow path); only the code and message say what is wrong. A Replay's warning for the
    /// same case is the bare <see cref="ReplaySubjectMissingWarning"/>, as the replay's other warnings are bare.</summary>
    public const string SignalSubjectMissing = "ontology.decision.signal_subject_missing";

    /// <summary>The replay-repair warning for <see cref="SignalSubjectMissing"/> (the sibling of <c>signal_close_not_authorized</c>).</summary>
    public const string ReplaySubjectMissingWarning = "signal_subject_missing";
}

/// <summary>Stable EventIds for the commit route (50360-50369, assigned by the coordinator 2026-10-11).</summary>
public static class DecisionCommitEvents
{
    /// <summary>Error: a step before the record failed; no record was written.</summary>
    public static readonly EventId StepFailed = new(50360, nameof(StepFailed));

    /// <summary>Critical: a message went out and the Decision Record could not be written (escalation trigger 1).</summary>
    public static readonly EventId SentButNotRecorded = new(50361, nameof(SentButNotRecorded));

    /// <summary>Warning: the record was written but a Signal could not be closed or linked.</summary>
    public static readonly EventId SignalCloseFailed = new(50362, nameof(SignalCloseFailed));

    /// <summary>Warning: a follow-on effect after the record (the budget-revision trigger, suppression) did not run.</summary>
    public static readonly EventId AfterRecordFailed = new(50363, nameof(AfterRecordFailed));

    /// <summary>Information: a review was recorded. Counts and ids only.</summary>
    public static readonly EventId Recorded = new(50364, nameof(Recorded));
}

/// <summary>
/// Records ONE decision (task 043, D-17): validate the whole review, run the taken actions' Dataverse writes in plan order,
/// create the Next steps, send messages last (the inquiry last of all), write the ONE Decision Record listing what actually
/// happened, then close the resolved Signals with the record linked.
/// </summary>
/// <remarks>
/// <para><b>Record last.</b> The record is append-only and a send cannot be recalled, so every check that can refuse runs before
/// the first write: the review's shape against the plan and catalog, every Signal as the caller
/// (<see cref="SignalCoreRecordAccess.AuthorizeAsync"/>), every executor's preflight (as the caller), and the record writer's own
/// no-I/O preflight on the projected record. A step that fails stops the commit: NO record, NO Signal change, and the result
/// lists each write that landed. No compensating delete is attempted.</para>
/// <para><b>Server-derived record facts (uac-r2 binding condition on #1600).</b> The core record comes from the Signal's own
/// server-written <c>sprk_corerecordtype</c> / <c>sprk_corerecordid</c> (the pair <see cref="SignalWriter"/> derived through
/// <see cref="CoreAncestorResolver"/>), its table from the record-type catalog; the no-core item's Decision Record owner is uac-r2's resolver answer for the
/// Signal's subject record. Nothing in the request body can name either.</para>
/// <para><b>Idempotent per review id.</b> A short-lived entry in the tenant cache (ADR-009: <see cref="ITenantCache"/>, keyed by
/// tenant and caller) marks a review in progress and then holds its first outcome; once written, the record itself holds the
/// review id in <c>sprk_steps</c> and is found by it when the cache has nothing. A cache that cannot be read fails closed.</para>
/// <para><b>Step 6 tolerates a system closure during the review</b> (032 review): a Signal the system resolved
/// (ConditionCleared, Superseded, PolicyRetired) after the review started is re-closed with this decision's resolution and
/// record, so the Signal and the record never disagree. One a PERSON resolved (another decision) is left alone and reported.</para>
/// </remarks>
public sealed class DecisionCommitService
{
    internal const string LedgerResource = "decision-review";
    internal const int LedgerVersion = 1;
    internal static readonly TimeSpan InProgressTtl = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan OutcomeTtl = TimeSpan.FromDays(7);

    private const int ReasonMaxLength = 2000;
    private const int MaxNextSteps = 10;
    private const int MaxAlsoResolve = 25;
    private const string WriteOperation = "write";
    private const int ConcurrencyVersionMismatch = unchecked((int)0x80060882);

    /// <summary>The state of a Signal the commit did not close or link.</summary>
    private const string Unchanged = "Unchanged";

    // Task 141: the subject is read from the subject lookup that carries the pair id (SignalWriter.SubjectEntityOf), never from
    // sprk_regardingrecordtype (a lookup to sprk_recordtype_ref since task 140), so the subject lookups are read instead of it.
    private static readonly string[] SignalColumns =
    [
        "sprk_lane", "sprk_policy", "sprk_policyversion", "sprk_corerecordtype", "sprk_corerecordid", "sprk_signalstatus",
        "sprk_resolutiontype", "sprk_resolvedon", "sprk_regardingrecordid",
        "sprk_factsnapshot", "sprk_privilegeflagged", "sprk_dedupekey", "sprk_decisionrecord", "versionnumber",
        .. SignalWriter.RegardingLookupByEntity.Values,
    ];

    private static readonly JsonSerializerOptions HashJson = new() { WriteIndented = false };

    private readonly SignalCoreRecordAccess _access;
    private readonly DecisionPlanService _plans;
    private readonly DecisionActionExecutors _executors;
    private readonly DecisionRecordWriter _records;
    private readonly OntologyWriterDataverseClient _writer;
    private readonly IGenericEntityService _entities;
    private readonly CallerRecordAccessProbe _probe;
    private readonly IIdentityNormalizationService _identity;
    private readonly AssignedAccessStore _links;
    private readonly ITenantCache _ledger;
    private readonly DecisionCommitFollowUps _followUps;
    private readonly IScheduledJobLease _lease;
    private readonly DecisionPlanReadStamp _planReads;
    private readonly TimeProvider _clock;
    private readonly ILogger<DecisionCommitService> _logger;

    public DecisionCommitService(
        SignalCoreRecordAccess access,
        DecisionPlanService plans,
        DecisionActionExecutors executors,
        DecisionRecordWriter records,
        OntologyWriterDataverseClient writer,
        IGenericEntityService entities,
        CallerRecordAccessProbe probe,
        IIdentityNormalizationService identity,
        AssignedAccessStore links,
        ITenantCache ledger,
        DecisionCommitFollowUps followUps,
        IScheduledJobLease lease,
        DecisionPlanReadStamp planReads,
        TimeProvider clock,
        ILogger<DecisionCommitService> logger)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _executors = executors ?? throw new ArgumentNullException(nameof(executors));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _followUps = followUps ?? throw new ArgumentNullException(nameof(followUps));
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _planReads = planReads ?? throw new ArgumentNullException(nameof(planReads));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The ledger entry for one review: in progress, or its first outcome.</summary>
    /// <remarks><c>ReviewStart</c> is the bounded time the review started (the plan read), kept so a resume judges a system
    /// closure against the SAME start. While a resume is in progress, <c>Outcome</c> still holds the failure it resumes, so a
    /// resume that ends without a write puts it back instead of forgetting the steps that landed.</remarks>
    internal sealed record LedgerEntry(bool InProgress, DateTimeOffset ReviewStart, string ContentHash, DecisionCommitResult? Outcome);

    /// <summary>What the route read about one Signal, app-only (server-written columns; never from the request).</summary>
    public sealed record SignalRow(
        Guid Id,
        DecisionLane? Lane,
        Guid? PolicyId,
        Guid? PolicyVersionId,
        Guid? CoreTypeId,
        Guid? CoreId,
        bool CoreMalformed,
        int? Status,
        int? Resolution,
        DateTime? ResolvedOn,
        string? SubjectEntity,
        Guid? SubjectId,
        string? FactSnapshot,
        bool PrivilegeFlagged,
        string? DedupeKey,
        Guid? DecisionRecordId,
        string? RowVersion)
    {
        public bool HasCore => CoreTypeId is not null && CoreId is not null;

        public bool IsOpen => Status is SignalWriter.SignalStatusOpen or SignalWriter.SignalStatusAcknowledged;

        /// <summary>Resolved by the SYSTEM (not a person) at or after <paramref name="since"/>.</summary>
        public bool SystemClosedSince(DateTimeOffset since) =>
            Status == SignalWriter.SignalStatusResolved
            && Resolution is SignalWriter.ResolutionConditionCleared or SignalWriter.ResolutionSuperseded or SignalWriter.ResolutionPolicyRetired
            && ResolvedOn is { } on && DateTime.SpecifyKind(on, DateTimeKind.Utc) >= since.UtcDateTime;
    }

    /// <summary>One step of the execution plan, in D-17 order.</summary>
    private sealed record ExecStep(string Key, int Phase, DecisionActionDefinition Def, bool IsNextStep, IReadOnlyDictionary<string, string?> Values);

    private enum Resolution
    {
        Acted,
        Dismissed,

        /// <summary>Only non-resolving actions were taken (D-119 the budget inquiry, D-128 the Do-lane reminder): the Signal is Acknowledged and linked, not closed.</summary>
        InquirySent,
    }

    /// <summary>Everything validation produced; nothing in it came from the request except the values and the "because".</summary>
    private sealed record Prepared(
        SignalRow Primary,
        IReadOnlyList<SignalRow> Resolved,
        DecisionLane Lane,
        ResolvedDecisionPlan Plan,
        IReadOnlyList<(DecisionActionDefinition Def, bool Taken, IReadOnlyDictionary<string, string?> Values)> Offered,
        IReadOnlyList<ExecStep> Steps,
        DecisionCoreRecord? Core,
        DecisionRecordCore? RecordCore,
        string? GateTier,
        string? DismissalText,
        Resolution Resolution)
    {
        /// <summary>Task 145 (D-34): the matter the decision is about, for the executors and the budget-revision trigger: the
        /// decision CONTEXT, so it comes from the Signal's core pair (<c>sprk_corerecordtype</c> + <c>sprk_corerecordid</c>), never
        /// from the typed <c>sprk_matter</c> lookup. Null when the core is not a matter (a project, a work assignment) or there is none.</summary>
        public Guid? MatterId => Core is { Entity: "sprk_matter" } core ? core.Id : null;
    }

    /// <summary>Commits one review on <paramref name="signalId"/>. Never throws for a failed write: every outcome is a result.</summary>
    public async Task<DecisionCommitResult> CommitAsync(Guid signalId, DecisionReview review, HttpContext http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        var now = _clock.GetUtcNow();

        // ---- Who is asking (no I/O) -----------------------------------------------------------------------------------
        var tenantId = http.User.FindFirst("tid")?.Value ?? http.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        var callerObjectId = CallerResolution.ResolveObjectId(http.User);
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(callerObjectId))
            return new DecisionCommitResult { Status = DecisionCommitStatus.CallerUnresolved, ReviewId = review?.ReviewId ?? Guid.Empty };

        if (review is null || review.ReviewId == Guid.Empty)
            return Refused(Guid.Empty, DecisionCommitReasons.ReviewInvalid, "A review needs a review id.");

        // ---- (1a) Every Signal, AS THE CALLER (task 036's one check), before the review id is even looked up: a caller who
        // cannot read an item learns nothing about it, not even a stored outcome. Nothing about the Signals is read app-only yet.
        var also = review.AlsoResolve ?? [];
        if (also.Count > MaxAlsoResolve)
            return Refused(review.ReviewId, DecisionCommitReasons.ReviewInvalid, $"A decision can resolve at most {MaxAlsoResolve} other items.");
        if (also.Any(id => id == Guid.Empty || id == signalId) || also.Distinct().Count() != also.Count)
            return Refused(review.ReviewId, DecisionCommitReasons.AlsoResolveNotAllowed, "Also resolve names the item itself, a blank id or the same item twice.");

        var ids = new List<Guid> { signalId };
        ids.AddRange(also);
        var lanes = new Dictionary<Guid, SignalAccessDecision>();
        foreach (var id in ids)
        {
            // Readability only here; the Do-lane "mine" half (D-141) follows below, once the review id is known (task 150).
            var decision = await _access.AuthorizeAsync(id, ct, enforceDoLaneMine: false).ConfigureAwait(false);
            if (decision.Outcome == SignalAccessOutcome.CallerUnresolved)
                return new DecisionCommitResult { Status = DecisionCommitStatus.CallerUnresolved, ReviewId = review.ReviewId };
            if (decision.Outcome == SignalAccessOutcome.Unavailable)
                return Unavailable(review.ReviewId, DecisionCommitReasons.SignalUnreadable, "This item could not be checked right now; nothing was done.");
            if (decision.Outcome != SignalAccessOutcome.Allowed)
                return new DecisionCommitResult { Status = DecisionCommitStatus.NotFound, ReviewId = review.ReviewId };
            lanes[id] = decision;
        }

        var hash = ContentHash(signalId, review);
        var ledgerKey = $"{callerObjectId}:{review.ReviewId:D}";

        // ---- (1b) D-141 (task 150): a Do item is the caller's own work, or the uniform 404, before any lock, ledger answer or write.
        // The one exception is THIS caller's own review of exactly this content already in the ledger: it passed this check on its
        // first attempt, and the decision may since have reassigned the item (Reassign), so its stored outcome is RETURNED (no
        // write) or reported as in progress. The skip stops at the first write: a resume or a repair replay of that review checks
        // "mine" again (CommitLeasedAsync) unless the review's own landed reassign step is what moved the item. Any other review
        // id, or the same id with other content, is checked here, so a not-mine item never gets an in-progress, review-changed or
        // replay answer an unreadable item would not.
        var mineConfirmed = !await IsSameReviewInLedgerAsync(tenantId, ledgerKey, hash, ct).ConfigureAwait(false);
        if (mineConfirmed && await ConfirmDoLaneMineAsync(review.ReviewId, ids, lanes, ct).ConfigureAwait(false) is { } notMine)
            return notMine;

        // ---- (0a) Locks, across instances: atomic leases (Redis SET NX PX when Redis is on; the same primitive
        // NoAccessShareEnforcer uses as a keyed mutex). One for the review id (the ledger's marker alone is a get-then-set), and
        // one per covered Signal (K1: two review ids on one Signal must never both send and record), taken in one fixed order
        // and released in the same finally. A lock is taken WITHOUT the request's token and the token is checked once it is
        // held (K7), so a cancellation can never strand a lock that was granted for its 10 minutes.
        var lockIds = new List<string> { LeaseId(tenantId, callerObjectId, review.ReviewId) };
        lockIds.AddRange(ids.Select(id => SignalLeaseId(tenantId, id)).OrderBy(id => id, StringComparer.Ordinal));
        // Task 161 fix round 1 (K2a): a review that TAKES Create budget also holds the matter's budget lease, after the others in
        // the same fixed order, so two commits on different Signals of one matter are serialized and the second one's recount sees
        // the first one's budget (and refuses). Not taken (skipped, or not in the plan): no lease, and nothing else waits on it.
        if (review.Actions?.Any(a => a is { Taken: true, Code: ResolvedDecisionPlan.CreateBudgetCode }) == true
            && lanes[signalId] is { Outcome: SignalAccessOutcome.Allowed, CoreId: { } budgetMatterId } budgetCore
            && string.Equals(budgetCore.CoreTable, "sprk_matter", StringComparison.OrdinalIgnoreCase))
        {
            lockIds.Add(BudgetLeaseId(tenantId, budgetMatterId));
        }

        var held = new List<(string Id, string Token)>();
        try
        {
            foreach (var lockId in lockIds)
            {
                ScheduledJobLeaseGrant grant;
                try
                {
                    grant = await _lease.TryAcquireAsync(lockId, occurrenceUtc: null, InProgressTtl, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Decision commit: a lock could not be taken for review {ReviewId} ({ExceptionType}); refusing (fail closed).",
                        review.ReviewId, ex.GetType().Name);
                    return Unavailable(review.ReviewId);
                }

                if (grant.Status != ScheduledJobLeaseStatus.Granted || grant.Token is not { } token)
                    return InProgress(review.ReviewId);
                held.Add((lockId, token));
                ct.ThrowIfCancellationRequested();
            }

            return await CommitLeasedAsync(signalId, review, http, now, tenantId, callerObjectId, ids, lanes, hash, ledgerKey, mineConfirmed, ct).ConfigureAwait(false);
        }
        finally
        {
            for (var i = held.Count - 1; i >= 0; i--)
            {
                try
                {
                    await _lease.ReleaseAsync(held[i].Id, held[i].Token, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // It expires on its own (InProgressTtl); until then a repeat is told the review is in progress.
                    _logger.LogWarning("Decision commit: a lock could not be released ({ExceptionType}).", ex.GetType().Name);
                }
            }
        }
    }

    internal static string LeaseId(string tenantId, string callerObjectId, Guid reviewId) =>
        $"{LedgerResource}:{tenantId}:{callerObjectId}:{reviewId:D}";

    /// <summary>The lock on one Signal: held by whichever review is committing it (any caller, any review id).</summary>
    internal static string SignalLeaseId(string tenantId, Guid signalId) => $"decision-signal:{tenantId}:{signalId:D}";

    /// <summary>Task 161 fix round 1 (K2a): the lock on one matter's budget creation, held by whichever review is taking Create budget.</summary>
    internal static string BudgetLeaseId(string tenantId, Guid matterId) => $"decision-budget:{tenantId}:{matterId:D}";

    /// <summary>K3: a review never started earlier than the plan-read window, so a resume or replay days later cannot reach back
    /// past it to re-close a system resolution.</summary>
    private static DateTimeOffset Bounded(DateTimeOffset reviewStart, DateTimeOffset now) =>
        reviewStart > now - DecisionPlanReadStamp.Ttl ? reviewStart : now - DecisionPlanReadStamp.Ttl;

    /// <summary>
    /// Task 150: whether THIS caller's ledger already holds this review id with this content (in progress or with an outcome). A
    /// ledger fault answers false, so the Do-lane "mine" check runs (fail closed; the locked read then answers the fault itself).
    /// </summary>
    private async Task<bool> IsSameReviewInLedgerAsync(string tenantId, string ledgerKey, string hash, CancellationToken ct)
    {
        try
        {
            var prior = await _ledger.GetAsync<LedgerEntry>(tenantId, LedgerResource, ledgerKey, LedgerVersion, ct: ct).ConfigureAwait(false);
            return prior is not null && string.Equals(prior.ContentHash, hash, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision commit: the review ledger could not be read before the Do-lane check ({ExceptionType}); checking.",
                ex.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// D-141 (task 150): every Do-lane Signal in the review must be the caller's own work (<see cref="SignalCoreRecordAccess.ConfirmDoLaneMineAsync"/>,
    /// the worklist's rule). Null when they all are; otherwise exactly the answer the readability check gives an unreadable item.
    /// </summary>
    private async Task<DecisionCommitResult?> ConfirmDoLaneMineAsync(
        Guid reviewId, IReadOnlyList<Guid> ids, IReadOnlyDictionary<Guid, SignalAccessDecision> lanes, CancellationToken ct)
    {
        foreach (var id in ids)
        {
            var decision = await _access.ConfirmDoLaneMineAsync(lanes[id], ct).ConfigureAwait(false);
            if (decision.Outcome == SignalAccessOutcome.CallerUnresolved)
                return new DecisionCommitResult { Status = DecisionCommitStatus.CallerUnresolved, ReviewId = reviewId };
            if (decision.Outcome == SignalAccessOutcome.Unavailable)
                return Unavailable(reviewId, DecisionCommitReasons.SignalUnreadable, "This item could not be checked right now; nothing was done.");
            if (decision.Outcome != SignalAccessOutcome.Allowed)
                return new DecisionCommitResult { Status = DecisionCommitStatus.NotFound, ReviewId = reviewId };
        }

        return null;
    }

    /// <summary>Task 150 (pass 2 F): whether the review's own <c>reassign</c> step landed. That step moved the Do item away from the
    /// caller, after the caller passed the "mine" check on the first attempt, so a resume or replay of this review is not judged by
    /// whose item it is now.</summary>
    private static bool OwnReassignLanded(DecisionCommitResult first) =>
        first.Steps.Any(s => s.Code == ReassignActionCode && s.Status == nameof(DecisionActionStatus.Done));

    private const string ReassignActionCode = "reassign";

    private static DecisionCommitResult InProgress(Guid reviewId) =>
        new()
        {
            Status = DecisionCommitStatus.InProgress,
            ReviewId = reviewId,
            ReasonCode = DecisionCommitReasons.ReviewInProgress,
            Detail = "This decision is already being recorded.",
        };

    private async Task<DecisionCommitResult> CommitLeasedAsync(
        Guid signalId, DecisionReview review, HttpContext http, DateTimeOffset now, string tenantId, string callerObjectId,
        IReadOnlyList<Guid> ids, IReadOnlyDictionary<Guid, SignalAccessDecision> lanes, string hash, string ledgerKey, bool mineConfirmed,
        CancellationToken ct)
    {
        // ---- (0) The review id: in progress, or a first outcome to return --------------------------------------------
        LedgerEntry? prior;
        try
        {
            prior = await _ledger.GetAsync<LedgerEntry>(tenantId, LedgerResource, ledgerKey, LedgerVersion, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Decision commit: the review ledger could not be read for review {ReviewId}; refusing (fail closed).", review.ReviewId);
            return Unavailable(review.ReviewId);
        }

        // D-141 (task 150): the check was skipped before the lock because this review was in the ledger; under the lock it is not
        // (or holds other content), so this is a first attempt after all and the check runs now.
        if (!mineConfirmed && (prior is null || !string.Equals(prior.ContentHash, hash, StringComparison.Ordinal))
            && await ConfirmDoLaneMineAsync(review.ReviewId, ids, lanes, ct).ConfigureAwait(false) is { } notMine)
            return notMine;

        DecisionCommitResult? resume = null;
        if (prior is { InProgress: true })
            return InProgress(review.ReviewId);

        if (prior is { Outcome: { } first })
        {
            // The same review id is the same review: the stored hash covers the Signal id and everything the review asks for.
            if (!string.Equals(prior.ContentHash, hash, StringComparison.Ordinal))
                return Refused(review.ReviewId, DecisionCommitReasons.ReviewChanged, "This review id was already used for a different decision.");
            // D-141 (task 150, pass 2 F): returning the stored outcome writes nothing, so it keeps the skipped "mine" check. A resume
            // and a repair replay WRITE (as the caller, on a Do item someone else may own now), so unless the check already ran for
            // this attempt they run it first; the one exception is a review whose own reassign step landed: that step is what moved
            // the item, and the first attempt passed the check before it ran (task 150 review F1).
            var mineNeeded = !mineConfirmed && !OwnReassignLanded(first);
            if (first.Status == DecisionCommitStatus.Recorded)
                return await ReplayRecordedAsync(first, prior, Bounded(prior.ReviewStart, now), ids, lanes, review, http, tenantId, ledgerKey, mineNeeded, ct).ConfigureAwait(false);
            if (!review.Retry || first.Status != DecisionCommitStatus.Failed)
                return first with { Replayed = true };
            if (!first.Resumable)
                return first with { Replayed = true, ReasonCode = DecisionCommitReasons.NotResumable };
            if (mineNeeded && await ConfirmDoLaneMineAsync(review.ReviewId, ids, lanes, ct).ConfigureAwait(false) is { } notMineOnResume)
                return notMineOnResume;
            resume = first;
        }

        // The review started when the SERVER last served this caller the plan (K4: never a client-sent time); a resume keeps
        // the first attempt's start, but never earlier than the plan-read window (K3); no stamp means it started now (any earlier
        // system closure is then a conflict).
        var reviewStart = prior is { } resumed
            ? Bounded(resumed.ReviewStart, now)
            : await _planReads.ReadAsync(tenantId, callerObjectId, signalId, ct).ConfigureAwait(false) ?? now;

        if (!review.Confirmed)
            return Refused(review.ReviewId, DecisionCommitReasons.NotConfirmed, "The decision was not confirmed; nothing was done.");

        // Mark the review in progress BEFORE any read that could take time, so a double-click meets the marker. A resume keeps
        // the failure it resumes inside the marker: if this attempt ends without a write outcome, that failure is put back.
        // Written uncancellable: a cancellation during the write must not leave a marker the try below never releases (it
        // would block this review for InProgressTtl). A cancellation is seen inside the try, which frees the marker.
        var marked = await TrySetLedgerAsync(tenantId, ledgerKey, new LedgerEntry(true, reviewStart, hash, resume), InProgressTtl, CancellationToken.None).ConfigureAwait(false);
        if (!marked)
            return Unavailable(review.ReviewId);

        DecisionCommitResult result;
        var progress = new CommitProgress();
        try
        {
            result = await CommitCoreAsync(ids, lanes, review, http, now, reviewStart, hash, resume, progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Only reachable before the first write: from then on the writes run uncancellable (CommitCoreAsync). Nothing new
            // landed, so free the review id (or put back the failure being resumed).
            await ReleaseLedgerAsync(tenantId, ledgerKey, reviewStart, hash, resume).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Defensive: CommitCoreAsync turns every expected failure into a result. A fault before the first write wrote
            // nothing: 503, and the review id is freed (or a resumed failure put back). After it, report the fault without
            // claiming what landed, and keep the review id blocked.
            _logger.LogError(DecisionCommitEvents.StepFailed, ex, "Decision commit: review {ReviewId} faulted unexpectedly (writing: {Writing}).",
                review.ReviewId, progress.Writing);
            if (progress.Writing && !progress.FollowUpsRan)
            {
                // F4: a revision that landed still gets its trigger, so the condition it cleared is re-evaluated now.
                try
                {
                    await _followUps.AfterFailedCommitAsync(progress.MatterId, progress.Executed, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception followUpFault)
                {
                    _logger.LogWarning(DecisionCommitEvents.AfterRecordFailed, "Decision commit: the after-failure trigger did not run ({ExceptionType}).",
                        followUpFault.GetType().Name);
                }
            }

            result = !progress.Writing ? Unavailable(review.ReviewId) : new DecisionCommitResult
            {
                Status = DecisionCommitStatus.Failed,
                ReviewId = review.ReviewId,
                ReasonCode = DecisionCommitReasons.StepFailed,
                Detail = "The decision could not be completed; check the item before trying again.",
                Resumable = false,
            };
        }

        // Only an outcome after which a write may have happened is the review's first outcome. A refusal wrote nothing, so the
        // same review id may be corrected and sent again.
        if (result.Status is DecisionCommitStatus.Recorded or DecisionCommitStatus.Failed)
        {
            if (!await TrySetLedgerAsync(tenantId, ledgerKey, new LedgerEntry(false, reviewStart, hash, result), OutcomeTtl, CancellationToken.None).ConfigureAwait(false))
            {
                if (result.Status == DecisionCommitStatus.Recorded)
                {
                    _logger.LogWarning("Decision commit: the outcome of review {ReviewId} could not be stored; a repeat will find the record by its review id.", review.ReviewId);
                }
                else
                {
                    // A failure has no record to find: a repeat after the in-progress marker expires could run the landed writes
                    // again. Name them for the operator.
                    _logger.LogError(DecisionCommitEvents.StepFailed,
                        "Decision commit: the FAILED outcome of review {ReviewId} could not be stored; a repeat may write again. Landed: {Landed}.",
                        review.ReviewId, string.Join(", ", result.Landed.Select(l => $"{l.Entity}:{l.Id:D}")));
                }
            }
        }
        else
        {
            await ReleaseLedgerAsync(tenantId, ledgerKey, reviewStart, hash, resume).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>A repeat of a Recorded review: returns the first outcome and closes (or links) any covered Signal its first run
    /// did not (a close that failed, a crash between the record and the closes). <see cref="CloseSignalAsync"/>'s own
    /// "already done" check makes this idempotent. Nothing else is written again.</summary>
    /// <remarks>K5: FR-51 is asked again, as the caller, before any repair (rights may have been revoked since the first run).
    /// When the caller may no longer decide, or it cannot be checked, nothing is closed: the stored outcome is returned with
    /// each still-open Signal listed as Unchanged and a warning saying why, and the stored outcome is left as it was so a later
    /// repeat can try again. K6: a repair that dismisses runs suppression as a first run does.
    /// Task 150 (pass 2 F, D-141): when <paramref name="mineNeeded"/>, the Do-lane "mine" check runs before the first read or write
    /// of a repair, and a not-mine answer is returned with nothing written; a replay with nothing to close writes nothing and skips it.</remarks>
    private async Task<DecisionCommitResult> ReplayRecordedAsync(
        DecisionCommitResult first, LedgerEntry prior, DateTimeOffset reviewStart, IReadOnlyList<Guid> ids,
        IReadOnlyDictionary<Guid, SignalAccessDecision> lanes, DecisionReview review, HttpContext http, string tenantId, string ledgerKey,
        bool mineNeeded, CancellationToken ct)
    {
        var closedAlready = first.Signals.Where(s => s.State != Unchanged).Select(s => s.SignalId).ToHashSet();
        var open = ids.Where(id => !closedAlready.Contains(id)).ToList();
        if (open.Count == 0 || first.DecisionRecordId is not { } recordId)
            return first with { Replayed = true };

        if (mineNeeded && await ConfirmDoLaneMineAsync(review.ReviewId, ids, lanes, ct).ConfigureAwait(false) is { } notMine)
            return notMine;

        Guid? confirmedBy;
        try
        {
            confirmedBy = await _probe.GetCallerSystemUserIdAsync(TokenHelper.ExtractBearerTokenOrNull(http), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Decision commit: the caller's user id could not be read to finish closing review {ReviewId} ({ExceptionType}).",
                review.ReviewId, ex.GetType().Name);
            confirmedBy = null;
        }

        if (confirmedBy is not { } by || by == Guid.Empty)
            return first with { Replayed = true };

        // K5: the caller's rights, again, on what the repair would touch: AppendTo on the core record and Write on each subject.
        var primary = await ReadSignalAsync(ids[0], CancellationToken.None).ConfigureAwait(false);
        var rows = new List<SignalRow>();
        foreach (var id in open)
        {
            if (await ReadSignalAsync(id, CancellationToken.None).ConfigureAwait(false) is { } row)
                rows.Add(row);
        }

        string? blocked = null;
        if (primary is null || rows.Count != open.Count)
        {
            blocked = "signal_close_not_checked";
        }
        else
        {
            DecisionCoreRecord? core = null;
            if (primary.HasCore)
            {
                var (table, fault) = await CoreTableAsync(primary.CoreTypeId!.Value, CancellationToken.None).ConfigureAwait(false);
                if (table is null)
                    blocked = fault ? "signal_close_not_checked" : "signal_close_not_authorized";
                else
                    core = new DecisionCoreRecord(table, primary.CoreId!.Value, primary.CoreTypeId);
            }

            if (blocked is null && await CallerMayDecideAsync(http, core, rows, CancellationToken.None).ConfigureAwait(false) is { } denied)
                blocked = denied.ReasonCode == DecisionCommitReasons.SignalSubjectMissing ? DecisionCommitReasons.ReplaySubjectMissingWarning
                    : denied.Status == DecisionCommitStatus.Forbidden ? "signal_close_not_authorized" : "signal_close_not_checked";
        }

        if (blocked is { } why)
        {
            _logger.LogWarning(DecisionCommitEvents.SignalCloseFailed,
                "Decision commit: the replay of review {ReviewId} did not close {Open} Signal(s): {Code}.", review.ReviewId, open.Count, why);
            var kept = first.Signals.Where(s => closedAlready.Contains(s.SignalId))
                .Concat(open.Select(id => new DecisionCommitSignal(id, Unchanged, why)))
                .ToList();
            return first with { Replayed = true, Signals = kept, Warnings = kept.Where(x => x.Warning is not null).Select(x => x.Warning!).ToList() };
        }

        var (resolution, dismissalText) = ResolutionOf(review, lanes[ids[0]].Lane);
        var closed = await CloseAllAsync(open, resolution, dismissalText, recordId, by, reviewStart).ConfigureAwait(false);
        var signals = first.Signals.Where(s => closedAlready.Contains(s.SignalId)).Concat(closed).ToList();
        var warnings = signals.Where(x => x.Warning is not null).Select(x => x.Warning!).ToList();
        warnings.AddRange(await SuppressAfterRepairAsync(resolution, rows, closed).ConfigureAwait(false));
        var updated = first with { Signals = signals, Warnings = warnings };
        await TrySetLedgerAsync(tenantId, ledgerKey, prior with { ReviewStart = reviewStart, Outcome = updated }, OutcomeTtl, CancellationToken.None).ConfigureAwait(false);
        return updated with { Replayed = true };
    }

    /// <summary>Closes (or links) <paramref name="signalIds"/> with an already-written record. Uncancellable: these are writes
    /// after the record.</summary>
    private async Task<IReadOnlyList<DecisionCommitSignal>> CloseAllAsync(
        IReadOnlyList<Guid> signalIds, Resolution resolution, string? dismissalText, Guid recordId, Guid confirmedBy, DateTimeOffset reviewStart)
    {
        var signals = new List<DecisionCommitSignal>();
        foreach (var id in signalIds)
            signals.Add(await CloseSignalAsync(id, resolution, dismissalText, recordId, confirmedBy, reviewStart, CancellationToken.None).ConfigureAwait(false));
        return signals;
    }

    /// <summary>K6: a repair (replay or stored-record) that dismissed Signals applies suppression for them, as a first run does
    /// (034 hand-off; the service's answer is discarded inside the follow-ups and never reaches the caller).</summary>
    private async Task<IReadOnlyList<string>> SuppressAfterRepairAsync(
        Resolution resolution, IReadOnlyList<SignalRow> rows, IReadOnlyList<DecisionCommitSignal> closed)
    {
        if (resolution != Resolution.Dismissed)
            return [];
        var dismissedNow = closed.Where(c => c.State == nameof(Resolution.Dismissed)).Select(c => c.SignalId).ToHashSet();
        var dismissed = rows.Where(r => dismissedNow.Contains(r.Id)).ToList();
        if (dismissed.Count == 0)
            return [];
        // A repair runs no step, so no budget-revision trigger needs the matter (task 145: it is never read from typed sprk_matter).
        return await _followUps.AfterRecordedAsync(null, [], dismissed, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>The resolution a (previously validated) review gives its Signals, and its dismissal text.</summary>
    private static (Resolution Resolution, string? DismissalText) ResolutionOf(DecisionReview review, DecisionLane? lane)
    {
        var taken = (review.Actions ?? []).Where(a => a is { Taken: true }).Select(a => a.Code).ToList();
        if (taken.Count == 0 && (review.NextSteps ?? []).Count == 0)
        {
            var reason = lane is { } l && review.Dismissal is { Code: { Length: > 0 } code }
                ? DecisionActionCatalog.DismissalReasons(l).FirstOrDefault(r => r.Code == code)
                : null;
            return (Resolution.Dismissed, reason is null ? null : DismissalText(reason, review.Dismissal!.Detail));
        }

        return (taken.Any(c => DecisionActionCatalog.TryGet(c, out var def) && def.ResolvesSignal) ? Resolution.Acted : Resolution.InquirySent, null);
    }

    private static string DismissalText(DecisionDismissalReason reason, string? detail)
    {
        var trimmed = detail?.Trim();
        var text = $"{reason.Code} - {(string.IsNullOrEmpty(trimmed) ? reason.Label : trimmed)}";
        return text.Length > ReasonMaxLength ? text[..ReasonMaxLength] : text;
    }

    /// <summary>Ends an attempt that wrote nothing new: a first attempt frees its review id; a resume puts back the failure it
    /// resumed, so the steps that landed are never forgotten (and never run again).</summary>
    private async Task ReleaseLedgerAsync(string tenantId, string ledgerKey, DateTimeOffset reviewStart, string hash, DecisionCommitResult? resume)
    {
        if (resume is null)
        {
            await TryRemoveLedgerAsync(tenantId, ledgerKey).ConfigureAwait(false);
            return;
        }

        if (!await TrySetLedgerAsync(tenantId, ledgerKey, new LedgerEntry(false, reviewStart, hash, resume), OutcomeTtl, CancellationToken.None).ConfigureAwait(false))
        {
            _logger.LogError(DecisionCommitEvents.StepFailed,
                "Decision commit: the failure resumed by review {ReviewId} could not be put back; its in-progress marker holds it until it expires.",
                resume.ReviewId);
        }
    }

    /// <summary>Whether a commit has reached its first write (set once, before step 2).</summary>
    private sealed class CommitProgress
    {
        public bool Writing { get; set; }

        /// <summary>The after-record or after-failure follow-ups have run (so a fault after them does not queue twice).</summary>
        public bool FollowUpsRan { get; set; }

        public Guid? MatterId { get; set; }

        public IReadOnlyList<DecisionCommitStep> Executed { get; set; } = [];
    }

    private async Task<DecisionCommitResult> CommitCoreAsync(
        IReadOnlyList<Guid> ids, IReadOnlyDictionary<Guid, SignalAccessDecision> lanes, DecisionReview review, HttpContext http,
        DateTimeOffset now, DateTimeOffset reviewStart, string hash, DecisionCommitResult? resume, CommitProgress progress, CancellationToken ct)
    {
        var reviewId = review.ReviewId;

        // The confirming human (sprk_confirmedby, sprk_resolvedby): the caller's own systemuserid, never a request field.
        Guid? callerUserId;
        try
        {
            callerUserId = await _probe.GetCallerSystemUserIdAsync(TokenHelper.ExtractBearerTokenOrNull(http), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision commit: the caller's user id could not be read ({ExceptionType}).", ex.GetType().Name);
            callerUserId = null;
        }

        if (callerUserId is not { } confirmedBy || confirmedBy == Guid.Empty)
            return new DecisionCommitResult { Status = DecisionCommitStatus.CallerUnresolved, ReviewId = reviewId };

        // ---- (1b) A record already written for this review (the cache expired or never got the outcome) ---------------
        StoredRecord? existingRecord;
        try
        {
            existingRecord = await FindRecordAsync(reviewId, confirmedBy, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Decision commit: could not check whether review {ReviewId} was already recorded; refusing (fail closed).", reviewId);
            return Unavailable(reviewId);
        }

        // F1: a record found by (caller, review id) answers only for the SAME review: its stored content hash and its stored
        // Signals must be this request's. Otherwise the review id is being reused for something else, and nothing is closed.
        if (existingRecord is { } stored && !stored.Covers(ids, hash))
            return Refused(reviewId, DecisionCommitReasons.ReviewChanged, "This review id was already used for a different decision.");

        // ---- (1c) The Signals, app-only: their server-written columns -------------------------------------------------
        var rows = new List<SignalRow>();
        foreach (var id in ids)
        {
            var row = await ReadSignalAsync(id, ct).ConfigureAwait(false);
            if (row is null)
                return Unavailable(reviewId, DecisionCommitReasons.SignalUnreadable, "An item in this decision could not be read; nothing was done.");
            rows.Add(row);
        }

        var finishing = existingRecord is not null && resume is null;
        var createBudgetLanded = (resume?.Steps ?? []).Any(s => s.Key == $"action:{ResolvedDecisionPlan.CreateBudgetCode}" && s.Status == nameof(DecisionActionStatus.Done));
        var prepared = await PrepareAsync(review, rows, lanes, reviewStart, finishing ? existingRecord!.Id : null, createBudgetLanded, resume is not null, ct).ConfigureAwait(false);
        if (prepared.Refusal is { } refusal)
            return refusal with { ReviewId = reviewId };
        var p = prepared.Ready!;

        // ---- (1c') FR-51: as the caller, Write on every Signal's subject and AppendTo on its core record. A decision acts on the
        // item and files a record under the matter, even when it only dismisses or approves (record-only actions write nothing
        // of their own, so no executor preflight would ask). Fails closed.
        if (await CallerMayDecideAsync(http, p.Core, p.Resolved, ct).ConfigureAwait(false) is { } denied)
            return denied with { ReviewId = reviewId };

        if (finishing)
        {
            // The ledger forgot the outcome (or never got it): the record answers, validated and authorized as above, and any
            // Signal not yet closed with it is closed with the RECORD's resolution (never the body's).
            var (resolution, dismissalText) = existingRecord!.RecordedResolution();
            var closedNow = await CloseAllAsync(ids, resolution, dismissalText, existingRecord.Id, confirmedBy, reviewStart).ConfigureAwait(false);
            var finishWarnings = closedNow.Where(x => x.Warning is not null).Select(x => x.Warning!).ToList();
            finishWarnings.AddRange(await SuppressAfterRepairAsync(resolution, p.Resolved, closedNow).ConfigureAwait(false));
            return new DecisionCommitResult
            {
                Status = DecisionCommitStatus.Recorded,
                ReviewId = reviewId,
                DecisionRecordId = existingRecord.Id,
                Replayed = true,
                Detail = "This decision was already recorded.",
                Signals = closedNow,
                Warnings = finishWarnings,
            };
        }

        // ---- (1c-walls) Task 142 (contract C9; uac-r2 #1665 N1 / #1778): ONE Decision Record never spans two secure walls. Also
        // resolve is one core record's (PrepareAsync), but a core record a non-member reads can hold Signals of more than one secure
        // family (D-123), so their secure-root sets must be the same set. After FR-51 (the answer is given only to a caller who may act
        // on every item) and before anything is written.
        if (p.Resolved.Count > 1 && await SecureWallsRefusalAsync(reviewId, p.Resolved, ct).ConfigureAwait(false) is { } walls)
            return walls;

        // ---- (1c'') F4-a (uac-r2): a no-core item's Decision Record owner is resolved only AFTER FR-51 (step 1f), so whether the
        // subject row exists never shows through a different answer to a caller who may not act on it. Here, with no I/O: a
        // no-core Signal that names no subject has nothing to file its record under.
        if (!p.Primary.HasCore && SubjectOf(p.Primary) is null)
            return Refused(reviewId, DecisionCommitReasons.ItemOwnerUnsupported, ItemOwnerUnsupportedDetail);

        progress.MatterId = p.MatterId;

        // D-129: the deciding individual is the caller's linked internal contact (Assigned To Internal), never an owner. Resolved
        // once, after FR-51. A user with no linked contact leaves the column empty (warning); a user whose contact could not be READ
        // is unknown, not absent, and the record is append-only, so that answers Unavailable before anything is written.
        var (deciderContact, deciderUnreadable) = await DeciderContactAsync(confirmedBy, ct).ConfigureAwait(false);
        if (deciderUnreadable)
            return Unavailable(reviewId, DecisionCommitReasons.RecordRefused, "The deciding user could not be identified right now; nothing was done. Try again shortly.");

        // ---- (1d) Every executor's preflight, as the caller. A resumed review skips the steps that already landed. ------
        var done = (resume?.Steps ?? []).Where(s => s.Status == nameof(DecisionActionStatus.Done)).ToDictionary(s => s.Key, StringComparer.Ordinal);
        foreach (var step in p.Steps.Where(s => !done.ContainsKey(s.Key)))
        {
            if (!_executors.TryGet(step.Def.Code, out var executor))
                return Refused(reviewId, DecisionCommitReasons.UnresolvedAction, $"'{step.Def.Code}' cannot be carried out here.", step.Def.Code);

            DecisionActionOutcome? refused;
            try
            {
                refused = await executor.PreflightAsync(Request(http, p, step), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Decision commit: the preflight of {Code} faulted for review {ReviewId}; refusing.", step.Def.Code, reviewId);
                return Unavailable(reviewId, DecisionCommitReasons.StepFailed, $"'{step.Def.Label}' could not be checked; nothing was done.");
            }

            if (refused is not null)
            {
                return new DecisionCommitResult
                {
                    Status = refused.ReasonCode == DecisionActionReasons.NotAuthorized ? DecisionCommitStatus.Forbidden : DecisionCommitStatus.Refused,
                    ReviewId = reviewId,
                    ActionCode = step.Def.Code,
                    ReasonCode = refused.ReasonCode,
                    Detail = refused.Detail,
                };
            }
        }

        // ---- (1e) The record writer's own no-I/O preflight on the projected record ----------------------------------------
        try
        {
            _records.Preflight(RecordRequest(review, p, confirmedBy, now, ProjectedSteps(p), ProjectedFollowOns(p), executed: null, hash, deciderContact));
        }
        catch (DecisionRecordRefusedException ex)
        {
            return Refused(reviewId, DecisionCommitReasons.RecordRefused, $"This decision cannot be recorded ({ex.Reason}); nothing was done.");
        }

        // ---- (1f) The record's owner, resolved BEFORE the first write: an owner refusal refuses the decision, not the record
        // after the sends. The answer is handed to the write at step 5. The owner is uac-r2's answer for EVERY parent the record
        // sits under (the core, if any, the record's subject and every core it derives (task 142), and the subject of every Signal
        // it resolves: secure-if-any, uac-r2 F1; D-129), taken as returned; and (F4-b) a refusal is 422 (a fixed text for a no-core
        // item) while a fault of the resolver or of the filing derivation is 503.
        var noCore = p.RecordCore is null;

        // D-134: a no-core review of a PERSONAL To Do is owned by its Assigned To user's business-unit team when every parent is
        // ordinary. The user is read here, app-only and after FR-51, through the one shared helper; no single user keeps the
        // parents' answer. A read FAULT is not "no user": it is held until the parents' answer is known (item 7: a secure parent
        // wins, so a fault there decides nothing), and otherwise answers Unavailable before any write.
        Guid? assignee = null;
        var assigneeUnreadable = false;
        if (noCore && SubjectOf(p.Primary) is { } item && string.Equals(item.Entity, PersonalToDoAssignee.Table, StringComparison.OrdinalIgnoreCase))
            (assignee, assigneeUnreadable) = await PersonalToDoAssigneeAsync(item.Id, ct).ConfigureAwait(false);

        DecisionRecordOwner recordOwner;
        try
        {
            recordOwner = await _records.ResolveOwnerAsync(RecordRequest(review, p, confirmedBy, now, ProjectedSteps(p), ProjectedFollowOns(p), executed: null, hash, deciderContact, assignee), ct).ConfigureAwait(false);
        }
        catch (DecisionRecordRefusedException ex) when (ex.Reason is DecisionRecordRefusalReason.OwnerResolutionFailed or DecisionRecordRefusalReason.FilingFailed)
        {
            return noCore
                ? Unavailable(reviewId, DecisionCommitReasons.ItemOwnerUnsupported, "The item's owner could not be checked; nothing was done.")
                : Unavailable(reviewId, DecisionCommitReasons.RecordRefused, "The decision's owner could not be checked; nothing was done.");
        }
        catch (DecisionRecordRefusedException ex)
        {
            return noCore
                ? Refused(reviewId, DecisionCommitReasons.ItemOwnerUnsupported, ItemOwnerUnsupportedDetail)
                : Refused(reviewId, DecisionCommitReasons.RecordRefused, $"This decision cannot be recorded ({ex.Reason}); nothing was done.");
        }

        if (assigneeUnreadable && recordOwner.Resolution is not { IsSecureOwner: true })
            return Unavailable(reviewId, DecisionCommitReasons.ItemOwnerUnsupported, "The item's owner could not be checked; nothing was done.");

        // ---- (2)(3)(4) The writes, in D-17 order ----------------------------------------------------------------------------
        // From the first write on, the request's cancellation no longer applies: a client that disconnects after a send must
        // still get its record written (or its failure stored), never a half-done commit with no outcome.
        var writeCt = CancellationToken.None;
        progress.Writing = true;
        var executed = new List<DecisionCommitStep>();
        progress.Executed = executed;
        foreach (var step in p.Steps)
        {
            if (done.TryGetValue(step.Key, out var already))
            {
                executed.Add(already);
                continue;
            }

            _executors.TryGet(step.Def.Code, out var executor);
            DecisionActionOutcome outcome;
            try
            {
                outcome = await executor.ExecuteAsync(Request(http, p, step), writeCt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(DecisionCommitEvents.StepFailed, ex, "Decision commit: {Code} threw for review {ReviewId}.", step.Def.Code, reviewId);
                outcome = DecisionActionOutcome.Failed(step.Def.Code, DecisionActionReasons.WriteFailed, "The step did not finish; it may have been applied.");
            }

            var record = ToStep(step, outcome);
            executed.Add(record);
            if (outcome.Status != DecisionActionStatus.Done)
            {
                var failed = Failure(reviewId, step, outcome, executed, p);
                _logger.LogError(DecisionCommitEvents.StepFailed,
                    "Decision commit: step {Phase} ({Code}) {Status} for review {ReviewId}: {ReasonCode}. No record written; {Landed} write(s) landed.",
                    step.Phase, step.Def.Code, outcome.Status, reviewId, outcome.ReasonCode, failed.Landed.Count);
                OntologyWriterTelemetry.RecordFailure("decision_commit_step_failed");
                await _followUps.AfterFailedCommitAsync(p.MatterId, executed, writeCt).ConfigureAwait(false);
                progress.FollowUpsRan = true;
                return failed;
            }
        }

        // ---- (5) The ONE Decision Record, listing what actually happened --------------------------------------------------
        var steps = RecordSteps(p, executed);
        var followOns = RecordFollowOns(p, executed);
        Guid recordId;
        string recordClass;
        if (existingRecord is { } found)
        {
            // A resumed review whose record was written by the first attempt (a create that timed out after committing).
            recordId = found.Id;
            recordClass = "Existing";
        }
        else
        {
            try
            {
                var written = await _records.WriteAsync(RecordRequest(review, p, confirmedBy, now, steps, followOns, executed, hash, deciderContact, assignee), recordOwner, writeCt).ConfigureAwait(false);
                recordId = written.RecordId;
                recordClass = written.RecordClass.ToString();
            }
            catch (Exception ex)
            {
                // The writer wraps every fault but a cancellation; with no request token here, that is a client-side timeout, so
                // the create may have committed.
                var refusedRecord = ex as DecisionRecordRefusedException ?? new DecisionRecordRefusedException(DecisionRecordRefusalReason.CreateFailed,
                    "The Decision Record create did not answer; it may have been written.", innerException: ex);
                var failed = RecordFailed(reviewId, refusedRecord, executed, p);
                // F4: the steps landed and the Signal was not closed, so a landed revision still gets its trigger now.
                await _followUps.AfterFailedCommitAsync(p.MatterId, executed, writeCt).ConfigureAwait(false);
                progress.FollowUpsRan = true;
                return failed;
            }
        }

        // ---- (6) Close (or, for an inquiry only, link) every Signal the decision covers -------------------------------------
        var signals = new List<DecisionCommitSignal>();
        var warnings = new List<string>();
        foreach (var row in p.Resolved)
        {
            var closed = await CloseSignalAsync(row.Id, p.Resolution, p.DismissalText, recordId, confirmedBy, reviewStart, writeCt).ConfigureAwait(false);
            signals.Add(closed);
            if (closed.Warning is { } warning)
                warnings.Add(warning);
        }

        warnings.AddRange(await _followUps.AfterRecordedAsync(p.MatterId, executed, p.Resolution == Resolution.Dismissed ? p.Resolved : [], writeCt).ConfigureAwait(false));
        progress.FollowUpsRan = true;

        // ---- (7) Task 168 (D-194): tell Assign Task's assignee, AFTER the record (D-17). Never fails or changes the decision. ------
        try
        {
            var notices = AssignTaskNotices(p.Steps, executed, p.Core);
            if (notices.Count > 0 && await _followUps.NotifyAssigneesAsync(http, notices, writeCt).ConfigureAwait(false) is { } notified)
                warnings.AddRange(notified);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(DecisionCommitEvents.AfterRecordFailed, ex,
                "Decision commit: review {ReviewId} recorded as {RecordId}; notifying the assignee stopped.", reviewId, recordId);
            warnings.Add(AssignTaskNotifier.EmailNotSentWarning);
        }

        _logger.LogInformation(DecisionCommitEvents.Recorded,
            "Decision commit: review {ReviewId} recorded as {RecordId} ({Steps} step(s), {Signals} Signal(s), {Warnings} warning(s)).",
            reviewId, recordId, executed.Count, signals.Count, warnings.Count);

        return new DecisionCommitResult
        {
            Status = DecisionCommitStatus.Recorded,
            ReviewId = reviewId,
            DecisionRecordId = recordId,
            RecordClass = recordClass,
            GateTier = p.GateTier,
            Steps = executed,
            FollowOns = followOns.Select(f => new DecisionCommitRef(f.Entity, f.Id)).ToList(),
            Signals = signals,
            Warnings = warnings,
        };
    }

    // ── Validation ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Task 142 (child-record contract C9, acceptance 7; uac-r2 #1665 N1 / #1778): the refusal when the Signals one decision resolves are
    /// not all in ONE secure family (the same set of secure roots above each Signal and its subject: <see cref="SuppressionPartition"/>,
    /// uac-r2's walk), else null. Ordinary beside secure is two families. Fail closed: an undetermined family is a family of its own (a
    /// refusal), and a fault or a short answer is Unavailable; nothing is written either way. The text names no record.
    /// </summary>
    private async Task<DecisionCommitResult?> SecureWallsRefusalAsync(Guid reviewId, IReadOnlyList<SignalRow> rows, CancellationToken ct)
    {
        IReadOnlyList<SuppressionPartition>? families;
        try
        {
            families = await _followUps.SecureFamiliesAsync(rows.Select(r => r.Id).ToList(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Decision commit: the secure families of review {ReviewId}'s items could not be read; refusing (fail closed).", reviewId);
            families = null;
        }

        if (families is null || families.Count != rows.Count)
            return Unavailable(reviewId, DecisionCommitReasons.SecureFamilyUnavailable, "Whether these items sit under the same secure records could not be checked; nothing was done. Try again shortly.");

        if (families.Any(f => f.IsUndetermined) || families.Distinct().Count() > 1)
        {
            _logger.LogWarning("Decision commit: review {ReviewId} resolves items under different secure records ({Families}); refused, nothing written.",
                reviewId, string.Join(" | ", families.Select(f => f.IsUndetermined ? "undetermined" : f.IsOrdinary ? "ordinary" : "secure")));
            return Refused(reviewId, DecisionCommitReasons.AlsoResolveNotAllowed, "Also resolve accepts only items under the same secure records; nothing was done.");
        }

        return null;
    }

    private async Task<(DecisionCommitResult? Refusal, Prepared? Ready)> PrepareAsync(
        DecisionReview review, IReadOnlyList<SignalRow> rows, IReadOnlyDictionary<Guid, SignalAccessDecision> access,
        DateTimeOffset reviewStart, Guid? linkedRecord, bool createBudgetLanded, bool resumed, CancellationToken ct)
    {
        static (DecisionCommitResult?, Prepared?) No(string reason, string detail, string? action = null, DecisionCommitStatus status = DecisionCommitStatus.Refused) =>
            (new DecisionCommitResult { Status = status, ReasonCode = reason, Detail = detail, ActionCode = action }, null);

        var primary = rows[0];
        if (primary.Lane is not { } lane || primary.PolicyVersionId is not { } versionId
            || access[primary.Id].Lane != lane || access[primary.Id].PolicyVersionId != versionId)
            return No(DecisionCommitReasons.SignalUnreadable, "The item could not be read consistently; nothing was done.", status: DecisionCommitStatus.Unavailable);

        foreach (var row in rows)
        {
            if (row.CoreMalformed)
                return No(DecisionCommitReasons.CoreUnsupported, "An item's core record could not be read; nothing was done.");
            // K2: the core pair the caller's own read authorized must be the pair this route acts on (re-read app-only).
            if (access[row.Id].CoreTypeId != row.CoreTypeId || access[row.Id].CoreId != row.CoreId)
                return No(DecisionCommitReasons.CoreChanged, "An item's matter or project changed while the decision was made; nothing was done.");
            // A Signal already closed WITH the record being finished (a stored-record repeat) is not a conflict: its close is done.
            if (!row.IsOpen && !row.SystemClosedSince(reviewStart) && !(linkedRecord is { } link && row.DecisionRecordId == link))
                return No(DecisionCommitReasons.SignalNotOpen, "This item was already resolved; nothing was done.", status: DecisionCommitStatus.Conflict);
        }

        // R-7 / D-34 / D-35: Also resolve is Decide-lane only, on the same CORE record; a no-core item never resolves another.
        if (rows.Count > 1)
        {
            if (lane != DecisionLane.Decide || !primary.HasCore)
                return No(DecisionCommitReasons.AlsoResolveNotAllowed, "Only a Decide item on a matter or project can resolve other items.");
            foreach (var other in rows.Skip(1))
            {
                if (other.Lane != DecisionLane.Decide || !other.HasCore || other.CoreTypeId != primary.CoreTypeId || other.CoreId != primary.CoreId)
                    return No(DecisionCommitReasons.AlsoResolveNotAllowed, "Also resolve accepts only Decide items on the same matter or project.");
            }
        }

        var resolution = await _plans.GetAsync(versionId, lane, ct).ConfigureAwait(false);
        if (resolution.Plan is not { } plan)
            return No(DecisionCommitReasons.PlanRefused, "This item's decision plan does not resolve against the action catalog.");

        var given = review.Actions ?? [];

        // Task 154 (D-111, 059 review): an OPEN inquiry's "Record the outcome" To Do is completed by recording the inquiry's
        // disposition (071's route), never by Mark complete, which would close the To Do and leave the inquiry open with nobody
        // prompted again. Once the inquiry is closed the To Do is ordinary (adversarial pass 1). The plan route offers the reduced plan
        // (ResolvedDecisionPlan.WithoutMarkComplete); this route applies the same reduction and refuses a Mark complete that is taken.
        // Read AS THE CALLER, only when the plan offers Mark complete on a To Do: the To Do, then the hinted inquiry or (no usable
        // hint) the matter's open inquiries, the for-todo lookup's own identification.
        // Pass 2 K4: not on the finishing path (linkedRecord set). A writer-created record of this same review already exists, so no
        // executor runs; the check would protect nothing and could only refuse (or 503) and leave the Signals unclosed.
        var isTodo = lane == DecisionLane.Do && string.Equals(primary.SubjectEntity, "sprk_todo", StringComparison.OrdinalIgnoreCase);
        if (linkedRecord is null && isTodo
            && primary.SubjectId is { } todoId && plan.Actions.Any(a => a.Code == ResolvedDecisionPlan.MarkCompleteCode))
        {
            var inquiryOutcome = await _access.CheckInquiryOutcomeTodoAsync(todoId, ct).ConfigureAwait(false);
            if (inquiryOutcome == InquiryOutcomeTodoCheck.Unavailable)
                return No(DecisionCommitReasons.SignalUnreadable, "This item could not be checked right now; nothing was done.", status: DecisionCommitStatus.Unavailable);
            if (inquiryOutcome == InquiryOutcomeTodoCheck.NotReadable)
                return (new DecisionCommitResult { Status = DecisionCommitStatus.NotFound }, null);
            if (inquiryOutcome == InquiryOutcomeTodoCheck.OpenInquiry)
            {
                if (given.Any(a => a?.Code == ResolvedDecisionPlan.MarkCompleteCode && a.Taken))
                    return No(DecisionCommitReasons.InquiryDispositionRequired,
                        "This To Do is completed by recording how the inquiry turned out, not by marking it complete; nothing was done.",
                        ResolvedDecisionPlan.MarkCompleteCode, DecisionCommitStatus.Conflict);

                plan = plan.WithoutMarkComplete();
                // A review built from an earlier plan may still list Mark complete as skipped: that is not an action of this plan.
                given = given.Where(a => a?.Code != ResolvedDecisionPlan.MarkCompleteCode).ToList();
            }
        }
        else if (linkedRecord is not null && isTodo && plan.Actions.Any(a => a.Code == ResolvedDecisionPlan.MarkCompleteCode)
            && given.All(a => a?.Code != ResolvedDecisionPlan.MarkCompleteCode))
        {
            // K4, finishing a review recorded under the reduced plan (the inquiry was open then): the stored record carries this
            // review's content hash (StoredRecord.Covers), so the review was matched against that plan when it was written. Without
            // the read, the plan it was matched against is the one without Mark complete.
            plan = plan.WithoutMarkComplete();
        }

        // Task 161 (D-180, D-17): a matter with no active budget is offered Create budget in place of Revise budget; the plan route
        // applies the same substitution (SignalCoreRecordAccess.WithCreateBudgetWhenNoneAsync), recomputed here and never trusted from
        // the client. "Has no budget" is the BFF's own count (F1-a), never the caller's read. A review that names Create budget when the
        // matter now has a budget is refused with its own reason (one action is never silently converted into the other), and when the
        // count cannot be read it is Unavailable (F1-b): a read that failed is not a change in the budgets. Exceptions: a review whose
        // Create budget step already landed (a resume of a failed commit, or finishing a stored record) now sees a budget, which is the
        // step's own effect; and a resume that SKIPPED Create budget writes nothing for it, so a budget somebody else made meanwhile
        // changes nothing (K2b). Both keep the plan the review was taken under, without a read.
        var namesCreateBudget = given.Any(a => a?.Code == ResolvedDecisionPlan.CreateBudgetCode);
        var createBudgetTaken = given.Any(a => a?.Code == ResolvedDecisionPlan.CreateBudgetCode && a.Taken);
        if (namesCreateBudget && given.All(a => a?.Code != ResolvedDecisionPlan.ReviseBudgetCode)
            && (linkedRecord is not null || createBudgetLanded || (resumed && !createBudgetTaken)))
        {
            plan = plan.WithCreateBudgetInsteadOfRevise();
        }
        else if (SignalCoreRecordAccess.CanSubstituteCreateBudget(plan, access[primary.Id]))
        {
            var count = await _access.CountMatterBudgetsAsync(access[primary.Id], ct).ConfigureAwait(false);
            if (count == MatterBudgetCount.None)
            {
                plan = plan.WithCreateBudgetInsteadOfRevise();
            }
            else if (namesCreateBudget)
            {
                return count == MatterBudgetCount.Unknown
                    ? No(DecisionCommitReasons.SignalUnreadable, "Whether the matter has a budget could not be checked; nothing was done. Try again.", status: DecisionCommitStatus.Unavailable)
                    : No(DecisionCommitReasons.ActionsDoNotMatchPlan, "The matter's budgets changed while you were deciding; reopen the item.");
            }
        }

        // Every offered action exactly once, nothing else. An unknown code is refused, never defaulted (C-3).
        foreach (var action in given)
        {
            if (!DecisionActionCatalog.TryGet(action?.Code, out _))
                return No(DecisionCommitReasons.UnresolvedAction, $"'{action?.Code}' is not an action this decision can record.", action?.Code);
        }

        if (given.Count != plan.Actions.Count
            || given.Select(a => a.Code).Distinct(StringComparer.Ordinal).Count() != given.Count
            || plan.Actions.Any(def => given.All(a => a.Code != def.Code)))
            return No(DecisionCommitReasons.ActionsDoNotMatchPlan, "The review must list every action the plan offers, each once, taken or skipped.");

        var offered = new List<(DecisionActionDefinition, bool, IReadOnlyDictionary<string, string?>)>();
        foreach (var def in plan.Actions)
        {
            var action = given.First(a => a.Code == def.Code);
            if (!action.Taken)
            {
                offered.Add((def, false, new Dictionary<string, string?>()));
                continue;
            }

            var (values, invalid) = ReadValues(def, action.Values);
            if (invalid is not null)
                return No(DecisionCommitReasons.InvalidParameter, invalid, def.Code);
            offered.Add((def, true, values!));
        }

        var nextGiven = review.NextSteps ?? [];
        if (nextGiven.Count > MaxNextSteps)
            return No(DecisionCommitReasons.ReviewInvalid, $"A decision can create at most {MaxNextSteps} Next steps.");
        var next = new List<(DecisionActionDefinition Def, IReadOnlyDictionary<string, string?> Values)>();
        foreach (var step in nextGiven)
        {
            if (!DecisionActionCatalog.TryGet(step?.Code, out var def))
                return No(DecisionCommitReasons.UnresolvedAction, $"'{step?.Code}' is not a Next step this decision can create.", step?.Code);
            if (plan.NextSteps.All(n => n.Code != def.Code))
                return No(DecisionCommitReasons.ActionsDoNotMatchPlan, $"'{def.Label}' is not a Next step this plan offers.", def.Code);
            var (values, invalid) = ReadValues(def, step!.Values);
            if (invalid is not null)
                return No(DecisionCommitReasons.InvalidParameter, invalid, def.Code);
            next.Add((def, values!));
        }

        var taken = offered.Where(o => o.Item2).Select(o => o.Item1).ToList();
        if (DecisionActionCatalog.FirstConflict(taken.Select(t => t.Code)) is { } clash)
            return No(DecisionCommitReasons.ActionsExclude, $"'{clash.First}' and '{clash.Second}' cannot both be taken.");

        // Every taken action and Next step must declare its gate (D-52): refused, never defaulted.
        var tiers = new List<GateRiskTier>();
        foreach (var def in taken.Concat(next.Select(n => n.Def)))
        {
            if (def.Gate is not { } gate)
                return No(DecisionCommitReasons.GateUndeclared, $"'{def.Label}' declares no gate tier, so it cannot be recorded.", def.Code);
            tiers.Add(DecisionGateTier.Resolve(gate));
        }

        // Z-7 / R-8: taking nothing is a dismissal with a reason from the lane's closed list; Other needs detail.
        string? dismissalText = null;
        Resolution outcome;
        if (taken.Count == 0 && next.Count == 0)
        {
            if (review.Dismissal is not { Code: { Length: > 0 } code } dismissal)
                return No(DecisionCommitReasons.DismissalReasonRequired, "Taking nothing is a dismissal, and a dismissal needs a reason.");
            var reason = DecisionActionCatalog.DismissalReasons(lane).FirstOrDefault(r => r.Code == code);
            if (reason is null)
                return No(DecisionCommitReasons.DismissalReasonUnknown, $"'{code}' is not a dismissal reason for this lane.");
            if (reason.RequiresDetail && string.IsNullOrEmpty(dismissal.Detail?.Trim()))
                return No(DecisionCommitReasons.DismissalDetailRequired, $"'{reason.Label}' needs a short explanation.");
            dismissalText = DismissalText(reason, dismissal.Detail);
            outcome = Resolution.Dismissed;
        }
        else
        {
            if (review.Dismissal is not null)
                return No(DecisionCommitReasons.DismissalWithActions, "A decision that takes an action or creates a Next step is not a dismissal.");

            // D-119: only an action that resolves (not the inquiry, an intermediate step) closes the item; Next steps alone don't.
            outcome = taken.Any(t => t.ResolvesSignal) ? Resolution.Acted : Resolution.InquirySent;
        }

        // ---- Server-derived core record (uac-r2 binding condition). A no-core item's Decision Record owner is resolved AFTER FR-51 (F4-a). A
        // catalog row that is missing or not a core type is 422; a read that faulted is 503 (F4-b).
        DecisionCoreRecord? core = null;
        DecisionRecordCore? recordCore = null;
        if (primary.HasCore)
        {
            var (table, fault) = await CoreTableAsync(primary.CoreTypeId!.Value, ct).ConfigureAwait(false);
            if (fault)
                return No(DecisionCommitReasons.CoreUnsupported, "The item's matter or project could not be read; nothing was done.", status: DecisionCommitStatus.Unavailable);
            if (table is null)
                return No(DecisionCommitReasons.CoreUnsupported, "The item's matter or project could not be resolved; nothing was done.");
            core = new DecisionCoreRecord(table, primary.CoreId!.Value, primary.CoreTypeId);
            recordCore = new DecisionRecordCore(table, primary.CoreId!.Value, primary.CoreTypeId!.Value);
        }

        // ---- The execution plan, D-17 order ----------------------------------------------------------------------------------
        var steps = new List<ExecStep>();
        foreach (var (def, isTaken, values) in offered.Where(o => o.Item2 && !o.Item1.SendsMessage))
            steps.Add(new ExecStep($"action:{def.Code}", 2, def, false, values));
        for (var i = 0; i < next.Count; i++)
        {
            if (!next[i].Def.SendsMessage)
                steps.Add(new ExecStep($"next-step:{next[i].Def.Code}#{i}", 3, next[i].Def, true, next[i].Values));
        }

        foreach (var (def, _, values) in offered.Where(o => o.Item2 && o.Item1.SendsMessage && o.Item1.Code != BudgetInquiryExecutor.ActionCode))
            steps.Add(new ExecStep($"action:{def.Code}", 4, def, false, values));
        for (var i = 0; i < next.Count; i++)
        {
            if (next[i].Def.SendsMessage)
                steps.Add(new ExecStep($"next-step:{next[i].Def.Code}#{i}", 4, next[i].Def, true, next[i].Values));
        }

        foreach (var (def, _, values) in offered.Where(o => o.Item2 && o.Item1.Code == BudgetInquiryExecutor.ActionCode))
            steps.Add(new ExecStep($"action:{def.Code}", 4, def, false, values)); // the inquiry is always last (D-17)

        var gateTier = DecisionGateTier.Strictest(tiers) is { } strictest ? DecisionGateTier.ToText(strictest) : null;
        return (null, new Prepared(primary, rows, lane, plan, offered, steps, core, recordCore, gateTier, dismissalText, outcome));
    }

    /// <summary>Task 168 (D-194): one notice per Assign Task step that is Done with Send email notification on (omitted is on), from the
    /// step's own values and the task it wrote.</summary>
    private static List<AssignTaskNotice> AssignTaskNotices(
        IReadOnlyList<ExecStep> steps, IReadOnlyList<DecisionCommitStep> executed, DecisionCoreRecord? core)
    {
        var notices = new List<AssignTaskNotice>();
        foreach (var step in steps.Where(s => s.Def.Code == AssignTaskNotifier.ActionCode))
        {
            if (executed.FirstOrDefault(e => e.Key == step.Key) is not { } done || done.Status != nameof(DecisionActionStatus.Done))
                continue;
            if (AssignTaskNotice.From(step.Values, done.Written, core) is { } notice)
                notices.Add(notice);
        }

        return notices;
    }

    /// <summary>The wizard's values as the executors take them. Unknown codes and non-scalar values are refused; a required
    /// parameter must be present.</summary>
    internal static (IReadOnlyDictionary<string, string?>? Values, string? Invalid) ReadValues(
        DecisionActionDefinition def, IReadOnlyDictionary<string, JsonElement>? given)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in given ?? new Dictionary<string, JsonElement>())
        {
            if (def.Parameters.All(p => p.Code != key))
                return (null, $"'{def.Label}' has no input '{key}'.");
            string? text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => "\u0000",
            };
            if (text == "\u0000")
                return (null, $"'{def.Label}': '{key}' must be a single value.");
            // Task 168 (D-194): a yes/no is true or false (or omitted, its default); anything else is refused, not guessed at.
            if (def.Parameters.First(p => p.Code == key).Kind == DecisionParameterKind.Boolean && !string.IsNullOrWhiteSpace(text))
            {
                if (!bool.TryParse(text.Trim(), out var flag))
                    return (null, $"'{def.Label}': '{key}' must be true or false.");
                text = flag ? "true" : "false";
            }

            values[key] = text;
        }

        foreach (var parameter in def.Parameters.Where(p => p.Required))
        {
            if (!values.TryGetValue(parameter.Code, out var v) || string.IsNullOrWhiteSpace(v))
                return (null, $"'{def.Label}' needs '{parameter.Label}'.");
        }

        return (values, null);
    }

    /// <summary>FR-51: the caller must be able to WRITE each Signal's subject and APPEND TO the core record, asked as the caller
    /// through the caller-rights probe. Null when they may; Forbidden when a right is missing (every "could not answer" from the
    /// probe is a no); Unavailable when a table's entity set cannot be looked up.</summary>
    private async Task<DecisionCommitResult?> CallerMayDecideAsync(
        HttpContext http, DecisionCoreRecord? decisionCore, IEnumerable<SignalRow> resolved, CancellationToken ct)
    {
        var sets = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (decisionCore is { } core)
        {
            if (await CallerHoldsOnAsync(http, sets, core.Entity, core.Id, DecisionExec.AppendToOperation, ct).ConfigureAwait(false) is not { } mayAppend)
                return Unavailable(Guid.Empty);
            if (!mayAppend)
                return Forbidden("You do not have permission to record a decision on this matter.");
        }

        var checkedSubjects = new HashSet<(string, Guid)>();
        foreach (var row in resolved)
        {
            if (row.SubjectId is not { } subjectId || subjectId == Guid.Empty)
                continue; // nothing beyond the core record to act on
            // Task 141 review F3: a pair id whose subject table cannot be derived (no subject lookup, or two, carry it; for
            // example the subject was deleted and its RemoveLink cascade cleared the lookup) is an unknown subject, never "no
            // subject": the caller's Write on it cannot be shown, so the decision is refused like an unreadable subject.
            if (row.SubjectEntity is not { Length: > 0 } subject)
                return new DecisionCommitResult
                {
                    Status = DecisionCommitStatus.Forbidden, // K2: the refusal is unchanged; only its code and message are specific
                    ReasonCode = DecisionCommitReasons.SignalSubjectMissing,
                    Detail = "This Signal's subject no longer exists.",
                };
            if (!checkedSubjects.Add((subject, subjectId)))
                continue;
            if (await CallerHoldsOnAsync(http, sets, subject, subjectId, WriteOperation, ct).ConfigureAwait(false) is not { } mayWrite)
                return Unavailable(Guid.Empty);
            if (!mayWrite)
                return Forbidden("You do not have permission to change an item in this decision.");
        }

        return null;

        static DecisionCommitResult Forbidden(string detail) =>
            new() { Status = DecisionCommitStatus.Forbidden, ReasonCode = DecisionCommitReasons.NotAuthorized, Detail = detail };
    }

    /// <summary>True/false: the caller holds / lacks <paramref name="operation"/> on the record. Null: the table's entity set
    /// could not be looked up (a metadata fault, not an answer about the caller). Set names are memoised per commit.</summary>
    private async Task<bool?> CallerHoldsOnAsync(
        HttpContext http, Dictionary<string, string?> sets, string table, Guid id, string operation, CancellationToken ct)
    {
        if (!sets.TryGetValue(table, out var set))
        {
            try
            {
                set = await _entities.GetEntitySetNameAsync(table, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Decision commit: the entity set of {Table} could not be looked up ({ExceptionType}).", table, ex.GetType().Name);
                return null;
            }

            sets[table] = set;
        }

        return !string.IsNullOrEmpty(set) && await DecisionExec.CallerHoldsAsync(_probe, http, set, id, operation, ct).ConfigureAwait(false);
    }

    // ── Reads ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<SignalRow?> ReadSignalAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var e = await _writer.RetrieveAsync("sprk_signal", id, SignalColumns, ct).ConfigureAwait(false);
            return ToRow(id, e);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision commit: Signal {SignalId} could not be read app-only ({ExceptionType}).", id, ex.GetType().Name);
            return null;
        }
    }

    internal static SignalRow ToRow(Guid id, Entity e)
    {
        var laneValue = e.GetAttributeValue<OptionSetValue>("sprk_lane")?.Value;
        DecisionLane? lane = laneValue is { } l && Enum.IsDefined(typeof(DecisionLane), l) ? (DecisionLane)l : null;
        var coreType = e.GetAttributeValue<EntityReference>("sprk_corerecordtype")?.Id;
        var coreText = e.GetAttributeValue<string>("sprk_corerecordid");
        Guid? coreId = Guid.TryParse(coreText, out var c) && c != Guid.Empty ? c : null;
        var malformed = (coreType is null) != (coreId is null) || (!string.IsNullOrWhiteSpace(coreText) && coreId is null);
        var subjectText = e.GetAttributeValue<string>("sprk_regardingrecordid");
        Guid? subjectId = Guid.TryParse(subjectText, out var s) && s != Guid.Empty ? s : null;
        var version = e.RowVersion
            ?? (e.Contains("versionnumber") ? Convert.ToString(e["versionnumber"], CultureInfo.InvariantCulture) : null);
        DateTime? resolvedOn = e.Contains("sprk_resolvedon") ? e.GetAttributeValue<DateTime>("sprk_resolvedon") : null;

        return new SignalRow(
            id,
            lane,
            e.GetAttributeValue<EntityReference>("sprk_policy")?.Id,
            e.GetAttributeValue<EntityReference>("sprk_policyversion")?.Id,
            coreId is null ? null : coreType,
            coreType is null ? null : coreId,
            malformed,
            e.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value,
            e.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value,
            resolvedOn,
            subjectId is { } pairId ? SignalWriter.SubjectEntityOf(pairId, lookup => e.GetAttributeValue<EntityReference>(lookup)?.Id) : null,
            subjectId,
            e.GetAttributeValue<string>("sprk_factsnapshot"),
            e.GetAttributeValue<bool>("sprk_privilegeflagged"),
            e.GetAttributeValue<string>("sprk_dedupekey"),
            e.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id,
            version);
    }

    /// <summary>The core record's table from the record-type catalog (configuration, read app-only as task 036 does); it must
    /// be one of uac-r2's core types (<see cref="CoreAncestorResolver.CoreRecordEntities"/>).</summary>
    /// <returns>The table, or null with <c>Fault</c> false when the catalog row is missing or not a core type (422), or null with
    /// <c>Fault</c> true when the read faulted (503; F4-b, uac-r2 on #1665).</returns>
    private async Task<(string? Table, bool Fault)> CoreTableAsync(Guid recordTypeRefId, CancellationToken ct)
    {
        try
        {
            var row = await _entities.RetrieveAsync("sprk_recordtype_ref", recordTypeRefId, ["sprk_recordlogicalname"], ct).ConfigureAwait(false);
            var table = row.GetAttributeValue<string>("sprk_recordlogicalname")?.Trim().ToLowerInvariant();
            return (table is not null && CoreAncestorResolver.CoreRecordEntities.Contains(table, StringComparer.Ordinal) ? table : null, false);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return (null, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision commit: the core record type could not be read ({ExceptionType}).", ex.GetType().Name);
            return (null, true);
        }
    }

    /// <summary>Fixed text for a no-core item whose Decision Record owner cannot be resolved (nothing about the row or the resolver's
    /// reason is echoed: F4-b).</summary>
    private const string ItemOwnerUnsupportedDetail = "The item's decision cannot be filed under an owning team, so it cannot be recorded; nothing was done.";

    /// <summary>A no-core Signal's subject record (the subject lookup that carries the pair id <c>sprk_regardingrecordid</c>, task 141), server-written;
    /// null when it names none. D-39 / D-129: the Decision Record's owner is uac-r2's answer for THIS record as the parent, resolved
    /// by <see cref="DecisionRecordWriter.ResolveOwnerAsync"/> only after FR-51 (F4-a).</summary>
    private static DecisionRecordRef? SubjectOf(SignalRow signal) =>
        signal.SubjectEntity is { Length: > 0 } entity && signal.SubjectId is { } subjectId ? new DecisionRecordRef(entity, subjectId) : null;

    /// <summary>uac-r2 F1: the subject of every Signal the record resolves, as parents of the Decision Record's owner question
    /// (secure-if-any). A Signal with no pair id names no subject and adds none. A Signal whose pair id is set but whose subject
    /// table cannot be derived (no subject lookup, or two, carry the pair id: task 141 review F3) is an UNKNOWN parent, never
    /// dropped: dropping it could leave a secure parent out of the answer. It refuses the record, as an unreadable parent does.</summary>
    /// <exception cref="DecisionRecordRefusedException">A Signal's subject is unknown (<see cref="DecisionRecordRefusalReason.OwnerRefused"/>).</exception>
    internal static IReadOnlyList<DecisionRecordRef> SignalSubjectsOf(IEnumerable<SignalRow> rows)
    {
        var subjects = new List<DecisionRecordRef>();
        foreach (var row in rows)
        {
            if (row.SubjectId is not { } subjectId || subjectId == Guid.Empty)
                continue;
            if (SubjectOf(row) is not { } subject)
            {
                throw new DecisionRecordRefusedException(
                    DecisionRecordRefusalReason.OwnerRefused,
                    "A Signal the decision resolves names a subject whose table cannot be derived, so the Decision Record owner is unknown; nothing was written.",
                    RecordOwnerRefusal.ParentUndetermined);
            }

            if (!subjects.Contains(subject))
                subjects.Add(subject);
        }

        return subjects;
    }

    /// <summary>D-129: the deciding caller's linked internal contact, through the identity service task 141 feeds (the user's
    /// <c>sprk_primarycontact</c> link, else the contact bound to their Entra oid). Null, with a warning, when the user genuinely has
    /// none: the Decision Record's <c>sprk_assignedtointernal</c> then stays empty and the commit goes on. <c>Unreadable</c> is true
    /// when the read FAULTED (<see cref="PersonIdentity.ContactUnreadable"/>, or the service threw): the answer is unknown, so the
    /// caller must not write the append-only record without a decider.</summary>
    private async Task<(Guid? Contact, bool Unreadable)> DeciderContactAsync(Guid systemUserId, CancellationToken ct)
    {
        try
        {
            var identity = await _identity.ResolveAsync(systemUserId, ct).ConfigureAwait(false);
            if (identity.ContactId is { } contactId && contactId != Guid.Empty)
                return (contactId, false);

            if (identity.ContactUnreadable)
            {
                _logger.LogWarning("Decision commit: the deciding user's linked contact could not be read (identity read faults: {Faults}); refusing before any write.",
                    identity.Faults);
                return (null, true);
            }

            _logger.LogWarning("Decision commit: the deciding user has no linked internal contact; the Decision Record's Assigned To Internal is left empty.");
            return (null, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision commit: the deciding user's linked contact could not be read ({ExceptionType}); refusing before any write.",
                ex.GetType().Name);
            return (null, true);
        }
    }

    /// <summary>D-134: a personal To Do's Assigned To user, through <see cref="PersonalToDoAssignee"/> (null: no single enabled user).
    /// <c>Unreadable</c> is true when a read FAULTED: whether a user exists is unknown, so the caller must not fall back.</summary>
    private async Task<(Guid? User, bool Unreadable)> PersonalToDoAssigneeAsync(Guid todoId, CancellationToken ct)
    {
        try
        {
            return (await PersonalToDoAssignee.ResolveAsync(_entities, _links, todoId, ct).ConfigureAwait(false), false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision commit: the personal To Do's Assigned To user could not be read ({ExceptionType}).", ex.GetType().Name);
            return (null, true);
        }
    }

    /// <summary>A by-id read that failed because the row does not exist (<c>0x80040217</c> anywhere in the chain).</summary>
    private static bool IsNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (RecordContainerResolver.IsRecordNotFound(e))
                return true;
        }

        return false;
    }

    /// <summary>The record this caller already wrote for this review id, if any (<c>sprk_steps</c> carries the review id; created by the
    /// ontology writer itself, not by a person), with what it stored about the review: its content hash, its Signals and what it did (F1).</summary>
    private async Task<StoredRecord?> FindRecordAsync(Guid reviewId, Guid confirmedBy, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_decisionrecord")
        {
            ColumnSet = new ColumnSet("sprk_decisionrecordid", "sprk_steps", "sprk_followons", "sprk_reason"),
            TopCount = 1,
        };
        query.Criteria.AddCondition("sprk_confirmedby", ConditionOperator.Equal, confirmedBy);
        query.Criteria.AddCondition("sprk_steps", ConditionOperator.Like, $"%\"reviewId\":\"{reviewId:D}\"%");
        // Task 153 (uac-r2 re-check F1): and only a record the ontology writer itself created. Console User and Ontology Administrator have Deep
        // Create on sprk_decisionrecord, and sprk_confirmedby / sprk_steps are not field-secured, so a person can write a row carrying their own
        // confirmer, this review id, the (predictable) review hash and the Signals; without this condition it would be taken as "already written"
        // and the finishing path would close the Signals with the forged resolution, running no executor. A WhoAmI fault throws here and lands in
        // the caller's catch (Unavailable, fail closed), never as "no record found".
        query.Criteria.AddCondition(DecisionRecordWriter.CreatedByColumn, ConditionOperator.Equal, await _writer.GetWriterUserIdAsync(ct).ConfigureAwait(false));
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return rows.Entities.FirstOrDefault() is { } row && row.Id != Guid.Empty ? StoredRecord.From(row) : null;
    }

    /// <summary>What a written Decision Record says about its review (read back from <c>sprk_steps</c> and
    /// <c>sprk_followons</c>). A record that cannot be read this way proves nothing, so it never answers a repeat.</summary>
    private sealed record StoredRecord(
        Guid Id, string? ReviewHash, IReadOnlyList<Guid>? ResolvedSignalIds, IReadOnlyList<string> TakenCodes, int FollowOnCount, string? Reason)
    {
        /// <summary>F1: the record is THIS review's: same content hash, and exactly the request's Signals.</summary>
        public bool Covers(IReadOnlyList<Guid> ids, string hash) =>
            ReviewHash is { Length: > 0 } stored && string.Equals(stored, hash, StringComparison.Ordinal)
            && ResolvedSignalIds is { } covered && covered.Count == ids.Count && covered.ToHashSet().SetEquals(ids);

        /// <summary>The resolution the record gives its Signals, from what it recorded (never from a request body).</summary>
        public (Resolution Resolution, string? DismissalText) RecordedResolution()
        {
            if (TakenCodes.Count == 0 && FollowOnCount == 0)
                return (Resolution.Dismissed, Reason);
            // Task 153 (uac-r2 re-check K2): the same rule the closure retry uses to recognise this record as one that left the item open.
            return (DecisionRecordWriter.LeavesSignalOpen(TakenCodes, FollowOnCount)
                ? Resolution.InquirySent
                : Resolution.Acted, null);
        }

        public static StoredRecord From(Entity row)
        {
            string? hash = null;
            List<Guid>? signals = null;
            var taken = new List<string>();
            var followOns = 0;
            try
            {
                using var steps = JsonDocument.Parse(row.GetAttributeValue<string>("sprk_steps") ?? "{}");
                var root = steps.RootElement;
                if (root.TryGetProperty("reviewHash", out var h) && h.ValueKind == JsonValueKind.String)
                    hash = h.GetString();
                if (root.TryGetProperty("resolvedSignalIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
                {
                    signals = [];
                    foreach (var id in ids.EnumerateArray())
                    {
                        if (id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var g))
                        {
                            signals = null;
                            break;
                        }

                        signals.Add(g);
                    }
                }

                if (root.TryGetProperty("steps", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var step in list.EnumerateArray())
                    {
                        if (step.TryGetProperty("taken", out var t) && t.ValueKind == JsonValueKind.True
                            && step.TryGetProperty("code", out var c) && c.GetString() is { } code)
                            taken.Add(code);
                    }
                }

                using var next = JsonDocument.Parse(row.GetAttributeValue<string>("sprk_followons") ?? "[]");
                followOns = next.RootElement.ValueKind == JsonValueKind.Array ? next.RootElement.GetArrayLength() : 0;
            }
            catch (JsonException)
            {
                return new StoredRecord(row.Id, null, null, [], 0, null);
            }

            return new StoredRecord(row.Id, hash, signals, taken, followOns, row.GetAttributeValue<string>("sprk_reason"));
        }
    }

    // ── Steps and the record ─────────────────────────────────────────────────────────────────────────────────────────

    private static DecisionActionRequest Request(HttpContext http, Prepared p, ExecStep step) =>
        new(http,
            p.Primary.Id,
            p.Primary.SubjectEntity is { Length: > 0 } entity && p.Primary.SubjectId is { } id ? new DecisionRecordRef(entity, id) : null,
            p.Core,
            p.MatterId,
            step.Values);

    private static DecisionCommitStep ToStep(ExecStep step, DecisionActionOutcome outcome) =>
        new(step.Key,
            step.Def.Code,
            step.Phase,
            outcome.Status.ToString(),
            outcome.Written.Select(w => new DecisionCommitRef(w.Entity, w.Id)).ToList(),
            outcome.PossiblyWritten.Select(w => new DecisionCommitRef(w.Entity, w.Id)).ToList(),
            outcome.ReasonCode,
            outcome.Detail);

    private static IReadOnlyList<DecisionRecordStep> ProjectedSteps(Prepared p) =>
        p.Offered.Select(o => new DecisionRecordStep(
                o.Def.Code, o.Taken, o.Taken ? AsObjects(o.Values) : null, o.Taken ? "Done" : null,
                o.Taken ? [new DecisionRecordRef("projected", Guid.NewGuid()), new DecisionRecordRef("projected", Guid.NewGuid())] : null))
            .ToList();

    private static IReadOnlyList<DecisionRecordFollowOn> ProjectedFollowOns(Prepared p) =>
        p.Steps.Where(s => s.IsNextStep).Select(s => new DecisionRecordFollowOn(s.Def.Code, "projected", Guid.NewGuid())).ToList();

    private static IReadOnlyList<DecisionRecordStep> RecordSteps(Prepared p, IReadOnlyList<DecisionCommitStep> executed) =>
        p.Offered.Select(o =>
            {
                if (!o.Taken)
                    return new DecisionRecordStep(o.Def.Code, false, null, null);
                var done = executed.First(e => e.Key == $"action:{o.Def.Code}");
                return new DecisionRecordStep(o.Def.Code, true, AsObjects(o.Values), done.Status,
                    done.Written.Select(w => new DecisionRecordRef(w.Entity, w.Id)).ToList());
            })
            .ToList();

    /// <summary>Each Next step's created row. A step that created none it can name (a send whose core tracked no id) is listed
    /// as an unnamed Next step in the steps envelope instead (see <see cref="RecordRequest"/>).</summary>
    private static IReadOnlyList<DecisionRecordFollowOn> RecordFollowOns(Prepared p, IReadOnlyList<DecisionCommitStep> executed) =>
        executed.Where(e => e.Key.StartsWith("next-step:", StringComparison.Ordinal))
            .SelectMany(e => e.Written.Take(1).Select(w => new DecisionRecordFollowOn(e.Code, w.Entity, w.Id)))
            .ToList();

    private static Dictionary<string, object?> AsObjects(IReadOnlyDictionary<string, string?> values) =>
        values.ToDictionary(v => v.Key, v => (object?)v.Value, StringComparer.Ordinal);

    private static DecisionRecordRequest RecordRequest(
        DecisionReview review, Prepared p, Guid confirmedBy, DateTimeOffset now,
        IReadOnlyList<DecisionRecordStep> steps, IReadOnlyList<DecisionRecordFollowOn> followOns, IReadOnlyList<DecisionCommitStep>? executed,
        string reviewHash, Guid? deciderContact, Guid? assignee = null) =>
        new(review.ReviewId,
            p.Primary.PolicyVersionId!.Value,
            p.RecordCore,
            // Task 142 (contract C1): the record is filed under the primary Signal's subject (the core record when it names none).
            SubjectOf(p.Primary),
            confirmedBy,
            now,
            steps,
            followOns,
            p.Resolution == Resolution.Dismissed ? null : p.GateTier,
            p.Resolved.Select(r => r.Id).ToList(),
            FactValues(p.Resolved),
            DeciderRole: null,
            Because: string.IsNullOrWhiteSpace(review.Because) ? null : review.Because.Trim(),
            Reason: p.DismissalText,
            PrivilegeFlagged: p.Resolved.Any(r => r.PrivilegeFlagged),
            // A Next step that ran but named no row (a notification whose communication row was not tracked) is still something
            // done: listed in sprk_steps, so the record never reads as a dismissal after an email went out.
            UnnamedNextSteps: (executed ?? [])
                .Where(e => e.Key.StartsWith("next-step:", StringComparison.Ordinal) && e.Written.Count == 0)
                .Select(e => e.Code)
                .ToList(),
            // F1: the record carries the review's content hash, so a repeat found by its review id can prove it is the same review.
            ReviewHash: reviewHash,
            DeciderContactId: deciderContact,
            // uac-r2 F1: the record's owner is asked about EVERY Signal it resolves (primary and also-resolve), not just the core, so a
            // Signal whose subject sits under a secure record makes the record secure-owned even under an ordinary matter.
            // Defence in depth: FR-51 (CallerMayDecideAsync, always before this) already refuses the same rows for the same predicate
            // (pair id set, subject table underivable), so a commit never reaches the throw here; SignalSubjectsOf is tested directly.
            SignalSubjects: SignalSubjectsOf(p.Resolved),
            // D-134: a personal To Do's Assigned To user (no-core only; the writer applies it only when every parent is ordinary).
            AssigneeSystemUserId: assignee);

    /// <summary>What the data said at decision time: each covered Signal's own server-written fact snapshot, by Signal id.</summary>
    internal static IReadOnlyDictionary<string, object?> FactValues(IEnumerable<SignalRow> rows)
    {
        var facts = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            object? value = null;
            if (!string.IsNullOrWhiteSpace(row.FactSnapshot))
            {
                try
                {
                    value = JsonDocument.Parse(row.FactSnapshot).RootElement.Clone();
                }
                catch (JsonException)
                {
                    value = row.FactSnapshot;
                }
            }

            facts[row.Id.ToString("D", CultureInfo.InvariantCulture)] = value;
        }

        return facts;
    }

    // ── Failure results ─────────────────────────────────────────────────────────────────────────────────────────────

    private DecisionCommitResult Failure(Guid reviewId, ExecStep step, DecisionActionOutcome outcome, IReadOnlyList<DecisionCommitStep> executed, Prepared p)
    {
        var landed = executed.SelectMany(e => e.Written).ToList();
        var possibly = outcome.PossiblyWritten.Select(w => new DecisionCommitRef(w.Entity, w.Id)).ToList();

        // Only a step that was REFUSED (by contract: nothing written) can resume. A Failed step may have landed (a create that
        // timed out after committing, a message that went out, a revision whose amount was then refused), so running it again
        // could write it twice; a thrown step is Failed. The steps that landed in full before it are never repeated.
        var resumable = outcome.Status == DecisionActionStatus.Refused
                        && possibly.Count == 0
                        && outcome.Written.Count == 0;
        return new DecisionCommitResult
        {
            Status = DecisionCommitStatus.Failed,
            ReviewId = reviewId,
            ActionCode = step.Def.Code,
            ReasonCode = outcome.ReasonCode ?? DecisionCommitReasons.StepFailed,
            Detail = outcome.Detail ?? $"'{step.Def.Label}' did not complete. Nothing was recorded.",
            FailedStep = step.Phase,
            Steps = executed,
            Landed = landed,
            PossiblyLanded = possibly,
            GateTier = p.GateTier,
            Resumable = resumable,
        };
    }

    private DecisionCommitResult RecordFailed(Guid reviewId, DecisionRecordRefusedException ex, IReadOnlyList<DecisionCommitStep> executed, Prepared p)
    {
        var landed = executed.SelectMany(e => e.Written).ToList();
        var sent = executed.Any(e => p.Steps.Any(s => s.Key == e.Key && s.Def.SendsMessage) && e.Status == nameof(DecisionActionStatus.Done));
        var mayExist = ex.Reason == DecisionRecordRefusalReason.CreateFailed;

        if (sent)
        {
            // ESCALATION TRIGGER 1 (task 043): a message went out and the record was not written. Stop; do not compensate. The
            // first outcome is kept (a retry with this review id returns it and sends nothing); the owner decides the rest.
            _logger.LogCritical(DecisionCommitEvents.SentButNotRecorded, ex,
                "Decision commit: review {ReviewId} SENT a message but its Decision Record was not written ({Reason}). " +
                "Landed: {Landed}. Escalate to the owner; nothing is compensated.",
                reviewId, ex.Reason, string.Join(", ", landed.Select(l => $"{l.Entity}:{l.Id:D}")));
            OntologyWriterTelemetry.RecordFailure("decision_commit_sent_not_recorded");
        }
        else
        {
            _logger.LogError(DecisionCommitEvents.StepFailed, ex,
                "Decision commit: review {ReviewId}: the Decision Record was not written ({Reason}); {Landed} write(s) landed.",
                reviewId, ex.Reason, landed.Count);
            OntologyWriterTelemetry.RecordFailure("decision_commit_record_failed");
        }

        return new DecisionCommitResult
        {
            Status = DecisionCommitStatus.Failed,
            ReviewId = reviewId,
            ReasonCode = DecisionCommitReasons.RecordWriteFailed,
            Detail = sent
                ? "The message was sent but the decision could not be recorded. This has been escalated; do not send it again."
                : "The decision could not be recorded. Nothing was closed.",
            FailedStep = 5,
            Steps = executed,
            Landed = landed,
            RecordMayExist = mayExist,
            SentButNotRecorded = sent,
            GateTier = p.GateTier,
            // Without a send, a retry re-uses every landed step and only writes the record (finding a record a timed-out create
            // left). After a send, the owner decides (escalation trigger 1).
            Resumable = !sent,
        };
    }

    // ── Step 6 ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Closes (or links) one Signal with the record. Applied only to a Signal still open, or one the SYSTEM resolved since the
    /// review started (re-closed with this decision, so Signal and record agree). A Signal a person resolved is left alone.
    /// Row-version conditional; a concurrent change is re-read once.
    /// </summary>
    private async Task<DecisionCommitSignal> CloseSignalAsync(
        Guid signalId, Resolution resolution, string? dismissalText, Guid recordId, Guid confirmedBy, DateTimeOffset reviewStart, CancellationToken ct)
    {
        var state = resolution.ToString();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var row = await ReadSignalAsync(signalId, ct).ConfigureAwait(false);
            if (row is null)
                return Warn(signalId, "signal_unreadable", "The item could not be read to close it.");

            if (row.DecisionRecordId == recordId && (row.Status == SignalWriter.SignalStatusResolved || resolution == Resolution.InquirySent))
                return new DecisionCommitSignal(signalId, state, null); // already done (a resumed review)

            var open = row.IsOpen;
            var systemClosed = row.SystemClosedSince(reviewStart);
            if (!open && !(systemClosed && resolution != Resolution.InquirySent))
            {
                return Warn(signalId, row.Status == SignalWriter.SignalStatusResolved && !systemClosed ? "resolved_by_another_decision" : "closed_during_review",
                    "The item was closed by someone else while this decision was made; it was left as it is.");
            }

            if (row.RowVersion is not { Length: > 0 } version)
                return Warn(signalId, "signal_unversioned", "The item could not be closed safely.");

            var fields = new Dictionary<string, object> { ["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", recordId) };
            if (resolution == Resolution.InquirySent)
            {
                fields["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusAcknowledged);
            }
            else
            {
                fields["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
                fields["sprk_resolutiontype"] = new OptionSetValue(resolution == Resolution.Dismissed ? SignalWriter.ResolutionDismissed : SignalWriter.ResolutionActed);
                // D-120 (025): written with sprk_resolvedon in the SAME update and the SAME timestamp, as SignalWriter.CloseAsync
                // does (CloseAsync takes system resolutions only). A lastevaluated later than resolvedon would read as "the
                // condition was observed cleared" on a Threshold Signal, and the next crossing would re-raise.
                var resolvedOn = _clock.GetUtcNow().UtcDateTime;
                fields["sprk_resolvedon"] = resolvedOn;
                fields["sprk_lastevaluated"] = resolvedOn;
                fields["sprk_resolvedby"] = new EntityReference("systemuser", confirmedBy);
                if (dismissalText is { } reason)
                    fields["sprk_resolutionnotes"] = reason;
            }

            try
            {
                await _writer.UpdateIfRowVersionMatchesAsync("sprk_signal", signalId, version, fields, ct).ConfigureAwait(false);
                return new DecisionCommitSignal(signalId, state, systemClosed ? "reclosed_after_system_resolution" : null);
            }
            catch (Exception ex) when (IsVersionMismatch(ex) && attempt == 0)
            {
                continue; // changed since read: read again and decide again
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(DecisionCommitEvents.SignalCloseFailed, ex,
                    "Decision commit: Signal {SignalId} could not be closed with record {RecordId}.", signalId, recordId);
                OntologyWriterTelemetry.RecordFailure("decision_commit_signal_close_failed");
                return new DecisionCommitSignal(signalId, Unchanged, "signal_close_failed");
            }
        }

        return Warn(signalId, "signal_changed", "The item kept changing while it was being closed; it was left as it is.");

        DecisionCommitSignal Warn(Guid id, string code, string why)
        {
            _logger.LogWarning(DecisionCommitEvents.SignalCloseFailed,
                "Decision commit: Signal {SignalId} not closed with record {RecordId}: {Code}.", id, recordId, code);
            OntologyWriterTelemetry.RecordFailure("decision_commit_signal_close_failed");
            return new DecisionCommitSignal(id, Unchanged, code);
        }
    }

    private static bool IsVersionMismatch(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.ServiceModel.FaultException<OrganizationServiceFault> fault && fault.Detail?.ErrorCode == ConcurrencyVersionMismatch)
                return true;
            if (e.Message?.Contains("0x80060882", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        return false;
    }

    // ── Ledger and helpers ──────────────────────────────────────────────────────────────────────────────────────────

    private async Task<bool> TrySetLedgerAsync(string tenantId, string key, LedgerEntry entry, TimeSpan ttl, CancellationToken ct)
    {
        try
        {
            await _ledger.SetAsync(tenantId, LedgerResource, key, LedgerVersion, entry, ttl, ct: ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Decision commit: the review ledger could not be written.");
            return false;
        }
    }

    private async Task TryRemoveLedgerAsync(string tenantId, string key)
    {
        try
        {
            await _ledger.RemoveAsync(tenantId, LedgerResource, key, LedgerVersion, ct: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The marker expires on its own (InProgressTtl); until then a repeat is told the review is in progress.
            _logger.LogWarning(ex, "Decision commit: the in-progress marker could not be removed.");
        }
    }

    /// <summary>A stable hash of what the review asks for (not the retry flag or the plan-read time), so a retry can be checked
    /// to be the same review.</summary>
    internal static string ContentHash(Guid signalId, DecisionReview review)
    {
        var canonical = new
        {
            signalId,
            also = (review.AlsoResolve ?? []).OrderBy(g => g).ToArray(),
            actions = (review.Actions ?? []).OrderBy(a => a?.Code, StringComparer.Ordinal)
                .Select(a => new { a?.Code, a?.Taken, values = Canonical(a?.Values) }).ToArray(),
            next = (review.NextSteps ?? []).Select(n => new { n?.Code, values = Canonical(n?.Values) }).ToArray(),
            dismissal = review.Dismissal is null ? null : new { review.Dismissal.Code, review.Dismissal.Detail },
            review.Because,
        };
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, HashJson)));
        return Convert.ToHexString(bytes);

        static object? Canonical(IReadOnlyDictionary<string, JsonElement>? values) =>
            values?.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => new[] { v.Key, v.Value.GetRawText() }).ToArray();
    }

    private static DecisionCommitResult Refused(Guid reviewId, string reason, string detail, string? action = null) =>
        new() { Status = DecisionCommitStatus.Refused, ReviewId = reviewId, ReasonCode = reason, Detail = detail, ActionCode = action };

    private static DecisionCommitResult Unavailable(Guid reviewId, string? reason = null, string? detail = null) =>
        new()
        {
            Status = DecisionCommitStatus.Unavailable,
            ReviewId = reviewId,
            ReasonCode = reason ?? DecisionCommitReasons.IdempotencyUnavailable,
            Detail = detail ?? "The decision could not be recorded right now; nothing was done. Try again shortly.",
        };
}
