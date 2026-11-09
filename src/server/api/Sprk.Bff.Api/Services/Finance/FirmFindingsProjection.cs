using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Finance;

// ═══════════════════════════════════════════════════════════════════════════════════════════════
// Firm-visible outcome contract (ebilling-r1 task 130a, FR-38, D-66, EB-D-202). An explicit allow-list: every member is set field by field in
// FirmFindingsProjection. Nothing here is an id of a Procedure, a rule set, a reviewer or a document, and nothing is a link (model 7.4, D-63).
// ═══════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>What a firm is told about one invoice or submission.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FirmOutcomeKind
{
    /// <summary>The submission is Received or Processing: status and billed total only.</summary>
    SubmissionInProgress,

    /// <summary>The submission was rejected: the findings that rejected it, from its own stored findings (130c, FR-18); none when it has none.</summary>
    SubmissionRejected,

    /// <summary>The invoice is In Review: its decided and reviewer-added lines with the reviewer's reason, and no approved total (EB-D-202).</summary>
    InvoiceInReview,

    Approved,

    /// <summary>Approved, with at least one Reduced, Disallowed or invoice-level adjustment item.</summary>
    ApprovedWithAdjustments,

    Rejected,

    ReturnedToFirm,

    SentToAp,

    Paid,

    /// <summary>A read FAULTED, so nothing may be told right now (fail closed). Always <see cref="FirmOutcome.Degraded"/>; the route answers 503.</summary>
    Unavailable,

    /// <summary>
    /// A closed state at the time of the request, with nothing more to tell: a failed submission, a cancelled invoice, an invoice replaced by a
    /// correction, or a status this contract does not know (logged). The status label and tracking number only, no items and no totals, never
    /// <see cref="FirmOutcome.Degraded"/>: it is not a fault, so the route answers 200 (130b fix round 1). It can change later (undoing a
    /// correction returns a superseded invoice to In Review).
    /// </summary>
    Closed,
}

/// <summary>The kind of one projected item.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FirmOutcomeDecision
{
    Reduced,
    Disallowed,

    /// <summary>A line the reviewer added (EB-D-193); the firm did not bill it.</summary>
    Added,

    /// <summary>An invoice-level adjustment (a percent or fixed write-down).</summary>
    Adjustment,

    /// <summary>A finding that rejected a submission (FR-18): its line, the values read and, when the firm flag publishes it, the rule and clause.</summary>
    RejectionFinding,
}

/// <summary>
/// The firm-visible outcome of one invoice or submission. <see cref="ApprovedTotal"/> is null for a state that has not been decided.
/// <see cref="Degraded"/> is true when a read did not complete: the outcome then carries the status only (fail closed).
/// </summary>
/// <param name="TrackingNumber">The tracking number the firm was given on submission.</param>
/// <param name="StatusLabel">What the firm sees as the status.</param>
/// <param name="NotShownCount">
/// A rejected submission only (130c fix round 2): the number of rejection findings the stored copy holds beyond the most that are shown
/// (200), so the list in <see cref="Items"/> is known to be incomplete. Null (omitted from the
/// JSON) when every rejection finding is shown, and for every other outcome.
/// </param>
public sealed record FirmOutcome(
    FirmOutcomeKind Kind,
    string? TrackingNumber,
    string StatusLabel,
    decimal? BilledTotal,
    decimal? ApprovedTotal,
    IReadOnlyList<FirmOutcomeItem> Items,
    bool Degraded,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? NotShownCount = null);

/// <summary>One decided or reviewer-added line, or one invoice-level adjustment, with the reviewer's reason and the rules behind it.</summary>
/// <param name="LineId">The invoice line, for a line item; null for an adjustment.</param>
/// <param name="Line">The line as the firm billed it (or the reviewer added it); null for an adjustment.</param>
/// <param name="Adjustment">The adjustment; null for a line item.</param>
/// <param name="Reason">The reviewer's reason; null when none is on record for this item (never substituted).</param>
/// <param name="FirmReasons">
/// The explanations addressed to the firm (<c>sprk_signal.sprk_firmreason</c>) of the Billing rules the item's decision resolved: one per rule
/// version that has one (the first non-blank in creation order), duplicates removed, in the order those chosen Signals were created (then by id);
/// empty when none
/// (never substituted). NOT a citation and not gated by the firm flag (130b fix round 2): shown whether or not the rule is cited. Empty for an
/// invoice-level adjustment (it resolves no Signal).
/// </param>
/// <param name="LineNumber">
/// A rejection finding only (130c): the one-based position of the line it is about, counting the lines in the order the submission read
/// them from the invoice (the Nth line read; it is not a number printed on the invoice); null for an invoice-level finding and for every
/// other item (omitted from the JSON). Never a line id, and never read from a live line.
/// </param>
/// <param name="ValuesRead">
/// A rejection finding only (130c): the values the rule read, as the submission stored them (name and value, nothing else); null for every other
/// item (omitted from the JSON), empty when the finding shows none (a Duplicate, a withheld detail, nothing stored).
/// </param>
public sealed record FirmOutcomeItem(
    FirmOutcomeDecision Decision,
    Guid? LineId,
    FirmOutcomeLine? Line,
    FirmOutcomeAdjustment? Adjustment,
    string? Reason,
    IReadOnlyList<string> FirmReasons,
    IReadOnlyList<FirmOutcomeCitation> Citations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? LineNumber = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FirmOutcomeValueRead>? ValuesRead = null);

/// <summary>One value a rule read for a rejection finding (130c): its name and the value as the submission stored it.</summary>
public sealed record FirmOutcomeValueRead(string Name, string Value);

/// <param name="DescriptionExcerpt">The first part of the line's description as billed.</param>
/// <param name="ApprovedAmount">The reviewer's figure for the line.</param>
public sealed record FirmOutcomeLine(
    DateOnly? Date, string? TimekeeperAsBilled, string? DescriptionExcerpt, decimal BilledAmount, decimal? ApprovedAmount);

/// <param name="Method"><c>Percent</c> or <c>Fixed</c>.</param>
/// <param name="Amount">The computed adjustment.</param>
public sealed record FirmOutcomeAdjustment(string Method, decimal? Value, decimal Amount);

/// <summary>
/// The rule behind an item, shown ONLY as the policy's firm flag publishes it (130b fix round 2): the clause reader returned text for this
/// version on this invoice (the rule set's visible-to-firms flag on AND the invoice's snapshot applied the version) and the rule is not an
/// InternalStandard Procedure's. Otherwise (flag off, flag unreadable, no snapshot, an InternalStandard rule) there is no citation at all: no
/// name, no label, no clause (D-66, D-79). No member is an id of a policy, version, rule set or Procedure. The firm reason is not a citation
/// field: it is <see cref="FirmOutcomeItem.FirmReasons"/>, shown whatever the flag says.
/// </summary>
/// <param name="RuleName">The rule's (policy's) name. Never the rule set's name.</param>
/// <param name="Label">The Procedure's firm-facing name, or "Billing instructions for this record"; null for a legacy rule set.</param>
public sealed record FirmOutcomeCitation(string? RuleName, string? Label, int? Version, string? SectionReference, string? Quote);

/// <summary>The clause text a firm may be shown for a rule version on an invoice: <see cref="RuleSetService.GetFirmClauseAsync"/>.</summary>
public delegate Task<FirmClauseText> FirmClauseReader(Guid policyVersionId, Guid invoiceId, CancellationToken ct);

/// <summary>
/// THE server-side projection of firm-visible outcome content (ebilling-r1 task 130a; FR-38, D-66, EB-D-202). Read-only. It has no caller
/// check: the CALLER of this class has already established that the invoice may be shown (the external route's firm and core-record checks);
/// every id passed in is one the caller read server-side. It must only be called behind <c>FirmOutcomeAuthorizationFilter</c>; enforced by the
/// ArchTest <c>FirmFindingsProjectionCallerBoundaryGuardTests</c> (only <c>FirmOutcomeEndpoints</c> and <c>FinanceModule</c> may reference it).
/// </summary>
/// <remarks>
/// <para><b>What is shown.</b> A submission Received or Processing: status and billed total only. An invoice In Review: status, billed total,
/// its reviewer-added lines and its decided lines (Reduced, Disallowed) with the reviewer's reason; no approved total and no invoice-level
/// adjustment (EB-D-202). Approved, Sent to AP and Paid: the same items plus the invoice-level adjustments, and the approved total. Rejected
/// and Returned to Firm: status and billed total. Pending, Flagged, Clean, Accepted, overridden and AI-observation findings never appear: an
/// item exists only because a line was decided, added or adjusted by a reviewer.</para>
/// <para><b>Where each part comes from (all read app-only).</b> The line and its status: the invoice's lines
/// (<see cref="InvoiceReviewReadService.LineQuery"/>). The reason: the <c>reduce-line</c>, <c>disallow-line</c> or <c>add-line</c> Decision Record
/// that carries the LINE's link (and, if it names an invoice, this one); a record with neither link of this invoice is never projected, and neither
/// is a record the BFF writer identity did not create (<see cref="WriterCreatedRecord"/>, EB-D-208: its line stays, with no reason). The
/// rules: the Signals that record resolved (<c>sprk_signal.sprk_decisionrecord</c>), their policy version and <c>sprk_firmreason</c>. An invoice
/// adjustment writes no Decision Record: its reason is <c>sprk_adjustmentreason</c> on the line and its rule is <c>sprk_adjustmentrule</c>.
/// The clause: <see cref="FirmClauseReader"/> once per distinct policy version per invoice. Reviewer identity, internal notes, confidence
/// scores and Signal messages are never read into the output.</para>
/// <para><b>Visibility rule (130b fix round 2).</b> Rule citations on a firm's line are shown only as the policy's firm flag publishes them:
/// a citation (rule name, label, version, section reference, quote) exists only when the clause reader returned text (the rule
/// set's flag on, the version applied by the invoice's snapshot) AND the rule is not an InternalStandard Procedure's, whatever its flag. In
/// every other case, including a fault reading the flag or the Procedure's type, the citation is omitted entirely; the line, the reviewer's
/// reason and the firm reasons (<see cref="FirmOutcomeItem.FirmReasons"/>, which are not citation fields) stay. There is no member for a Procedure Library document, container, drive item or link, nor for any policy, version, rule set or
/// Procedure id.</para>
/// <para><b>A rejected submission (130c, FR-18).</b> One <see cref="FirmOutcomeDecision.RejectionFinding"/> item per finding that rejected it,
/// built from the submission's OWN stored findings (<see cref="FirmRejectionFindings"/>: an allow-list parse that refuses an unknown rule type
/// and drops a malformed entry): its one-based line number and the values the rule read, as stored. The rule and clause (the citation) and the
/// firm reason follow the same rules as an invoice item's, for the submission's derived invoice and only when the caller decided it:
/// the citation as <see cref="RuleSetService.GetFirmClauseAsync"/> and the InternalStandard check publish it, and the firm reason only from a
/// Signal the BFF writer created, whatever the citation says. A live invoice line is never read and nothing is re-extracted. A Blocked
/// submission (an internal hold) projects no finding.</para>
/// <para><b>Fail closed.</b> No exception other than cancellation leaves this class. A fault reading the header, or a header with no status,
/// gives an <see cref="FirmOutcomeKind.Unavailable"/> outcome; a fault after it gives the status only, flagged
/// <see cref="FirmOutcome.Degraded"/> (a fault deciding a citation omits that citation instead). A closed state at the time of the request
/// (failed, cancelled, superseded, an unknown status) is NOT a fault: it is a <see cref="FirmOutcomeKind.Closed"/> outcome, status only and
/// not degraded.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="InvoiceReviewReadService"/> is the INTERNAL review read model
/// (it carries Signal sentences, confidence and reviewer identity); <see cref="RuleSetService.GetFirmClauseAsync"/> is the clause decision.
/// Extension: the review model cannot be narrowed to the firm view without a second set of rules inside it; this reuses its line query and
/// paging and the clause reader as they are. Cost of doing nothing: 131, 133 and 134 each re-derive what a firm may see, and a clause or
/// reviewer note reaches the wrong reader.</para>
/// </remarks>
public sealed class FirmFindingsProjection
{
    internal const string RecordEntity = InvoiceReviewReadService.DecisionRecordEntity;
    internal const string SignalEntity = "sprk_signal";

    internal const string ReduceActionCode = "reduce-line";
    internal const string DisallowActionCode = "disallow-line";

    internal const int ExcerptLength = 160;

    private const int ChunkSize = 200;
    private const int PageSize = 5000;

    private static readonly string[] RecordColumns =
    [
        "sprk_actioncode", "sprk_reason", "sprk_regardinginvoice", "sprk_regardinginvoiceline", "sprk_decidedon", "createdon",
    ];

    private static readonly string[] SignalColumns =
    [
        "sprk_decisionrecord", "sprk_policyversion", "sprk_firmreason", "sprk_regardinginvoice", "sprk_regardinginvoiceline", "createdon",
    ];

    private readonly IGenericEntityService _entities;
    private readonly FirmClauseReader _clauses;
    private readonly Func<CancellationToken, Task<Guid>> _writerUserId;
    private readonly ILogger<FirmFindingsProjection> _logger;

    /// <summary>The stored-findings parser (<see cref="FirmRejectionFindings.Parse"/>); replaceable by the test assembly to inject a parser fault.</summary>
    internal Func<string?, ILogger, Guid, FirmRejectionFindings.ParseResult> ParseRejectionFindings { get; init; } = FirmRejectionFindings.Parse;

    /// <param name="writerUserId">The <c>systemuserid</c> of the identity that creates every Decision Record (<c>BillingDecisionRecorder.WriterUserIdAsync</c>).
    /// Asked at most once per projection, and only when the invoice has decided lines to give a reason to; a fault, or an empty id, degrades the
    /// outcome to the status only (EB-D-208, 130a open question 3).</param>
    public FirmFindingsProjection(
        IGenericEntityService entities, FirmClauseReader clauses, Func<CancellationToken, Task<Guid>> writerUserId,
        ILogger<FirmFindingsProjection> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _clauses = clauses ?? throw new ArgumentNullException(nameof(clauses));
        _writerUserId = writerUserId ?? throw new ArgumentNullException(nameof(writerUserId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The firm outcome of <paramref name="invoiceId"/>, or null when the invoice does not exist. The caller has already established that the
    /// invoice may be shown; nothing here checks a caller.
    /// </summary>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    public async Task<FirmOutcome?> GetForInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        if (invoiceId == Guid.Empty)
        {
            return null;
        }

        InvoiceReviewReadService.InvoiceHeaderFacts? invoice;
        try
        {
            invoice = await InvoiceReviewReadService.ReadHeaderFactsAsync(_entities, invoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsFault(ex, ct))
        {
            LogFault(ex, "the invoice header", invoiceId);
            return Unavailable();
        }

        if (invoice is null)
        {
            return null;
        }

        var tracking = invoice.TrackingNumber;
        if (invoice.StatusCode is not { } statusCode)
        {
            // No status is not an unknown status: nothing is known about the invoice's state, so nothing may be told (130b fix round 2, K1).
            _logger.LogWarning("Invoice {Id} was read with no status code; the firm outcome is unavailable.", invoiceId);
            return Unavailable();
        }

        if (!InvoiceStatusCodes.TryFromStatusCode(statusCode, out var lifecycle))
        {
            _logger.LogWarning("Invoice {Id} has a status code ({StatusCode}) the firm outcome does not know; projecting it as closed, status only.",
                invoiceId, statusCode);
            return Closed(tracking, ClosedLabel);
        }

        var billed = invoice.Billed;
        switch (lifecycle)
        {
            case InvoiceLifecycleStatus.Rejected:
                return new FirmOutcome(FirmOutcomeKind.Rejected, tracking, "Rejected", billed, null, [], false);
            case InvoiceLifecycleStatus.ReturnedToFirm:
                return new FirmOutcome(FirmOutcomeKind.ReturnedToFirm, tracking, "Returned to firm", billed, null, [], false);
            case InvoiceLifecycleStatus.InReview:
            case InvoiceLifecycleStatus.Approved:
            case InvoiceLifecycleStatus.SentToAp:
            case InvoiceLifecycleStatus.Paid:
                break;
            case InvoiceLifecycleStatus.Cancelled:
                return Closed(tracking, "Cancelled");
            case InvoiceLifecycleStatus.Superseded:
                return Closed(tracking, "Replaced by a correction");
            default:
                // A lifecycle value added later: permanent until this contract learns it, so closed, not a fault.
                _logger.LogWarning("Invoice {Id} is in a state ({Lifecycle}) the firm outcome does not cover; projecting it as closed, status only.",
                    invoiceId, lifecycle);
                return Closed(tracking, ClosedLabel);
        }

        var decided = lifecycle != InvoiceLifecycleStatus.InReview;
        var statusOnly = lifecycle switch
        {
            InvoiceLifecycleStatus.InReview => new FirmOutcome(FirmOutcomeKind.InvoiceInReview, null, "In review", null, null, [], true),
            InvoiceLifecycleStatus.Approved => new FirmOutcome(FirmOutcomeKind.Approved, null, "Approved", null, null, [], true),
            InvoiceLifecycleStatus.SentToAp => new FirmOutcome(FirmOutcomeKind.SentToAp, null, "Sent to accounts payable", null, null, [], true),
            _ => new FirmOutcome(FirmOutcomeKind.Paid, null, "Paid", null, null, [], true),
        };

        IReadOnlyList<FirmOutcomeItem> items;
        try
        {
            items = await BuildItemsAsync(invoiceId, includeAdjustments: decided, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsFault(ex, ct))
        {
            LogFault(ex, "the items", invoiceId);
            return statusOnly;
        }

        var approved = decided ? invoice.Approved : null;
        return lifecycle switch
        {
            InvoiceLifecycleStatus.InReview => new FirmOutcome(FirmOutcomeKind.InvoiceInReview, tracking, "In review", billed, null, items, false),
            InvoiceLifecycleStatus.SentToAp => new FirmOutcome(FirmOutcomeKind.SentToAp, tracking, "Sent to accounts payable", billed, approved, items, false),
            InvoiceLifecycleStatus.Paid => new FirmOutcome(FirmOutcomeKind.Paid, tracking, "Paid", billed, approved, items, false),
            _ => new FirmOutcome(
                items.Any(i => i.Decision != FirmOutcomeDecision.Added) ? FirmOutcomeKind.ApprovedWithAdjustments : FirmOutcomeKind.Approved,
                tracking, "Approved", billed, approved, items, false),
        };
    }

    /// <summary>
    /// The firm outcome of <paramref name="submissionId"/>, or null when it does not exist. Received or Processing: status (and the billed total
    /// once its invoice exists and was decided) only; a fault reading that total is Degraded, never a null total. Accepted: the invoice's
    /// outcome. Rejected: the findings that rejected it, from the submission's OWN stored findings (130c, FR-18; never a live line, never a
    /// re-run of the extraction). Failed or an unknown status: <see cref="FirmOutcomeKind.Closed"/>.
    /// </summary>
    /// <param name="invoiceDecided">
    /// Whether the caller decided the submission's derived invoice by the invoice rule (the route's <c>FirmOutcomeScope.InvoiceDecided</c>; an
    /// internal caller that shows the invoice passes true). False (130b fix round 2, F1): nothing is read from that invoice. Received or
    /// Processing is the status with no billed total; Accepted is <see cref="FirmOutcomeKind.Unavailable"/> (a 503: the retry is decided
    /// afresh, now that the invoice exists). Rejected (130c): the findings still show their line and values (the submission's own), but with no
    /// citation and no firm reason, because both name the invoice (the clause reader and the Signals are keyed by it).
    /// </param>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    public async Task<FirmOutcome?> GetForSubmissionAsync(Guid submissionId, bool invoiceDecided, CancellationToken ct)
    {
        if (submissionId == Guid.Empty)
        {
            return null;
        }

        Entity? submission;
        try
        {
            submission = await ReadSubmissionAsync(submissionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsFault(ex, ct))
        {
            LogFault(ex, "the submission", submissionId);
            return Unavailable();
        }

        if (submission is null)
        {
            return null;
        }

        var tracking = submission.GetAttributeValue<string>("sprk_trackingnumber");
        var status = submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value;
        switch (status)
        {
            case (int)InvoiceSubmissionStatus.Received:
            case (int)InvoiceSubmissionStatus.Processing:
                var label = status == (int)InvoiceSubmissionStatus.Received ? "Received" : "Processing";
                if (!invoiceDecided)
                {
                    // The invoice was not decided (it did not exist when the caller checked): its total is not read, whether or not it exists now.
                    return new FirmOutcome(FirmOutcomeKind.SubmissionInProgress, tracking, label, null, null, [], false);
                }

                decimal? billed;
                try
                {
                    // The submission's own invoice (a derived id, never a link read from a column), once it exists.
                    var own = await InvoiceReviewReadService.ReadHeaderFactsAsync(
                        _entities, InvoiceSubmissionPersister.InvoiceIdFor(submissionId), ct).ConfigureAwait(false);
                    billed = own?.Billed;
                }
                catch (Exception ex) when (IsFault(ex, ct))
                {
                    // A total that could not be read is not "no total": the status only, Degraded (the route answers 503).
                    LogFault(ex, "the submission's invoice total", submissionId);
                    return new FirmOutcome(FirmOutcomeKind.SubmissionInProgress, null, label, null, null, [], true);
                }

                return new FirmOutcome(FirmOutcomeKind.SubmissionInProgress, tracking, label, billed, null, [], false);
            case (int)InvoiceSubmissionStatus.Accepted:
                if (!invoiceDecided)
                {
                    // The invoice was not decided: it did not exist when the caller checked (an Accepted submission whose invoice is missing, or an
                    // invoice created since). No invoice content without its decision. A 503; a retry decides it, or the inconsistency persists. Both
                    // warrant an alertable signal, so it carries the stable code (fix round 3, F2).
                    _logger.LogWarning("[{Code}] Submission {Id} is Accepted but its invoice was not decided by the caller (it may not exist); the firm outcome is unavailable.",
                        AcceptedWithoutInvoiceCode, submissionId);
                    return Unavailable();
                }

                var accepted = await GetForInvoiceAsync(InvoiceSubmissionPersister.InvoiceIdFor(submissionId), ct).ConfigureAwait(false);
                if (accepted is null)
                {
                    // Inconsistent data, not a fault that a retry clears: an Accepted submission always has its invoice. Logged with a stable code.
                    _logger.LogError("[{Code}] Submission {Id} is Accepted but its invoice does not exist; the firm outcome is unavailable.",
                        AcceptedWithoutInvoiceCode, submissionId);
                    return Unavailable();
                }

                return accepted;
            case (int)InvoiceSubmissionStatus.Rejected:
                return await ProjectRejectionAsync(submissionId, tracking, invoiceDecided, ct).ConfigureAwait(false);
            case (int)InvoiceSubmissionStatus.Failed:
                return Closed(tracking, "Failed");
            default:
                _logger.LogWarning("Submission {Id} has a status ({Status}) the firm outcome does not know; projecting it as closed, status only.",
                    submissionId, status);
                return Closed(tracking, ClosedLabel);
        }
    }

    // ── Items ──

    private sealed record Candidate(Entity Row, FirmOutcomeDecision Decision, string ActionCode);

    private async Task<IReadOnlyList<FirmOutcomeItem>> BuildItemsAsync(Guid invoiceId, bool includeAdjustments, CancellationToken ct)
    {
        var lineQuery = InvoiceReviewReadService.LineQuery(invoiceId);
        lineQuery.ColumnSet.AddColumn(InvoiceAdjustmentService.LineRule); // the review's column set omits the cited rule of an Adjustment line
        var rows = (await InvoiceReviewReadService.ReadPagedAsync(_entities, lineQuery, ct).ConfigureAwait(false))
            .Where(r => r.GetAttributeValue<EntityReference>(InvoiceTotalsService.LineInvoiceLookup)?.Id == invoiceId)
            .OrderBy(r => r.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) ?? 0)
            .ThenBy(r => r.Id)
            .ToList();

        var lineCandidates = new List<Candidate>();
        var adjustmentRows = new List<Entity>();
        foreach (var row in rows)
        {
            switch (row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value)
            {
                case (int)InvoiceLineCostType.Adjustment:
                    if (includeAdjustments)
                    {
                        adjustmentRows.Add(row);
                    }

                    break;
                case (int)InvoiceLineCostType.Fee:
                case (int)InvoiceLineCostType.Expense:
                    if (ExtractionProvenance.IsReviewerAdded(row))
                    {
                        lineCandidates.Add(new Candidate(row, FirmOutcomeDecision.Added, InvoiceLineAddService.ActionCode));
                    }
                    else
                    {
                        switch (row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value)
                        {
                            case (int)InvoiceLineStatus.Reduced:
                                lineCandidates.Add(new Candidate(row, FirmOutcomeDecision.Reduced, ReduceActionCode));
                                break;
                            case (int)InvoiceLineStatus.Disallowed:
                                lineCandidates.Add(new Candidate(row, FirmOutcomeDecision.Disallowed, DisallowActionCode));
                                break;
                        }
                    }

                    break;
            }
        }

        if (lineCandidates.Count == 0 && adjustmentRows.Count == 0)
        {
            return [];
        }

        // The reasons and the Signals each decision resolved.
        var records = lineCandidates.Count == 0
            ? new Dictionary<Guid, Entity>()
            : await ReadDecisionRecordsAsync(invoiceId, lineCandidates, ct).ConfigureAwait(false);
        var signals = records.Count == 0
            ? new List<Entity>()
            : await ReadSignalsAsync(invoiceId, records, ct).ConfigureAwait(false);
        var signalsOf = signals
            .GroupBy(s => s.GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.GetAttributeValue<DateTime?>("createdon")).ThenBy(s => s.Id).ToList());

        // The rules cited, resolved once: names and numbers, then one clause lookup per distinct policy version for this invoice.
        var adjustmentRules = adjustmentRows
            .Select(r => r.GetAttributeValue<EntityReference>(InvoiceAdjustmentService.LineRule)?.Id)
            .OfType<Guid>().Where(id => id != Guid.Empty);
        var versionIds = signals
            .Select(s => s.GetAttributeValue<EntityReference>("sprk_policyversion")?.Id)
            .OfType<Guid>().Where(id => id != Guid.Empty)
            .Concat(adjustmentRules)
            .Distinct().OrderBy(id => id).ToList();
        var rules = await ReadRulesAsync(versionIds, ct).ConfigureAwait(false);
        var clauses = new Dictionary<Guid, FirmClauseText>();
        foreach (var versionId in rules.Keys)
        {
            clauses[versionId] = await ReadClauseAsync(versionId, invoiceId, ct).ConfigureAwait(false);
        }

        // Fix round 2: only the versions the policy's firm flag publishes (clause text) and that are not an InternalStandard Procedure's.
        var citable = await CitableVersionsAsync(rules, clauses, invoiceId, ct).ConfigureAwait(false);

        var items = new List<FirmOutcomeItem>(lineCandidates.Count + adjustmentRows.Count);
        foreach (var candidate in lineCandidates)
        {
            var row = candidate.Row;
            var record = records.TryGetValue(row.Id, out var chosen) && string.Equals(
                chosen.GetAttributeValue<string>("sprk_actioncode"), candidate.ActionCode, StringComparison.Ordinal) ? chosen : null;
            var citations = new List<FirmOutcomeCitation>();
            var reasonSignals = new List<Entity>();
            if (record is not null && signalsOf.TryGetValue(record.Id, out var resolved))
            {
                // Every Billing rule the decision resolved gives its firm reason (not gated: it is addressed to the firm); only a citable one
                // is cited (fix round 2).
                foreach (var byVersion in resolved
                    .Where(s => s.GetAttributeValue<EntityReference>("sprk_policyversion")?.Id is { } v && rules.ContainsKey(v))
                    .GroupBy(s => s.GetAttributeValue<EntityReference>("sprk_policyversion")!.Id))
                {
                    // The version's reason is its first non-blank Signal's; that Signal's creation (then id) orders the reasons (fix round 3, K4).
                    var reasonSignal = byVersion.FirstOrDefault(s => NullIfBlank(s.GetAttributeValue<string>("sprk_firmreason")) is not null);
                    if (reasonSignal is not null)
                    {
                        reasonSignals.Add(reasonSignal);
                    }

                    if (citable.Contains(byVersion.Key))
                    {
                        citations.Add(Cite(rules[byVersion.Key], clauses[byVersion.Key]));
                    }
                }
            }

            var firmReasons = new List<string>();
            foreach (var reasonSignal in reasonSignals.OrderBy(s => s.GetAttributeValue<DateTime?>("createdon")).ThenBy(s => s.Id))
            {
                var firmReason = NullIfBlank(reasonSignal.GetAttributeValue<string>("sprk_firmreason"))!;
                if (!firmReasons.Contains(firmReason, StringComparer.Ordinal))
                {
                    firmReasons.Add(firmReason);
                }
            }

            items.Add(new FirmOutcomeItem(
                candidate.Decision,
                row.Id,
                new FirmOutcomeLine(
                    DateOf(row, InvoiceSubmissionPersister.LineEventDate),
                    NullIfBlank(row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTimekeeper)),
                    Excerpt(row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineDescription)),
                    Money(row, InvoiceTotalsService.LineAmount) ?? 0m,
                    Money(row, InvoiceTotalsService.LineApprovedAmount)),
                null,
                record is null ? null : NullIfBlank(record.GetAttributeValue<string>("sprk_reason")),
                firmReasons,
                citations.OrderBy(c => c.RuleName, StringComparer.Ordinal).ToList()));
        }

        foreach (var row in adjustmentRows)
        {
            var citations = new List<FirmOutcomeCitation>();
            if (row.GetAttributeValue<EntityReference>(InvoiceAdjustmentService.LineRule)?.Id is { } versionId && citable.Contains(versionId))
            {
                citations.Add(Cite(rules[versionId], clauses[versionId]));
            }

            var method = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineAdjustmentMethod)?.Value;
            items.Add(new FirmOutcomeItem(
                FirmOutcomeDecision.Adjustment,
                null,
                null,
                new FirmOutcomeAdjustment(
                    method is { } m && Enum.IsDefined(typeof(InvoiceAdjustmentMethod), m) ? ((InvoiceAdjustmentMethod)m).ToString() : "Unknown",
                    row.TryGetAttributeValue<object>(InvoiceTotalsService.LineAdjustmentValue, out var value) ? AsDecimal(value) : null,
                    Money(row, InvoiceTotalsService.LineAmount) ?? 0m),
                NullIfBlank(row.GetAttributeValue<string>(InvoiceReviewReadService.LineReason)),
                [],
                citations));
        }

        return items;
    }

    /// <summary>
    /// A citable rule's citation (<see cref="CitableVersionsAsync"/> decided it may be shown): the rule name and the label, version, section
    /// and quote the clause reader published. Never called for a version that is not citable: such a rule has no citation.
    /// </summary>
    private static FirmOutcomeCitation Cite(RuleRead rule, FirmClauseText clause) =>
        new(rule.Name, clause.FirmFacingName, rule.Number, clause.SectionReference, clause.Quote);

    /// <summary>A clause the reader published: a quote or a section reference (a label alone is not a clause).</summary>
    private static bool Published(FirmClauseText clause) => clause.Quote is not null || clause.SectionReference is not null;

    /// <summary>
    /// The clause the firm may be shown for <paramref name="versionId"/> on this invoice (<see cref="FirmClauseReader"/>: the flag and the
    /// snapshot). A fault reading it is no clause, so the citation is omitted (fail closed; 130b fix round 2), never a guess.
    /// </summary>
    private async Task<FirmClauseText> ReadClauseAsync(Guid versionId, Guid invoiceId, CancellationToken ct)
    {
        try
        {
            return await _clauses(versionId, invoiceId, ct).ConfigureAwait(false) ?? FirmClauseText.None;
        }
        catch (Exception ex) when (IsFault(ex, ct))
        {
            _logger.LogWarning("A firm clause read faulted ({ExceptionType}) for invoice {Id}; that rule is not cited.", ex.GetType().Name, invoiceId);
            _logger.LogDebug(ex, "Firm clause read fault detail");
            return FirmClauseText.None;
        }
    }

    /// <summary>
    /// The versions a firm may see cited (130b fix round 2): the clause reader published text for them (the policy's firm flag on and the
    /// invoice's snapshot applied them) AND their rule set is not an InternalStandard Procedure's, whatever that set's flag says. A rule set with
    /// no Procedure (a legacy or a record rule set) is decided by its flag alone. Fails closed: a rule set or Procedure that cannot be read, a
    /// Procedure with no type, or a fault reading either, cites nothing. Reads only the rule set's Procedure link and the Procedure's type,
    /// never a name.
    /// </summary>
    private async Task<HashSet<Guid>> CitableVersionsAsync(
        IReadOnlyDictionary<Guid, RuleRead> rules, IReadOnlyDictionary<Guid, FirmClauseText> clauses, Guid invoiceId, CancellationToken ct)
    {
        var published = rules.Keys.Where(v => Published(clauses[v]) && rules[v].RuleSetId is not null).ToList();
        if (published.Count == 0)
        {
            return [];
        }

        try
        {
            var setIds = published.Select(v => rules[v].RuleSetId!.Value).Distinct().OrderBy(id => id).ToList();
            var procedureOf = new Dictionary<Guid, Guid?>();
            foreach (var set in await ReadByIdsAsync(RuleSetService.RuleSetEntity, setIds, RuleSetService.ProcedureColumn, ct).ConfigureAwait(false))
            {
                procedureOf[set.Id] = set.GetAttributeValue<EntityReference>(RuleSetService.ProcedureColumn)?.Id;
            }

            var procedureIds = procedureOf.Values.OfType<Guid>().Distinct().OrderBy(id => id).ToList();
            var typeOf = new Dictionary<Guid, int?>();
            foreach (var procedure in await ReadByIdsAsync(ProcedureEntity.Entity, procedureIds, ProcedureEntity.TypeColumn, ct).ConfigureAwait(false))
            {
                typeOf[procedure.Id] = procedure.GetAttributeValue<OptionSetValue>(ProcedureEntity.TypeColumn)?.Value;
            }

            return published.Where(v => procedureOf.TryGetValue(rules[v].RuleSetId!.Value, out var procedureId)
                    && (procedureId is null
                        || (typeOf.TryGetValue(procedureId.Value, out var type) && type is { } t && t != (int)ProcedureType.InternalStandard)))
                .ToHashSet();
        }
        catch (Exception ex) when (IsFault(ex, ct))
        {
            _logger.LogWarning("A firm outcome read faulted ({ExceptionType}) deciding the citations of invoice {Id}; no rule is cited.",
                ex.GetType().Name, invoiceId);
            _logger.LogDebug(ex, "Firm citation decision fault detail");
            return [];
        }
    }

    /// <summary>The rows of <paramref name="entity"/> with these ids, one column (app-only, chunked). Rows not asked for are dropped.</summary>
    private async Task<List<Entity>> ReadByIdsAsync(string entity, IReadOnlyList<Guid> ids, string column, CancellationToken ct)
    {
        var rows = new List<Entity>();
        foreach (var chunk in ids.Chunk(ChunkSize))
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(column),
                Criteria = new FilterExpression(LogicalOperator.And),
                PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
                NoLock = true,
            };
            query.Criteria.AddCondition($"{entity}id", ConditionOperator.In, chunk.Cast<object>().ToArray());
            query.AddOrder($"{entity}id", OrderType.Ascending);
            rows.AddRange(await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false));
        }

        return rows.Where(r => ids.Contains(r.Id)).ToList();
    }

    // ── Reads (app-only, each bounded by this invoice) ──

    private async Task<Entity?> ReadSubmissionAsync(Guid submissionId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(InvoiceSubmissionPersister.SubmissionStatus, "sprk_trackingnumber"),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition(FinanceBaseValues.SubmissionKeyColumn, ConditionOperator.Equal, submissionId);
        var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return page.Entities.FirstOrDefault(r => r.Id == submissionId);
    }

    /// <summary>
    /// The line decision records of the candidates, one per line (the newest of the line's records with the action code its status needs).
    /// A record counts only when it carries THIS invoice's line's link and, if it names an invoice, this invoice; a record with neither link
    /// of this invoice is never returned, whatever the query gave back (EB-D-202). <b>And only when the BFF writer identity created it</b>
    /// (<see cref="WriterCreatedRecord"/>, EB-D-208, the rule 165b applies): a user can create a Decision Record with these action codes and a
    /// line link, so the link alone does not make a record the server's decision. A record the writer did not create is never projected: its
    /// line stays, with a null reason. The writer id is asked here, once, because only here is a reason wanted; a fault propagates and the
    /// caller degrades to the status only. Keyed by line id.
    /// </summary>
    private async Task<Dictionary<Guid, Entity>> ReadDecisionRecordsAsync(Guid invoiceId, IReadOnlyList<Candidate> candidates, CancellationToken ct)
    {
        var writer = await _writerUserId(ct).ConfigureAwait(false);
        var lineIds = candidates.Select(c => c.Row.Id).ToHashSet();
        var codeOf = candidates.ToDictionary(c => c.Row.Id, c => c.ActionCode);
        var found = new List<Entity>();
        foreach (var chunk in lineIds.OrderBy(id => id).Chunk(ChunkSize))
        {
            var query = new QueryExpression(RecordEntity)
            {
                ColumnSet = new ColumnSet(RecordColumns),
                Criteria = new FilterExpression(LogicalOperator.And),
                PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
                NoLock = true,
            };
            query.Criteria.AddCondition("sprk_regardinginvoiceline", ConditionOperator.In, chunk.Cast<object>().ToArray());
            query.Criteria.AddCondition("sprk_actioncode", ConditionOperator.In, ReduceActionCode, DisallowActionCode, InvoiceLineAddService.ActionCode);
            WriterCreatedRecord.AddConditions(query.Criteria, writer); // throws for an empty id: the caller degrades, it is never read as "no record"
            query.AddOrder($"{RecordEntity}id", OrderType.Ascending);
            found.AddRange(await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false));
        }

        var chosen = new Dictionary<Guid, Entity>();
        foreach (var record in found
            .Where(r => r.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline")?.Id is { } line && lineIds.Contains(line)
                && (r.GetAttributeValue<EntityReference>("sprk_regardinginvoice") is not { } invoice || invoice.Id == invoiceId))
            .OrderBy(r => r.GetAttributeValue<DateTime?>("sprk_decidedon") ?? r.GetAttributeValue<DateTime?>("createdon"))
            .ThenBy(r => r.Id))
        {
            var lineId = record.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline")!.Id;
            if (string.Equals(record.GetAttributeValue<string>("sprk_actioncode"), codeOf[lineId], StringComparison.Ordinal))
            {
                chosen[lineId] = record; // ascending: the newest of the matching records wins
            }
        }

        return chosen;
    }

    /// <summary>
    /// The Signals the chosen records resolved (<c>sprk_signal.sprk_decisionrecord</c>, the decision's link to its citations). A Signal counts
    /// only when it names one of those records and its own invoice and line links, where present, are this invoice and the record's line.
    /// </summary>
    private async Task<List<Entity>> ReadSignalsAsync(Guid invoiceId, IReadOnlyDictionary<Guid, Entity> recordsByLine, CancellationToken ct)
    {
        var lineOfRecord = recordsByLine.ToDictionary(p => p.Value.Id, p => p.Key);
        var found = new List<Entity>();
        foreach (var chunk in lineOfRecord.Keys.OrderBy(id => id).Chunk(ChunkSize))
        {
            var query = new QueryExpression(SignalEntity)
            {
                ColumnSet = new ColumnSet(SignalColumns),
                Criteria = new FilterExpression(LogicalOperator.And),
                PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
                NoLock = true,
            };
            query.Criteria.AddCondition("sprk_decisionrecord", ConditionOperator.In, chunk.Cast<object>().ToArray());
            query.AddOrder($"{SignalEntity}id", OrderType.Ascending);
            found.AddRange(await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false));
        }

        return found
            .Where(s => s.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id is { } record && lineOfRecord.ContainsKey(record)
                && (s.GetAttributeValue<EntityReference>("sprk_regardinginvoice") is not { } invoice || invoice.Id == invoiceId)
                && (s.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline") is not { } line || line.Id == lineOfRecord[record]))
            .ToList();
    }

    /// <param name="RuleSetId">The policy's rule set (for the Procedure-type decision only; never shown).</param>
    private sealed record RuleRead(string? Name, int? Number, Guid? RuleSetId);

    /// <summary>The rule name (the policy's, never the rule set's), version number and rule set of each version; a version whose Billing policy cannot be read is left out.</summary>
    private async Task<Dictionary<Guid, RuleRead>> ReadRulesAsync(IReadOnlyList<Guid> versionIds, CancellationToken ct)
    {
        var rules = new Dictionary<Guid, RuleRead>();
        if (versionIds.Count == 0)
        {
            return rules;
        }

        var versions = new List<Entity>();
        foreach (var chunk in versionIds.Chunk(ChunkSize))
        {
            var query = new QueryExpression(RuleSetService.VersionEntity)
            {
                ColumnSet = new ColumnSet("sprk_policy", "sprk_versionnumber"),
                Criteria = new FilterExpression(LogicalOperator.And),
                PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
                NoLock = true,
            };
            query.Criteria.AddCondition("sprk_policyversionid", ConditionOperator.In, chunk.Cast<object>().ToArray());
            query.AddOrder("sprk_policyversionid", OrderType.Ascending);
            versions.AddRange(await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false));
        }

        var policyIds = versions
            .Select(v => v.GetAttributeValue<EntityReference>("sprk_policy")?.Id).OfType<Guid>().Where(id => id != Guid.Empty)
            .Distinct().OrderBy(id => id).ToList();
        var policies = new Dictionary<Guid, Entity>();
        foreach (var chunk in policyIds.Chunk(ChunkSize))
        {
            var query = new QueryExpression(RuleSetService.PolicyEntity)
            {
                ColumnSet = new ColumnSet("sprk_name", RuleDomains.PolicyDomainColumn, "sprk_ruleset"),
                Criteria = new FilterExpression(LogicalOperator.And),
                PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
                NoLock = true,
            };
            query.Criteria.AddCondition("sprk_policyid", ConditionOperator.In, chunk.Cast<object>().ToArray());
            query.AddOrder("sprk_policyid", OrderType.Ascending);
            foreach (var policy in await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false))
            {
                policies[policy.Id] = policy;
            }
        }

        foreach (var version in versions.Where(v => versionIds.Contains(v.Id)))
        {
            if (version.GetAttributeValue<EntityReference>("sprk_policy")?.Id is { } policyId
                && policies.TryGetValue(policyId, out var policy) && RuleDomains.IsBillingRow(policy))
            {
                rules[version.Id] = new RuleRead(
                    NullIfBlank(policy.GetAttributeValue<string>("sprk_name")), version.GetAttributeValue<int?>("sprk_versionnumber"),
                    policy.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id);
            }
        }

        return rules;
    }

    // ── Rejection findings (130c, FR-18) ──

    private static readonly string[] RejectionSignalColumns =
    [
        "sprk_policyversion", "sprk_firmreason", "sprk_regardinginvoice", "sprk_regardinginvoiceline", "createdon",
    ];

    /// <summary>
    /// A Rejected submission's outcome: the findings that rejected it, from the submission's OWN stored findings. A fault reading them, or
    /// anything after, is the status only, Degraded (fail closed); a stored copy with no usable rejection finding is the status only, not a fault.
    /// </summary>
    private async Task<FirmOutcome> ProjectRejectionAsync(Guid submissionId, string? tracking, bool invoiceDecided, CancellationToken ct)
    {
        string? json;
        try
        {
            json = await ReadFindingsJsonAsync(submissionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsFault(ex, ct))
        {
            LogFault(ex, "the submission's stored findings", submissionId);
            return new FirmOutcome(FirmOutcomeKind.SubmissionRejected, tracking, "Rejected", null, null, [], true);
        }

        FirmRejectionFindings.ParseResult parsed;
        try
        {
            parsed = ParseRejectionFindings(json, _logger, submissionId);
        }
        catch (Exception ex) when (IsFault(ex, ct))
        {
            // Whatever the cause, a stored copy that cannot be parsed is an unreadable one: the status only, with the stable code (no content).
            _logger.LogDebug(ex, "Firm outcome rejection findings parse fault detail");
            FirmRejectionFindings.LogUnreadable(_logger, submissionId);
            return new FirmOutcome(FirmOutcomeKind.SubmissionRejected, tracking, "Rejected", null, null, [], false);
        }

        var findings = parsed.Findings;
        if (findings.Count == 0)
        {
            return new FirmOutcome(FirmOutcomeKind.SubmissionRejected, tracking, "Rejected", null, null, [], false);
        }

        try
        {
            var items = await BuildRejectionItemsAsync(submissionId, findings, invoiceDecided, ct).ConfigureAwait(false);

            // Findings past the cap are left out, but the firm is told how many, so a capped list is never read as the whole of it.
            return new FirmOutcome(
                FirmOutcomeKind.SubmissionRejected, tracking, "Rejected", null, null, items, false, parsed.NotShown > 0 ? parsed.NotShown : null);
        }
        catch (Exception ex) when (IsFault(ex, ct))
        {
            LogFault(ex, "the rejection findings", submissionId);
            return new FirmOutcome(FirmOutcomeKind.SubmissionRejected, tracking, "Rejected", null, null, [], true);
        }
    }

    /// <summary>
    /// One item per rejection finding: its one-based line number and the values read (both from the stored finding), and, only for the
    /// invoice the caller decided: the citation as the firm flag publishes it (exactly as an invoice item's, 130b fix round 2) and the firm
    /// reason of the writer-created Signal that finding raised (independent of the citation gate). Reads no live invoice line and runs no
    /// extraction.
    /// </summary>
    private async Task<IReadOnlyList<FirmOutcomeItem>> BuildRejectionItemsAsync(
        Guid submissionId, IReadOnlyList<FirmRejectionFindings.Finding> findings, bool invoiceDecided, CancellationToken ct)
    {
        var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(submissionId);
        var rules = new Dictionary<Guid, RuleRead>();
        var clauses = new Dictionary<Guid, FirmClauseText>();
        var citable = new HashSet<Guid>();
        var signals = new List<Entity>();
        if (invoiceDecided)
        {
            // The clause reader and the Signals are keyed by the invoice, so they are asked only for an invoice the caller decided.
            var versionIds = findings.Select(f => f.PolicyVersionId).Distinct().OrderBy(id => id).ToList();
            rules = await ReadRulesAsync(versionIds, ct).ConfigureAwait(false);
            foreach (var versionId in rules.Keys)
            {
                clauses[versionId] = await ReadClauseAsync(versionId, invoiceId, ct).ConfigureAwait(false);
            }

            citable = await CitableVersionsAsync(rules, clauses, invoiceId, ct).ConfigureAwait(false);
            if (rules.Count > 0)
            {
                var writer = await _writerUserId(ct).ConfigureAwait(false);
                signals = await ReadRejectionSignalsAsync(invoiceId, rules.Keys.ToList(), writer, ct).ConfigureAwait(false);
            }
        }

        var items = new List<FirmOutcomeItem>(findings.Count);
        foreach (var finding in findings.OrderBy(f => f.LineIndex ?? int.MaxValue))
        {
            var version = finding.PolicyVersionId;
            var lineId = finding.LineIndex is { } index ? InvoiceSubmissionPersister.LineIdFor(submissionId, index) : (Guid?)null;
            var reason = signals
                .Where(s => s.GetAttributeValue<EntityReference>("sprk_policyversion")?.Id == version
                    && s.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline")?.Id == lineId)
                .OrderBy(s => s.GetAttributeValue<DateTime?>("createdon")).ThenBy(s => s.Id)
                .Select(s => NullIfBlank(s.GetAttributeValue<string>("sprk_firmreason")))
                .FirstOrDefault(r => r is not null);

            items.Add(new FirmOutcomeItem(
                FirmOutcomeDecision.RejectionFinding,
                null,
                null,
                null,
                null,
                reason is null ? [] : [reason],
                citable.Contains(version) ? [Cite(rules[version], clauses[version])] : [],
                finding.LineIndex is { } zeroBased ? zeroBased + 1 : null,
                finding.ValuesRead.Select(v => new FirmOutcomeValueRead(v.Name, v.Value)).ToList()));
        }

        return items;
    }

    /// <summary>The submission's stored findings text (app-only; the column is field-secured to the BFF identity). Null when absent.</summary>
    private async Task<string?> ReadFindingsJsonAsync(Guid submissionId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(InvoiceSubmissionPersister.SubmissionFindingsJson),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition(FinanceBaseValues.SubmissionKeyColumn, ConditionOperator.Equal, submissionId);
        var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return page.Entities.FirstOrDefault(r => r.Id == submissionId)?.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
    }

    /// <summary>
    /// The Signals the BFF writer created for the decided invoice and these rule versions (<see cref="WriterCreatedRecord"/>: a Signal anyone
    /// else created is never read; the writer id comes from the caller and a fault propagates, so an unknown writer is never "no Signal").
    /// A Signal counts only when it names this invoice and one of the versions, whatever the query gave back.
    /// </summary>
    private async Task<List<Entity>> ReadRejectionSignalsAsync(Guid invoiceId, IReadOnlyList<Guid> versionIds, Guid writer, CancellationToken ct)
    {
        var found = new List<Entity>();
        foreach (var chunk in versionIds.OrderBy(id => id).Chunk(ChunkSize))
        {
            var query = new QueryExpression(SignalEntity)
            {
                ColumnSet = new ColumnSet(RejectionSignalColumns),
                Criteria = new FilterExpression(LogicalOperator.And),
                PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
                NoLock = true,
            };
            query.Criteria.AddCondition("sprk_regardinginvoice", ConditionOperator.Equal, invoiceId);
            query.Criteria.AddCondition("sprk_policyversion", ConditionOperator.In, chunk.Cast<object>().ToArray());
            WriterCreatedRecord.AddConditions(query.Criteria, writer);
            query.AddOrder($"{SignalEntity}id", OrderType.Ascending);
            found.AddRange(await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false));
        }

        return found
            .Where(s => s.GetAttributeValue<EntityReference>("sprk_regardinginvoice")?.Id == invoiceId
                && s.GetAttributeValue<EntityReference>("sprk_policyversion")?.Id is { } version && versionIds.Contains(version))
            .ToList();
    }

    // ── Helpers ──

    private static FirmOutcome Unavailable() => new(FirmOutcomeKind.Unavailable, null, "Unavailable", null, null, [], true);

    /// <summary>The label of a status this contract does not know.</summary>
    internal const string ClosedLabel = "Closed";

    /// <summary>The stable log code of an Accepted submission whose invoice does not exist (inconsistent data; 130b fix round 2).</summary>
    internal const string AcceptedWithoutInvoiceCode = "EBILLING-FIRM-OUTCOME-ACCEPTED-WITHOUT-INVOICE";

    /// <summary>A permanent state: the status label and tracking number only, no items, no totals, not degraded.</summary>
    private static FirmOutcome Closed(string? tracking, string label) => new(FirmOutcomeKind.Closed, tracking, label, null, null, [], false);

    private static bool IsFault(Exception ex, CancellationToken ct) => ex is not OperationCanceledException || !ct.IsCancellationRequested;

    /// <summary>Information and above carry the exception type and the id only (a Dataverse fault message can name records).</summary>
    private void LogFault(Exception ex, string what, Guid id)
    {
        _logger.LogWarning("A firm outcome read faulted ({ExceptionType}) reading {What} of {Id}; only the status is projected.", ex.GetType().Name, what, id);
        _logger.LogDebug(ex, "Firm outcome read fault detail");
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The first <see cref="ExcerptLength"/> characters of the description, whitespace collapsed; null when blank.</summary>
    internal static string? Excerpt(string? description)
    {
        var text = NullIfBlank(description);
        if (text is null)
        {
            return null;
        }

        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= ExcerptLength ? text : text[..ExcerptLength].TrimEnd() + "...";
    }

    private static DateOnly? DateOf(Entity row, string column) =>
        row.GetAttributeValue<DateTime?>(column) is { } date ? DateOnly.FromDateTime(date) : null;

    private static decimal? Money(Entity row, string column) => row.GetAttributeValue<Money>(column)?.Value;

    private static decimal? AsDecimal(object? raw) => raw switch
    {
        decimal d => d,
        double d => (decimal)d,
        float f => (decimal)f,
        int i => i,
        long l => l,
        _ => null,
    };
}
