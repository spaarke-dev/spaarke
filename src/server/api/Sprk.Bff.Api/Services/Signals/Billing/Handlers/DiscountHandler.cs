using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 13, Discount (ebilling-r1 task 115; procedure-and-file-model.md 6.3): a fee line's billed rate is at most the rack rate
/// less the agreed discount, plus the rule's tolerance. A check only ("agreed discount not applied"): Flag, no suggested adjustment.
/// </summary>
/// <remarks>
/// <para><b>The check.</b> The limit is <c>rack x (1 - discountPercent / 100) x (1 + tolerancePercent / 100)</c> (no tolerance = 0),
/// rounded to the cent (<see cref="BillingMath.ToCent"/>). The billed rate, rounded to the cent, fires when it is <b>above</b> the
/// limit; equal holds. As <see cref="ApprovedRateHandler"/>.</para>
/// <para><b>Rack rate.</b> The rack rate on the rate-card row 046 resolved for the line (<see cref="RateResolution.RackRate"/>, status
/// Found). A line with no resolved rack rate is skipped without a finding: ApprovedRate already reports a rate that could not be
/// established, and a discount off an unknown rate is not a breach.</para>
/// <para><b>Skipped:</b> expense lines, fee lines with no billed rate, and credit lines (D-105: a reversal is not billed at a rate).</para>
/// <para><b>Reads:</b> Rate, Date, TimekeeperAsBilled (extracted), the credit-test reads (<see cref="HandlerContext.WithCreditTestReads"/>),
/// and the rack rate, the discounted limit and the rate card ids as reference values.</para>
/// </remarks>
internal static class DiscountHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var discount = BillingRuleHandlers.Require(context.Rule.DiscountPercent, context, "discountPercent");
        var tolerance = context.Rule.TolerancePercent ?? 0m;

        var findings = new List<BillingFinding>();
        foreach (var line in context.Facts.Lines)
        {
            if (line.LineType != InvoiceLineType.Fee || line.BilledRate is not { } billed || BillingMath.IsCreditLine(line))
            {
                continue;
            }

            if (line.ApprovedRate is not { Status: RateResolutionStatus.Found, RackRate: { } rack } resolution)
            {
                continue;
            }

            var billedCent = BillingMath.ToCent(billed);
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
                HandlerContext.Reference("discountedRate", BillingText.Money(limit)),
                HandlerContext.Reference("rateCard.id", resolution.RateCardId?.ToString("D")),
                HandlerContext.Reference("rateCard.rateId", resolution.RateId?.ToString("D")),
            };

            var facts = new FactValues()
                .Add("line.date", BillingText.Date(line.Date))
                .Add("line.timekeeperName", BillingText.Text(line.TimekeeperName))
                .Add("line.rate", BillingText.Money(billedCent))
                .Add("rackRate", BillingText.Money(rack))
                .Add("discountedRate", BillingText.Money(limit))
                .Add("rule.discountPercent", BillingText.Number(discount));

            findings.Add(context.Finding(
                BillingSubject.ForLine(line.Index), BillingReasonCodes.DiscountNotApplied, context.WithCreditTestReads(line, values), facts));
        }

        return findings;
    }
}
