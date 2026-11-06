namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 7, InvoiceAge (design.md 6.1): a line dated more than N days before the invoice date.
/// </summary>
/// <remarks>
/// <b>The check</b> is the exact whole-day difference <c>invoiceDate - lineDate</c>. The rule fires when it is <b>greater than</b> N:
/// a line dated exactly N days before the invoice date holds; N + 1 days fires. A line dated after the invoice date is not old.
/// With no invoice date, or no line date, there is nothing to compare and nothing fires (a missing date is RequiredData's).
/// <para><b>Credit lines are skipped (D-105).</b> A line with negative hours or a negative amount
/// (<see cref="BillingMath.IsCreditLine"/>) is not judged: a reversal legitimately carries the old date of the charge it reverses.</para>
/// <para><b>Reads:</b> Date (line) and InvoiceDate (header), both extracted, and the line's Hours and Amount when it has them: the
/// credit test that precedes the finding reads both (<see cref="HandlerContext.WithCreditTestReads"/>).</para>
/// </remarks>
internal static class InvoiceAgeHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var maxAgeDays = BillingRuleHandlers.Require(context.Rule.MaxAgeDays, context, "maxAgeDays");
        var findings = new List<BillingFinding>();
        if (context.Facts.Invoice.InvoiceDate is not { } invoiceDate)
        {
            return findings;
        }

        foreach (var line in context.Facts.Lines)
        {
            // A credit line (D-105) is skipped: a reversal legitimately carries the date of the charge it reverses.
            if (BillingMath.IsCreditLine(line) || line.Date is not { } lineDate || invoiceDate.DayNumber - lineDate.DayNumber <= maxAgeDays)
            {
                continue;
            }

            var values = new[]
            {
                context.LineDate(line),
                context.HeaderField("invoice.date", BillingFieldNames.InvoiceDate, BillingText.Date(invoiceDate)),
            };

            var facts = new FactValues()
                .Add("line.date", BillingText.Date(lineDate))
                .Add("invoice.date", BillingText.Date(invoiceDate))
                .Add("rule.maxAgeDays", BillingText.Number(maxAgeDays));

            findings.Add(context.Finding(
                BillingSubject.ForLine(line.Index), BillingReasonCodes.InvoiceTooOld, context.WithCreditTestReads(line, values), facts));
        }

        return findings;
    }
}
