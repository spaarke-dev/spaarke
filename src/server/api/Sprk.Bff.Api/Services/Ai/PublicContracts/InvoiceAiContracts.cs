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

    public InvoiceAiException(string reasonCode, string consumerType, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ReasonCode = reasonCode ?? throw new ArgumentNullException(nameof(reasonCode));
        ConsumerType = consumerType ?? throw new ArgumentNullException(nameof(consumerType));
    }

    /// <summary>One of <see cref="BindingMissing"/>, <see cref="CompletionFailed"/>, <see cref="OutputInvalid"/>.</summary>
    public string ReasonCode { get; }

    /// <summary>The <see cref="ConsumerTypes"/> key of the capability that failed.</summary>
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
/// Output of <see cref="IInvoiceAi.ClassifyAsync"/>. Mirrors the Finance <c>ClassificationResult</c> field
/// for field so task 042 maps one-to-one. Output JSON: <c>classification</c> (required),
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
