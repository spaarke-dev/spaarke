using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 13, Discount (ebilling-r1 task 115; procedure-and-file-model.md 6.3): a fee line's billed rate is at most the rack rate
/// less the agreed discount, plus the rule's tolerance. A check only ("rate above the agreed discounted rate"): Flag, no suggested
/// adjustment.
/// </summary>
/// <remarks>
/// <para><b>Per line only.</b> The check compares each fee line's rate. A firm may give the discount at invoice level (a discount
/// line or header adjustment) or give part of it elsewhere; that is not seen, so the finding says only that a rate is above the
/// discounted rate, never that the discount was not applied (ontology-r1 115 review F2-2).</para>
/// <para><b>The check.</b> The discounted rate is <c>rack x (1 - discountPercent / 100)</c>, to the cent; the limit is
/// <c>rack x (1 - discountPercent / 100) x (1 + tolerancePercent / 100)</c> (no tolerance = 0), to the cent, and is carried as its own
/// value (<c>discountLimit</c>; review F2-1). The billed rate, to the cent, fires when it is <b>above</b> the limit; equal holds. As
/// <see cref="ApprovedRateHandler"/>.</para>
/// <para><b>Rack rate.</b> The rack rate on the rate-card row 046 resolved for the line (<see cref="RateResolution.RackRate"/>, status
/// Found). When any line in scope has none, the invoice gets ONE flag-only finding,
/// <see cref="BillingReasonCodes.DiscountNotCheckedNoRackRate"/> ("discount not checked: no rack rate"; the D-115 pattern, review
/// F2-3), reading those lines; the lines that do have a rack rate are still checked.</para>
/// <para><b>Skipped:</b> expense lines, fee lines with no billed rate, and credit lines (D-105: a reversal is not billed at a rate).</para>
/// <para><b>Reads:</b> Rate, Date, TimekeeperAsBilled (extracted), the credit-test reads (<see cref="HandlerContext.WithCreditTestReads"/>),
/// and the rack rate, the discounted rate, the limit and the rate card ids as reference values.</para>
/// </remarks>
internal static class DiscountHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var discount = BillingRuleHandlers.Require(context.Rule.DiscountPercent, context, "discountPercent");
        var tolerance = context.Rule.TolerancePercent ?? 0m;

        var findings = new List<BillingFinding>();
        var notChecked = new List<BillingValueRead>();
        foreach (var line in context.Facts.Lines)
        {
            if (line.LineType != InvoiceLineType.Fee || line.BilledRate is not { } billed || BillingMath.IsCreditLine(line))
            {
                continue;
            }

            var billedCent = BillingMath.ToCent(billed);
            if (line.ApprovedRate is not { Status: RateResolutionStatus.Found, RackRate: { } rack } resolution)
            {
                notChecked.Add(context.LineField(line, "line.rate", BillingFieldNames.Rate, BillingText.Money(billedCent)));
                notChecked.Add(context.LineDate(line));
                continue;
            }

            var discounted = BillingMath.ToCent(rack * (1m - (discount / 100m)));
            var limit = BillingMath.ToCent(rack * (1m - (discount / 100m)) * (1m + (tolerance / 100m)));
            if (billedCent <= limit)
            {
                continue;
            }

            var values = new List<BillingValueRead>
            {
                context.LineField(line, "line.rate", BillingFieldNames.Rate, BillingText.Money(billedCent)),
                context.LineDate(line),
                context.LineTimekeeper(line),
                HandlerContext.Reference("rackRate", BillingText.Money(rack)),
                HandlerContext.Reference("discountedRate", BillingText.Money(discounted)),
                HandlerContext.Reference("discountLimit", BillingText.Money(limit)),
                HandlerContext.Reference("rateCard.id", resolution.RateCardId?.ToString("D")),
                HandlerContext.Reference("rateCard.rateId", resolution.RateId?.ToString("D")),
            };

            var facts = new FactValues()
                .Add("line.date", BillingText.Date(line.Date))
                .Add("line.timekeeperName", BillingText.Text(line.TimekeeperName))
                .Add("line.rate", BillingText.Money(billedCent))
                .Add("rackRate", BillingText.Money(rack))
                .Add("discountedRate", BillingText.Money(discounted))
                .Add("discountLimit", BillingText.Money(limit))
                .Add("rule.discountPercent", BillingText.Number(discount));

            findings.Add(context.Finding(
                BillingSubject.ForLine(line.Index),
                BillingReasonCodes.DiscountRateAboveDiscounted,
                context.WithCreditTestReads(line, values),
                facts));
        }

        if (notChecked.Count > 0)
        {
            // One per invoice, flag only: the message is the reason's own (no token), the reads are the unchecked lines' rate and date.
            findings.Add(context.Finding(
                BillingSubject.ForInvoice(), BillingReasonCodes.DiscountNotCheckedNoRackRate, notChecked, new FactValues()));
        }

        return findings;
    }
}
