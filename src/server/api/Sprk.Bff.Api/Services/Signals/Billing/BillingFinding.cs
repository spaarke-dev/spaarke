using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Signals.Billing;

// What a billing rule handler returns (ebilling-r1 task 071; spec FR-24, FR-25, FR-26). A finding is a fact about one invoice or
// one line, with the evidence behind it. It is not a decision: a handler never rejects a submission (task 073 decides, from the
// consequence on the policy version, the confidence of the fields the finding read and the type's eligibility), and it carries no
// prose: the firm-visible text is composed downstream from the reason code (D-66, FR-15) and the policy version's message template.

/// <summary>Whether a finding is about the invoice as a whole or one line of it.</summary>
public enum BillingSubjectKind
{
    Invoice,
    Line,
}

/// <summary>
/// The subject of a finding. Invoice and line rows do not exist while an invoice is being evaluated (the persist step is
/// all-or-nothing, task 049), so a line is named by its <see cref="BillingLineFacts.Index"/>; 049 maps it to the row it creates.
/// </summary>
public readonly record struct BillingSubject(BillingSubjectKind Kind, int? LineIndex)
{
    public static BillingSubject ForInvoice() => new(BillingSubjectKind.Invoice, null);

    public static BillingSubject ForLine(int lineIndex) => new(BillingSubjectKind.Line, lineIndex);
}

/// <summary>The kind of adjustment a finding suggests. Never applied by a handler: applying it is a reviewer action (WP4).</summary>
public enum BillingAdjustmentKind
{
    /// <summary>Reduce the line's amount by <see cref="BillingSuggestedAdjustment.Amount"/> (ApprovedRate).</summary>
    Reduce,

    /// <summary>Disallow the line; <see cref="BillingSuggestedAdjustment.Amount"/> is the line amount that would be disallowed (NonBillableActivity).</summary>
    Disallow,
}

/// <param name="Kind">Reduce or disallow.</param>
/// <param name="Amount">Money, rounded to the cent (midpoint away from zero), or <c>null</c> when the amount cannot be computed from what was read.</param>
public sealed record BillingSuggestedAdjustment(BillingAdjustmentKind Kind, decimal? Amount);

/// <summary>
/// One value a finding read. An <b>extracted</b> value carries the <see cref="FieldRef"/> that names it and its combined
/// confidence (D-47): task 073 requires every extracted field a Reject finding read to be reliable. A <b>reference</b> value
/// (an approved rate, a core-record date) is data from the system's own tables, has no field reference and no confidence.
/// </summary>
public sealed record BillingValueRead
{
    /// <summary>A readable key, such as <c>line.rate</c> or <c>approvedRate</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The value in invariant text (dates <c>yyyy-MM-dd</c>, money to the cent, numbers without trailing zeros); <c>null</c> = absent.</summary>
    public string? Value { get; init; }

    /// <summary>Set for an extracted value; <c>null</c> for a reference value.</summary>
    public FieldRef? ExtractedField { get; init; }

    /// <summary>The combined confidence of an extracted value; <c>null</c> = none is known (task 073 treats it as not reliable).</summary>
    public decimal? Confidence { get; init; }

    /// <summary>
    /// True for an extracted value whose confidence is below the reliability threshold or unknown (NFR-05): the finding says so,
    /// instead of presenting a doubtful value as a fact. Always false for a reference value.
    /// </summary>
    public bool BelowReliabilityThreshold { get; init; }

    public bool IsExtracted => ExtractedField.HasValue;
}

/// <summary>
/// The policy version a finding came from, besides what <see cref="BillingRuleDefinition"/> already holds (its version id and
/// policy code): the policy id and the clause the rule was drawn from (<c>sprk_policyversion.sprk_clausereference</c> and
/// <c>sprk_clauselocation</c>, task 019). Task 072 reads them with the version row.
/// </summary>
public sealed record BillingRuleOrigin(Guid PolicyId, string? ClauseReference, string? ClauseLocation);

/// <summary>One finding: a rule that does not hold for an invoice or a line, with the values behind it.</summary>
public sealed record BillingFinding
{
    public required RuleType RuleType { get; init; }

    public required Guid PolicyId { get; init; }

    public required string PolicyCode { get; init; }

    public required Guid PolicyVersionId { get; init; }

    /// <summary>The quoted clause the rule was drawn from (spec FR-09), or <c>null</c> when the version has none.</summary>
    public string? ClauseReference { get; init; }

    /// <summary>Where the clause is (section, page), or <c>null</c>.</summary>
    public string? ClauseLocation { get; init; }

    public required BillingSubject Subject { get; init; }

    /// <summary>A stable code, see <see cref="BillingReasonCodes"/>. The firm-visible text is composed from it downstream.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>Every value the finding read, extracted and reference, in a stable order, with the confidence of the extracted ones.</summary>
    public required IReadOnlyList<BillingValueRead> ValuesRead { get; init; }

    /// <summary>The adjustment the rule suggests, where design.md 6.1 allows one; <c>null</c> otherwise.</summary>
    public BillingSuggestedAdjustment? SuggestedAdjustment { get; init; }

    /// <summary>
    /// The values for the <c>{{token}}</c>s of the policy version's message template: only keys of
    /// <see cref="BillingRuleCatalogue.TemplateFactFields"/> for <see cref="RuleType"/> (enforced when a finding is built), only
    /// non-null values, in invariant text. A token with no value for this subject (a line token on an invoice-level
    /// finding) is absent, and the signal writer refuses a template that names it.
    /// </summary>
    public required IReadOnlyDictionary<string, string> FactValues { get; init; }

    /// <summary>
    /// True for a Duplicate finding whose matching prior is across the secure wall or whose wall could not be established
    /// (D-112): it carries no tracking number, no tracking-number token and no line detail, and its message is
    /// <see cref="BillingRuleCatalogue.WithheldDuplicateTemplate"/> (the composer uses that, not the policy version's template,
    /// which names the withheld token). Its <see cref="ValuesRead"/> still list the fields the match read, with the value text
    /// left out, so task 073 still sees every read field and its confidence.
    /// </summary>
    public bool DetailWithheld { get; init; }

    /// <summary>
    /// The extracted fields the finding read (task 073): one entry per extracted value, whether or not it was the one that
    /// failed. Reference values are not listed: they have no extraction confidence to check.
    /// </summary>
    public IReadOnlyList<FieldRef> ReadFields => ValuesRead.Where(v => v.ExtractedField.HasValue).Select(v => v.ExtractedField!.Value).ToArray();

    /// <summary>True when any extracted value the finding read is below the reliability threshold or has no known confidence (NFR-05).</summary>
    public bool ReadsLowConfidenceValue => ValuesRead.Any(v => v.BelowReliabilityThreshold);
}

/// <summary>
/// The stable reason codes of billing findings. One or a few per type; the sub-reasons of ApprovedTimekeeper and ApprovedRate mirror
/// the resolution reasons (046) so the firm is told why a name or a rate could not be established.
/// </summary>
public static class BillingReasonCodes
{
    // ApprovedTimekeeper: the roster match of a fee line failed (046 TimekeeperUnmatchedReason).
    public const string TimekeeperUnmatched = "approved_timekeeper.unmatched";
    public const string TimekeeperNameMissing = "approved_timekeeper.name_missing";
    public const string TimekeeperLineDateMissing = "approved_timekeeper.line_date_missing";
    public const string TimekeeperNameIncomplete = "approved_timekeeper.name_incomplete";
    public const string TimekeeperNotOnRoster = "approved_timekeeper.not_on_roster";
    public const string TimekeeperNotApprovedOnLineDate = "approved_timekeeper.not_approved_on_line_date";
    public const string TimekeeperAmbiguous = "approved_timekeeper.ambiguous";
    public const string TimekeeperAiBelowThreshold = "approved_timekeeper.ai_below_threshold";
    public const string TimekeeperAiNotClearlyAhead = "approved_timekeeper.ai_not_clearly_ahead";
    public const string TimekeeperAiUnavailable = "approved_timekeeper.ai_unavailable";

    // ApprovedRate.
    public const string RateAboveApproved = "approved_rate.above_approved";
    public const string RateUnresolved = "approved_rate.unresolved";
    public const string RateTimekeeperUnmatched = "approved_rate.timekeeper_unmatched";
    public const string RateLineDateMissing = "approved_rate.line_date_missing";
    public const string RateNoRateInForce = "approved_rate.no_rate_in_force";
    public const string RateAmbiguous = "approved_rate.ambiguous";

    public const string TimeNotAMultipleOfIncrement = "time_increment.not_a_multiple";
    public const string DailyHoursOverCap = "daily_hours_cap.over_cap";
    public const string NonBillableCode = "non_billable_activity.code_listed";
    public const string ExpenseCodeNotReimbursable = "expense_rule.code_not_reimbursable";
    public const string ExpenseAmountOverCap = "expense_rule.amount_over_cap";
    public const string InvoiceTooOld = "invoice_age.too_old";
    public const string BeforeCoreRecordOpened = "core_record_window.before_open";
    public const string AfterCoreRecordClosed = "core_record_window.after_close";
    public const string CoreRecordClosed = "core_record_window.core_record_closed";
    public const string CoreRecordClosedToBilling = "core_record_window.closed_to_billing";
    public const string DuplicateInvoiceNumber = "duplicate.invoice_number";
    public const string DuplicateOfPriorSubmissionLine = "duplicate.line_prior_submission";
    public const string DuplicateOfEarlierLine = "duplicate.line_same_invoice";
    public const string RequiredFieldMissing = "required_data.missing_field";
    public const string CurrencyNotUsd = "required_data.currency_not_usd";
    public const string AiBlockBilling = "ai_observation.block_billing";
    public const string AiVagueDescription = "ai_observation.vague_description";

    // Task 115 (procedure model 6.3). Flag only: neither type is reject-eligible and neither suggests an adjustment.
    public const string AddedFeeOverCap = "added_fee.over_cap";

    /// <summary>A percent cap could not be judged: the other lines total 0 or less, or one of them has no amount. Flag only, invoice level.</summary>
    public const string AddedFeeNotChecked = "added_fee.not_checked";

    /// <summary>A line's billed rate is above the agreed discounted rate plus the tolerance. Per line only: an invoice-level discount, or a
    /// discount applied in part elsewhere on the invoice, is not seen, so the reason claims only the rate comparison.</summary>
    public const string DiscountRateAboveDiscounted = "discount.rate_above_discounted";

    /// <summary>At least one fee line in scope has no resolved rack rate, so its discount was not checked. One per invoice, Flag only (as D-115).</summary>
    public const string DiscountNotCheckedNoRackRate = "discount.not_checked_no_rack_rate";

    // SubmissionIntegrity (D-125): findings task 049 creates directly, never from a rule body. Flag only.
    public const string SubmissionInvoiceNumberMismatch = "submission.invoice_number_mismatch";

    /// <summary>The D-91 mismatch when the number read is not an invoice number (<see cref="BillingRuleCatalogue.IsInvoiceNumberShaped"/>), so
    /// it is not cited (049 F1-3): a long or crafted extracted value never reaches <c>sprk_sentence</c>, and the finding still holds.</summary>
    public const string SubmissionInvoiceNumberMismatchNotShown = "submission.invoice_number_mismatch_not_shown";
    public const string CodingRefusedCode = "coding.refused_code";

    /// <summary>A code outside the line's code set that is not a code identifier, so its text is not cited (049 part D, D-125 review (b)).</summary>
    public const string CodingRefusedCodeNotShown = "coding.refused_code_not_shown";

    /// <summary>A code of a kind the line's type does not take (047 <c>NotApplicableToLineType</c>; D-125 review (b)).</summary>
    public const string CodingCodeKindNotForLine = "coding.code_kind_not_for_line";
    public const string CodingFirmCodeMismatch = "coding.firm_code_mismatch";

    /// <summary>The firm-code mismatch when the firm's code is not a code identifier (<see cref="BillingRuleCatalogue.IsCodeShaped"/>), so it
    /// is not cited (049 F1-3), as <see cref="CodingRefusedCodeNotShown"/>.</summary>
    public const string CodingFirmCodeMismatchNotShown = "coding.firm_code_mismatch_not_shown";
    public const string CodingCodeSetNeedsReview = "coding.code_set_needs_review";

    // D-126 (task 049 part C): the invoice was read but its lines could not be coded, so it is created In Review with the lines uncoded.
    public const string CodingNoCodeSet = "coding.no_code_set";
    public const string CodingUnavailable = "coding.unavailable";
}
