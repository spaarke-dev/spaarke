namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The firm-facing label of each value a rejection finding read (ebilling-r1 ISS-042, #2933; D-66, EB-D-209, EB-D-210): a closed table keyed by the
/// internal name the rule's handler recorded (<c>line.rate</c>), defined here in code, with the label a firm reads ("Rate").
/// </summary>
/// <remarks>
/// <para><b>Partner-visible text.</b> Every label is static, short, sentence-case English: letters and spaces only, no interpolated data, no
/// internal key, reason code or rule-type name. The projection (<see cref="FirmFindingsProjection"/>) maps the stored name to the label when it
/// builds <see cref="FirmOutcomeValueRead"/>; the value itself is unchanged, and the wire shape is unchanged (<c>name</c> now carries the label).
/// Nothing internal reads <see cref="FirmOutcomeValueRead.Name"/> as a key: the stored findings document keeps the raw name.</para>
/// <para><b>Only extracted values reach a firm.</b> <see cref="FirmRejectionFindings"/> drops a value that carries no extracted-field reference
/// (an approved rate, a core-record date, a duplicate's earlier invoice) before the projection sees it, so the table lists only the names the
/// producers (the Billing handlers and the evaluator) give an extracted value of the firm's own invoice. A name outside the table falls back to
/// <see cref="GenericLabel"/>; the raw name never reaches the firm. <c>FirmValueReadLabelGuardTests</c> scans the producers, fails when one
/// emits an extracted name with no entry here, and fails closed on a call or name shape it cannot read (see the guard's remarks for the exact list).</para>
/// <para><b>Why a separate file.</b> It is the same closed-table-with-fallback shape as <see cref="FirmRejectionReasonText"/>, for a different
/// key (a value name, not a reason code) and a different reason to change (a new extracted field, not a new rule reason).</para>
/// </remarks>
internal static class FirmValueReadLabel
{
    /// <summary>The stable log code for a value whose name has no entry (the name only; Warning, server side).</summary>
    internal const string MissingEntryCode = "EBILLING-FIRM-OUTCOME-VALUE-LABEL-MISSING";

    /// <summary>What a firm reads for a value whose name has no entry: neutral, and never blank or a raw key.</summary>
    internal const string GenericLabel = "Value";

    /// <summary>The longest label (a guard bound; a label is a column heading, not a sentence).</summary>
    internal const int MaxLabelLength = 40;

    /// <summary>Internal value name to firm label (ordinal). The producer of each name is listed in notes/iss042-firm-value-labels.md.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Table = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Line values.
        ["line.rate"] = "Rate",
        ["line.hours"] = "Hours",
        ["line.amount"] = "Amount",
        ["line.date"] = "Line date",
        ["line.lineDate"] = "Line date",
        ["line.timekeeperName"] = "Timekeeper",
        ["line.timekeeper"] = "Timekeeper",
        ["line.narrative"] = "Description",
        ["line.expenseCode"] = "Expense code",
        ["line.taskCode"] = "Task code",
        ["line.activityCode"] = "Activity code",

        // Invoice (header) values.
        ["invoice.date"] = "Invoice date",
        ["invoice.invoiceDate"] = "Invoice date",
        ["invoice.firmInvoiceNumber"] = "Invoice number",
        ["invoice.billingPeriodStart"] = "Billing period start",
        ["invoice.billingPeriodEnd"] = "Billing period end",
        ["invoice.currency"] = "Currency",
    };

    /// <summary>
    /// The label to show for the value named <paramref name="name"/> from <paramref name="table"/>; <see cref="GenericLabel"/> when the name has no
    /// entry or the entry is blank (a Warning with <see cref="MissingEntryCode"/> and the name only; the name never reaches the firm).
    /// </summary>
    internal static string For(IReadOnlyDictionary<string, string> table, string name, ILogger logger, Guid submissionId)
    {
        if (table.TryGetValue(name, out var label) && !string.IsNullOrWhiteSpace(label))
        {
            return label.Trim();
        }

        logger.LogWarning(
            "[{Code}] Submission {Id}: the value name {ValueName} has no firm label; the generic label is shown.",
            MissingEntryCode, submissionId, name);
        return GenericLabel;
    }
}
