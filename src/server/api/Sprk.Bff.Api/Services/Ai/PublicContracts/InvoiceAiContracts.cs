namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

// ─────────────────────────────────────────────────────────────────────────────
// Request / result contracts for the four Action-backed IInvoiceAi capability methods
// (ebilling-r1 task 041; spec FR-22, FR-27, FR-46, FR-50; D-52).
//
// These types live in PublicContracts and reference NO Services/Finance type: Finance consumes the
// facade, never the reverse. Each Action's output schema (authored by 042 / 047 / 075 / 131 in its
// sprk_analysisaction row and mirrored in infra/dataverse/outputschemas/) MUST stay in step with the
// result type here and with InvoiceAi's parser — the JSON property name of each result member is given
// in its XML doc. Tenant ids travel in the LinearRunContext, never in the model operand.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Typed, fail-closed failure of an <see cref="IInvoiceAi"/> capability method (FR-46). Carries a stable
/// <see cref="ReasonCode"/> callers switch on (for example to mark a submission Failed) and the consumer
/// type that failed. Never carries document text or model output.
/// </summary>
/// <remarks>
/// The kill switch is NOT this type: a disabled AI stack surfaces as
/// <see cref="Sprk.Bff.Api.Configuration.FeatureDisabledException"/> (ADR-032), and cancellation as
/// <see cref="OperationCanceledException"/>.
/// </remarks>
public sealed class InvoiceAiException : Exception
{
    /// <summary>No Binding row (or no Action behind it) could be resolved for the consumer type.</summary>
    public const string BindingMissing = "ai.invoice.binding_missing";

    /// <summary>The Action ran but the completion failed (model / transport / malformed JSON).</summary>
    public const string CompletionFailed = "ai.invoice.completion_failed";

    /// <summary>The completion returned JSON that does not parse into the capability's result type.</summary>
    public const string OutputInvalid = "ai.invoice.output_invalid";

    // ── ReadInvoiceAsync (FR-21). These classify WHERE the read failed, not whether a retry helps. ──

    /// <summary>
    /// The service could not open the document: encrypted / password-protected, corrupt, or empty (Content
    /// Understanding <c>FailedToExtractPageCount</c>, <c>ContentEmpty</c>).
    /// </summary>
    public const string ReadUnreadable = "invoice.read.unreadable";

    /// <summary>The document has more pages than <c>ContentUnderstanding:MaxPages</c> (or the service's own page limit).</summary>
    public const string ReadPageLimit = "invoice.read.page_limit";

    /// <summary>The document was read but holds no invoice: no invoice number, date or total, and no fee or expense row.</summary>
    public const string ReadNoInvoice = "invoice.read.no_invoice";

    /// <summary>
    /// The call to the service failed: transport, authentication, throttling, analyzer missing, operation failed
    /// or timed out, or a response the client could not use. Often transient, but a missing analyzer or a missing
    /// role assignment is not.
    /// </summary>
    public const string ReadServiceFailed = "invoice.read.service_failed";

    /// <summary>
    /// The <see cref="ConsumerType"/> value on read failures. Reading is a Content Understanding call, not an Action,
    /// so this key is NOT a <see cref="ConsumerTypes"/> Binding key and is never added to <c>ConsumerTypes.All</c>.
    /// </summary>
    public const string ReadCapability = "invoice-read";

    public InvoiceAiException(string reasonCode, string consumerType, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ReasonCode = reasonCode ?? throw new ArgumentNullException(nameof(reasonCode));
        ConsumerType = consumerType ?? throw new ArgumentNullException(nameof(consumerType));
    }

    /// <summary>
    /// One of <see cref="BindingMissing"/>, <see cref="CompletionFailed"/>, <see cref="OutputInvalid"/> (Action-backed
    /// methods), or <see cref="ReadUnreadable"/>, <see cref="ReadPageLimit"/>, <see cref="ReadNoInvoice"/>,
    /// <see cref="ReadServiceFailed"/> (<see cref="IInvoiceAi.ReadInvoiceAsync"/>).
    /// </summary>
    public string ReasonCode { get; }

    /// <summary>
    /// The <see cref="ConsumerTypes"/> key of the capability that failed, or <see cref="ReadCapability"/> for
    /// <see cref="IInvoiceAi.ReadInvoiceAsync"/>.
    /// </summary>
    public string ConsumerType { get; }
}

// ── ClassifyAsync (consumer type invoice-classify; FR-46) ────────────────────

/// <summary>Input to <see cref="IInvoiceAi.ClassifyAsync"/>: is this attachment an invoice?</summary>
public sealed record InvoiceClassifyRequest
{
    /// <summary>Extracted text of the attachment. Sent to the model as <c>documentText</c>.</summary>
    public required string DocumentText { get; init; }

    /// <summary>Tenant id for the run context (telemetry / cache scope). Not sent to the model.</summary>
    public string? TenantId { get; init; }
}

/// <summary>Classification verdict. Output JSON values are these names verbatim.</summary>
public enum InvoiceClassification
{
    InvoiceCandidate,
    NotInvoice,
    Unknown,
}

/// <summary>
/// Output of <see cref="IInvoiceAi.ClassifyAsync"/>. Mapped one-to-one by
/// <c>AttachmentClassificationJobHandler</c> (task 042) to the Finance <c>DocumentClassification</c> / <c>InvoiceHints</c>. Output JSON: <c>classification</c> (required),
/// <c>confidence</c> (required, 0–1), <c>hints</c> (optional object or null), <c>reasoning</c> (optional).
/// </summary>
public sealed record InvoiceClassifyResult
{
    public required InvoiceClassification Classification { get; init; }

    public required decimal Confidence { get; init; }

    public InvoiceClassifyHints? Hints { get; init; }

    public string? Reasoning { get; init; }
}

/// <summary>
/// Best-effort header hints read during classification (all optional). Output JSON: <c>vendorName</c>,
/// <c>invoiceNumber</c>, <c>invoiceDate</c> (YYYY-MM-DD string), <c>totalAmount</c>, <c>currency</c>,
/// <c>matterReference</c>.
/// </summary>
public sealed record InvoiceClassifyHints
{
    public string? VendorName { get; init; }

    public string? InvoiceNumber { get; init; }

    public string? InvoiceDate { get; init; }

    public decimal? TotalAmount { get; init; }

    public string? Currency { get; init; }

    public string? MatterReference { get; init; }
}

// ── CodeLinesAsync (consumer type invoice-line-code; FR-22) ──────────────────

/// <summary>Kind of invoice line. Sent to the model as the name verbatim.</summary>
public enum InvoiceLineType
{
    Fee,
    Expense,
}

/// <summary>
/// Input to <see cref="IInvoiceAi.CodeLinesAsync"/>: one chunk of lines plus the closed list of codes the
/// lines' code set allows. The caller chunks (FR-48 chunk size) and validates every returned code against
/// the line's code set (FR-22) — this facade only runs and parses.
/// </summary>
public sealed record InvoiceCodeLinesRequest
{
    /// <summary>Lines in this chunk. Sent as <c>lines</c>.</summary>
    public required IReadOnlyList<InvoiceLineToCode> Lines { get; init; }

    /// <summary>UTBMS task codes allowed for this chunk. Sent as <c>allowedTaskCodes</c>.</summary>
    public required IReadOnlyList<InvoiceCodeOption> AllowedTaskCodes { get; init; }

    /// <summary>UTBMS activity codes allowed for this chunk. Sent as <c>allowedActivityCodes</c>.</summary>
    public required IReadOnlyList<InvoiceCodeOption> AllowedActivityCodes { get; init; }

    /// <summary>UTBMS expense codes allowed for this chunk. Sent as <c>allowedExpenseCodes</c>.</summary>
    public required IReadOnlyList<InvoiceCodeOption> AllowedExpenseCodes { get; init; }

    /// <summary>Tenant id for the run context. Not sent to the model.</summary>
    public string? TenantId { get; init; }
}

/// <summary>One line to code. Sent with camelCase property names (<c>lineKey</c>, <c>date</c>, …).</summary>
public sealed record InvoiceLineToCode
{
    /// <summary>Caller-chosen key, echoed back in <see cref="InvoiceLineCoding.LineKey"/>.</summary>
    public required string LineKey { get; init; }

    public DateOnly? Date { get; init; }

    public required InvoiceLineType LineType { get; init; }

    public string? Narrative { get; init; }

    public decimal? Hours { get; init; }

    public decimal? Amount { get; init; }

    /// <summary>Task code as billed by the firm, if any.</summary>
    public string? BilledTaskCode { get; init; }

    /// <summary>Activity code as billed by the firm, if any.</summary>
    public string? BilledActivityCode { get; init; }

    /// <summary>Expense code as billed by the firm, if any.</summary>
    public string? BilledExpenseCode { get; init; }
}

/// <summary>An allowed code with the description and guidance text the model reads (FR-22).</summary>
public sealed record InvoiceCodeOption(string Code, string Description, string? Guidance);

/// <summary>Output of <see cref="IInvoiceAi.CodeLinesAsync"/>. Output JSON: <c>lines</c> (required array).</summary>
public sealed record InvoiceCodeLinesResult
{
    public required IReadOnlyList<InvoiceLineCoding> Lines { get; init; }
}

/// <summary>
/// The model's coding of one line: codes, confidence and observations only — never amounts. Output JSON:
/// <c>lineKey</c> (required), <c>taskCode</c> / <c>activityCode</c> / <c>expenseCode</c> (string or null),
/// <c>confidence</c> (required, 0–1), <c>observations</c> (required array, may be empty).
/// </summary>
public sealed record InvoiceLineCoding
{
    public required string LineKey { get; init; }

    public string? TaskCode { get; init; }

    public string? ActivityCode { get; init; }

    public string? ExpenseCode { get; init; }

    public required decimal Confidence { get; init; }

    public required IReadOnlyList<InvoiceLineObservation> Observations { get; init; }
}

/// <summary>Kind of AI observation on a line. Output JSON values are these names verbatim.</summary>
public enum InvoiceLineObservationType
{
    BlockBilling,
    VagueDescription,
}

/// <summary>
/// One observation. Output JSON: <c>type</c> (required), <c>confidence</c> (required, 0–1),
/// <c>quotedText</c> (required — the narrative text the observation is about).
/// </summary>
public sealed record InvoiceLineObservation
{
    public required InvoiceLineObservationType Type { get; init; }

    public required decimal Confidence { get; init; }

    public required string QuotedText { get; init; }
}

// ── MatchCodeSetAsync (consumer type invoice-codeset-match; FR-22, D-93) ─────

/// <summary>Which reference table the row to match comes from. Sent to the model as the name verbatim.</summary>
public enum InvoiceCodeSetReferenceType
{
    MatterType,
    PracticeArea,
}

/// <summary>
/// Input to <see cref="IInvoiceAi.MatchCodeSetAsync"/>: one matter type or practice area, the code sets it may be mapped to (already
/// pre-ranked by the caller) and confirmed mappings from the same environment as examples. The caller refuses any returned id that
/// is not one of <see cref="Candidates"/> (ebilling-r1 task 054) — this facade only runs and parses.
/// </summary>
public sealed record InvoiceCodeSetMatchRequest
{
    /// <summary>Sent as <c>reference.type</c>.</summary>
    public required InvoiceCodeSetReferenceType ReferenceType { get; init; }

    /// <summary>The matter type or practice area name. Sent as <c>reference.name</c>.</summary>
    public required string ReferenceName { get; init; }

    /// <summary>The closed list of code sets the model may choose from. Sent as <c>candidates</c>.</summary>
    public required IReadOnlyList<InvoiceCodeSetCandidate> Candidates { get; init; }

    /// <summary>Confirmed mappings (examples, never candidates). Sent as <c>examples</c>; may be empty.</summary>
    public required IReadOnlyList<InvoiceCodeSetMatchExample> Examples { get; init; }

    /// <summary>Tenant id for the run context. Not sent to the model.</summary>
    public string? TenantId { get; init; }
}

/// <summary>
/// One code set the model may choose. Sent with camelCase names (<c>codeSetId</c>, <c>name</c>, <c>body</c>, <c>description</c>,
/// <c>matchHints</c>, <c>taskCodes</c>). <see cref="TaskCodes"/> are "code short-name" strings of the set's active Task codes.
/// </summary>
public sealed record InvoiceCodeSetCandidate(
    Guid CodeSetId,
    string Name,
    string? Body,
    string? Description,
    string? MatchHints,
    IReadOnlyList<string> TaskCodes);

/// <summary>A confirmed mapping an administrator made. Sent as <c>referenceType</c>, <c>referenceName</c>, <c>codeSetName</c>.</summary>
public sealed record InvoiceCodeSetMatchExample(InvoiceCodeSetReferenceType ReferenceType, string ReferenceName, string CodeSetName);

/// <summary>
/// Output of <see cref="IInvoiceAi.MatchCodeSetAsync"/>. Output JSON: <c>codeSetId</c> (required: a candidate's id as given, or
/// <see cref="NoneFits"/>), <c>confidence</c> (required, 0–1), <c>reason</c> (required, non-empty). <see cref="CodeSetId"/> is returned as
/// the model wrote it; the caller decides whether it is a candidate.
/// </summary>
public sealed record InvoiceCodeSetMatchResult
{
    /// <summary>The <c>codeSetId</c> value that means "no candidate fits".</summary>
    public const string NoneFits = "none";

    public required string CodeSetId { get; init; }

    public required decimal Confidence { get; init; }

    public required string Reason { get; init; }
}

// ── DraftRulesAsync (consumer type billing-rule-draft; FR-27) ────────────────

/// <summary>
/// Input to <see cref="IInvoiceAi.DraftRulesAsync"/>: a guideline / rate / procedure document and the closed
/// catalogue of billing rule types the model may propose (D-27).
/// </summary>
public sealed record BillingRuleDraftRequest
{
    /// <summary>Extracted text of the source document. Sent as <c>documentText</c>.</summary>
    public required string DocumentText { get; init; }

    /// <summary>The source document's id — logged only, not sent to the model.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>The closed list of rule types. Sent as <c>ruleTypes</c>.</summary>
    public required IReadOnlyList<BillingRuleTypeOption> RuleTypes { get; init; }

    /// <summary>Tenant id for the run context. Not sent to the model.</summary>
    public string? TenantId { get; init; }
}

/// <summary>
/// One allowed rule type. <see cref="ParameterSchemaJson"/> must be a JSON object (the type's parameter
/// schema); it is embedded in the operand as JSON, not as a string.
/// </summary>
public sealed record BillingRuleTypeOption(string RuleType, string Description, string ParameterSchemaJson);

/// <summary>
/// Output of <see cref="IInvoiceAi.DraftRulesAsync"/>. Output JSON: <c>proposals</c> (required array) and
/// <c>notConverted</c> (required array). The facade does not validate a proposal against its rule type —
/// the caller (075) does, dropping unknown or schema-invalid proposals (FR-27).
/// </summary>
public sealed record BillingRuleDraftResult
{
    public required IReadOnlyList<BillingRuleProposal> Proposals { get; init; }

    public required IReadOnlyList<BillingRuleNotConverted> NotConverted { get; init; }
}

/// <summary>
/// One proposed rule. Output JSON: <c>ruleType</c> (required), <c>parameters</c> (required JSON object,
/// surfaced here as its raw JSON text), <c>consequence</c> (required), <c>clauseQuote</c> (required),
/// <c>clauseLocation</c> (string or null).
/// </summary>
public sealed record BillingRuleProposal
{
    public required string RuleType { get; init; }

    public required string ParametersJson { get; init; }

    public required string Consequence { get; init; }

    public required string ClauseQuote { get; init; }

    public string? ClauseLocation { get; init; }
}

/// <summary>
/// Text the model could not convert to a catalogue rule. Output JSON: <c>text</c> (required),
/// <c>location</c> (string or null), <c>reason</c> (required).
/// </summary>
public sealed record BillingRuleNotConverted
{
    public required string Text { get; init; }

    public string? Location { get; init; }

    public required string Reason { get; init; }
}

// ── DraftFirmNoticeAsync (consumer type invoice-firm-notice; FR-50, D-60) ────

/// <summary>The outcome the notice explains. Sent to the model as the name verbatim.</summary>
public enum InvoiceFirmNoticeOutcome
{
    SubmissionRejected,
    ReturnedToFirm,
    InvoiceRejected,
    ApprovedWithAdjustments,
}

/// <summary>
/// Input to <see cref="IInvoiceAi.DraftFirmNoticeAsync"/>. Deliberately carries NO numeric member: amounts,
/// line references, clause quotes and the findings table are inserted from data by the notice template
/// (FR-50), so they never reach the model.
/// </summary>
public sealed record InvoiceFirmNoticeRequest
{
    /// <summary>Sent as <c>outcome</c>.</summary>
    public required InvoiceFirmNoticeOutcome Outcome { get; init; }

    /// <summary>Structured findings summary. Sent as <c>findings</c>.</summary>
    public required IReadOnlyList<InvoiceFirmNoticeFinding> Findings { get; init; }

    /// <summary>Tenant id for the run context. Not sent to the model.</summary>
    public string? TenantId { get; init; }
}

/// <summary>
/// One finding as the notice model sees it: the rule (and rule set) that produced it, its consequence and a
/// short non-numeric statement of the issue. Callers MUST NOT put amounts, line numbers or clause text in
/// <see cref="Issue"/> — the template inserts those (FR-50).
/// </summary>
public sealed record InvoiceFirmNoticeFinding(string RuleSetName, string RuleName, string Consequence, string Issue);

/// <summary>
/// Output of <see cref="IInvoiceAi.DraftFirmNoticeAsync"/>: prose only, no numeric member (FR-50). Output
/// JSON: <c>subject</c> (required, non-empty), <c>body</c> (required, non-empty).
/// </summary>
public sealed record InvoiceFirmNoticeDraft
{
    public required string Subject { get; init; }

    public required string Body { get; init; }
}

// ── ReadInvoiceAsync (Content Understanding custom analyzer; FR-21, FR-04, D-49, D-81) ──────────────────────
//
// Every extracted value is an ExtractedInvoiceField<T>: the value plus where it came from and how sure the
// analyzer was, so task 049 can write FR-04's per-field extraction-confidence JSON directly and the review UI can
// highlight the source (prototype DocumentView contract F-4: {page, x, y, w, h}, normalised 0-1).

/// <summary>Input to <see cref="IInvoiceAi.ReadInvoiceAsync"/>: one invoice document.</summary>
public sealed record InvoiceReadRequest
{
    /// <summary>The document bytes (PDF or image). Sent to Content Understanding as-is; never logged.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>MIME type of <see cref="Content"/>. Default <c>application/pdf</c>.</summary>
    public string ContentType { get; init; } = "application/pdf";

    /// <summary>The document's id (for example the sprk_document id) — logged only, never sent.</summary>
    public required Guid DocumentId { get; init; }
}

/// <summary>How a value was changed by the deterministic completion step after extraction (task 005 condition 1 / 2).</summary>
public enum ExtractedValueRepair
{
    /// <summary>The value is as the analyzer returned it.</summary>
    None,

    /// <summary>Clock-notation hours read as a decimal (1:30 returned as 1.30) converted to decimal hours (1.5), because hours × rate did not equal the amount and the converted hours do.</summary>
    ClockHoursConverted,

    /// <summary>The row printed no rate; the rate is the timekeeper's rate from the invoice's own timekeeper summary. Page, box and confidence are the summary value's.</summary>
    RateFromTimekeeperSummary,

    /// <summary>The value was missing and is computed from the row's other two values (rate = amount / hours, hours = amount / rate, amount = hours × rate). Page and box are absent.</summary>
    ComputedFromRow,

    /// <summary>A date outside the billing period whose day and month, swapped, fall inside it (a day-first date read month-first).</summary>
    DayMonthSwapped,
}

/// <summary>Where on the page a value was read: normalised 0-1 against the page's width and height, origin top-left.</summary>
/// <param name="Page">1-based page number in the ORIGINAL file (also for page-range reads).</param>
public sealed record ExtractedBoundingBox(int Page, decimal X, decimal Y, decimal W, decimal H);

/// <summary>
/// One extracted value. <typeparamref name="T"/> is <c>string?</c>, <c>DateOnly?</c> or <c>decimal?</c>; a value the
/// document does not show is <c>null</c> (often still with a confidence — the analyzer's confidence that it is absent).
/// </summary>
public sealed record ExtractedInvoiceField<T>
{
    /// <summary>The value after deterministic completion; null when the document does not show it.</summary>
    public T? Value { get; init; }

    /// <summary>Analyzer confidence 0-1, or null when the analyzer returned none.</summary>
    public decimal? Confidence { get; init; }

    /// <summary>1-based page in the original file, or null when the analyzer returned no source.</summary>
    public int? Page { get; init; }

    /// <summary>Source box, or null when there is no source or the page size is unknown.</summary>
    public ExtractedBoundingBox? BoundingBox { get; init; }

    /// <summary>How the completion step changed the value; <see cref="ExtractedValueRepair.None"/> when it did not.</summary>
    public ExtractedValueRepair Repair { get; init; }

    /// <summary>The value exactly as the analyzer returned it (differs from <see cref="Value"/> only when <see cref="Repair"/> is not None).</summary>
    public T? ReturnedValue { get; init; }
}

/// <summary>Invoice header (FR-21). Analyzer field names in brackets.</summary>
public sealed record ExtractedInvoiceHeader
{
    /// <summary>[FirmName] The issuing firm.</summary>
    public required ExtractedInvoiceField<string?> FirmName { get; init; }

    /// <summary>[FirmInvoiceNumber] As printed.</summary>
    public required ExtractedInvoiceField<string?> FirmInvoiceNumber { get; init; }

    /// <summary>[InvoiceDate]</summary>
    public required ExtractedInvoiceField<DateOnly?> InvoiceDate { get; init; }

    /// <summary>[BillingPeriodStart]</summary>
    public required ExtractedInvoiceField<DateOnly?> BillingPeriodStart { get; init; }

    /// <summary>[BillingPeriodEnd]</summary>
    public required ExtractedInvoiceField<DateOnly?> BillingPeriodEnd { get; init; }

    /// <summary>[ClientName] As addressed.</summary>
    public required ExtractedInvoiceField<string?> ClientName { get; init; }

    /// <summary>[MatterName] As printed.</summary>
    public required ExtractedInvoiceField<string?> MatterName { get; init; }

    /// <summary>[FirmMatterReference] The firm's own matter reference, as printed.</summary>
    public required ExtractedInvoiceField<string?> FirmMatterReference { get; init; }

    /// <summary>[PurchaseOrderAsQuoted] PO as quoted by the firm.</summary>
    public required ExtractedInvoiceField<string?> PurchaseOrderAsQuoted { get; init; }

    /// <summary>[FeesTotal]</summary>
    public required ExtractedInvoiceField<decimal?> FeesTotal { get; init; }

    /// <summary>[ExpensesTotal]</summary>
    public required ExtractedInvoiceField<decimal?> ExpensesTotal { get; init; }

    /// <summary>[AdjustmentsTotal] Negative for a reduction.</summary>
    public required ExtractedInvoiceField<decimal?> AdjustmentsTotal { get; init; }

    /// <summary>[TotalDue]</summary>
    public required ExtractedInvoiceField<decimal?> TotalDue { get; init; }

    /// <summary>[Currency] ISO 4217 code as read. A non-USD value is returned as read, never refused here (the evaluator's finding).</summary>
    public required ExtractedInvoiceField<string?> Currency { get; init; }
}

/// <summary>
/// One fee row ([TimeEntries]) or expense row ([Expenses]). Fee rows use the timekeeper, title, hours, rate and
/// task / activity codes; expense rows use quantity, unit cost and the expense code. The members a row type does
/// not use are empty fields (null value, no confidence).
/// </summary>
public sealed record ExtractedInvoiceLine
{
    /// <summary>Fee (time entry) or Expense.</summary>
    public required InvoiceLineType LineType { get; init; }

    /// <summary>0-based position within its table, in document order (fee and expense rows are numbered separately).</summary>
    public required int RowIndex { get; init; }

    /// <summary>[Date]</summary>
    public required ExtractedInvoiceField<DateOnly?> Date { get; init; }

    /// <summary>Fee: [TimekeeperAsBilled].</summary>
    public required ExtractedInvoiceField<string?> TimekeeperAsBilled { get; init; }

    /// <summary>Fee: [TitleAsBilled] title or classification as billed.</summary>
    public required ExtractedInvoiceField<string?> TitleAsBilled { get; init; }

    /// <summary>Fee: [Hours] decimal hours.</summary>
    public required ExtractedInvoiceField<decimal?> Hours { get; init; }

    /// <summary>Fee: [Rate] hourly rate.</summary>
    public required ExtractedInvoiceField<decimal?> Rate { get; init; }

    /// <summary>Expense: [Quantity].</summary>
    public required ExtractedInvoiceField<decimal?> Quantity { get; init; }

    /// <summary>Expense: [UnitCost].</summary>
    public required ExtractedInvoiceField<decimal?> UnitCost { get; init; }

    /// <summary>[Amount] the row's amount.</summary>
    public required ExtractedInvoiceField<decimal?> Amount { get; init; }

    /// <summary>Fee: [Narrative]; Expense: [Description].</summary>
    public required ExtractedInvoiceField<string?> Narrative { get; init; }

    /// <summary>Fee: [TaskCodeAsBilled].</summary>
    public required ExtractedInvoiceField<string?> TaskCodeAsBilled { get; init; }

    /// <summary>Fee: [ActivityCodeAsBilled].</summary>
    public required ExtractedInvoiceField<string?> ActivityCodeAsBilled { get; init; }

    /// <summary>Expense: [ExpenseCodeAsBilled].</summary>
    public required ExtractedInvoiceField<string?> ExpenseCodeAsBilled { get; init; }

    /// <summary>
    /// The row's box: the union of its values' boxes on the page of its first located value (prototype DocumentView
    /// row box, F-4). Null when no value has a box.
    /// </summary>
    public ExtractedBoundingBox? RowBoundingBox { get; init; }
}

/// <summary>One row of the invoice's timekeeper summary table ([TimekeeperSummary]).</summary>
public sealed record ExtractedTimekeeperSummaryRow
{
    /// <summary>[Name]</summary>
    public required ExtractedInvoiceField<string?> Name { get; init; }

    /// <summary>[Title]</summary>
    public required ExtractedInvoiceField<string?> Title { get; init; }

    /// <summary>[Hours]</summary>
    public required ExtractedInvoiceField<decimal?> Hours { get; init; }

    /// <summary>[Rate]</summary>
    public required ExtractedInvoiceField<decimal?> Rate { get; init; }

    /// <summary>[Amount]</summary>
    public required ExtractedInvoiceField<decimal?> Amount { get; init; }
}

/// <summary>
/// Output of <see cref="IInvoiceAi.ReadInvoiceAsync"/>: the whole invoice as the analyzer read it, after the
/// deterministic completion step. Never partial: a read that fails anywhere raises <see cref="InvoiceAiException"/>.
/// </summary>
public sealed record ExtractedInvoice
{
    public required ExtractedInvoiceHeader Header { get; init; }

    /// <summary>Fee rows first, then expense rows, each in document order.</summary>
    public required IReadOnlyList<ExtractedInvoiceLine> Lines { get; init; }

    public required IReadOnlyList<ExtractedTimekeeperSummaryRow> TimekeeperSummary { get; init; }

    /// <summary>Pages analyzed (the document's page count).</summary>
    public required int PageCount { get; init; }

    /// <summary>The analyzer that produced the result (for the extraction record and evaluation).</summary>
    public required string AnalyzerId { get; init; }

    /// <summary>The api-version the analyzer ran on.</summary>
    public required string ApiVersion { get; init; }
}
