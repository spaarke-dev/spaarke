using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
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
/// record and the item owner are derived on the server from the Signals (uac-r2 condition on #1600), never read from here.</summary>
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

    /// <summary>When the wizard read the plan. Only used to tell a system closure DURING the review (tolerated, step 6) from
    /// one before it; bounded to the last 24 hours and never later than now.</summary>
    public DateTimeOffset? PlanReadOn { get; init; }

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
/// <see cref="CoreAncestorResolver"/>), its table from the record-type catalog; the no-core item owner is read app-only from the
/// item row. Nothing in the request body can name either.</para>
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
    internal static readonly TimeSpan PlanReadWindow = TimeSpan.FromHours(24);

    private const int ReasonMaxLength = 2000;
    private const int MaxNextSteps = 10;
    private const int MaxAlsoResolve = 25;
    private const string WriteOperation = "write";
    private const int ConcurrencyVersionMismatch = unchecked((int)0x80060882);

    private static readonly string[] SignalColumns =
    [
        "sprk_lane", "sprk_policy", "sprk_policyversion", "sprk_corerecordtype", "sprk_corerecordid", "sprk_signalstatus",
        "sprk_resolutiontype", "sprk_resolvedon", "sprk_regardingrecordtype", "sprk_regardingrecordid", "sprk_matter",
        "sprk_factsnapshot", "sprk_privilegeflagged", "sprk_dedupekey", "sprk_decisionrecord", "versionnumber",
    ];

    private static readonly JsonSerializerOptions HashJson = new() { WriteIndented = false };

    private readonly SignalCoreRecordAccess _access;
    private readonly DecisionPlanService _plans;
    private readonly DecisionActionExecutors _executors;
    private readonly DecisionRecordWriter _records;
    private readonly OntologyWriterDataverseClient _writer;
    private readonly IGenericEntityService _entities;
    private readonly CallerRecordAccessProbe _probe;
    private readonly ITenantCache _ledger;
    private readonly DecisionCommitFollowUps _followUps;
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
        ITenantCache ledger,
        DecisionCommitFollowUps followUps,
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
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _followUps = followUps ?? throw new ArgumentNullException(nameof(followUps));
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
        Guid? MatterId,
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
        Guid? ItemOwnerUserId,
        string? GateTier,
        string? DismissalText,
        Resolution Resolution);

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
            return Refused(review.ReviewId, DecisionCommitReasons.AlsoResolveNotAllowed, $"A decision can resolve at most {MaxAlsoResolve} other items.");
        if (also.Any(id => id == Guid.Empty || id == signalId) || also.Distinct().Count() != also.Count)
            return Refused(review.ReviewId, DecisionCommitReasons.AlsoResolveNotAllowed, "Also resolve names the item itself, a blank id or the same item twice.");

        var ids = new List<Guid> { signalId };
        ids.AddRange(also);
        var lanes = new Dictionary<Guid, SignalAccessDecision>();
        foreach (var id in ids)
        {
            var decision = await _access.AuthorizeAsync(id, ct).ConfigureAwait(false);
            if (decision.Outcome == SignalAccessOutcome.CallerUnresolved)
                return new DecisionCommitResult { Status = DecisionCommitStatus.CallerUnresolved, ReviewId = review.ReviewId };
            if (decision.Outcome != SignalAccessOutcome.Allowed)
                return new DecisionCommitResult { Status = DecisionCommitStatus.NotFound, ReviewId = review.ReviewId };
            lanes[id] = decision;
        }

        var hash = ContentHash(signalId, review);
        var ledgerKey = $"{callerObjectId}:{review.ReviewId:D}";

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

        DecisionCommitResult? resume = null;
        if (prior is { InProgress: true })
        {
            return new DecisionCommitResult
            {
                Status = DecisionCommitStatus.InProgress,
                ReviewId = review.ReviewId,
                ReasonCode = DecisionCommitReasons.ReviewInProgress,
                Detail = "This decision is already being recorded.",
            };
        }

        if (prior is { Outcome: { } first })
        {
            if (!review.Retry || first.Status != DecisionCommitStatus.Failed)
                return first with { Replayed = true };
            if (!first.Resumable)
                return first with { Replayed = true, ReasonCode = DecisionCommitReasons.NotResumable };
            if (!string.Equals(prior.ContentHash, hash, StringComparison.Ordinal))
                return Refused(review.ReviewId, DecisionCommitReasons.ReviewChanged, "A retry must repeat the review it retries.");
            resume = first;
        }

        // The review started when the wizard read the plan (bounded); a resume keeps the first attempt's start.
        var reviewStart = prior?.ReviewStart ?? BoundPlanReadOn(review.PlanReadOn, now);

        if (!review.Confirmed)
            return Refused(review.ReviewId, DecisionCommitReasons.NotConfirmed, "The decision was not confirmed; nothing was done.");

        // Mark the review in progress BEFORE any read that could take time, so a double-click meets the marker. A resume keeps
        // the failure it resumes inside the marker: if this attempt ends without a write outcome, that failure is put back.
        var marked = await TrySetLedgerAsync(tenantId, ledgerKey, new LedgerEntry(true, reviewStart, hash, resume), InProgressTtl, ct).ConfigureAwait(false);
        if (!marked)
            return Unavailable(review.ReviewId);

        DecisionCommitResult result;
        var progress = new CommitProgress();
        try
        {
            result = await CommitCoreAsync(ids, lanes, review, http, now, reviewStart, resume, progress, ct).ConfigureAwait(false);
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
    }

    private async Task<DecisionCommitResult> CommitCoreAsync(
        IReadOnlyList<Guid> ids, IReadOnlyDictionary<Guid, SignalAccessDecision> lanes, DecisionReview review, HttpContext http,
        DateTimeOffset now, DateTimeOffset reviewStart, DecisionCommitResult? resume, CommitProgress progress, CancellationToken ct)
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
        Guid? existingRecord;
        try
        {
            existingRecord = await FindRecordAsync(reviewId, confirmedBy, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Decision commit: could not check whether review {ReviewId} was already recorded; refusing (fail closed).", reviewId);
            return Unavailable(reviewId);
        }

        if (existingRecord is { } recorded && resume is null)
        {
            return new DecisionCommitResult
            {
                Status = DecisionCommitStatus.Recorded,
                ReviewId = reviewId,
                DecisionRecordId = recorded,
                Replayed = true,
                Detail = "This decision was already recorded.",
            };
        }

        // ---- (1c) The Signals, app-only: their server-written columns -------------------------------------------------
        var rows = new List<SignalRow>();
        foreach (var id in ids)
        {
            var row = await ReadSignalAsync(id, ct).ConfigureAwait(false);
            if (row is null)
                return Unavailable(reviewId, DecisionCommitReasons.SignalUnreadable, "An item in this decision could not be read; nothing was done.");
            rows.Add(row);
        }

        var prepared = await PrepareAsync(review, rows, lanes, reviewStart, ct).ConfigureAwait(false);
        if (prepared.Refusal is { } refusal)
            return refusal with { ReviewId = reviewId };
        var p = prepared.Ready!;

        // ---- (1c') FR-51: as the caller, Write on every Signal's subject and AppendTo on its core record. A decision acts on the
        // item and files a record under the matter, even when it only dismisses or approves (record-only actions write nothing
        // of their own, so no executor preflight would ask). Fails closed.
        if (await CallerMayDecideAsync(http, p, ct).ConfigureAwait(false) is { } denied)
            return denied with { ReviewId = reviewId };

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
            _records.Preflight(RecordRequest(review, p, confirmedBy, now, ProjectedSteps(p), ProjectedFollowOns(p), executed: null));
        }
        catch (DecisionRecordRefusedException ex)
        {
            return Refused(reviewId, DecisionCommitReasons.RecordRefused, $"This decision cannot be recorded ({ex.Reason}); nothing was done.");
        }

        // ---- (2)(3)(4) The writes, in D-17 order ----------------------------------------------------------------------------
        // From the first write on, the request's cancellation no longer applies: a client that disconnects after a send must
        // still get its record written (or its failure stored), never a half-done commit with no outcome.
        var writeCt = CancellationToken.None;
        progress.Writing = true;
        var executed = new List<DecisionCommitStep>();
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
                await _followUps.AfterFailedCommitAsync(p.Primary.MatterId, executed, writeCt).ConfigureAwait(false);
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
            recordId = found;
            recordClass = "Existing";
        }
        else
        {
            try
            {
                var written = await _records.WriteAsync(RecordRequest(review, p, confirmedBy, now, steps, followOns, executed), writeCt).ConfigureAwait(false);
                recordId = written.RecordId;
                recordClass = written.RecordClass.ToString();
            }
            catch (DecisionRecordRefusedException ex)
            {
                return RecordFailed(reviewId, ex, executed, p);
            }
            catch (Exception ex)
            {
                // The writer wraps every fault but a cancellation; with no request token here, that is a client-side timeout, so
                // the create may have committed.
                return RecordFailed(reviewId, new DecisionRecordRefusedException(DecisionRecordRefusalReason.CreateFailed,
                    "The Decision Record create did not answer; it may have been written.", innerException: ex), executed, p);
            }
        }

        // ---- (6) Close (or, for an inquiry only, link) every Signal the decision covers -------------------------------------
        var signals = new List<DecisionCommitSignal>();
        var warnings = new List<string>();
        foreach (var row in p.Resolved)
        {
            var closed = await CloseSignalAsync(row.Id, p, recordId, confirmedBy, reviewStart, writeCt).ConfigureAwait(false);
            signals.Add(closed);
            if (closed.Warning is { } warning)
                warnings.Add(warning);
        }

        warnings.AddRange(await _followUps.AfterRecordedAsync(p.Primary.MatterId, executed, p.Resolution == Resolution.Dismissed ? p.Resolved : [], writeCt).ConfigureAwait(false));

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

    private async Task<(DecisionCommitResult? Refusal, Prepared? Ready)> PrepareAsync(
        DecisionReview review, IReadOnlyList<SignalRow> rows, IReadOnlyDictionary<Guid, SignalAccessDecision> access,
        DateTimeOffset reviewStart, CancellationToken ct)
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
            if (!row.IsOpen && !row.SystemClosedSince(reviewStart))
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

        // Every offered action exactly once, nothing else. An unknown code is refused, never defaulted (C-3).
        var given = review.Actions ?? [];
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
            var detail = dismissal.Detail?.Trim();
            if (reason.RequiresDetail && string.IsNullOrEmpty(detail))
                return No(DecisionCommitReasons.DismissalDetailRequired, $"'{reason.Label}' needs a short explanation.");
            dismissalText = $"{reason.Code} - {(string.IsNullOrEmpty(detail) ? reason.Label : detail)}";
            if (dismissalText.Length > ReasonMaxLength)
                dismissalText = dismissalText[..ReasonMaxLength];
            outcome = Resolution.Dismissed;
        }
        else
        {
            if (review.Dismissal is not null)
                return No(DecisionCommitReasons.DismissalWithActions, "A decision that takes an action or creates a Next step is not a dismissal.");

            // D-119: only an action that resolves (not the inquiry, an intermediate step) closes the item; Next steps alone don't.
            outcome = taken.Any(t => t.ResolvesSignal) ? Resolution.Acted : Resolution.InquirySent;
        }

        // ---- Server-derived core record and item owner (uac-r2 binding condition) ------------------------------------------
        DecisionCoreRecord? core = null;
        DecisionRecordCore? recordCore = null;
        Guid? itemOwner = null;
        if (primary.HasCore)
        {
            var table = await CoreTableAsync(primary.CoreTypeId!.Value, ct).ConfigureAwait(false);
            if (table is null)
                return No(DecisionCommitReasons.CoreUnsupported, "The item's matter or project could not be resolved; nothing was done.");
            core = new DecisionCoreRecord(table, primary.CoreId!.Value, primary.CoreTypeId);
            recordCore = new DecisionRecordCore(table, primary.CoreId!.Value, primary.CoreTypeId!.Value);
        }
        else
        {
            itemOwner = await ItemOwnerAsync(primary, ct).ConfigureAwait(false);
            if (itemOwner is null)
                return No(DecisionCommitReasons.ItemOwnerUnsupported, "The item's owner could not be resolved to a person, so its decision cannot be recorded.");
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
        return (null, new Prepared(primary, rows, lane, plan, offered, steps, core, recordCore, itemOwner, gateTier, dismissalText, outcome));
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
    private async Task<DecisionCommitResult?> CallerMayDecideAsync(HttpContext http, Prepared p, CancellationToken ct)
    {
        var sets = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (p.Core is { } core)
        {
            if (await CallerHoldsOnAsync(http, sets, core.Entity, core.Id, DecisionExec.AppendToOperation, ct).ConfigureAwait(false) is not { } mayAppend)
                return Unavailable(Guid.Empty);
            if (!mayAppend)
                return Forbidden("You do not have permission to record a decision on this matter.");
        }

        var checkedSubjects = new HashSet<(string, Guid)>();
        foreach (var row in p.Resolved)
        {
            if (row.SubjectEntity is not { Length: > 0 } subject || row.SubjectId is not { } subjectId || subjectId == Guid.Empty)
                continue; // nothing beyond the core record to act on
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
            e.GetAttributeValue<string>("sprk_regardingrecordtype")?.Trim().ToLowerInvariant(),
            subjectId,
            e.GetAttributeValue<EntityReference>("sprk_matter")?.Id,
            e.GetAttributeValue<string>("sprk_factsnapshot"),
            e.GetAttributeValue<bool>("sprk_privilegeflagged"),
            e.GetAttributeValue<string>("sprk_dedupekey"),
            e.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id,
            version);
    }

    /// <summary>The core record's table from the record-type catalog (configuration, read app-only as task 036 does); it must
    /// be one of uac-r2's core types (<see cref="CoreAncestorResolver.CoreRecordEntities"/>).</summary>
    private async Task<string?> CoreTableAsync(Guid recordTypeRefId, CancellationToken ct)
    {
        try
        {
            var row = await _entities.RetrieveAsync("sprk_recordtype_ref", recordTypeRefId, ["sprk_recordlogicalname"], ct).ConfigureAwait(false);
            var table = row.GetAttributeValue<string>("sprk_recordlogicalname")?.Trim().ToLowerInvariant();
            return table is not null && CoreAncestorResolver.CoreRecordEntities.Contains(table, StringComparer.Ordinal) ? table : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision commit: the core record type could not be read ({ExceptionType}).", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>D-39: a no-core item's record is owned by the item's owner, read app-only from the item row. A team owner is not
    /// supported by the record writer (it owns by systemuser), so it is refused before anything is written.</summary>
    private async Task<Guid?> ItemOwnerAsync(SignalRow signal, CancellationToken ct)
    {
        if (signal.SubjectEntity is not { Length: > 0 } entity || signal.SubjectId is not { } subjectId)
            return null;
        try
        {
            var row = await _entities.RetrieveAsync(entity, subjectId, ["ownerid"], ct).ConfigureAwait(false);
            return row.GetAttributeValue<EntityReference>("ownerid") is { LogicalName: "systemuser", Id: var owner } && owner != Guid.Empty
                ? owner
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision commit: the item's owner could not be read ({ExceptionType}).", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>The record this caller already wrote for this review id, if any (<c>sprk_steps</c> carries the review id).</summary>
    private async Task<Guid?> FindRecordAsync(Guid reviewId, Guid confirmedBy, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_decisionrecord")
        {
            ColumnSet = new ColumnSet("sprk_decisionrecordid"),
            TopCount = 1,
        };
        query.Criteria.AddCondition("sprk_confirmedby", ConditionOperator.Equal, confirmedBy);
        query.Criteria.AddCondition("sprk_steps", ConditionOperator.Like, $"%\"reviewId\":\"{reviewId:D}\"%");
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return rows.Entities.FirstOrDefault()?.Id is { } id && id != Guid.Empty ? id : null;
    }

    // ── Steps and the record ─────────────────────────────────────────────────────────────────────────────────────────

    private static DecisionActionRequest Request(HttpContext http, Prepared p, ExecStep step) =>
        new(http,
            p.Primary.Id,
            p.Primary.SubjectEntity is { Length: > 0 } entity && p.Primary.SubjectId is { } id ? new DecisionRecordRef(entity, id) : null,
            p.Core,
            p.Primary.MatterId,
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
        IReadOnlyList<DecisionRecordStep> steps, IReadOnlyList<DecisionRecordFollowOn> followOns, IReadOnlyList<DecisionCommitStep>? executed) =>
        new(review.ReviewId,
            p.Primary.PolicyVersionId!.Value,
            p.RecordCore,
            p.ItemOwnerUserId,
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
                .ToList());

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

        // A message that failed may still have gone out; resuming would send it again. A failed step that wrote part of its work
        // (a revision whose budget amount was then refused) would write it again on a resume. Anything else can resume: the steps
        // that landed in full are not repeated.
        var resumable = !(step.Def.SendsMessage && outcome.Status == DecisionActionStatus.Failed)
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
        Guid signalId, Prepared p, Guid recordId, Guid confirmedBy, DateTimeOffset reviewStart, CancellationToken ct)
    {
        var state = p.Resolution.ToString();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var row = await ReadSignalAsync(signalId, ct).ConfigureAwait(false);
            if (row is null)
                return Warn(signalId, "signal_unreadable", "The item could not be read to close it.");

            if (row.DecisionRecordId == recordId && (row.Status == SignalWriter.SignalStatusResolved || p.Resolution == Resolution.InquirySent))
                return new DecisionCommitSignal(signalId, state, null); // already done (a resumed review)

            var open = row.IsOpen;
            var systemClosed = row.SystemClosedSince(reviewStart);
            if (!open && !(systemClosed && p.Resolution != Resolution.InquirySent))
            {
                return Warn(signalId, row.Status == SignalWriter.SignalStatusResolved && !systemClosed ? "resolved_by_another_decision" : "closed_during_review",
                    "The item was closed by someone else while this decision was made; it was left as it is.");
            }

            if (row.RowVersion is not { Length: > 0 } version)
                return Warn(signalId, "signal_unversioned", "The item could not be closed safely.");

            var fields = new Dictionary<string, object> { ["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", recordId) };
            if (p.Resolution == Resolution.InquirySent)
            {
                fields["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusAcknowledged);
            }
            else
            {
                fields["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
                fields["sprk_resolutiontype"] = new OptionSetValue(p.Resolution == Resolution.Dismissed ? SignalWriter.ResolutionDismissed : SignalWriter.ResolutionActed);
                fields["sprk_resolvedon"] = _clock.GetUtcNow().UtcDateTime;
                fields["sprk_resolvedby"] = new EntityReference("systemuser", confirmedBy);
                if (p.DismissalText is { } reason)
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
                return new DecisionCommitSignal(signalId, "Unchanged", "signal_close_failed");
            }
        }

        return Warn(signalId, "signal_changed", "The item kept changing while it was being closed; it was left as it is.");

        DecisionCommitSignal Warn(Guid id, string code, string why)
        {
            _logger.LogWarning(DecisionCommitEvents.SignalCloseFailed,
                "Decision commit: Signal {SignalId} not closed with record {RecordId}: {Code}.", id, recordId, code);
            OntologyWriterTelemetry.RecordFailure("decision_commit_signal_close_failed");
            return new DecisionCommitSignal(id, "Unchanged", code);
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

    internal static DateTimeOffset BoundPlanReadOn(DateTimeOffset? planReadOn, DateTimeOffset now) =>
        planReadOn is { } read && read <= now && read >= now - PlanReadWindow ? read : now;

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
