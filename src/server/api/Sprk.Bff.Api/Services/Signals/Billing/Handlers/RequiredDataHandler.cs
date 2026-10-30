using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 10, RequiredData (design.md 6.1; spec FR-21): the header and line data the rule lists must be present, and the
/// invoice must be in US dollars.
/// </summary>
/// <remarks>
/// <para><b>Listed fields.</b> One finding per missing field per subject, reason <c>required_data.missing_field</c>, the field's
/// name in the <c>missingField</c> token (the schema's own spelling). A field applies to the lines it makes sense for: the
/// timekeeper, task code and activity code to fee lines; the expense code to expense lines; the line date and narrative to every
/// line. The header fields are checked once. A value of only whitespace is missing. The codes are the ones the firm billed
/// (<see cref="BillingLineFacts.BilledTaskCode"/> and its siblings), not the AI's: the rule asks what the invoice carries.</para>
/// <para><b>Currency (FR-21, D-20, D-119).</b> R1 is USD only. Whenever this rule runs, an invoice whose currency is read and is not
/// USD by the one shared normaliser (<see cref="BillingCurrency.IsUsd"/>: <c>USD</c> in any case, a bare <c>"$"</c>, <c>"US$"</c> or <c>"USD$"</c>) gets one
/// invoice-level finding, reason <c>required_data.currency_not_usd</c>, <c>missingField</c> =
/// <c>currency</c>, message <see cref="BillingRuleCatalogue.CurrencyNotUsdTemplate"/>; that reason is flag-only
/// (<see cref="BillingRuleCatalogue.IsFlagOnlyReason"/>). It is a finding, never an exception, and it does not depend on the rule listing a field: the rule's
/// vocabulary has no currency member, and a foreign-currency invoice is unsupported however the rule is configured. A currency
/// that was not read, or was read below the confidence floor (<c>HeaderConfidence["Currency"]</c> unknown or under the reliability
/// threshold, the evaluator's own test), is not flagged (no evidence, D-123); the evaluator's flag-only D-115 finding reports it
/// instead ("missing" or "unclear"). A bare "$" is USD
/// (D-119), as it is for the evaluator's money rules, so a "$" invoice is never both rate-checked and flagged "not USD".</para>
/// <para><b>An absent value is not reliably absent.</b> A missing field's confidence is whatever the fact set holds for it, or
/// unknown, which task 073 treats as not reliable: a field the extraction simply failed to find cannot reject an invoice (NFR-06).</para>
/// <para><b>Reads:</b> the missing field itself, or Currency; nothing else.</para>
/// </remarks>
internal static class RequiredDataHandler
{
    // Header fields and line fields in the order of the 070 vocabulary, so output order does not depend on the body's order.
    private static readonly string[] HeaderOrder = ["firmInvoiceNumber", "invoiceDate", "billingPeriodStart", "billingPeriodEnd"];
    private static readonly string[] LineOrder = ["timekeeper", "lineDate", "narrative", "taskCode", "activityCode", "expenseCode"];

    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var required = BillingRuleHandlers.RequireList(context.Rule.Fields, context, "fields");
        var invoice = context.Facts.Invoice;
        var findings = new List<BillingFinding>();

        foreach (var field in HeaderOrder.Where(f => required.Contains(f, StringComparer.Ordinal)))
        {
            var (fieldName, present) = field switch
            {
                "firmInvoiceNumber" => (BillingFieldNames.FirmInvoiceNumber, BillingText.Text(invoice.FirmInvoiceNumber) is not null),
                "invoiceDate" => (BillingFieldNames.InvoiceDate, invoice.InvoiceDate is not null),
                "billingPeriodStart" => (BillingFieldNames.BillingPeriodStart, invoice.BillingPeriodStart is not null),
                _ => (BillingFieldNames.BillingPeriodEnd, invoice.BillingPeriodEnd is not null),
            };

            if (!present)
            {
                var values = new[] { context.HeaderField("invoice." + field, fieldName, null) };
                findings.Add(context.Finding(BillingSubject.ForInvoice(), BillingReasonCodes.RequiredFieldMissing, values, new FactValues().Add("missingField", field)));
            }
        }

        var currency = BillingText.Text(invoice.Currency);
        if (currency is not null && !BillingCurrency.IsUsd(currency))
        {
            // Only a currency read reliably is reported as "not USD" (D-123): a "EUR" read below the floor, or with no known
            // confidence, may be a misread "USD", and the evaluator's D-115 finding says "unclear" for it. Same floor as the
            // evaluator's (HandlerContext marks a read below it from the same ReliabilityThreshold).
            var read = context.HeaderField("invoice.currency", BillingFieldNames.Currency, currency);
            if (!read.BelowReliabilityThreshold)
            {
                findings.Add(context.Finding(BillingSubject.ForInvoice(), BillingReasonCodes.CurrencyNotUsd, new[] { read }, new FactValues().Add("missingField", "currency")));
            }
        }

        foreach (var line in context.Facts.Lines)
        {
            foreach (var field in LineOrder.Where(f => required.Contains(f, StringComparer.Ordinal)))
            {
                if (!Applies(field, line) || IsPresent(field, line))
                {
                    continue;
                }

                var values = new[] { context.LineField(line, "line." + field, FieldNameOf(field), null) };
                findings.Add(context.Finding(BillingSubject.ForLine(line.Index), BillingReasonCodes.RequiredFieldMissing, values, new FactValues().Add("missingField", field)));
            }
        }

        return findings;
    }

    private static bool Applies(string field, BillingLineFacts line) => field switch
    {
        "timekeeper" or "taskCode" or "activityCode" => line.LineType == InvoiceLineType.Fee,
        "expenseCode" => line.LineType == InvoiceLineType.Expense,
        _ => true,
    };

    private static bool IsPresent(string field, BillingLineFacts line) => field switch
    {
        "timekeeper" => BillingText.Text(line.TimekeeperName) is not null,
        "lineDate" => line.Date is not null,
        "narrative" => BillingText.Text(line.Narrative) is not null,
        "taskCode" => BillingText.Text(line.BilledTaskCode) is not null,
        "activityCode" => BillingText.Text(line.BilledActivityCode) is not null,
        _ => BillingText.Text(line.BilledExpenseCode) is not null,
    };

    private static string FieldNameOf(string field) => field switch
    {
        "timekeeper" => BillingFieldNames.TimekeeperAsBilled,
        "lineDate" => BillingFieldNames.Date,
        "narrative" => BillingFieldNames.Narrative,
        "taskCode" => BillingFieldNames.BilledTaskCode,
        "activityCode" => BillingFieldNames.BilledActivityCode,
        _ => BillingFieldNames.BilledExpenseCode,
    };
}
