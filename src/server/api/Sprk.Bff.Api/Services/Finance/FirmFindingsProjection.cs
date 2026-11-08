using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
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

    /// <summary>The submission was rejected (the rejection findings are not projected yet; see the 130a notes).</summary>
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

    /// <summary>Nothing may be told: a closed state this contract does not cover, or a read that did not complete (fail closed).</summary>
    Unavailable,
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
}

/// <summary>
/// The firm-visible outcome of one invoice or submission. <see cref="ApprovedTotal"/> is null for a state that has not been decided.
/// <see cref="Degraded"/> is true when a read did not complete: the outcome then carries the status only (fail closed).
/// </summary>
/// <param name="TrackingNumber">The tracking number the firm was given on submission.</param>
/// <param name="StatusLabel">What the firm sees as the status.</param>
public sealed record FirmOutcome(
    FirmOutcomeKind Kind,
    string? TrackingNumber,
    string StatusLabel,
    decimal? BilledTotal,
    decimal? ApprovedTotal,
    IReadOnlyList<FirmOutcomeItem> Items,
    bool Degraded);

/// <summary>One decided or reviewer-added line, or one invoice-level adjustment, with the reviewer's reason and the rules behind it.</summary>
/// <param name="LineId">The invoice line, for a line item; null for an adjustment.</param>
/// <param name="Line">The line as the firm billed it (or the reviewer added it); null for an adjustment.</param>
/// <param name="Adjustment">The adjustment; null for a line item.</param>
/// <param name="Reason">The reviewer's reason; null when none is on record for this item (never substituted).</param>
public sealed record FirmOutcomeItem(
    FirmOutcomeDecision Decision,
    Guid? LineId,
    FirmOutcomeLine? Line,
    FirmOutcomeAdjustment? Adjustment,
    string? Reason,
    IReadOnlyList<FirmOutcomeCitation> Citations);

/// <param name="DescriptionExcerpt">The first part of the line's description as billed.</param>
/// <param name="ApprovedAmount">The reviewer's figure for the line.</param>
public sealed record FirmOutcomeLine(
    DateOnly? Date, string? TimekeeperAsBilled, string? DescriptionExcerpt, decimal BilledAmount, decimal? ApprovedAmount);

/// <param name="Method"><c>Percent</c> or <c>Fixed</c>.</param>
/// <param name="Amount">The computed adjustment.</param>
public sealed record FirmOutcomeAdjustment(string Method, decimal? Value, decimal Amount);

/// <summary>
/// The rule behind an item. With the rule set's visible-to-firms flag off (or no clause text for any other reason) only <see cref="RuleName"/>
/// and <see cref="FirmReason"/> are set; with it on the label, version, section reference and quote are set too (D-66, D-79).
/// </summary>
/// <param name="RuleName">The rule's (policy's) name. Never the rule set's name.</param>
/// <param name="FirmReason">The rule's firm-visible reason (<c>sprk_signal.sprk_firmreason</c>); null when the Signal has none.</param>
/// <param name="Label">The Procedure's firm-facing name, or "Billing instructions for this record"; null for a legacy rule set.</param>
public sealed record FirmOutcomeCitation(string? RuleName, string? FirmReason, string? Label, int? Version, string? SectionReference, string? Quote);

/// <summary>The clause text a firm may be shown for a rule version on an invoice: <see cref="RuleSetService.GetFirmClauseAsync"/>.</summary>
public delegate Task<FirmClauseText> FirmClauseReader(Guid policyVersionId, Guid invoiceId, CancellationToken ct);

/// <summary>
/// THE server-side projection of firm-visible outcome content (ebilling-r1 task 130a; FR-38, D-66, EB-D-202). Read-only. It has no caller
/// check: the CALLER of this class has already established that the invoice may be shown (the external route's firm and core-record checks,
/// or an internal job); every id passed in is one the caller read server-side.
/// </summary>
/// <remarks>
/// <para><b>What is shown.</b> A submission Received or Processing: status and billed total only. An invoice In Review: status, billed total,
/// its reviewer-added lines and its decided lines (Reduced, Disallowed) with the reviewer's reason; no approved total and no invoice-level
/// adjustment (EB-D-202). Approved, Sent to AP and Paid: the same items plus the invoice-level adjustments, and the approved total. Rejected
/// and Returned to Firm: status and billed total. Pending, Flagged, Clean, Accepted, overridden and AI-observation findings never appear: an
/// item exists only because a line was decided, added or adjusted by a reviewer.</para>
/// <para><b>Where each part comes from (all read app-only).</b> The line and its status: the invoice's lines
/// (<see cref="InvoiceReviewReadService.LineQuery"/>). The reason: the <c>reduce-line</c>, <c>disallow-line</c> or <c>add-line</c> Decision Record
/// that carries the LINE's link (and, if it names an invoice, this one); a record with neither link of this invoice is never projected. The
/// rules: the Signals that record resolved (<c>sprk_signal.sprk_decisionrecord</c>), their policy version and <c>sprk_firmreason</c>. An invoice
/// adjustment writes no Decision Record: its reason is <c>sprk_adjustmentreason</c> on the line and its rule is <c>sprk_adjustmentrule</c>.
/// The clause: <see cref="FirmClauseReader"/> once per distinct policy version per invoice. Reviewer identity, internal notes, confidence
/// scores and Signal messages are never read into the output.</para>
/// <para><b>Visibility rule.</b> A citation carries the label, version, section reference and quote only when the clause reader returned text
/// (the rule set's flag on, the version applied by the invoice's snapshot). In every other case, including a lookup fault, it carries the rule
/// name and the firm reason only. There is no member for a Procedure Library document, container, drive item or link.</para>
/// <para><b>Fail closed.</b> No exception other than cancellation leaves this class. A fault reading the header gives an
/// <see cref="FirmOutcomeKind.Unavailable"/> outcome; a fault after it gives the status only, flagged <see cref="FirmOutcome.Degraded"/>.</para>
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

    private static readonly string[] HeaderColumns =
    [
        "statuscode", InvoiceReviewService.InvTrackingNumber, InvoiceTotalsService.InvoiceBilledTotal, InvoiceTotalsService.InvoiceApprovedTotal,
    ];

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
    private readonly ILogger<FirmFindingsProjection> _logger;

    public FirmFindingsProjection(IGenericEntityService entities, FirmClauseReader clauses, ILogger<FirmFindingsProjection> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _clauses = clauses ?? throw new ArgumentNullException(nameof(clauses));
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

        Entity? invoice;
        try
        {
            invoice = await ReadInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
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

        var statusCode = invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 0;
        if (!InvoiceStatusCodes.TryFromStatusCode(statusCode, out var lifecycle))
        {
            return Unavailable();
        }

        var tracking = invoice.GetAttributeValue<string>(InvoiceReviewService.InvTrackingNumber);
        var billed = Money(invoice, InvoiceTotalsService.InvoiceBilledTotal);
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
            default:
                // Cancelled, Superseded: this contract tells a firm nothing about them.
                return Unavailable();
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

        var approved = decided ? Money(invoice, InvoiceTotalsService.InvoiceApprovedTotal) : null;
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
    /// once its invoice exists) only. Accepted: the invoice's outcome. Rejected: status only (the rejection findings are not projected yet).
    /// Failed or unknown: nothing.
    /// </summary>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    public async Task<FirmOutcome?> GetForSubmissionAsync(Guid submissionId, CancellationToken ct)
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
                decimal? billed = null;
                try
                {
                    // The submission's own invoice (a derived id, never a link read from a column), once it exists.
                    var own = await ReadInvoiceAsync(InvoiceSubmissionPersister.InvoiceIdFor(submissionId), ct).ConfigureAwait(false);
                    billed = own is null ? null : Money(own, InvoiceTotalsService.InvoiceBilledTotal);
                }
                catch (Exception ex) when (IsFault(ex, ct))
                {
                    LogFault(ex, "the submission's invoice total", submissionId);
                }

                return new FirmOutcome(
                    FirmOutcomeKind.SubmissionInProgress, tracking, status == (int)InvoiceSubmissionStatus.Received ? "Received" : "Processing",
                    billed, null, [], false);
            case (int)InvoiceSubmissionStatus.Accepted:
                return await GetForInvoiceAsync(InvoiceSubmissionPersister.InvoiceIdFor(submissionId), ct).ConfigureAwait(false)
                    ?? Unavailable();
            case (int)InvoiceSubmissionStatus.Rejected:
                return new FirmOutcome(FirmOutcomeKind.SubmissionRejected, tracking, "Rejected", null, null, [], false);
            default:
                return Unavailable();
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
            clauses[versionId] = await _clauses(versionId, invoiceId, ct).ConfigureAwait(false) ?? FirmClauseText.None;
        }

        var items = new List<FirmOutcomeItem>(lineCandidates.Count + adjustmentRows.Count);
        foreach (var candidate in lineCandidates)
        {
            var row = candidate.Row;
            var record = records.TryGetValue(row.Id, out var chosen) && string.Equals(
                chosen.GetAttributeValue<string>("sprk_actioncode"), candidate.ActionCode, StringComparison.Ordinal) ? chosen : null;
            var citations = new List<FirmOutcomeCitation>();
            if (record is not null && signalsOf.TryGetValue(record.Id, out var resolved))
            {
                foreach (var byVersion in resolved
                    .Where(s => s.GetAttributeValue<EntityReference>("sprk_policyversion")?.Id is { } v && rules.ContainsKey(v))
                    .GroupBy(s => s.GetAttributeValue<EntityReference>("sprk_policyversion")!.Id))
                {
                    var firmReason = byVersion.Select(s => NullIfBlank(s.GetAttributeValue<string>("sprk_firmreason"))).FirstOrDefault(t => t is not null);
                    citations.Add(Cite(rules[byVersion.Key], clauses[byVersion.Key], firmReason));
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
                citations.OrderBy(c => c.RuleName, StringComparer.Ordinal).ToList()));
        }

        foreach (var row in adjustmentRows)
        {
            var citations = new List<FirmOutcomeCitation>();
            if (row.GetAttributeValue<EntityReference>(InvoiceAdjustmentService.LineRule)?.Id is { } versionId && rules.ContainsKey(versionId))
            {
                citations.Add(Cite(rules[versionId], clauses[versionId], firmReason: null));
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
                citations));
        }

        return items;
    }

    /// <summary>
    /// A rule's citation under the visibility rule: the label, version, section and quote only when the clause reader returned text for this
    /// version on this invoice (flag on and applied); otherwise the rule name and the firm reason only.
    /// </summary>
    private static FirmOutcomeCitation Cite(RuleRead rule, FirmClauseText clause, string? firmReason) =>
        clause.Quote is not null || clause.SectionReference is not null
            ? new FirmOutcomeCitation(rule.Name, firmReason, clause.FirmFacingName, rule.Number, clause.SectionReference, clause.Quote)
            : new FirmOutcomeCitation(rule.Name, firmReason, null, null, null, null);

    // ── Reads (app-only, each bounded by this invoice) ──

    private async Task<Entity?> ReadInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.InvoiceEntity)
        {
            ColumnSet = new ColumnSet(HeaderColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition($"{InvoiceReviewReadService.InvoiceEntity}id", ConditionOperator.Equal, invoiceId);
        var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return page.Entities.FirstOrDefault(r => r.Id == invoiceId);
    }

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
    /// of this invoice is never returned, whatever the query gave back (EB-D-202). Keyed by line id.
    /// </summary>
    private async Task<Dictionary<Guid, Entity>> ReadDecisionRecordsAsync(Guid invoiceId, IReadOnlyList<Candidate> candidates, CancellationToken ct)
    {
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

    private sealed record RuleRead(string? Name, int? Number);

    /// <summary>The rule name (the policy's, never the rule set's) and version number of each version; a version whose Billing policy cannot be read is left out.</summary>
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
                ColumnSet = new ColumnSet("sprk_name", RuleDomains.PolicyDomainColumn),
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
                rules[version.Id] = new RuleRead(NullIfBlank(policy.GetAttributeValue<string>("sprk_name")), version.GetAttributeValue<int?>("sprk_versionnumber"));
            }
        }

        return rules;
    }

    // ── Helpers ──

    private static FirmOutcome Unavailable() => new(FirmOutcomeKind.Unavailable, null, "Unavailable", null, null, [], true);

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
