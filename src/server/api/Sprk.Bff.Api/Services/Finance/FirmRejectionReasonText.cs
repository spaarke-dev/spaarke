using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The firm-safe description of each reason a submission can be REJECTED on (ebilling-r1 task 130d, FR-18; owner decision 2026-11-09, "option C
/// both"): a closed table keyed by the reason codes of <see cref="BillingRuleCatalogue.RejectCapableReasons"/>, defined here in code.
/// </summary>
/// <remarks>
/// <para><b>Partner-visible text.</b> Every entry is static, neutral, firm-facing English of one or two sentences. An entry never interpolates
/// anything (no value, name, amount, date, clause text, rule set name or section reference), and holds no format placeholder. It is shown on a
/// rejection finding whatever the rule set's visible-to-firms flag says, and a non-blank <c>sprk_signal.sprk_firmreason</c> replaces it
/// (<see cref="FirmFindingsProjection"/>). Adding a reject-capable reason to the catalogue without an entry here fails
/// <c>FirmRejectionReasonTextGuardTests</c> (and, if one ships anyway, the rejection fails closed to <see cref="GenericSentence"/>).</para>
/// <para><b>One text per code.</b> The text never varies with what else is known about the finding: a Duplicate rejection reads the same whether
/// or not its detail was withheld (the earlier invoice is outside the caller's scope), so the reason cannot tell a firm which case it is in.</para>
/// <para><b>Why a separate file.</b> <see cref="FirmRejectionFindings"/> is the allow-list parse of the stored findings document; this table is
/// reviewed wording, a different reason to change, and the guard and the future Signal writer (DEF-060) read it without the parser.</para>
/// </remarks>
internal static class FirmRejectionReasonText
{
    /// <summary>The stable log code for a rejecting finding whose reason code has no entry (the code only; Warning, server side).</summary>
    internal const string MissingEntryCode = "EBILLING-FIRM-OUTCOME-REASON-TEXT-MISSING";

    /// <summary>What a firm is told when a rejection's reason has no entry: neutral, and never blank or a raw code.</summary>
    internal const string GenericSentence = "This invoice did not meet a billing rule.";

    internal static readonly IReadOnlyDictionary<string, string> Table = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BillingReasonCodes.RateAboveApproved] =
            "A billed rate is higher than the rate approved for this timekeeper.",
        [BillingReasonCodes.ExpenseCodeNotReimbursable] =
            "An expense was billed under a code that is not reimbursable.",
        [BillingReasonCodes.ExpenseAmountOverCap] =
            "An expense amount is higher than the maximum allowed for that expense.",
        [BillingReasonCodes.InvoiceTooOld] =
            "The invoice is dated further in the past than the billing guidelines allow.",
        [BillingReasonCodes.BeforeCoreRecordOpened] =
            "A billed date falls before the matter, project or work assignment was opened.",
        [BillingReasonCodes.AfterCoreRecordClosed] =
            "A billed date falls after the matter, project or work assignment was closed.",
        [BillingReasonCodes.CoreRecordClosed] =
            "The matter, project or work assignment this invoice is billed against is closed.",
        [BillingReasonCodes.CoreRecordClosedToBilling] =
            "The matter, project or work assignment this invoice is billed against is closed to new billing.",
        [BillingReasonCodes.DuplicateInvoiceNumber] =
            "This invoice number has already been used with this client. Please check it or contact the client.",
        [BillingReasonCodes.RequiredFieldMissing] =
            "A required invoice field is missing.",
    };

    /// <summary>
    /// The text to show for <paramref name="reasonCode"/> from <paramref name="table"/>; <see cref="GenericSentence"/> when the code has no entry
    /// or the entry is blank (a Warning with <see cref="MissingEntryCode"/> and the code only; the code never reaches the firm in the text).
    /// </summary>
    internal static string For(IReadOnlyDictionary<string, string> table, string reasonCode, ILogger logger, Guid submissionId)
    {
        if (table.TryGetValue(reasonCode, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        logger.LogWarning(
            "[{Code}] Submission {Id}: the rejection reason {ReasonCode} has no firm-safe text; the generic sentence is shown.",
            MissingEntryCode, submissionId, reasonCode);
        return GenericSentence;
    }
}
