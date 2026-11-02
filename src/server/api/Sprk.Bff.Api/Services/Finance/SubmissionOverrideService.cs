using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using FindingGroup = Sprk.Bff.Api.Services.Finance.DuplicateDetailGate.FindingGroup;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Where a blocked internal submission stands, as the review screens show it.</summary>
public static class BlockedSubmissionStates
{
    /// <summary>The rejection rule would have rejected it; the invoice is In Review and held until Override or Return.</summary>
    public const string Blocked = "Blocked";

    /// <summary>A person with the eBilling Override privilege accepted it; the invoice is released to approval.</summary>
    public const string Overridden = "Overridden";

    /// <summary>A person returned it to the firm; the invoice is Returned to Firm.</summary>
    public const string Returned = "Returned";

    /// <summary>Anything else: not an internal blocked submission (an SPA submission, one still processing, accepted, rejected for another reason, or failed).</summary>
    public const string NotBlocked = "NotBlocked";
}

/// <summary>
/// Stable error codes of the blocked-submission routes (ADR-019). The strings are a contract with the Invoice Review wizard and page
/// (tasks 052 and 111). <see cref="NotPermitted"/> is the one the acceptance criteria name.
/// </summary>
public static class BlockedSubmissionErrors
{
    /// <summary>403: the caller does not hold the eBilling Override privilege, the probe could not tell, or the caller could not be identified.</summary>
    public const string NotPermitted = "ebilling.override.not_permitted";

    /// <summary>403: the decision closes Signals and the caller lacks AppendTo on the invoice's core record or Write on a Signal's subject
    /// (FR-51, the commit route's rule, asked as the caller). Nothing was changed.</summary>
    public const string NotAuthorized = "ebilling.blocked.not_authorized";

    /// <summary>403 (Return only): the caller could not be identified as a Dataverse user, so the Decision Record could not name its decider.</summary>
    public const string DeciderUnverified = "ebilling.blocked.decider_unverified";

    /// <summary>400: a reason is required (shared with the invoice-level reject and return, task 104).</summary>
    public const string ReasonRequired = "ebilling.reason_required";

    /// <summary>400: the reason is longer than the Decision Record's reason column.</summary>
    public const string ReasonTooLong = "ebilling.reason_too_long";

    /// <summary>400: the decision as built would be refused by the Decision Record writer before any write (an invalid request).</summary>
    public const string DecisionInvalid = "ebilling.blocked.decision_invalid";

    /// <summary>404: the submission does not exist (the route's filter has already passed, so this is a delete between the check and the read).</summary>
    public const string NotFound = "ebilling.blocked.not_found";

    /// <summary>409: an SPA (or other non-internal) submission. Only an internal submission can be overridden or returned (D-24).</summary>
    public const string NotInternal = "ebilling.blocked.not_internal";

    /// <summary>409: the submission Failed. Nothing was decided about it; it has no invoice to release.</summary>
    public const string SubmissionFailed = "ebilling.blocked.submission_failed";

    /// <summary>409: processing accepted it (no blocking failure), so there is nothing to override.</summary>
    public const string AlreadyAccepted = "ebilling.blocked.already_accepted";

    /// <summary>409: it was already overridden, so it cannot be returned.</summary>
    public const string AlreadyOverridden = "ebilling.blocked.already_overridden";

    /// <summary>409: it was already returned, so it cannot be overridden.</summary>
    public const string AlreadyReturned = "ebilling.blocked.already_returned";

    /// <summary>409: it was not blocked (rejected for another reason, or received and not yet processed).</summary>
    public const string NotBlocked = "ebilling.blocked.not_blocked";

    /// <summary>409: processing has not finished writing its outcome; retry in a moment.</summary>
    public const string StillProcessing = "ebilling.blocked.still_processing";

    /// <summary>409: the invoice the submission created is missing or no longer In Review (for an override), so there is nothing to release.</summary>
    public const string InvoiceNotHeld = "ebilling.blocked.invoice_not_held";

    /// <summary>409: the stored findings cannot be read or are not those of a blocked submission, so the decision's facts cannot be built. Fails closed.</summary>
    public const string FindingsUnreadable = "ebilling.blocked.findings_unreadable";

    /// <summary>409: adding the decision to the stored findings would pass the column's limit.</summary>
    public const string FindingsTooLarge = "ebilling.blocked.findings_too_large";

    /// <summary>409: another decision on this invoice is running; retry in a moment.</summary>
    public const string InProgress = "ebilling.blocked.in_progress";

    /// <summary>409: the submission or the invoice changed while the decision ran; nothing was left half done that a retry does not finish.</summary>
    public const string ConcurrentChange = "ebilling.blocked.concurrent_change";

    /// <summary>409: the Decision Record for this decision already exists with another reason or another decider, so this call is not a repeat of it.</summary>
    public const string DecisionConflict = "ebilling.blocked.decision_conflict";

    /// <summary>409: the Decision Record writer refused the decision for a reason a retry does not change (its subject or owner).</summary>
    public const string RecordRefused = "ebilling.blocked.record_refused";

    /// <summary>503: the Decision Record could not be written right now; nothing was changed. Retry.</summary>
    public const string RecordUnavailable = "ebilling.blocked.record_unavailable";

    /// <summary>503: a read or the lock could not be done right now. Retry.</summary>
    public const string Unavailable = "ebilling.blocked.unavailable";
}

/// <summary>The body of the override and return routes. The reason is required and is the only input: the decider is the authenticated caller.</summary>
public sealed record BlockedDecisionRequest(string? Reason);

/// <summary>Who decided a blocked submission, when, why, and the Decision Record that holds it. Internal reviewers only.</summary>
public sealed record BlockedDecisionStamp(Guid RecordId, Guid BySystemUserId, DateTimeOffset On, string Reason);

/// <summary>One value a rule read (as stored on the submission, D-112 scrubbing already applied; none for a group withheld at read time, D-176).</summary>
public sealed record BlockedValueRead(string? Name, string? Value, int? Line, string? Field, decimal? Confidence, bool BelowReliabilityThreshold);

/// <summary>
/// One finding of the stored evaluation. <see cref="Blocking"/> is true for a finding that made the rejection rule reject; once the
/// submission is overridden those are marked <see cref="Overridden"/>, and they stay on the page: an override never deletes or rewrites a finding.
/// <see cref="DetailWithheld"/> is true, and <see cref="ValuesRead"/> empty, for a duplicate across a secure wall (D-112, at write time) and for
/// every finding of a (policy, subject, line) group holding a Duplicate whose earlier invoice the caller cannot read (D-176, at read time).
/// </summary>
public sealed record BlockedFindingView(
    int Index,
    string? RuleType,
    string? PolicyCode,
    Guid? PolicyVersionId,
    string? Clause,
    string? ClauseLocation,
    string? Subject,
    int? Line,
    string? ReasonCode,
    bool DetailWithheld,
    string? Consequence,
    bool Blocking,
    string? HeldBecause,
    bool Overridden,
    IReadOnlyList<BlockedValueRead> ValuesRead);

/// <summary>The read of a blocked submission (GET). <see cref="CanOverride"/> is true only for a caller who holds the eBilling Override privilege.</summary>
public sealed record BlockedSubmissionView
{
    public required Guid SubmissionId { get; init; }
    public Guid? InvoiceId { get; init; }
    public string? TrackingNumber { get; init; }
    public string? FirmInvoiceNumber { get; init; }
    public string? Channel { get; init; }
    public string? Status { get; init; }
    public string? OutcomeReasonCode { get; init; }

    /// <summary>One of <see cref="BlockedSubmissionStates"/>.</summary>
    public required string State { get; init; }

    /// <summary>The caller may override: the submission is blocked, its invoice is still held In Review, and the caller holds the privilege.</summary>
    public bool CanOverride { get; init; }

    /// <summary>The submission is blocked and its invoice has not been decided otherwise. Return needs no privilege (D-73); the route still needs the caller's rights on the submission.</summary>
    public bool CanReturn { get; init; }

    public IReadOnlyList<BlockedFindingView> Findings { get; init; } = [];
    public BlockedDecisionStamp? Override { get; init; }
    public BlockedDecisionStamp? Return { get; init; }
}

/// <summary>How an override or a return ended.</summary>
public enum BlockedDecisionKind
{
    /// <summary>Decided now; the Decision Record was written (or found, on a retry that finished an interrupted decision).</summary>
    Decided,

    /// <summary>The submission was already decided this way; nothing was written (a repeat is a no-op).</summary>
    AlreadyDecided,

    /// <summary>Refused; <see cref="BlockedDecisionResult.StatusCode"/> and <see cref="BlockedDecisionResult.ErrorCode"/> say why. Nothing was changed except what a retry finishes.</summary>
    Refused,
}

/// <summary>The result of an override or a return.</summary>
public sealed record BlockedDecisionResult
{
    public required BlockedDecisionKind Kind { get; init; }
    public int StatusCode { get; init; } = StatusCodes.Status200OK;
    public string? ErrorCode { get; init; }
    public string? Detail { get; init; }
    public Guid SubmissionId { get; init; }
    public Guid? InvoiceId { get; init; }
    public Guid? RecordId { get; init; }

    /// <summary><c>Overridden</c> or <c>Returned</c>; null when refused.</summary>
    public string? Outcome { get; init; }

    internal static BlockedDecisionResult Refuse(Guid submissionId, int status, string code, string detail, Guid? invoiceId = null) =>
        new() { Kind = BlockedDecisionKind.Refused, StatusCode = status, ErrorCode = code, Detail = detail, SubmissionId = submissionId, InvoiceId = invoiceId };
}

/// <summary>
/// Override and Return of a blocked internal submission (ebilling-r1 task 105; FR-19, FR-37, D-24, D-39, D-73, D-76, D-101, D-102).
/// </summary>
/// <remarks>
/// <para><b>What "blocked" is.</b> Task 049 persists a submission the rejection rule would reject, on the internal channel, as an invoice
/// In Review (D-101) whose submission is Rejected with the reason code <see cref="SubmissionReasonCodes.Blocked"/>. That is the HELD set
/// (<see cref="FinanceBaseValues.HeldSubmissionConditions"/>): the invoice is excluded from pending spend (D-102) and from approval. This
/// service is the only way out of it.</para>
/// <para><b>Override</b> (privilege "eBilling Override", a reason, one person). There is no second invoice-creation path: the invoice and
/// its lines already exist, so an override changes nothing in them. It (1) records <c>override-submission</c> under the invoice
/// (<see cref="BillingDecisionRecorder"/>, D-100), then (2) sets the submission Accepted with the code <see cref="SubmissionReasonCodes.Overridden"/>
/// and stores the decision (who, when, why, the record) in <c>sprk_findingsjson</c> under <c>override</c>. Accepted leaves the held set,
/// which releases the invoice to approval and to pending spend. The blocking findings are not touched: the stored findings keep them and
/// the read marks them Overridden (FR-37, I-5); their Signals are resolved by the decision (below).</para>
/// <para><b>Signals (D-122, the human-decision close agreed with ontology-r1).</b> An override and a return each resolve every Open or
/// Acknowledged Signal on the invoice and on its lines, of every policy: the set is read once and stored in <c>sprk_findingsjson</c> under
/// <see cref="HumanDecisionSetMember"/> BEFORE the record (049's way, with this decision's id), the record lists it
/// (<c>ResolvedSignalIds</c>), then each Signal is closed as Acted by the record naming the decider (app-identity writer, no
/// impersonation), then the state changes. A retry reuses the set the writer stored with the decision's record when one exists (K1),
/// else the findings' set, never a fresh read. The findings column is user-writable (F1-1), so a set read back from it is checked
/// (<see cref="StoredSignalSetCheck"/>: each Signal's subject from its ROW, the invoice or one of its lines; the read time inside the
/// submission's life), else 409 <see cref="BlockedSubmissionErrors.FindingsUnreadable"/>. When the set is not empty the caller must first
/// hold AppendTo on the invoice's core record and Write on every Signal's subject, a line's asked on its parent invoice (ontology-r1
/// 2026-10-20, D-90), asked as the caller; otherwise 403 <see cref="BlockedSubmissionErrors.NotAuthorized"/> and nothing is written. A
/// close that fails (a timeout, or a Signal that kept changing) warns and the rest are still attempted, and the decision stands (the
/// state change and the notification still happen), but it is then answered 503 <see cref="BlockedSubmissionErrors.Unavailable"/>, never
/// Decided or AlreadyDecided (2026-10-29, ontology-r1 review of #1911, F2: nothing else closes that Signal later). A repeat of a decided
/// submission re-runs the close over the WRITER-CREATED record's set with that record and its decider (never the stamp), writes no second
/// record, and answers AlreadyDecided only once every close is done, else 503 again.</para>
/// <para><b>Return</b> (no privilege, D-73; the route requires the caller's rights on the submission). It records <c>return-to-firm</c>
/// under the invoice, moves the invoice In Review to Returned to Firm through the lifecycle service (FR-06, D-43), and sets the submission's
/// code to <see cref="SubmissionReasonCodes.Returned"/> with the decision in <c>return</c>.</para>
/// <para><b>Order and retries (074 caller obligations).</b> Everything runs under a per-invoice lock (<see cref="IIdempotencyService"/>,
/// key <see cref="LockKey"/>), so two decisions on one invoice never run together (K-4). Inside the lock the submission and invoice are
/// re-read, every refusal that needs no write is given first, the Decision Record's I/O-free refusals run (<c>Preflight</c>), the record
/// is written, and only then does anything change (K-3). The decision id is derived from the submission id (a submission can be overridden,
/// or returned, once), so a retry after any partial failure finds the same record and finishes; its content (decider and reason) is the
/// same on every attempt, built only from the stored findings and the call (K-5). The decider is the authenticated caller's
/// <c>systemuserid</c> from the on-behalf-of identity, never request input. The resolved Signals are the stored set (above), and the
/// policy version is the first deciding finding's, read from the stored findings.</para>
/// <para><b>Fail closed.</b> No privilege, a probe that faults, a caller that cannot be identified, a missing reason, an unreadable
/// finding set, a lock or read that faults, and a transition the lifecycle does not allow all refuse. Logs carry ids and codes only.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="BillingDecisionRecorder"/> (the record),
/// <see cref="IInvoiceLifecycleService"/> (the invoice move), <see cref="InvoiceSubmissionService"/> (the submission read for the
/// reviewer) and 049's persister, which already created the invoice. Extension: none of them decides a held submission; the recorder and
/// lifecycle service are callers' tools and 044's service only reads status for the submitter. Cost of doing nothing: an internal user
/// cannot accept a legitimately blocked invoice (a negotiated rate not yet in the rate card) or return it, so FR-19 and FR-37 fail and a
/// held invoice stays held forever, outside pending spend and approval.</para>
/// </remarks>
public sealed class SubmissionOverrideService
{
    internal const string Entity = FinanceBaseValues.SubmissionEntity;
    private const string ChannelColumn = "sprk_channel";
    private const string RowVersionColumn = "versionnumber";
    private const string StatusCodeColumn = "statuscode";
    private const int ReasonMaxLength = 2000;
    internal const string OverrideMember = "override";
    internal const string ReturnMember = "return";

    /// <summary>The <c>sprk_findingsjson</c> member that stores the D-122 set an override or a return resolves, before its record:
    /// <c>{ "decisionId", "setReadAtUtc", "signals": [ { "id", "policyCode" } ], "subjects": [ { "entity", "id" } ] }</c>. Separate from
    /// 049's <c>resolvedSignalSet</c> (the automatic rejection's), which is left alone.</summary>
    internal const string HumanDecisionSetMember = "humanDecisionSignalSet";

    private static readonly string[] InvoiceDecisionColumns = [StatusCodeColumn, "sprk_matter", "sprk_project", "sprk_regardingworkassignment"];
    internal const string OverrideActionCode = "override-submission";
    internal const string ReturnActionCode = "return-to-firm";
    private const string OverrideDeciderRole = "internal user with the eBilling Override privilege";
    private const string ReturnDeciderRole = "internal user";

    /// <summary>How long a decision may hold the invoice's lock. A decision is a few reads and writes; a crashed one is released by this.</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

    private static readonly string[] SubmissionColumns =
    [
        ChannelColumn, InvoiceSubmissionPersister.SubmissionStatus, InvoiceSubmissionPersister.SubmissionReasonCode,
        InvoiceSubmissionPersister.SubmissionInvoice, InvoiceSubmissionPersister.SubmissionFindingsJson,
        TrackingNumberAllocator.TrackingNumberColumn, TrackingNumberAllocator.FirmInvoiceNumberColumn, RowVersionColumn,
        StoredSignalSetCheck.ProcessingStartColumn,
    ];

    private readonly IGenericEntityService _entities;
    private readonly IInvoiceLifecycleService _lifecycle;
    private readonly IIdempotencyService _idempotency;
    private readonly IEbillingNotificationSink _sink;
    private readonly CallerRecordAccessProbe _probe;
    private readonly IDataverseUserClient _user;
    private readonly EarlierInvoiceReadability _earlierInvoices;
    private readonly Func<BillingDecision, CancellationToken, Task<BillingDecisionRecordResult>> _record;
    private readonly Action<BillingDecision> _preflight;
    private readonly HumanDecisionSignalClose.CloseActed _closeActed;
    private readonly Func<string, Guid, Guid, CancellationToken, Task<DecisionRecordWriter.BillingStoredResolvedSet?>> _findStored;
    private readonly TimeProvider _time;
    private readonly JobSubmissionService? _jobs;
    private readonly ILogger<SubmissionOverrideService> _logger;

    /// <param name="user">The CALLER's Dataverse client (OBO): the read's D-176 earlier-invoice check reads with it, never app-only (ADR-028).</param>
    public SubmissionOverrideService(
        IGenericEntityService entities,
        IInvoiceLifecycleService lifecycle,
        BillingDecisionRecorder recorder,
        IIdempotencyService idempotency,
        IEbillingNotificationSink sink,
        CallerRecordAccessProbe probe,
        IDataverseUserClient user,
        EarlierInvoiceReadability earlierInvoices,
        SignalWriter signals,
        ILogger<SubmissionOverrideService> logger,
        JobSubmissionService? jobs = null)
        : this(
            entities, lifecycle, idempotency, sink, probe, user, earlierInvoices, logger,
            (recorder ?? throw new ArgumentNullException(nameof(recorder))).RecordAsync,
            recorder.Preflight,
            (signals ?? throw new ArgumentNullException(nameof(signals))).CloseActedByDecisionAsync,
            recorder.FindStoredResolvedSetAsync,
            time: null,
            jobs: jobs)
    {
    }

    /// <summary>The seam the unit tests use: the recorder and the Signal writer are concrete, so they are passed as their calls (the pattern
    /// of 049's after-steps). <paramref name="findStored"/> is <see cref="BillingDecisionRecorder.FindStoredResolvedSetAsync"/> (the set,
    /// record id and decider of a decision's WRITER-CREATED record).</summary>
    internal SubmissionOverrideService(
        IGenericEntityService entities,
        IInvoiceLifecycleService lifecycle,
        IIdempotencyService idempotency,
        IEbillingNotificationSink sink,
        CallerRecordAccessProbe probe,
        IDataverseUserClient user,
        EarlierInvoiceReadability earlierInvoices,
        ILogger<SubmissionOverrideService> logger,
        Func<BillingDecision, CancellationToken, Task<BillingDecisionRecordResult>> record,
        Action<BillingDecision> preflight,
        HumanDecisionSignalClose.CloseActed closeActed,
        Func<string, Guid, Guid, CancellationToken, Task<DecisionRecordWriter.BillingStoredResolvedSet?>> findStored,
        TimeProvider? time = null,
        JobSubmissionService? jobs = null)
    {
        _jobs = jobs; // Task 152: null in a test seam; the DI path always supplies it (JobSubmissionService is a singleton).
        _closeActed = closeActed ?? throw new ArgumentNullException(nameof(closeActed));
        _findStored = findStored ?? throw new ArgumentNullException(nameof(findStored));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _earlierInvoices = earlierInvoices ?? throw new ArgumentNullException(nameof(earlierInvoices));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// The lock key of every decision on one invoice. Tasks 103 and 104 (approve, reject, return of the invoice) take the same key, so an
    /// approval and an override of one invoice never run together (074 K-4).
    /// </summary>
    public static string LockKey(Guid invoiceId) => $"ebilling-invoice-decision:{invoiceId:N}";

    /// <summary>The Decision Record's decision id of a submission's override: deterministic, so a retry finds the same record.</summary>
    public static Guid OverrideDecisionIdFor(Guid submissionId) => InvoiceSubmissionPersister.IdOf($"ebilling-override-{submissionId:D}");

    /// <summary>The Decision Record's decision id of a blocked submission's return.</summary>
    public static Guid ReturnDecisionIdFor(Guid submissionId) => InvoiceSubmissionPersister.IdOf($"ebilling-blocked-return-{submissionId:D}");

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Disposition
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    internal enum Disposition
    {
        Blocked,
        StillProcessing,
        Overridden,
        Returned,
        AcceptedByProcessing,
        Failed,
        NotInternal,
        NotBlocked,
    }

    /// <summary>Classifies a stored submission. Pure. Anything it does not recognise is NotInternal or NotBlocked (fail closed).</summary>
    internal static Disposition Classify(Entity submission)
    {
        if (submission.GetAttributeValue<OptionSetValue>(ChannelColumn)?.Value != (int)InvoiceSubmissionChannel.Internal)
        {
            return Disposition.NotInternal;
        }

        var status = submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value;
        var code = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode);
        return status switch
        {
            (int)InvoiceSubmissionStatus.Failed => Disposition.Failed,
            (int)InvoiceSubmissionStatus.Accepted =>
                string.Equals(code, SubmissionReasonCodes.Overridden, StringComparison.Ordinal) ? Disposition.Overridden : Disposition.AcceptedByProcessing,
            (int)InvoiceSubmissionStatus.Rejected => code switch
            {
                SubmissionReasonCodes.Blocked => Disposition.Blocked,
                SubmissionReasonCodes.Returned => Disposition.Returned,
                _ => Disposition.NotBlocked,
            },
            (int)InvoiceSubmissionStatus.Processing =>
                string.Equals(code, SubmissionReasonCodes.Blocked, StringComparison.Ordinal) ? Disposition.StillProcessing : Disposition.NotBlocked,
            _ => Disposition.NotBlocked,
        };
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Read
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The state of a submission for the review screens, with its stored findings (blocking ones marked, and Overridden once overridden) and
    /// whether THIS caller may override. Read app-only; the route's filter has decided the caller may read the submission. Null when the
    /// submission does not exist; a read fault throws <see cref="InvoiceSubmissionService.ReadUnavailable"/> (503).
    /// <para><b>D-176 (#2118, task 119 round 2).</b> A stored Duplicate finding quotes an earlier submission (<c>duplicateOf.invoiceId</c>,
    /// <c>duplicateOf.trackingNumber</c>, <c>duplicateOf.lineIndex</c>) and the duplicated line's values. They are returned only when the
    /// caller's own read returns that earlier invoice: <see cref="DuplicateDetailGate"/> (the helper the Invoice Review read model uses, over
    /// <see cref="EarlierInvoiceReadability"/>), one check per response with the caller's <see cref="IDataverseUserClient"/>. #2294 / DEF-043:
    /// a number-only Duplicate is withheld (no number is looked up); a same-invoice repeat is judged by the caller's Read of the submission's
    /// own invoice (<c>sprk_invoice</c>, refused to app-only generic writers). Otherwise every
    /// finding of the (policy, subject, line) group is returned with <c>detailWithheld</c> and no values read. A fault withholds every
    /// Duplicate group; the view still loads. This view carries no sentence, so there is none to replace.</para>
    /// </summary>
    public async Task<BlockedSubmissionView?> GetAsync(Guid submissionId, string? callerBearerToken, CancellationToken ct)
    {
        if (submissionId == Guid.Empty)
        {
            return null;
        }

        Entity? row;
        try
        {
            row = await _entities.RetrieveAsync(Entity, submissionId, SubmissionColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Blocked submission {SubmissionId} could not be read", submissionId);
            throw InvoiceSubmissionService.ReadUnavailable();
        }

        if (row is null)
        {
            return null;
        }

        var disposition = Classify(row);
        var invoiceId = row.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id;
        var json = row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        var root = ParseRoot(json);
        var overrideStamp = ReadStamp(root, OverrideMember);
        var returnStamp = ReadStamp(root, ReturnMember);

        var state = disposition switch
        {
            Disposition.Blocked or Disposition.StillProcessing => BlockedSubmissionStates.Blocked,
            Disposition.Overridden => BlockedSubmissionStates.Overridden,
            Disposition.Returned => BlockedSubmissionStates.Returned,
            _ => BlockedSubmissionStates.NotBlocked,
        };

        var canOverride = false;
        var canReturn = false;
        // F1-a: a planted sprk_invoice (not the one the submission created) is refused by Override and Return, so neither is offered.
        if (disposition == Disposition.Blocked && invoiceId is { } held && held == InvoiceSubmissionPersister.InvoiceIdFor(submissionId))
        {
            InvoiceLifecycleStatus? invoiceStatus;
            try
            {
                invoiceStatus = await ReadInvoiceStatusAsync(held, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, ct))
            {
                _logger.LogWarning(ex, "Invoice {InvoiceId} of blocked submission {SubmissionId} could not be read", held, submissionId);
                throw InvoiceSubmissionService.ReadUnavailable();
            }

            canReturn = invoiceStatus is InvoiceLifecycleStatus.InReview or InvoiceLifecycleStatus.ReturnedToFirm;
            canOverride = invoiceStatus == InvoiceLifecycleStatus.InReview
                && await EbillingOverridePrivilege.HoldsAsync(_probe, callerBearerToken, _logger, ct).ConfigureAwait(false);
        }

        var overridden = overrideStamp is not null;
        var findings = ReadFindings(root, overridden);
        var withheld = await DuplicateDetailGate.WithheldGroupsAsync(
            findings, GroupOf, f => f.ValuesRead.Select(v => (v.Name, v.Value)), f => f.ReasonCode, invoiceId,
            _earlierInvoices, _user, _logger, "Blocked submission", ct).ConfigureAwait(false);
        return new BlockedSubmissionView
        {
            SubmissionId = submissionId,
            InvoiceId = invoiceId,
            TrackingNumber = row.GetAttributeValue<string>(TrackingNumberAllocator.TrackingNumberColumn),
            FirmInvoiceNumber = row.GetAttributeValue<string>(TrackingNumberAllocator.FirmInvoiceNumberColumn),
            Channel = row.GetAttributeValue<OptionSetValue>(ChannelColumn)?.Value == (int)InvoiceSubmissionChannel.Internal ? "Internal" : "Other",
            Status = row.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value is { } s
                && Enum.IsDefined(typeof(InvoiceSubmissionStatus), s) ? ((InvoiceSubmissionStatus)s).ToString() : null,
            OutcomeReasonCode = row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode),
            State = state,
            CanOverride = canOverride,
            CanReturn = canReturn,
            Findings = withheld.Count == 0 ? findings : findings.Select(f => Withhold(f, withheld)).ToList(),
            Override = overrideStamp,
            Return = returnStamp,
        };
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Override and Return
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Accepts a blocked internal submission despite its blocking failure. The caller MUST hold the eBilling Override privilege (checked here,
    /// on every call, as the caller); the reason is required. A repeat is a no-op with no new Decision Record.
    /// </summary>
    public Task<BlockedDecisionResult> OverrideAsync(Guid submissionId, string? reason, string? callerBearerToken, CancellationToken ct) =>
        DecideAsync(Verb.Override, submissionId, reason, callerBearerToken, ct);

    /// <summary>
    /// Returns a blocked internal submission to the firm (D-73: no privilege). The reason is required. A repeat is a no-op with no new Decision Record.
    /// </summary>
    public Task<BlockedDecisionResult> ReturnAsync(Guid submissionId, string? reason, string? callerBearerToken, CancellationToken ct) =>
        DecideAsync(Verb.Return, submissionId, reason, callerBearerToken, ct);

    internal enum Verb { Override, Return }

    private async Task<BlockedDecisionResult> DecideAsync(Verb verb, Guid submissionId, string? reason, string? token, CancellationToken ct)
    {
        // ── 1. Who is asking, and may they. Before anything is read, so a refusal says nothing about the submission. ──
        var decider = await ResolveCallerAsync(token, ct).ConfigureAwait(false);
        if (verb == Verb.Override)
        {
            if (decider is null || !await EbillingOverridePrivilege.HoldsAsync(_probe, token, _logger, ct).ConfigureAwait(false))
            {
                _logger.LogWarning("Override of submission {SubmissionId} refused: the caller does not hold the privilege or could not be verified", submissionId);
                return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status403Forbidden, BlockedSubmissionErrors.NotPermitted,
                    "Overriding a blocked submission needs the eBilling Override privilege.");
            }
        }
        else if (decider is null)
        {
            _logger.LogWarning("Return of submission {SubmissionId} refused: the caller could not be verified", submissionId);
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status403Forbidden, BlockedSubmissionErrors.DeciderUnverified,
                "The caller could not be identified, so the decision cannot be attributed.");
        }

        // ── 2. The reason. ──
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status400BadRequest, BlockedSubmissionErrors.ReasonRequired, "A reason is required.");
        }

        if (trimmed.Length > ReasonMaxLength)
        {
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status400BadRequest, BlockedSubmissionErrors.ReasonTooLong,
                $"The reason may be at most {ReasonMaxLength} characters.");
        }

        // ── 3. Read, and every refusal that needs no write or lock. ──
        Entity? row;
        try
        {
            row = await ReadSubmissionAsync(submissionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Submission {SubmissionId} could not be read for a decision", submissionId);
            return Unavailable(submissionId);
        }

        if (row is null)
        {
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status404NotFound, BlockedSubmissionErrors.NotFound, "The submission was not found.");
        }

        if (Refusal(verb, Classify(row), submissionId, row.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id) is { } early)
        {
            if (early.Kind == BlockedDecisionKind.AlreadyDecided
                && await RecloseAsync(verb, row, token, ct).ConfigureAwait(false) > 0)
            {
                // A repeat finishes a close that failed (the stored set, the writer-created record). While one still fails it is never
                // answered AlreadyDecided: 503, and the next repeat tries again (F2).
                return Unavailable(submissionId, early.InvoiceId);
            }

            return early;
        }

        if (row.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id is not { } invoiceId || invoiceId == Guid.Empty)
        {
            _logger.LogError("Blocked submission {SubmissionId} links no invoice; it cannot be decided", submissionId);
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, BlockedSubmissionErrors.InvoiceNotHeld,
                "The submission has no invoice to release or return.");
        }

        if (invoiceId != InvoiceSubmissionPersister.InvoiceIdFor(submissionId))
        {
            // F1-a (ontology-r1 round 2): sprk_invoice is user-writable, but the persister only ever links the invoice derived from the
            // submission id (049 requires the same). Any other invoice is a planted link: deciding it would record and move an invoice the
            // caller may hold no Write on.
            _logger.LogWarning("Submission {SubmissionId} links invoice {InvoiceId}, not the invoice it created; it cannot be decided", submissionId, invoiceId);
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, BlockedSubmissionErrors.InvoiceNotHeld,
                "The submission's invoice is not the one it created, so there is nothing to release or return.");
        }

        // ── 4. One decision at a time per invoice (074 K-4). ──
        var key = LockKey(invoiceId);
        var owner = Guid.NewGuid().ToString("N");
        bool acquired;
        try
        {
            acquired = await _idempotency.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "The decision lock for invoice {InvoiceId} could not be taken", invoiceId);
            return Unavailable(submissionId, invoiceId);
        }

        if (!acquired)
        {
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, BlockedSubmissionErrors.InProgress,
                "Another decision on this invoice is running. Try again shortly.", invoiceId);
        }

        try
        {
            return await DecideLockedAsync(verb, submissionId, invoiceId, trimmed, decider!.Value, token, ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _idempotency.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The lock expires by itself; a failed release must not turn a finished decision into an error.
                _logger.LogWarning(ex, "The decision lock for invoice {InvoiceId} could not be released; it expires on its own", invoiceId);
            }
        }
    }

    private async Task<BlockedDecisionResult> DecideLockedAsync(
        Verb verb, Guid submissionId, Guid invoiceId, string reason, Guid decider, string? token, CancellationToken ct)
    {
        // Re-read under the lock: what was read before it may have been decided by the call that held it.
        Entity? row;
        Entity? invoice;
        try
        {
            row = await ReadSubmissionAsync(submissionId, ct).ConfigureAwait(false);
            invoice = await ReadInvoiceAsync(invoiceId, InvoiceDecisionColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Submission {SubmissionId} or its invoice {InvoiceId} could not be read for a decision", submissionId, invoiceId);
            return Unavailable(submissionId, invoiceId);
        }

        if (row is null)
        {
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status404NotFound, BlockedSubmissionErrors.NotFound, "The submission was not found.", invoiceId);
        }

        if (Refusal(verb, Classify(row), submissionId, invoiceId) is { } refused)
        {
            if (refused.Kind == BlockedDecisionKind.AlreadyDecided
                && await RecloseAsync(verb, row, token, ct).ConfigureAwait(false) > 0)
            {
                return Unavailable(submissionId, invoiceId);
            }

            return refused;
        }

        var invoiceStatus = StatusOf(invoice);

        // The invoice must still be where the decision expects it. Override releases a HELD invoice, so it must be In Review. Return
        // moves an In Review invoice, or finishes an interrupted return (the invoice already Returned to Firm, the submission not yet).
        // Task 097: a Superseded invoice (closed by its correction) is neither, so both verbs refuse it as not held.
        var invoiceOk = verb == Verb.Override
            ? invoiceStatus == InvoiceLifecycleStatus.InReview
            : invoiceStatus is InvoiceLifecycleStatus.InReview or InvoiceLifecycleStatus.ReturnedToFirm;
        if (!invoiceOk)
        {
            var code = invoiceStatus == InvoiceLifecycleStatus.ReturnedToFirm ? BlockedSubmissionErrors.AlreadyReturned : BlockedSubmissionErrors.InvoiceNotHeld;
            _logger.LogWarning("Submission {SubmissionId}: invoice {InvoiceId} is {Status}, not where a {Verb} expects it", submissionId, invoiceId, invoiceStatus, verb);
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, code,
                "The invoice is not held for review, so there is nothing to " + (verb == Verb.Override ? "release." : "return."), invoiceId);
        }

        var findingsJson = row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(findingsJson);
        if (stored is null || stored.InvoiceId != invoiceId || stored.Kind != SubmissionEvaluationKind.Blocked || stored.DecidingIndexes.Count == 0)
        {
            _logger.LogWarning("Submission {SubmissionId}: the stored findings are missing, unreadable, name another invoice or are not a blocked evaluation", submissionId);
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, BlockedSubmissionErrors.FindingsUnreadable,
                "The stored findings of this submission cannot be read, so the decision cannot be recorded.", invoiceId);
        }

        // ── D-122: the Signals this decision resolves. The set the WRITER stored with this decision's record wins (K1: a retry, also one
        //    across a deploy); else a set stored in the findings for THIS decision, checked (F1-1: that column is user-writable); else it is
        //    read now. The subjects are always the Signal ROWS' (never the findings'), each the invoice or one of its lines. ──
        var decisionId = verb == Verb.Override ? OverrideDecisionIdFor(submissionId) : ReturnDecisionIdFor(submissionId);
        DecisionRecordWriter.BillingStoredResolvedSet? recordedSet;
        try
        {
            recordedSet = await _findStored(verb == Verb.Override ? OverrideActionCode : ReturnActionCode, invoiceId, decisionId, ct).ConfigureAwait(false);
        }
        catch (DecisionRecordRefusedException ex)
        {
            return RecordRefusal(submissionId, invoiceId, ex);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Submission {SubmissionId}: whether its decision is already recorded could not be checked", submissionId);
            return Unavailable(submissionId, invoiceId);
        }

        InvoiceSubmissionAfterSteps.ResolvedSignalSet signalSet;
        IReadOnlyList<(string Entity, Guid Id)> subjects;
        var storeSet = false;
        try
        {
            StoredSignalSetCheck.Result check;
            if (recordedSet is not null)
            {
                signalSet = new InvoiceSubmissionAfterSteps.ResolvedSignalSet(
                    recordedSet.SetReadAtUtc ?? DateTime.MinValue,
                    recordedSet.Signals.Select(s => new InvoiceSubmissionAfterSteps.ResolvedSignal(s.SignalId, s.PolicyCode)).ToList());
                check = await StoredSignalSetCheck.SubjectsAsync(_entities, invoiceId, signalSet, ct).ConfigureAwait(false);
            }
            else if (ReadHumanDecisionSet(ParseRoot(findingsJson), decisionId) is { } held)
            {
                signalSet = held;
                check = await StoredSignalSetCheck.CheckAsync(_entities, _time, invoiceId, held,
                    row.GetAttributeValue<DateTime?>(StoredSignalSetCheck.ProcessingStartColumn), ct).ConfigureAwait(false);
            }
            else
            {
                var read = await InvoiceSubmissionAfterSteps.ReadOpenSignalSetWithSubjectsAsync(_entities, _time, invoiceId, _logger, ct).ConfigureAwait(false);
                (signalSet, storeSet) = (read.Set, true);
                check = new StoredSignalSetCheck.Result(true, null, read.Subjects);
            }

            if (!check.Trusted)
            {
                _logger.LogWarning("Submission {SubmissionId}: the stored Signal set of its {Verb} cannot be trusted ({Why}); nothing was changed", submissionId, verb, check.Why);
                return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, BlockedSubmissionErrors.FindingsUnreadable,
                    "The stored findings of this submission cannot be read, so the decision cannot be recorded.", invoiceId);
            }

            subjects = check.Subjects;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Submission {SubmissionId}: the Signals of invoice {InvoiceId} could not be read", submissionId, invoiceId);
            return Unavailable(submissionId, invoiceId);
        }

        // ── FR-51, before any write: closing Signals (app identity, no impersonation) needs the caller's rights, asked as the caller. ──
        if (await AuthorizeCloseAsync(submissionId, invoiceId, invoice!, signalSet, subjects, token, ct).ConfigureAwait(false) is { } denied)
        {
            return denied;
        }

        var decision = BuildDecision(verb, stored, submissionId, invoiceId, decider, reason, signalSet);
        try
        {
            _preflight(decision);
        }
        catch (DecisionRecordRefusedException ex)
        {
            _logger.LogWarning("Submission {SubmissionId}: the decision would be refused before any write ({Reason})", submissionId, ex.Reason);
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status400BadRequest, BlockedSubmissionErrors.DecisionInvalid,
                "The decision is not valid.", invoiceId);
        }

        // ── The set is stored BEFORE the record (049's way), so a retry closes exactly it. ──
        if (storeSet)
        {
            var withSet = WithHumanDecisionSet(findingsJson!, decisionId, signalSet, subjects);
            if (withSet.Length > InvoiceSubmissionPersister.FindingsJsonMaxLength)
            {
                return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, BlockedSubmissionErrors.FindingsTooLarge,
                    "The decision could not be added to the stored findings.", invoiceId);
            }

            if (RowVersionOf(row) is not { } setVersion)
            {
                _logger.LogWarning("Submission {SubmissionId} was read without a row version; its decision is not written", submissionId);
                return Unavailable(submissionId, invoiceId);
            }

            try
            {
                await _entities.BulkUpdateIfRowVersionsMatchAsync(
                    Entity, [(submissionId, new Dictionary<string, object> { [InvoiceSubmissionPersister.SubmissionFindingsJson] = withSet }, setVersion)], ct)
                    .ConfigureAwait(false);
                // Our own write moved the row version: the state write below rests on the row as it now is.
                row = await ReadSubmissionAsync(submissionId, ct).ConfigureAwait(false);
            }
            catch (System.Data.DBConcurrencyException)
            {
                // As at the state write: a concurrent run that finished this same decision makes this call a no-op, anything else a 409.
                return await AfterConflictAsync(verb, submissionId, invoiceId, recordId: null, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, ct))
            {
                _logger.LogError(ex, "Submission {SubmissionId}: the Signal set could not be stored", submissionId);
                return Unavailable(submissionId, invoiceId);
            }

            if (row is null || Classify(row) != Disposition.Blocked
                || row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson) is not { } reread)
            {
                return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, BlockedSubmissionErrors.ConcurrentChange,
                    "The submission changed while the decision was written. Try again.", invoiceId);
            }

            findingsJson = reread;
        }

        // ── Record first (074 K-3): a refusal here leaves the submission and the invoice exactly as they were. ──
        BillingDecisionRecordResult recorded;
        try
        {
            recorded = await _record(decision, ct).ConfigureAwait(false);
        }
        catch (DecisionRecordRefusedException ex)
        {
            return RecordRefusal(submissionId, invoiceId, ex);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogError(ex, "Submission {SubmissionId}: the Decision Record could not be written", submissionId);
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status503ServiceUnavailable, BlockedSubmissionErrors.RecordUnavailable,
                "The decision could not be recorded right now. Nothing was changed. Try again.", invoiceId);
        }

        // ── The stored set, closed as Acted by the record naming the decider (a retry: AlreadyClosedByThisRecord). Every Signal is still
        //    attempted and the state change below still runs (the decision stands, as for a line decision), but a close that failed is
        //    counted: nothing else closes that Signal later, so the answer is 503 unavailable, never Decided (F2). ──
        var signalsNotClosed = await HumanDecisionSignalClose.CloseSetAsync(_closeActed, signalSet, recorded.RecordId, decider, _logger, ct).ConfigureAwait(false);

        // ── Then change state. A failure from here on leaves a record a retry finds and finishes. ──
        if (verb == Verb.Return && invoiceStatus == InvoiceLifecycleStatus.InReview)
        {
            InvoiceTransitionResult moved;
            try
            {
                moved = await _lifecycle.TransitionAsync(invoiceId, InvoiceLifecycleStatus.ReturnedToFirm, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, ct))
            {
                _logger.LogError(ex, "Submission {SubmissionId}: invoice {InvoiceId} could not be moved to Returned to Firm", submissionId, invoiceId);
                return Unavailable(submissionId, invoiceId);
            }

            if (!moved.Succeeded)
            {
                _logger.LogWarning("Submission {SubmissionId}: invoice {InvoiceId} was not returned ({Reason})", submissionId, invoiceId, moved.Reason);
                var code = moved.Reason == InvoiceTransitionReasons.ConcurrentChange ? BlockedSubmissionErrors.ConcurrentChange : BlockedSubmissionErrors.InvoiceNotHeld;
                return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, code,
                    "The invoice could not be returned. Nothing further was changed; try again.", invoiceId);
            }
        }

        var stamp = new BlockedDecisionStamp(recorded.RecordId, decider, _time.GetUtcNow(), reason);
        var member = verb == Verb.Override ? OverrideMember : ReturnMember;
        var newJson = WithStamp(findingsJson!, member, stamp);
        if (newJson.Length > InvoiceSubmissionPersister.FindingsJsonMaxLength)
        {
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, BlockedSubmissionErrors.FindingsTooLarge,
                "The decision could not be added to the stored findings.", invoiceId);
        }

        var fields = new Dictionary<string, object>
        {
            [InvoiceSubmissionPersister.SubmissionReasonCode] = verb == Verb.Override ? SubmissionReasonCodes.Overridden : SubmissionReasonCodes.Returned,
            [InvoiceSubmissionPersister.SubmissionFindingsJson] = newJson,
        };
        if (verb == Verb.Override)
        {
            fields[InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted);
        }

        if (RowVersionOf(row) is not { } version)
        {
            _logger.LogWarning("Submission {SubmissionId} was read without a row version; its decision is not written", submissionId);
            return Unavailable(submissionId, invoiceId);
        }

        try
        {
            await _entities.BulkUpdateIfRowVersionsMatchAsync(Entity, [(submissionId, fields, version)], ct).ConfigureAwait(false);
        }
        catch (System.Data.DBConcurrencyException)
        {
            var afterConflict = await AfterConflictAsync(verb, submissionId, invoiceId, recorded.RecordId, ct).ConfigureAwait(false);

            // A concurrent run finished the state change, but this run's closes failed: the repeat (AlreadyDecided) re-runs them.
            return signalsNotClosed > 0 && afterConflict.Kind == BlockedDecisionKind.AlreadyDecided
                ? Unavailable(submissionId, invoiceId)
                : afterConflict;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogError(ex, "Submission {SubmissionId}: the decision could not be written to it", submissionId);
            return Unavailable(submissionId, invoiceId);
        }

        _logger.LogInformation("Submission {SubmissionId} {Outcome} by {Decider}; invoice {InvoiceId}, Decision Record {RecordId}, already recorded {AlreadyRecorded}, Signals not closed {SignalsNotClosed}",
            submissionId, verb == Verb.Override ? "overridden" : "returned", decider, invoiceId, recorded.RecordId, recorded.AlreadyRecorded, signalsNotClosed);

        // Task 152 (FR-43): an override releases the hold without a status move (a Return moves the status, and the lifecycle service re-indexes
        // that), so the index document is rebuilt here. The change is the override's decision id (one per submission). Best effort; never fails the decision.
        if (verb == Verb.Override)
        {
            await InvoiceIndexingJobHandler.TryEnqueueAsync(
                _jobs, _logger, invoiceId, $"o{decisionId.ToString("N", CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        }

        await NotifyDecidedAsync(verb, submissionId, invoiceId, ct).ConfigureAwait(false);

        if (signalsNotClosed > 0)
        {
            // The decision stands (the record, the state change and the notification are done), but a Signal is still Open with no Decision
            // Record link. The repeat finds the decided submission (AlreadyDecided) and re-runs the close over the writer-created record's set
            // (RecloseAsync): no second record, and the answer is Decided / AlreadyDecided only once every close is done.
            _logger.LogWarning("Submission {SubmissionId}: {Count} Signal close(s) of Decision Record {RecordId} did not finish; answering unavailable so the decision is repeated to complete them",
                submissionId, signalsNotClosed, recorded.RecordId);
            return Unavailable(submissionId, invoiceId);
        }

        return new BlockedDecisionResult
        {
            Kind = BlockedDecisionKind.Decided,
            SubmissionId = submissionId,
            InvoiceId = invoiceId,
            RecordId = recorded.RecordId,
            Outcome = verb == Verb.Override ? "Overridden" : "Returned",
        };
    }

    /// <summary>
    /// The submission changed between the read and the conditional write. Re-read once: already decided this way means a concurrent run
    /// finished it (a no-op); anything else means it changed under us and the caller retries (the record is found again).
    /// </summary>
    private async Task<BlockedDecisionResult> AfterConflictAsync(Verb verb, Guid submissionId, Guid invoiceId, Guid? recordId, CancellationToken ct)
    {
        Entity? row;
        try
        {
            row = await ReadSubmissionAsync(submissionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Submission {SubmissionId} could not be re-read after a version conflict", submissionId);
            return Unavailable(submissionId, invoiceId);
        }

        var now = row is null ? Disposition.NotBlocked : Classify(row);
        if ((verb == Verb.Override && now == Disposition.Overridden) || (verb == Verb.Return && now == Disposition.Returned))
        {
            return AlreadyDecided(verb, submissionId, invoiceId, recordId);
        }

        _logger.LogWarning("Submission {SubmissionId} changed while its {Verb} was written; nothing more was changed", submissionId, verb);
        return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, BlockedSubmissionErrors.ConcurrentChange,
            "The submission changed while the decision was written. Try again.", invoiceId);
    }

    private static BlockedDecisionResult AlreadyDecided(Verb verb, Guid submissionId, Guid? invoiceId, Guid? recordId = null) => new()
    {
        Kind = BlockedDecisionKind.AlreadyDecided,
        SubmissionId = submissionId,
        InvoiceId = invoiceId,
        RecordId = recordId,
        Outcome = verb == Verb.Override ? "Overridden" : "Returned",
    };

    /// <summary>
    /// The refusal (or no-op) a disposition gives for a verb; null when the submission is blocked and the verb may proceed.
    /// A repeat of the same verb is a no-op (<see cref="BlockedDecisionKind.AlreadyDecided"/>), never a second record.
    /// </summary>
    private static BlockedDecisionResult? Refusal(Verb verb, Disposition disposition, Guid submissionId, Guid? invoiceId)
    {
        BlockedDecisionResult Conflict(string code, string detail) =>
            BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, code, detail, invoiceId);

        return disposition switch
        {
            Disposition.Blocked => null,
            Disposition.Overridden => verb == Verb.Override
                ? AlreadyDecided(verb, submissionId, invoiceId)
                : Conflict(BlockedSubmissionErrors.AlreadyOverridden, "The submission was already overridden."),
            Disposition.Returned => verb == Verb.Return
                ? AlreadyDecided(verb, submissionId, invoiceId)
                : Conflict(BlockedSubmissionErrors.AlreadyReturned, "The submission was already returned to the firm."),
            Disposition.NotInternal => Conflict(BlockedSubmissionErrors.NotInternal, "Only an internal submission can be overridden or returned."),
            Disposition.Failed => Conflict(BlockedSubmissionErrors.SubmissionFailed, "The submission failed; there is nothing to decide."),
            Disposition.AcceptedByProcessing => Conflict(BlockedSubmissionErrors.AlreadyAccepted, "The submission was accepted; it has no blocking failure."),
            Disposition.StillProcessing => Conflict(BlockedSubmissionErrors.StillProcessing, "The submission is still being processed. Try again shortly."),
            _ => Conflict(BlockedSubmissionErrors.NotBlocked, "The submission is not blocked."),
        };
    }

    private BlockedDecisionResult RecordRefusal(Guid submissionId, Guid invoiceId, DecisionRecordRefusedException ex)
    {
        _logger.LogWarning("Submission {SubmissionId}: the Decision Record was refused ({Reason})", submissionId, ex.Reason);
        return RefusalOf(submissionId, invoiceId, ex);
    }

    /// <summary>What a refused Decision Record answers; logs nothing (a catch filter asks it, and the handler logs once).</summary>
    private static BlockedDecisionResult RefusalOf(Guid submissionId, Guid invoiceId, DecisionRecordRefusedException ex) =>
        ex.Reason switch
        {
            DecisionRecordRefusalReason.ReviewInvalid => BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict,
                BlockedSubmissionErrors.DecisionConflict,
                "This decision is already recorded with another reason or decider. Repeat it as it was first made.", invoiceId),
            DecisionRecordRefusalReason.FactSnapshotMissing or DecisionRecordRefusalReason.SubjectUnsupported
                or DecisionRecordRefusalReason.SubjectUnfileable or DecisionRecordRefusalReason.RecordClassUnderivable
                or DecisionRecordRefusalReason.CoreUnsupported or DecisionRecordRefusalReason.OwnerRefused
                or DecisionRecordRefusalReason.OwnerMissing => BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict,
                    BlockedSubmissionErrors.RecordRefused, "The decision could not be recorded for this invoice.", invoiceId),
            _ => BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status503ServiceUnavailable, BlockedSubmissionErrors.RecordUnavailable,
                "The decision could not be recorded right now. Nothing was changed. Try again.", invoiceId),
        };

    /// <summary>
    /// Best effort: tell the notification seam the held submission was decided. An override is Accepted (task 132 notifies the eligible
    /// approvers); a return is Returned (task 132 notifies the firm, D-143). The decision is already committed.
    /// </summary>
    private async Task NotifyDecidedAsync(Verb verb, Guid submissionId, Guid invoiceId, CancellationToken ct)
    {
        var notification = verb == Verb.Override
            ? new SubmissionDecidedNotification(submissionId, SubmissionDecisionOutcome.Accepted, InvoiceSubmissionChannel.Internal, invoiceId, null)
            : new SubmissionDecidedNotification(submissionId, SubmissionDecisionOutcome.Returned, InvoiceSubmissionChannel.Internal, invoiceId, SubmissionReasonCodes.Returned);
        try
        {
            await _sink.SubmissionDecidedAsync(notification, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Submission {SubmissionId}: the {Outcome} notification failed; the decision stands", submissionId, notification.Outcome);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // The decision
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The Decision Record's decision, built only from the stored findings and the call, so every attempt builds the same one (074 K-5):
    /// the code on the invoice, the deterministic decision id, a human decider (the authenticated caller), the reason, the first deciding
    /// finding's policy version, strings-only values, and the stored, scrubbed Signal plan as facts (D-112: never the raw findings; a
    /// Duplicate reason's sentence is the withheld one, #2235, <see cref="InvoiceSubmissionAfterSteps.RecordSentence"/>). It
    /// resolves the stored D-122 set <paramref name="signals"/> (<c>ResolvedSignalIds</c>; its read time and policy codes in the facts).
    /// </summary>
    internal static BillingDecision BuildDecision(
        Verb verb, InvoiceSubmissionAfterSteps.StoredFindings stored, Guid submissionId, Guid invoiceId, Guid decider, string reason,
        InvoiceSubmissionAfterSteps.ResolvedSignalSet signals)
    {
        var deciding = stored.DecidingIndexes
            .Select(i => stored.Findings.TryGetValue(i, out var f) ? f : null)
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();
        var codes = deciding.Select(f => f.ReasonCode).Distinct(StringComparer.Ordinal).ToList();
        var outcome = verb == Verb.Override ? "Overridden" : "Returned";
        var action = verb == Verb.Override ? OverrideActionCode : ReturnActionCode;

        var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["decision"] = action,
            ["outcome"] = outcome,
            ["submissionId"] = submissionId.ToString("D"),
            ["trackingNumber"] = stored.TrackingNumber,
            ["channel"] = "Internal",
            ["invoiceStatusAtDecision"] = "In Review (held)",
            ["reliabilityThreshold"] = stored.ReliabilityThreshold,
            ["blockingReasonCodes"] = codes,
            ["reasons"] = stored.Plan.Select(p => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["policyCode"] = p.Request.PolicyCode,
                ["policyVersionId"] = p.Request.PolicyVersionId.ToString("D"),
                ["subject"] = p.Request.SubjectEntityLogicalName,
                ["subjectId"] = p.Request.SubjectId.ToString("D"),
                ["reasonCodes"] = p.ReasonCodes,
                ["consequence"] = p.Consequence,
                ["fingerprint"] = p.Fingerprint,
                ["decision"] = p.Decision,
                ["sentence"] = InvoiceSubmissionAfterSteps.RecordSentence(p),
            }).ToList(),
        };
        if (signals.SetReadAtUtc != DateTime.MinValue)
        {
            // K1: a record written before the set was stored has no set fact; its retry rebuilds it without one, so the hash still matches.
            facts[DecisionRecordWriter.BillingResolvedSetFact] = InvoiceLineDecisionService.SetFact(signals);
        }
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["submissionId"] = submissionId.ToString("D"),
            ["trackingNumber"] = stored.TrackingNumber,
        };

        return new BillingDecision(
            verb == Verb.Override ? OverrideDecisionIdFor(submissionId) : ReturnDecisionIdFor(submissionId),
            action,
            invoiceId,
            BillingDecider.Human(decider, verb == Verb.Override ? OverrideDeciderRole : ReturnDeciderRole),
            facts,
            Outcome: outcome,
            Reason: reason,
            Values: values,
            PolicyVersionId: deciding.Count > 0 ? deciding[0].PolicyVersionId : null,
            ResolvedSignalIds: signals.Signals.Count > 0 ? signals.Signals.Select(s => s.Id).ToList() : null,
            Written: [new DecisionRecordRef(InvoiceReviewService.InvoiceEntity, invoiceId)]);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Signals (D-122): the stored set, the caller's rights, the close
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The set stored under <see cref="HumanDecisionSetMember"/> for <paramref name="decisionId"/>; null when none is stored for this
    /// decision (another decision's set is not this one's). An unreadable member is treated as none, so a fresh read replaces it. Only the
    /// ids, codes and read time are read: the stored <c>subjects</c> are never trusted (F1-1, the column is user-writable), and the set itself
    /// is a claim the caller checks (<see cref="StoredSignalSetCheck.CheckAsync"/>).</summary>
    internal static InvoiceSubmissionAfterSteps.ResolvedSignalSet? ReadHumanDecisionSet(JsonObject? root, Guid decisionId)
    {
        if (root?[HumanDecisionSetMember] is not JsonObject node
            || !Guid.TryParse(Text(node, "decisionId"), out var stored) || stored != decisionId)
        {
            return null;
        }

        try
        {
            return InvoiceSubmissionAfterSteps.ResolvedSignalSet.Parse(node);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Adds the set under <see cref="HumanDecisionSetMember"/>; every other member is kept as it was. The <c>subjects</c> are written
    /// for a reader of the row only; this service never reads them back (<see cref="ReadHumanDecisionSet"/>).</summary>
    internal static string WithHumanDecisionSet(
        string findingsJson, Guid decisionId, InvoiceSubmissionAfterSteps.ResolvedSignalSet set, IReadOnlyList<(string Entity, Guid Id)> subjects)
    {
        var root = ParseRoot(findingsJson) ?? throw new FormatException("The stored findings are not a JSON object.");
        var node = set.ToJson();
        node["decisionId"] = decisionId.ToString("D");
        node["subjects"] = new JsonArray(subjects
            .Select(s => (JsonNode)new JsonObject { ["entity"] = s.Entity, ["id"] = s.Id.ToString("D") }).ToArray());
        root[HumanDecisionSetMember] = node;
        return root.ToJsonString();
    }

    /// <summary>
    /// FR-51 when the set is not empty: AppendTo on the invoice's core record and Write on every Signal's subject, asked as the caller; a
    /// line's Write is asked on its parent invoice (ontology-r1 2026-10-20, D-90; <see cref="HumanDecisionSignalClose.AuthorizeAsync"/>).
    /// <paramref name="subjects"/> are row-derived and proven to be the invoice or its lines (<see cref="StoredSignalSetCheck"/>, or the open
    /// read by the invoice's lines). Null when allowed or there is nothing to close.
    /// </summary>
    private async Task<BlockedDecisionResult?> AuthorizeCloseAsync(
        Guid submissionId, Guid invoiceId, Entity invoice, InvoiceSubmissionAfterSteps.ResolvedSignalSet set,
        IReadOnlyList<(string Entity, Guid Id)> subjects, string? token, CancellationToken ct)
    {
        if (set.Signals.Count == 0)
        {
            return null;
        }

        if (CoreOf(invoice) is not { } core)
        {
            _logger.LogWarning("Submission {SubmissionId}: invoice {InvoiceId} names no single core record; its Signals cannot be closed", submissionId, invoiceId);
            return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status409Conflict, BlockedSubmissionErrors.RecordRefused,
                "The decision could not be recorded for this invoice.", invoiceId);
        }

        var refusal = await HumanDecisionSignalClose.AuthorizeAsync(_entities, _probe, token, core, invoiceId, subjects, _logger, ct).ConfigureAwait(false);
        switch (refusal)
        {
            case DecisionCallerRefusal.None:
                return null;
            case DecisionCallerRefusal.Unavailable:
                return Unavailable(submissionId, invoiceId);
            default:
                _logger.LogWarning("Submission {SubmissionId}: the caller may not close the invoice's Signals ({Refusal}); nothing was changed", submissionId, refusal);
                return BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status403Forbidden, BlockedSubmissionErrors.NotAuthorized,
                    "You do not have permission to record a decision on this invoice's findings.", invoiceId);
        }
    }

    /// <summary>
    /// A repeat of a decided submission: re-runs the close over the set the WRITER stored with that decision's Decision Record, with that
    /// record's id and its <c>sprk_confirmedby</c> decider (a close that failed is finished). Nothing is taken from the findings column
    /// (F1-1: user-writable, so neither the stamp nor the stored set is trusted); with no writer-created record nothing is closed. The set's
    /// subjects are read from the Signal rows and must be the invoice or its lines, then the caller's rights are asked, as for the decision;
    /// refused or untrusted, and nothing is written. Returns how many closes did NOT finish, and the caller answers 503 unavailable for a
    /// count above zero (F2), AlreadyDecided otherwise. Counted: a close that failed, and a TRANSIENT fault while reading the writer-created
    /// record, the stored Signals, the invoice or the entity sets (1: the Signals are still Open, so the repeat is not "done"). Not counted
    /// (PERMANENT, a known limit: only warn, AlreadyDecided): no writer-created record, a record naming no decider, untrusted subjects, a
    /// writer that refuses to read the set, an invoice that is gone, and a caller without the rights.
    /// </summary>
    private async Task<int> RecloseAsync(Verb verb, Entity row, string? token, CancellationToken ct)
    {
        var submissionId = row.Id;
        if (row.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id is not { } invoiceId || invoiceId == Guid.Empty
            || invoiceId != InvoiceSubmissionPersister.InvoiceIdFor(submissionId))
        {
            // F1-a: only the invoice the submission created (sprk_invoice is user-writable); any other link closes nothing.
            return 0;
        }

        var decisionId = verb == Verb.Override ? OverrideDecisionIdFor(submissionId) : ReturnDecisionIdFor(submissionId);
        InvoiceSubmissionAfterSteps.ResolvedSignalSet set;
        Guid recordId;
        Guid decider;
        try
        {
            if (await _findStored(verb == Verb.Override ? OverrideActionCode : ReturnActionCode, invoiceId, decisionId, ct).ConfigureAwait(false) is not { } recorded)
            {
                _logger.LogWarning("Submission {SubmissionId}: the repeat found no Decision Record written for its {Verb}; nothing is re-closed", submissionId, verb);
                return 0;
            }

            if (recorded.Signals.Count == 0)
            {
                return 0;
            }

            if (recorded.ConfirmedByUserId is not { } by)
            {
                // K3: a human decision's record always names its decider, so this one cannot be re-closed in anyone's name.
                _logger.LogWarning("Submission {SubmissionId}: Decision Record {RecordId} of its {Verb} names no decider; its {Count} Signals are not re-closed",
                    submissionId, recorded.RecordId, verb, recorded.Signals.Count);
                return 0;
            }

            (set, recordId, decider) = (
                new InvoiceSubmissionAfterSteps.ResolvedSignalSet(
                    recorded.SetReadAtUtc ?? DateTime.MinValue,
                    recorded.Signals.Select(s => new InvoiceSubmissionAfterSteps.ResolvedSignal(s.SignalId, s.PolicyCode)).ToList()),
                recorded.RecordId,
                by);

            var check = await StoredSignalSetCheck.SubjectsAsync(_entities, invoiceId, set, ct).ConfigureAwait(false);
            if (!check.Trusted
                || await ReadInvoiceAsync(invoiceId, InvoiceDecisionColumns, ct).ConfigureAwait(false) is not { } invoice)
            {
                // Permanent: untrusted subjects, or the invoice is gone. A repeat does not change either; the decision's answer stands.
                _logger.LogWarning("Submission {SubmissionId}: the repeat did not re-close the stored Signals (their subjects, the caller's rights or the invoice could not be shown)", submissionId);
                return 0;
            }

            if (await AuthorizeCloseAsync(submissionId, invoiceId, invoice, set, check.Subjects, token, ct).ConfigureAwait(false) is { } denied)
            {
                _logger.LogWarning("Submission {SubmissionId}: the repeat did not re-close the stored Signals (their subjects, the caller's rights or the invoice could not be shown)", submissionId);

                // A refusal is permanent (no rights, no core record): AlreadyDecided as before. A 503 is the entity-set lookup failing, which
                // is a read fault: the Signals are still Open, so the repeat is not answered as done (F2).
                return denied.StatusCode == StatusCodes.Status503ServiceUnavailable ? 1 : 0;
            }
        }
        catch (DecisionRecordRefusedException ex) when (RefusalOf(submissionId, invoiceId, ex).StatusCode != StatusCodes.Status503ServiceUnavailable)
        {
            // Permanent: the writer refused to read the stored set (more than one record under the key, or a set that cannot be read). A repeat
            // does not change it, so the repeat's answer stands. A lookup that merely faulted (503 in RefusalOf) falls to the next catch. The filter logs nothing; this handler logs the refusal once.
            _logger.LogWarning("Submission {SubmissionId}: the repeat could not use the stored Signals ({Reason}); nothing is re-closed", submissionId, ex.Reason);
            return 0;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            // A fault while reading the writer-created record, the stored Signals or the invoice is transient, and the Signals are still
            // Open: never an "already decided" answer (F2). Counted as one close not finished; the next repeat reads again.
            _logger.LogWarning("Submission {SubmissionId}: the repeat could not read the stored Signals ({ExceptionType}); answering unavailable so it is repeated", submissionId, ex.GetType().Name);
            return 1;
        }

        return await HumanDecisionSignalClose.CloseSetAsync(_closeActed, set, recordId, decider, _logger, ct).ConfigureAwait(false);
    }

    /// <summary>The invoice's ONE core record (approve's rule, <see cref="EligibleApproverResolver.SingleCoreOf"/>); null when it names none or several (fail closed, never the first found).</summary>
    private static DecisionCoreRecord? CoreOf(Entity invoice) =>
        EligibleApproverResolver.SingleCoreOf(InvoiceReviewReadService.InvoiceEntity, invoice) is { } core ? new DecisionCoreRecord(core.Entity, core.Id) : null;

    // ═════════════════════════════════════════════════════════════════════════════════════════════════
    // Reads and helpers
    // ═════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The caller's Dataverse <c>systemuserid</c> from the on-behalf-of identity; null when it cannot be established or the lookup faults (fail closed).</summary>
    private async Task<Guid?> ResolveCallerAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var id = await _probe.GetCallerSystemUserIdAsync(token, ct).ConfigureAwait(false);
            return id is { } value && value != Guid.Empty ? value : null;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning("The caller's identity could not be resolved for a blocked-submission decision ({ExceptionType}); refusing.", ex.GetType().Name);
            return null;
        }
    }

    private async Task<Entity?> ReadSubmissionAsync(Guid submissionId, CancellationToken ct)
    {
        try
        {
            return await _entities.RetrieveAsync(Entity, submissionId, SubmissionColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    /// <summary>The invoice's lifecycle status; null when it does not exist or its status is unknown to the lifecycle.</summary>
    private async Task<InvoiceLifecycleStatus?> ReadInvoiceStatusAsync(Guid invoiceId, CancellationToken ct) =>
        StatusOf(await ReadInvoiceAsync(invoiceId, [StatusCodeColumn], ct).ConfigureAwait(false));

    /// <summary>The invoice with <paramref name="columns"/>; null when it does not exist.</summary>
    private async Task<Entity?> ReadInvoiceAsync(Guid invoiceId, string[] columns, CancellationToken ct)
    {
        try
        {
            return await _entities.RetrieveAsync(InvoiceReviewService.InvoiceEntity, invoiceId, columns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    private static InvoiceLifecycleStatus? StatusOf(Entity? invoice) =>
        invoice?.GetAttributeValue<OptionSetValue>(StatusCodeColumn)?.Value is { } code && InvoiceStatusCodes.TryFromStatusCode(code, out var status)
            ? status
            : null;

    private static BlockedDecisionResult Unavailable(Guid submissionId, Guid? invoiceId = null) =>
        BlockedDecisionResult.Refuse(submissionId, StatusCodes.Status503ServiceUnavailable, BlockedSubmissionErrors.Unavailable,
            "The decision could not be completed right now. Try again.", invoiceId);

    private static long? RowVersionOf(Entity row) =>
        row.GetAttributeValue<long?>(RowVersionColumn)
        ?? (long.TryParse(row.RowVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null);

    private static bool IsCallerCancellation(Exception ex, CancellationToken ct) => ex is OperationCanceledException && ct.IsCancellationRequested;

    private static bool IsNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }

    // ── Stored findings (the JSON 049's persister wrote) ──

    private static JsonObject? ParseRoot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Adds the decision under <paramref name="member"/> and returns the JSON. Every other member is kept as it was.</summary>
    internal static string WithStamp(string findingsJson, string member, BlockedDecisionStamp stamp)
    {
        var root = ParseRoot(findingsJson) ?? throw new FormatException("The stored findings are not a JSON object.");
        root[member] = new JsonObject
        {
            ["recordId"] = stamp.RecordId.ToString("D"),
            ["bySystemUserId"] = stamp.BySystemUserId.ToString("D"),
            ["on"] = stamp.On.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["reason"] = stamp.Reason,
        };
        return root.ToJsonString();
    }

    private static BlockedDecisionStamp? ReadStamp(JsonObject? root, string member)
    {
        if (root?[member] is not JsonObject node
            || !Guid.TryParse(Text(node, "recordId"), out var recordId)
            || !Guid.TryParse(Text(node, "bySystemUserId"), out var by)
            || !DateTimeOffset.TryParse(Text(node, "on"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var on))
        {
            return null;
        }

        return new BlockedDecisionStamp(recordId, by, on, Text(node, "reason") ?? string.Empty);
    }

    private static IReadOnlyList<BlockedFindingView> ReadFindings(JsonObject? root, bool overridden)
    {
        if (root?["findings"] is not JsonArray array)
        {
            return [];
        }

        var findings = new List<BlockedFindingView>();
        foreach (var node in array.OfType<JsonObject>())
        {
            var blocking = node["rejects"]?.GetValueKind() == System.Text.Json.JsonValueKind.True;
            var values = new List<BlockedValueRead>();
            foreach (var v in (node["valuesRead"] as JsonArray ?? []).OfType<JsonObject>())
            {
                values.Add(new BlockedValueRead(
                    Text(v, "name"), Text(v, "value"), Int(v, "line"), Text(v, "field"), Dec(v, "confidence"),
                    v["belowReliabilityThreshold"]?.GetValueKind() == System.Text.Json.JsonValueKind.True));
            }

            findings.Add(new BlockedFindingView(
                Int(node, "index") ?? findings.Count,
                Text(node, "ruleType"),
                Text(node, "policyCode"),
                Guid.TryParse(Text(node, "policyVersionId"), out var version) ? version : null,
                Text(node, "clause"),
                Text(node, "clauseLocation"),
                Text(node, "subject"),
                Int(node, "line"),
                Text(node, "reasonCode"),
                node["detailWithheld"]?.GetValueKind() == System.Text.Json.JsonValueKind.True,
                Text(node, "consequence"),
                blocking,
                Text(node, "heldBecause"),
                blocking && overridden,
                values));
        }

        return findings;
    }

    private static FindingGroup GroupOf(BlockedFindingView finding) => new(finding.PolicyCode, finding.Subject, finding.Line);

    /// <summary>D-176: a finding of a withheld group in the write-time withheld shape (D-112): <c>detailWithheld</c>, no values read.</summary>
    private static BlockedFindingView Withhold(BlockedFindingView finding, IReadOnlySet<FindingGroup> withheld) =>
        withheld.Contains(GroupOf(finding)) ? finding with { DetailWithheld = true, ValuesRead = [] } : finding;

    private static string? Text(JsonObject node, string name) =>
        node.TryGetPropertyValue(name, out var value) && value is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;

    private static int? Int(JsonObject node, string name) =>
        node.TryGetPropertyValue(name, out var value) && value is JsonValue v && v.TryGetValue<int>(out var number) ? number : null;

    private static decimal? Dec(JsonObject node, string name) =>
        node.TryGetPropertyValue(name, out var value) && value is JsonValue v && v.TryGetValue<decimal>(out var number) ? number : null;
}
