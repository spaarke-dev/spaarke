using System.Globalization;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using FindingGroup = Sprk.Bff.Api.Services.Finance.DuplicateDetailGate.FindingGroup;

namespace Sprk.Bff.Api.Services.Finance;

// ═══════════════════════════════════════════════════════════════════════════════════════════════
// Response contract (ebilling-r1 task 100, FR-31). Camel-cased on the wire by the minimal-API defaults. This is the INTERNAL reviewer's
// read model: it carries the clause quotes and the values the rules read. The firm-visible projection is task 130's, built from the same
// stored findings with the D-66 / D-79 visibility rules; nothing here is a firm projection.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>The Invoice Review read model of one invoice (FR-31): header, totals, lines with status and findings, invoice-level findings, Decision Record history.</summary>
/// <param name="LineCounts">Lines by status name (every status, zeros included), for the totals strip.</param>
/// <param name="InvoicePdfFileId">
/// ebilling-r1 task 092 (model 9.2, 108): the id of the invoice's Servable Invoice PDF Stored File, which the review page loads through
/// <c>GET /api/finance/files/{fileId}/content</c> (that route makes its own checks); <c>null</c> when the invoice has none that is Servable
/// (an existing-document confirm, or an upload whose file is not yet, or never, servable).
/// </param>
public sealed record InvoiceReviewDto(
    ReviewHeaderDto Header,
    ReviewTotalsDto Totals,
    IReadOnlyList<ReviewLineDto> Lines,
    IReadOnlyList<ReviewFindingDto> InvoiceFindings,
    ReviewSubmissionDto? Submission,
    IReadOnlyList<ReviewDecisionRecordDto> DecisionHistory,
    IReadOnlyDictionary<string, int> LineCounts,
    ReviewFlagsDto Flags,
    Guid? InvoicePdfFileId);

/// <param name="Status">The lifecycle status name (<c>InReview</c>, <c>Approved</c>, ...); <c>Unknown</c> for a statuscode the lifecycle does not know.</param>
/// <param name="Held">True for a blocked internal submission awaiting Override / Return (D-101): In Review and the submission is Rejected with the blocked reason.</param>
public sealed record ReviewHeaderDto(
    Guid InvoiceId,
    string? Name,
    string? FirmInvoiceNumber,
    string? TrackingNumber,
    ReviewRefDto? Firm,
    ReviewCoreRecordDto? CoreRecord,
    DateOnly? InvoiceDate,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    string Status,
    int StatusCode,
    bool Held);

public sealed record ReviewRefDto(Guid Id, string? Name);

/// <param name="Type"><c>matter</c>, <c>project</c> or <c>workAssignment</c>.</param>
public sealed record ReviewCoreRecordDto(string Type, Guid Id, string? Name);

/// <summary>The five stored totals (FR-07, I-1) as 016 wrote them, and the billed total. Nothing is recomputed here.</summary>
public sealed record ReviewTotalsDto(
    decimal? Billed, decimal? Fees, decimal? Expenses, decimal? LineAdjustments, decimal? InvoiceAdjustments, decimal? Approved);

/// <param name="CanRead">True: the route's own filter passed (the caller holds Read on the invoice).</param>
/// <param name="CanDecide">The caller holds Write on the invoice (D-77) and the invoice is In Review. Advisory for the UI; the decide routes check again.</param>
public sealed record ReviewFlagsDto(bool CanRead, bool CanDecide);

/// <summary>One Invoice Line as the reviewer sees it.</summary>
/// <param name="Type"><c>Fee</c>, <c>Expense</c> or <c>Adjustment</c>.</param>
/// <param name="Status">The stored line status name (<c>Pending</c>, <c>Clean</c>, <c>Flagged</c>, <c>Accepted</c>, <c>Reduced</c>, <c>Disallowed</c>).</param>
/// <param name="StatusLabel">What the screen shows: the prototype shows Pending as <c>Processing</c> (D-58, F-1).</param>
/// <param name="Quantity">Hours for a Fee line, quantity for an Expense line.</param>
/// <param name="Rate">The billed rate of a Fee line, the unit cost of an Expense line.</param>
/// <param name="HighestSeverity"><c>Info</c>, <c>Warning</c> or <c>Critical</c>; null when the line has no finding.</param>
/// <param name="Fields">Per extracted field: its page, bounding box and confidence (the Document tab and the low-confidence marker).</param>
public sealed record ReviewLineDto(
    Guid Id,
    int Sequence,
    string Type,
    DateOnly? Date,
    string? TimekeeperAsBilled,
    ReviewTimekeeperDto? Timekeeper,
    decimal? Quantity,
    decimal? Rate,
    decimal? ApprovedRate,
    decimal Amount,
    string? Narrative,
    ReviewCodesDto Codes,
    string Status,
    string StatusLabel,
    decimal? ApprovedAmount,
    string? Reason,
    ReviewRefDto? ReviewedBy,
    DateTimeOffset? ReviewedOn,
    int FindingCount,
    string? HighestSeverity,
    IReadOnlyList<ReviewFindingDto> Findings,
    IReadOnlyDictionary<string, ReviewFieldDto> Fields);

/// <summary>The roster entry a fee line's timekeeper name matched (046), with its classification.</summary>
public sealed record ReviewTimekeeperDto(Guid Id, string? Name, int? Classification);

/// <param name="AsBilled">The code text the firm wrote (<c>sprk_taskcodeasbilled</c> ...), or null.</param>
/// <param name="Code">The resolved code the AI chose from the line's code set; null when none.</param>
/// <param name="FirmCode">The code the firm supplied, when present.</param>
/// <param name="Mismatch">The AI code and the firm's code differ.</param>
public sealed record ReviewCodeDto(string? AsBilled, string? Code, string? FirmCode, bool Mismatch);

/// <param name="Confidence">The coding confidence (<c>sprk_codingconfidence</c>), 0 to 1.</param>
/// <param name="CodeSetNeedsReview">The code set was an AI suggestion (FR-22).</param>
/// <param name="NotCoded">The line was read but not coded (D-126).</param>
/// <param name="Refused">Codes the AI named that are outside the line's code set (never stored as codes).</param>
public sealed record ReviewCodesDto(
    ReviewCodeDto Task, ReviewCodeDto Activity, ReviewCodeDto Expense,
    decimal? Confidence, bool CodeSetNeedsReview, bool NotCoded, IReadOnlyList<string> Refused);

/// <summary>An extracted field's source: page, normalised 0 to 1 bounding box (<c>{page, x, y, w, h}</c>) and confidence.</summary>
/// <param name="Confidence">The analyzer's confidence.</param>
/// <param name="Combined">046's combined confidence (D-47); the value the low-confidence marker uses when present.</param>
/// <param name="LowConfidence">The combined (else analyzer) confidence is below the reliability threshold in the eBilling settings. False when no confidence is known (the marker is for a known low value) or the threshold could not be read.</param>
/// <param name="Repair">How a value the analyzer returned was repaired (<c>None</c> when it was not).</param>
public sealed record ReviewFieldDto(
    decimal? Confidence, decimal? Combined, bool LowConfidence, int? Page, ReviewBoxDto? Box, string? Repair);

public sealed record ReviewBoxDto(int? Page, decimal X, decimal Y, decimal W, decimal H);

/// <summary>A value a rule read while it produced a finding.</summary>
/// <param name="Value">The value in invariant text; null for a withheld finding's values (which are not returned at all).</param>
public sealed record ReviewValueReadDto(string Name, string? Value, string? Field, decimal? Confidence, bool BelowReliabilityThreshold);

/// <summary>The adjustment a rule suggests (design.md 6.1). Applying it is the reviewer's decision (a Reduce with this amount).</summary>
/// <param name="Kind"><c>Reduce</c> or <c>Disallow</c>.</param>
/// <param name="Amount">For Reduce, the amount to take off the line; for Disallow, the line amount that would be disallowed. Null when it could not be computed.</param>
public sealed record ReviewSuggestionDto(string Kind, decimal? Amount);

/// <summary>
/// One finding: a rule that does not hold for the invoice or a line, citing policy, version and clause (FR-24). The wording of a reason is
/// the screen's, from <see cref="ReasonCode"/>; <see cref="Message"/> is the stored Signal sentence and only the primary reason of a group has one.
/// </summary>
/// <param name="Message">The Signal sentence, rendered at submission time, carried by the group's primary reason. Null for any other reason of a group (the Signal read route withholds per-reason text, D-123; the screen words those from the code).</param>
/// <param name="Severity"><c>Info</c>, <c>Warning</c> or <c>Critical</c> from the Signal plan; null when the plan had no entry for the finding.</param>
/// <param name="Consequence">The policy version's consequence (<c>Flag</c> or <c>Reject</c>), as 073 saw it; null when none was recorded.</param>
/// <param name="Rejects">073 decided this finding rejected the submission.</param>
/// <param name="HeldBecause">073's hold reason code when it would have rejected but did not (a read field below the threshold).</param>
/// <param name="DetailWithheld">The values read are not returned: a duplicate across a secure wall (D-112, at write time), or a Duplicate whose
/// earlier invoice the caller cannot read (D-176, at read time).</param>
/// <param name="Overridden">The submission this finding blocked was overridden (an <c>override-submission</c> Decision Record exists for the invoice).</param>
public sealed record ReviewFindingDto(
    string RuleType,
    string PolicyCode,
    Guid? PolicyId,
    Guid? PolicyVersionId,
    string? Clause,
    string? ClauseLocation,
    string ReasonCode,
    string? Message,
    string? Severity,
    string? Consequence,
    bool Rejects,
    string? HeldBecause,
    bool DetailWithheld,
    bool Overridden,
    ReviewSuggestionDto? SuggestedAdjustment,
    IReadOnlyList<ReviewValueReadDto> ValuesRead);

/// <summary>What the pipeline itself noted (codes only): the D-91 invoice-number mismatch, a code set needing review, a hold.</summary>
public sealed record ReviewPipelineFindingDto(string ReasonCode, IReadOnlyDictionary<string, string?> Values);

/// <param name="Decision">073's decision outcome (<c>Accepted</c> or <c>Rejected</c>) when recorded.</param>
public sealed record ReviewSubmissionDto(
    Guid SubmissionId, string? TrackingNumber, string? Kind, string? ReasonCode, string? Decision, IReadOnlyList<ReviewPipelineFindingDto> PipelineFindings);

/// <summary>One Decision Record about the invoice or one of its lines (FR-30, D-75). The fact snapshot is not returned.</summary>
/// <param name="Subject"><c>invoice</c> or <c>line</c>.</param>
/// <param name="LineId">The line, for a line decision.</param>
public sealed record ReviewDecisionRecordDto(
    Guid RecordId,
    string? Number,
    string? ActionCode,
    string Subject,
    Guid? LineId,
    string? RecordClass,
    string? Outcome,
    DateTimeOffset? DecidedOn,
    ReviewRefDto? DecidedBy,
    string? Reason);

/// <summary>What 049 stored on one line's <c>sprk_extractionconfidence</c> (FR-04), parsed tolerantly.</summary>
internal sealed record LineExtraction(
    IReadOnlyDictionary<string, ReviewFieldDto> Fields,
    string? CodingStatus,
    bool CodeSetNeedsReview,
    bool NotCoded,
    decimal? CodingConfidence,
    (string? Code, string? FirmCode, bool Mismatch) Task,
    (string? Code, string? FirmCode, bool Mismatch) Activity,
    (string? Code, string? FirmCode, bool Mismatch) Expense,
    IReadOnlyList<string> Refused)
{
    public static LineExtraction Empty { get; } = new(
        new Dictionary<string, ReviewFieldDto>(), null, false, false, null, default, default, default, []);

    /// <summary>Parses the stored JSON. Anything unreadable is the empty extraction: a damaged column must not fail the whole review page.</summary>
    public static LineExtraction Parse(string? json, decimal? reliabilityThreshold)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var fields = new Dictionary<string, ReviewFieldDto>(StringComparer.Ordinal);
            if (root.TryGetProperty("fields", out var fieldsJson) && fieldsJson.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in fieldsJson.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var confidence = Decimal(property.Value, "confidence");
                    var combined = Decimal(property.Value, "combined");
                    var known = combined ?? confidence;
                    fields[property.Name] = new ReviewFieldDto(
                        confidence,
                        combined,
                        reliabilityThreshold is { } threshold && known is { } value && value < threshold,
                        Int(property.Value, "page"),
                        property.Value.TryGetProperty("box", out var box) && box.ValueKind == JsonValueKind.Object
                            ? new ReviewBoxDto(Int(box, "page"), Decimal(box, "x") ?? 0m, Decimal(box, "y") ?? 0m, Decimal(box, "w") ?? 0m, Decimal(box, "h") ?? 0m)
                            : null,
                        Text(property.Value, "repair"));
                }
            }

            if (!root.TryGetProperty("coding", out var coding) || coding.ValueKind != JsonValueKind.Object)
            {
                return new LineExtraction(fields, null, false, false, null, default, default, default, []);
            }

            var status = Text(coding, "status");
            var refused = coding.TryGetProperty("refused", out var refusedJson) && refusedJson.ValueKind == JsonValueKind.Array
                ? refusedJson.EnumerateArray()
                    .Where(r => r.ValueKind == JsonValueKind.Object)
                    .Select(r => Text(r, "code"))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c!)
                    .ToList()
                : [];
            return new LineExtraction(
                fields,
                status,
                coding.TryGetProperty("needsReview", out var review) && review.ValueKind == JsonValueKind.True,
                string.Equals(status, "NotCoded", StringComparison.Ordinal),
                Decimal(coding, "confidence"),
                Code(coding, "task"),
                Code(coding, "activity"),
                Code(coding, "expense"),
                refused);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static (string? Code, string? FirmCode, bool Mismatch) Code(JsonElement coding, string name) =>
        coding.TryGetProperty(name, out var code) && code.ValueKind == JsonValueKind.Object
            ? (Text(code, "code"), Text(code, "firmCode"), code.TryGetProperty("mismatch", out var mismatch) && mismatch.ValueKind == JsonValueKind.True)
            : default;

    internal static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    internal static decimal? Decimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
}

/// <summary>
/// Builds the Invoice Review read model (FR-31, NFR-02): header, totals, lines with status and findings, invoice-level findings and the
/// Decision Record history of one invoice. Read-only.
/// </summary>
/// <remarks>
/// <para><b>Scope.</b> The route's filter has already authorized Read on the invoice as the caller; every read here is bounded by that invoice
/// (the lines are the rows whose <c>sprk_invoice</c> is it, the submission the one linked to it, the Decision Records those regarding it or one of
/// its lines), app-only like every read in this namespace. No id other than the invoice's comes from the request. The one exception is the
/// D-176 earlier-invoice check below, which reads AS THE CALLER.</para>
/// <para><b>D-176 (#2118, task 119): a Duplicate's earlier invoice.</b> A stored Duplicate finding quotes an earlier submission
/// (<c>duplicateOf.invoiceId</c>, <c>duplicateOf.trackingNumber</c>) and the duplicated line's values. Its detail is returned only when the
/// caller's own read returns that earlier invoice: the rule of <see cref="EarlierInvoiceReadability"/>, shared with the worklist's
/// <see cref="SignalReadService"/> (ontology task 157), called once per response with the caller's <see cref="IDataverseUserClient"/>. Otherwise
/// the finding's whole (policy, subject) group takes the write-time withheld shape (D-112, as the persister scrubs it): no values read, no
/// suggestion, <c>detailWithheld</c>, and <see cref="EarlierInvoiceReadability.WithheldSentence"/> as the group's sentence. A fault withholds
/// every Duplicate group; the page still loads.</para>
/// <para><b>Where the findings come from.</b> From the submission's stored <c>sprk_findingsjson</c> (task 049, notes section 5): per finding the rule
/// type, policy, version, clause and its location, the reason code, 073's decision, the values read and (task 100) the suggested adjustment; the
/// Signal plan in the same JSON gives each group's severity and sentence. They are NOT read back through the Signal read route: that route strips the
/// text of every evidence line that is not a witness reference (D-123 / D-142), so a merged billing Signal reaches a reader with its second and later
/// reasons as bare codes. The stored copy is the one source of the clause and the values; the screen words each reason from its code. A line is
/// matched to its findings by its sequence (<c>sprk_linesequence</c> = the finding's line index + 1).</para>
/// <para><b>Queries (independent of the line count; NFR-02).</b> One invoice read, one read of all the invoice's lines, then in parallel: the
/// linked submission (1), the Decision Records of the invoice and its lines (1, one OR filter), the roster entries of the matched timekeepers
/// (1, none when no line has one) and the caller's Write check (a rights lookup, not an entity read). At most four entity reads after the header
/// and lines; each is paged (5,000 rows a page), so 150 lines is one page. When the stored findings hold a Duplicate that quotes an earlier
/// invoice, the caller's earlier-invoice check adds one <c>sprk_invoices</c> read (per 200 ids), whatever the number of Duplicates; #2294: a
/// Duplicate that quotes only a tracking number adds no read (it is withheld).</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="InvoiceContextService"/> (task 106) assembles the context panel
/// and reads none of the lines, findings or history. Extension: the panel's sections are other invoices and rollups, a different shape and
/// different failure semantics (it degrades per section; this is the page's own data). Cost of doing nothing: the review page has no data (FR-31).</para>
/// </remarks>
public sealed class InvoiceReviewReadService
{
    private const int PageSize = 5000;

    internal const string InvoiceEntity = "sprk_invoice";
    internal const string LineEntity = "sprk_billingevent";
    internal const string SubmissionEntity = "sprk_invoicesubmission";
    internal const string DecisionRecordEntity = "sprk_decisionrecord";
    internal const string TimekeeperEntity = "sprk_timekeeper";

    internal const string LineReason = "sprk_adjustmentreason";
    internal const string LineReviewedBy = "sprk_reviewedby";
    internal const string LineReviewedOn = "sprk_reviewedon";

    private static readonly string[] InvoiceColumns =
    [
        "sprk_name", "sprk_invoicenumber", InvoiceReviewService.InvTrackingNumber, "sprk_invoicedate",
        InvoiceSubmissionPersister.InvoiceBillingPeriodStart, InvoiceSubmissionPersister.InvoiceBillingPeriodEnd,
        InvoiceSubmissionPersister.InvoiceVendorOrg, "sprk_matter", "sprk_project", "sprk_regardingworkassignment",
        InvoiceTotalsService.InvoiceBilledTotal, InvoiceTotalsService.InvoiceFeesTotal, InvoiceTotalsService.InvoiceExpensesTotal,
        InvoiceTotalsService.InvoiceLineAdjustmentsTotal, InvoiceTotalsService.InvoiceInvoiceAdjustmentsTotal, InvoiceTotalsService.InvoiceApprovedTotal,
        "statuscode",
    ];

    /// <summary>The line columns the review reads (the decision service reads the same set plus the core lookups it needs).</summary>
    internal static readonly string[] LineColumns =
    [
        InvoiceSubmissionPersister.LineName, InvoiceSubmissionPersister.LineSequence, InvoiceTotalsService.LineCostType,
        InvoiceSubmissionPersister.LineEventDate, InvoiceSubmissionPersister.LineTimekeeper, InvoiceSubmissionPersister.LineResolvedTimekeeper,
        InvoiceSubmissionPersister.LineQuantity, InvoiceSubmissionPersister.LineRate, InvoiceSubmissionPersister.LineApprovedRate,
        InvoiceTotalsService.LineAmount, InvoiceSubmissionPersister.LineDescription,
        InvoiceSubmissionPersister.LineTaskCodeAsBilled, InvoiceSubmissionPersister.LineActivityCodeAsBilled, InvoiceSubmissionPersister.LineExpenseCodeAsBilled,
        InvoiceSubmissionPersister.LineCodingConfidence, InvoiceSubmissionPersister.LineExtractionConfidence,
        InvoiceTotalsService.LineStatus, InvoiceTotalsService.LineApprovedAmount, LineReason, LineReviewedBy, LineReviewedOn,
        InvoiceTotalsService.LineAdjustmentMethod, InvoiceTotalsService.LineAdjustmentValue,
        InvoiceTotalsService.VersionNumber,
    ];

    private static readonly string[] SubmissionColumns =
    [
        InvoiceSubmissionPersister.SubmissionStatus, InvoiceSubmissionPersister.SubmissionReasonCode,
        InvoiceSubmissionPersister.SubmissionFindingsJson, "sprk_trackingnumber", "createdon",
    ];

    private static readonly string[] DecisionRecordColumns =
    [
        "sprk_decisionnumber", "sprk_actioncode", "sprk_recordclass", "sprk_decisionoutcome", "sprk_reason", "sprk_decidedon",
        "sprk_confirmedby", "sprk_regardinginvoice", "sprk_regardinginvoiceline", "createdon",
    ];

    private readonly IGenericEntityService _entities;
    private readonly AuthorizationService _authorization;
    private readonly IEbillingSettingsReader _settings;
    private readonly IDataverseUserClient _user;
    private readonly EarlierInvoiceReadability _earlierInvoices;
    private readonly ILogger<InvoiceReviewReadService> _logger;

    /// <param name="user">The CALLER's Dataverse client (OBO): the D-176 earlier-invoice check reads with it, never app-only (ADR-028).</param>
    public InvoiceReviewReadService(
        IGenericEntityService entities,
        AuthorizationService authorization,
        IEbillingSettingsReader settings,
        IDataverseUserClient user,
        EarlierInvoiceReadability earlierInvoices,
        ILogger<InvoiceReviewReadService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _earlierInvoices = earlierInvoices ?? throw new ArgumentNullException(nameof(earlierInvoices));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The read model of <paramref name="invoiceId"/>, or null when the invoice does not exist (deleted between the route check and the read).</summary>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    public async Task<InvoiceReviewDto?> GetAsync(Guid invoiceId, InvoiceContextService.InvoiceCaller caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var invoice = await ReadInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice is null)
        {
            return null;
        }

        var lineRows = await ReadPagedAsync(_entities, LineQuery(invoiceId), ct).ConfigureAwait(false);
        var lineIds = lineRows.Select(l => l.Id).ToList();
        var roster = lineRows.Select(l => l.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.LineResolvedTimekeeper)?.Id)
            .Where(id => id is not null && id != Guid.Empty).Select(id => id!.Value).Distinct().OrderBy(id => id).ToList();

        var submissionTask = ReadSubmissionAsync(invoiceId, ct);
        var historyTask = ReadHistoryAsync(invoiceId, lineIds, ct);
        var rosterTask = ReadRosterAsync(roster, ct);
        var thresholdTask = ReadThresholdAsync(ct);
        var pdfTask = ReadInvoicePdfFileIdAsync(_entities, invoiceId, ct);
        var canWriteTask = CallerReadCheck.CheckAsync(
            _authorization, caller, FinanceAuthorizationFilter.InvoiceEntitySet, "ebilling.invoice.decide", invoiceId, _logger, "Invoice review", ct);
        await Task.WhenAll(submissionTask, historyTask, rosterTask, thresholdTask, pdfTask, canWriteTask).ConfigureAwait(false);

        var submission = submissionTask.Result;
        var history = historyTask.Result;
        var threshold = thresholdTask.Result;

        var statusCode = invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 0;
        var known = InvoiceStatusCodes.TryFromStatusCode(statusCode, out var lifecycle);
        var held = known && lifecycle == InvoiceLifecycleStatus.InReview && submission is { Held: true };
        var overridden = history.Any(h => string.Equals(h.ActionCode, "override-submission", StringComparison.Ordinal));

        var parsed = submission?.Findings;
        var withheld = await DuplicateGroupsWithheldAsync(parsed, invoiceId, ct).ConfigureAwait(false);
        var bySequence = lineRows.ToDictionary(l => l.Id, l => l.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) ?? 0);
        var lines = new List<ReviewLineDto>(lineRows.Count);
        foreach (var row in lineRows.OrderBy(l => bySequence[l.Id]).ThenBy(l => l.Id))
        {
            var sequence = bySequence[row.Id];
            // The plan and the findings are both keyed on the line INDEX (D-176 K4): a row whose sequence was edited cannot pair one index's findings with another's plan sentence.
            var planSubject = submission is null ? row.Id : InvoiceSubmissionPersister.LineIdFor(submission.Dto.SubmissionId, sequence - 1);
            var findings = FindingsFor(parsed, "Line", sequence - 1, planSubject, overridden, withheld);
            lines.Add(ToLine(row, sequence, findings, roster: rosterTask.Result, threshold));
        }

        var invoiceFindings = FindingsFor(parsed, "Invoice", null, invoiceId, overridden, withheld);

        return new InvoiceReviewDto(
            ToHeader(invoice, known ? lifecycle.ToString() : "Unknown", statusCode, held),
            new ReviewTotalsDto(
                Money(invoice, InvoiceTotalsService.InvoiceBilledTotal), Money(invoice, InvoiceTotalsService.InvoiceFeesTotal),
                Money(invoice, InvoiceTotalsService.InvoiceExpensesTotal), Money(invoice, InvoiceTotalsService.InvoiceLineAdjustmentsTotal),
                Money(invoice, InvoiceTotalsService.InvoiceInvoiceAdjustmentsTotal), Money(invoice, InvoiceTotalsService.InvoiceApprovedTotal)),
            lines,
            invoiceFindings,
            submission?.Dto,
            history,
            Enum.GetValues<InvoiceLineStatus>().ToDictionary(s => s.ToString(), s => lines.Count(l => l.Status == s.ToString()), StringComparer.Ordinal),
            new ReviewFlagsDto(
                CanRead: true,
                CanDecide: canWriteTask.Result == CallerReadOutcome.Readable && known && lifecycle == InvoiceLifecycleStatus.InReview),
            pdfTask.Result);
    }

    // ── Reads ──

    private async Task<Entity?> ReadInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceEntity)
        {
            ColumnSet = new ColumnSet(InvoiceColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition($"{InvoiceEntity}id", ConditionOperator.Equal, invoiceId);
        var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return page.Entities.FirstOrDefault();
    }

    internal static QueryExpression LineQuery(Guid invoiceId)
    {
        var query = new QueryExpression(LineEntity)
        {
            ColumnSet = new ColumnSet(LineColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
        };
        query.Criteria.AddCondition(InvoiceTotalsService.LineInvoiceLookup, ConditionOperator.Equal, invoiceId);
        // Task 102 (NFR-09): a removed (deactivated) invoice-level Adjustment line is neither shown nor counted; 016's totals read does the same.
        InvoiceTotalsService.ExcludeInactiveAdjustments(query);
        query.AddOrder(InvoiceSubmissionPersister.LineSequence, OrderType.Ascending);
        query.AddOrder($"{LineEntity}id", OrderType.Ascending);
        return query;
    }

    private sealed record SubmissionRead(ReviewSubmissionDto Dto, bool Held, ParsedFindings? Findings);

    /// <summary>
    /// ebilling-r1 task 092: the newest Servable Invoice PDF Stored File of the invoice (app-only, after the route's checks), or <c>null</c>. Ids
    /// only; the row's other columns are the file route's to judge. A read fault propagates, as every other read here.
    /// </summary>
    internal static async Task<Guid?> ReadInvoicePdfFileIdAsync(IGenericEntityService entities, Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(StoredFileSchema.Entity)
        {
            ColumnSet = new ColumnSet(StoredFileSchema.IdColumn),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition(StoredFileSchema.InvoiceTable, ConditionOperator.Equal, invoiceId);
        query.Criteria.AddCondition(StoredFileSchema.Purpose, ConditionOperator.Equal, (int)StoredFilePurpose.InvoicePdf);
        query.Criteria.AddCondition(StoredFileSchema.Status, ConditionOperator.Equal, (int)StoredFileStatus.Servable);
        query.AddOrder(StoredFileSchema.CreatedOn, OrderType.Descending);
        var page = await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return page.Entities.FirstOrDefault() is { Id: var id } && id != Guid.Empty ? id : null;
    }

    private async Task<SubmissionRead?> ReadSubmissionAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(SubmissionEntity)
        {
            ColumnSet = new ColumnSet(SubmissionColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.Equal, invoiceId);
        query.AddOrder("createdon", OrderType.Descending);
        var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var row = page.Entities.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        var parsed = ParsedFindings.Parse(row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson), _logger);
        var reason = row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode);
        var status = row.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value;
        var held = status == (int)InvoiceSubmissionStatus.Rejected && string.Equals(reason, SubmissionReasonCodes.Blocked, StringComparison.Ordinal);
        return new SubmissionRead(
            new ReviewSubmissionDto(row.Id, row.GetAttributeValue<string>("sprk_trackingnumber"), parsed?.Kind, parsed?.ReasonCode ?? reason, parsed?.Decision, parsed?.Pipeline ?? []),
            held,
            parsed);
    }

    private async Task<IReadOnlyList<ReviewDecisionRecordDto>> ReadHistoryAsync(Guid invoiceId, IReadOnlyList<Guid> lineIds, CancellationToken ct)
    {
        var query = new QueryExpression(DecisionRecordEntity)
        {
            ColumnSet = new ColumnSet(DecisionRecordColumns),
            Criteria = new FilterExpression(LogicalOperator.Or),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
        };
        query.Criteria.AddCondition("sprk_regardinginvoice", ConditionOperator.Equal, invoiceId);
        if (lineIds.Count > 0)
        {
            query.Criteria.AddCondition("sprk_regardinginvoiceline", ConditionOperator.In, lineIds.Cast<object>().ToArray());
        }

        query.AddOrder("sprk_decidedon", OrderType.Ascending);
        query.AddOrder("createdon", OrderType.Ascending);
        var rows = await ReadPagedAsync(_entities, query, ct).ConfigureAwait(false);
        return rows.Select(r =>
        {
            var line = r.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline")?.Id;
            var by = r.GetAttributeValue<EntityReference>("sprk_confirmedby");
            return new ReviewDecisionRecordDto(
                r.Id,
                r.GetAttributeValue<string>("sprk_decisionnumber"),
                r.GetAttributeValue<string>("sprk_actioncode"),
                line is not null && line != Guid.Empty ? "line" : "invoice",
                line is not null && line != Guid.Empty ? line : null,
                r.GetAttributeValue<OptionSetValue>("sprk_recordclass")?.Value switch
                {
                    (int)RecordedDecisionClass.Judgement => "Judgement",
                    (int)RecordedDecisionClass.Routine => "Routine",
                    (int)RecordedDecisionClass.Dismissal => "Dismissal",
                    _ => null,
                },
                r.GetAttributeValue<OptionSetValue>("sprk_decisionoutcome")?.Value switch
                {
                    100000000 => "Authorized",
                    100000001 => "Denied",
                    100000002 => "Dismissed",
                    _ => null,
                },
                r.GetAttributeValue<DateTime?>("sprk_decidedon") is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
                by is null ? null : new ReviewRefDto(by.Id, by.Name),
                r.GetAttributeValue<string>("sprk_reason"));
        }).ToList();
    }

    private async Task<Dictionary<Guid, (string? Name, int? Classification)>> ReadRosterAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var query = new QueryExpression(TimekeeperEntity)
        {
            ColumnSet = new ColumnSet("sprk_name", "sprk_classification"),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
        };
        query.Criteria.AddCondition($"{TimekeeperEntity}id", ConditionOperator.In, ids.Cast<object>().ToArray());
        query.AddOrder($"{TimekeeperEntity}id", OrderType.Ascending);
        var rows = await ReadPagedAsync(_entities, query, ct).ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r => ((string?)r.GetAttributeValue<string>("sprk_name"), (int?)r.GetAttributeValue<OptionSetValue>("sprk_classification")?.Value));
    }

    /// <summary>The reliability threshold of the eBilling settings (a cached read). A fault yields null: the low-confidence marker is then off, the page still loads.</summary>
    private async Task<decimal?> ReadThresholdAsync(CancellationToken ct)
    {
        try
        {
            return (await _settings.GetAsync(ct).ConfigureAwait(false)).ReliabilityThreshold;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Invoice review: the eBilling settings could not be read ({ExceptionType}); the low-confidence marker is off.", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Reads every page of <paramref name="query"/> (the queries here are bounded by one invoice; a page is 5,000 rows).</summary>
    internal static async Task<List<Entity>> ReadPagedAsync(IGenericEntityService entities, QueryExpression query, CancellationToken ct)
    {
        var rows = new List<Entity>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(page.Entities);
            if (!page.MoreRecords)
            {
                return rows;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    // ── Mapping ──

    private static ReviewHeaderDto ToHeader(Entity invoice, string status, int statusCode, bool held)
    {
        ReviewCoreRecordDto? core = null;
        foreach (var (column, type) in new[] { ("sprk_matter", "matter"), ("sprk_project", "project"), ("sprk_regardingworkassignment", "workAssignment") })
        {
            if (invoice.GetAttributeValue<EntityReference>(column) is { } reference && reference.Id != Guid.Empty)
            {
                core = new ReviewCoreRecordDto(type, reference.Id, reference.Name);
                break;
            }
        }

        var firm = invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg);
        return new ReviewHeaderDto(
            invoice.Id,
            invoice.GetAttributeValue<string>("sprk_name"),
            invoice.GetAttributeValue<string>("sprk_invoicenumber"),
            invoice.GetAttributeValue<string>(InvoiceReviewService.InvTrackingNumber),
            firm is null ? null : new ReviewRefDto(firm.Id, firm.Name),
            core,
            DateOf(invoice, "sprk_invoicedate"),
            DateOf(invoice, InvoiceSubmissionPersister.InvoiceBillingPeriodStart),
            DateOf(invoice, InvoiceSubmissionPersister.InvoiceBillingPeriodEnd),
            status,
            statusCode,
            held);
    }

    private static ReviewLineDto ToLine(
        Entity row,
        int sequence,
        IReadOnlyList<ReviewFindingDto> findings,
        Dictionary<Guid, (string? Name, int? Classification)> roster,
        decimal? threshold)
    {
        var type = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value;
        var status = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value;
        var statusName = status is { } s && Enum.IsDefined(typeof(InvoiceLineStatus), s) ? ((InvoiceLineStatus)s).ToString() : "Unknown";
        var extraction = LineExtraction.Parse(row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExtractionConfidence), threshold);
        var timekeeperId = row.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.LineResolvedTimekeeper)?.Id;
        var by = row.GetAttributeValue<EntityReference>(LineReviewedBy);
        var severity = findings.Select(f => f.Severity).Where(v => v is not null).OrderByDescending(SeverityRank).FirstOrDefault();

        return new ReviewLineDto(
            row.Id,
            sequence,
            type is { } t && Enum.IsDefined(typeof(InvoiceLineCostType), t) ? ((InvoiceLineCostType)t).ToString() : "Unknown",
            DateOf(row, InvoiceSubmissionPersister.LineEventDate),
            row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTimekeeper),
            timekeeperId is { } id && id != Guid.Empty
                ? new ReviewTimekeeperDto(id, roster.TryGetValue(id, out var person) ? person.Name : null, roster.TryGetValue(id, out var p2) ? p2.Classification : null)
                : null,
            DecimalOf(row, InvoiceSubmissionPersister.LineQuantity),
            Money(row, InvoiceSubmissionPersister.LineRate),
            Money(row, InvoiceSubmissionPersister.LineApprovedRate),
            Money(row, InvoiceTotalsService.LineAmount) ?? 0m,
            row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineDescription),
            new ReviewCodesDto(
                new ReviewCodeDto(row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTaskCodeAsBilled), extraction.Task.Code, extraction.Task.FirmCode, extraction.Task.Mismatch),
                new ReviewCodeDto(row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineActivityCodeAsBilled), extraction.Activity.Code, extraction.Activity.FirmCode, extraction.Activity.Mismatch),
                new ReviewCodeDto(row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExpenseCodeAsBilled), extraction.Expense.Code, extraction.Expense.FirmCode, extraction.Expense.Mismatch),
                DecimalOf(row, InvoiceSubmissionPersister.LineCodingConfidence) ?? extraction.CodingConfidence,
                extraction.CodeSetNeedsReview,
                extraction.NotCoded,
                extraction.Refused),
            statusName,
            StatusLabelOf(statusName),
            Money(row, InvoiceTotalsService.LineApprovedAmount),
            row.GetAttributeValue<string>(LineReason),
            by is null ? null : new ReviewRefDto(by.Id, by.Name),
            row.GetAttributeValue<DateTime?>(LineReviewedOn) is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
            findings.Count,
            severity,
            findings,
            extraction.Fields);
    }

    /// <summary>The label the screen shows for a status: Pending is shown as Processing (D-58, prototype F-1).</summary>
    internal static string StatusLabelOf(string status) => status == nameof(InvoiceLineStatus.Pending) ? "Processing" : status;

    private static int SeverityRank(string? severity) => severity switch { "Critical" => 3, "Warning" => 2, "Info" => 1, _ => 0 };

    private static DateOnly? DateOf(Entity row, string column) =>
        row.GetAttributeValue<DateTime?>(column) is { } date ? DateOnly.FromDateTime(date) : null;

    private static decimal? Money(Entity row, string column) => row.GetAttributeValue<Money>(column)?.Value;

    private static decimal? DecimalOf(Entity row, string column) =>
        row.TryGetAttributeValue<object>(column, out var raw)
            ? raw switch
            {
                decimal d => d,
                double d => (decimal)d,
                float f => (decimal)f,
                int i => i,
                long l => l,
                _ => null,
            }
            : null;

    /// <param name="withheld">The (policy, subject) groups whose earlier-invoice detail the caller may not see (D-176): every finding of such a
    /// group is returned in the write-time withheld shape, and the group's sentence is <see cref="EarlierInvoiceReadability.WithheldSentence"/>.</param>
    private static IReadOnlyList<ReviewFindingDto> FindingsFor(
        ParsedFindings? parsed, string subject, int? lineIndex, Guid subjectId, bool overridden, IReadOnlySet<FindingGroup> withheld)
    {
        if (parsed is null)
        {
            return [];
        }

        return parsed.Findings
            .Where(f => string.Equals(f.Subject, subject, StringComparison.Ordinal) && f.Line == lineIndex)
            .Select(f =>
            {
                var plan = parsed.PlanFor(f.PolicyCode, subjectId);
                var primary = plan is not null && plan.ReasonCodes.Count > 0 && string.Equals(plan.ReasonCodes[0], f.ReasonCode, StringComparison.Ordinal);
                var hidden = withheld.Contains(GroupOf(f));
                return new ReviewFindingDto(
                    f.RuleType, f.PolicyCode, f.PolicyId, f.PolicyVersionId, f.Clause, f.ClauseLocation, f.ReasonCode,
                    primary ? (hidden ? EarlierInvoiceReadability.WithheldSentence : plan!.Sentence) : null,
                    plan?.Severity,
                    f.Consequence, f.Rejects, f.HeldBecause, f.DetailWithheld || hidden,
                    overridden && f.Rejects,
                    hidden ? null : f.Suggestion,
                    hidden ? [] : f.Values);
            })
            .ToList();
    }

    // ── D-176: a Duplicate's earlier invoice, judged as the caller (task 119) ──

    /// <summary>
    /// D-176: the groups holding a Duplicate whose earlier invoice the caller may not see. The rule, the references, the group key and the
    /// fail-closed wrap are <see cref="DuplicateDetailGate"/>'s, shared with the blocked-submission view so the two routes cannot diverge.
    /// #2294 / DEF-043: a number-only Duplicate is withheld (no number is looked up); a same-invoice repeat is judged by the caller's Read of
    /// THIS invoice (the route's own, whose submission holds the findings).
    /// </summary>
    private Task<IReadOnlySet<FindingGroup>> DuplicateGroupsWithheldAsync(ParsedFindings? parsed, Guid invoiceId, CancellationToken ct) =>
        DuplicateDetailGate.WithheldGroupsAsync(
            parsed?.Findings ?? [], GroupOf, f => f.Values.Select(v => ((string?)v.Name, v.Value)), f => f.ReasonCode, invoiceId,
            _earlierInvoices, _user, _logger, "Invoice review", ct);

    private static FindingGroup GroupOf(StoredFinding finding) => new(finding.PolicyCode, finding.Subject, finding.Line);

    // ── The stored findings JSON (task 049, notes section 5) ──

    private sealed record StoredFinding(
        string RuleType, string PolicyCode, Guid? PolicyId, Guid? PolicyVersionId, string? Clause, string? ClauseLocation, string Subject, int? Line,
        string ReasonCode, string? Consequence, bool Rejects, string? HeldBecause, bool DetailWithheld, ReviewSuggestionDto? Suggestion,
        IReadOnlyList<ReviewValueReadDto> Values);

    private sealed record PlanEntry(Guid SubjectId, string PolicyCode, string? Severity, string? Sentence, IReadOnlyList<string> ReasonCodes);

    private sealed class ParsedFindings
    {
        public string? Kind { get; private init; }

        public string? ReasonCode { get; private init; }

        public string? Decision { get; private init; }

        public IReadOnlyList<StoredFinding> Findings { get; private init; } = [];

        public IReadOnlyList<ReviewPipelineFindingDto> Pipeline { get; private init; } = [];

        private IReadOnlyList<PlanEntry> Plan { get; init; } = [];

        public PlanEntry? PlanFor(string policyCode, Guid subjectId) =>
            Plan.FirstOrDefault(p => p.SubjectId == subjectId && string.Equals(p.PolicyCode, policyCode, StringComparison.Ordinal));

        public static ParsedFindings? Parse(string? json, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var findings = new List<StoredFinding>();
                if (root.TryGetProperty("findings", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in list.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Object))
                    {
                        var policyCode = LineExtraction.Text(f, "policyCode");
                        var reason = LineExtraction.Text(f, "reasonCode");
                        var subject = LineExtraction.Text(f, "subject");
                        if (policyCode is null || reason is null || subject is null)
                        {
                            continue;
                        }

                        var values = new List<ReviewValueReadDto>();
                        if (f.TryGetProperty("valuesRead", out var read) && read.ValueKind == JsonValueKind.Array)
                        {
                            values.AddRange(read.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object).Select(v => new ReviewValueReadDto(
                                LineExtraction.Text(v, "name") ?? string.Empty,
                                LineExtraction.Text(v, "value"),
                                LineExtraction.Text(v, "field"),
                                LineExtraction.Decimal(v, "confidence"),
                                v.TryGetProperty("belowReliabilityThreshold", out var low) && low.ValueKind == JsonValueKind.True)));
                        }

                        ReviewSuggestionDto? suggestion = null;
                        if (f.TryGetProperty("suggestedAdjustment", out var s) && s.ValueKind == JsonValueKind.Object && LineExtraction.Text(s, "kind") is { } kind)
                        {
                            suggestion = new ReviewSuggestionDto(kind, LineExtraction.Decimal(s, "amount"));
                        }

                        findings.Add(new StoredFinding(
                            LineExtraction.Text(f, "ruleType") ?? string.Empty,
                            policyCode,
                            Guid(f, "policyId"),
                            Guid(f, "policyVersionId"),
                            LineExtraction.Text(f, "clause"),
                            LineExtraction.Text(f, "clauseLocation"),
                            subject,
                            LineExtraction.Int(f, "line"),
                            reason,
                            LineExtraction.Text(f, "consequence"),
                            f.TryGetProperty("rejects", out var rejects) && rejects.ValueKind == JsonValueKind.True,
                            LineExtraction.Text(f, "heldBecause"),
                            f.TryGetProperty("detailWithheld", out var withheld) && withheld.ValueKind == JsonValueKind.True,
                            suggestion,
                            values));
                    }
                }

                var plan = new List<PlanEntry>();
                if (root.TryGetProperty("signalPlan", out var planJson) && planJson.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in planJson.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object))
                    {
                        if (Guid(p, "subjectId") is not { } subjectId || LineExtraction.Text(p, "policyCode") is not { } policy)
                        {
                            continue;
                        }

                        plan.Add(new PlanEntry(
                            subjectId,
                            policy,
                            p.TryGetProperty("severity", out var severity) && severity.ValueKind == JsonValueKind.Number && severity.TryGetInt32(out var sev)
                                ? sev switch
                                {
                                    SignalWriter.SeverityInfo => "Info",
                                    SignalWriter.SeverityWarning => "Warning",
                                    SignalWriter.SeverityCritical => "Critical",
                                    _ => null,
                                }
                                : null,
                            LineExtraction.Text(p, "sentence"),
                            p.TryGetProperty("reasonCodes", out var codes) && codes.ValueKind == JsonValueKind.Array
                                ? codes.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToList()
                                : []));
                    }
                }

                var pipeline = new List<ReviewPipelineFindingDto>();
                if (root.TryGetProperty("pipelineFindings", out var pipelineJson) && pipelineJson.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in pipelineJson.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object))
                    {
                        if (LineExtraction.Text(p, "reasonCode") is not { } code)
                        {
                            continue;
                        }

                        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
                        if (p.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var item in v.EnumerateObject())
                            {
                                values[item.Name] = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : item.Value.ToString();
                            }
                        }

                        pipeline.Add(new ReviewPipelineFindingDto(code, values));
                    }
                }

                string? decision = null;
                if (root.TryGetProperty("decision", out var decisionJson) && decisionJson.ValueKind == JsonValueKind.Object)
                {
                    decision = LineExtraction.Text(decisionJson, "outcome");
                }

                return new ParsedFindings
                {
                    Kind = LineExtraction.Text(root, "kind"),
                    ReasonCode = LineExtraction.Text(root, "reasonCode"),
                    Decision = decision,
                    Findings = findings,
                    Plan = plan,
                    Pipeline = pipeline,
                };
            }
            catch (JsonException)
            {
                // A damaged stored copy shows no findings rather than failing the page; the cause is logged without the content (ADR-015).
                logger.LogWarning("Invoice review: the stored findings could not be parsed; the page shows none.");
                return null;
            }
        }

        private static Guid? Guid(JsonElement element, string name) =>
            LineExtraction.Text(element, name) is { } text && System.Guid.TryParse(text, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}
