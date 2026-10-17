using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 2, ApprovedRate (design.md 6.1): a fee line's billed rate must not exceed the approved rate in force on its date
/// for its timekeeper, else for the timekeeper's classification (task 046 resolves which rate that is), plus the rule's tolerance.
/// </summary>
/// <remarks>
/// <para><b>The check.</b> The limit is <c>approved x (1 + tolerancePercent / 100)</c>, rounded to the cent (midpoint away from
/// zero, <see cref="BillingMath.ToCent"/>). The rule fires when the billed rate is <b>above</b> the limit; a rate equal to it holds.</para>
/// <para><b>The suggested reduction</b> is the whole excess over the <b>approved</b> rate, not over the limit:
/// <c>(billed - approved) x hours</c>, rounded to the cent. The tolerance is the slack before the rule fires, not a rate the firm
/// is entitled to; the research note names the consequence "adjust to approved". Hours that are missing or not positive leave
/// the amount <c>null</c> (a reduction is suggested, its size is not known). This is an owner-visible reading of "excess times
/// hours": the alternative (reduce to the limit) is one line here.</para>
/// <para><b>No approved rate.</b> When the billed rate cannot be compared because 046 found no approved rate in force, found
/// several that disagree, or could not look one up, the rule fires with a reason that says so and suggests nothing. The Rate field's
/// reference-missing cap (D-47) exists for this case: such a finding holds and cannot reject. A line that bills no hourly rate is
/// not judged (nothing to approve); a missing rate is a RequiredData matter.</para>
/// <para><b>Reads:</b> Rate, Date, TimekeeperAsBilled, and Hours when it computes the reduction (all extracted); the approved
/// rate, its limit, basis, scope and ids are reference values.</para>
/// </remarks>
internal static class ApprovedRateHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var tolerance = BillingRuleHandlers.Require(context.Rule.TolerancePercent, context, "tolerancePercent");

        var findings = new List<BillingFinding>();
        foreach (var line in context.Facts.Lines)
        {
            if (line.LineType != InvoiceLineType.Fee || line.BilledRate is not { } billed)
            {
                continue;
            }

            var resolution = line.ApprovedRate;
            if (resolution is { Status: RateResolutionStatus.Found, ApprovedRate: { } approved })
            {
                var limit = BillingMath.ToCent(approved * (1m + (tolerance / 100m)));
                if (billed > limit)
                {
                    findings.Add(AboveApproved(context, line, billed, approved, limit, resolution));
                }

                continue;
            }

            findings.Add(NoApprovedRate(context, line, billed, resolution));
        }

        return findings;
    }

    private static BillingFinding AboveApproved(
        HandlerContext context, BillingLineFacts line, decimal billed, decimal approved, decimal limit, RateResolution resolution)
    {
        decimal? reduction = null;
        if (line.Hours is > 0)
        {
            reduction = BillingMath.ToCent((billed - approved) * line.Hours.Value);
        }

        var values = new List<BillingValueRead>
        {
            context.LineField(line, "line.rate", BillingFieldNames.Rate, BillingText.Money(billed)),
            context.LineHours(line),
            context.LineDate(line),
            context.LineTimekeeper(line),
            HandlerContext.Reference("approvedRate", BillingText.Money(approved)),
            HandlerContext.Reference("approvedRate.limit", BillingText.Money(limit)),
            HandlerContext.Reference("approvedRate.basis", resolution.Basis?.ToString()),
            HandlerContext.Reference("approvedRate.scope", resolution.Scope?.ToString()),
            HandlerContext.Reference("rateCard.id", resolution.RateCardId?.ToString("D")),
            HandlerContext.Reference("rateCard.rateId", resolution.RateId?.ToString("D")),
        };

        return context.Finding(
            BillingSubject.ForLine(line.Index),
            BillingReasonCodes.RateAboveApproved,
            values,
            Tokens(context, line, billed, approved),
            new BillingSuggestedAdjustment(BillingAdjustmentKind.Reduce, reduction));
    }

    private static BillingFinding NoApprovedRate(HandlerContext context, BillingLineFacts line, decimal billed, RateResolution? resolution)
    {
        var (reasonCode, status) = resolution switch
        {
            { Status: RateResolutionStatus.Ambiguous } => (BillingReasonCodes.RateAmbiguous, "Ambiguous"),
            { Status: RateResolutionStatus.NotFound, Reason: RateNotFoundReason.TimekeeperUnmatched } =>
                (BillingReasonCodes.RateTimekeeperUnmatched, "NotFound.TimekeeperUnmatched"),
            { Status: RateResolutionStatus.NotFound, Reason: RateNotFoundReason.LineDateMissing } =>
                (BillingReasonCodes.RateLineDateMissing, "NotFound.LineDateMissing"),
            { Status: RateResolutionStatus.NotFound } => (BillingReasonCodes.RateNoRateInForce, "NotFound.NoRateInForce"),

            // No resolution at all, "not applicable" on a fee line, or "found" without a rate: the rate was not established.
            _ => (BillingReasonCodes.RateUnresolved, "Unresolved"),
        };

        var values = new[]
        {
            context.LineField(line, "line.rate", BillingFieldNames.Rate, BillingText.Money(billed)),
            context.LineDate(line),
            context.LineTimekeeper(line),
            HandlerContext.Reference("approvedRate.status", status),
        };

        return context.Finding(BillingSubject.ForLine(line.Index), reasonCode, values, Tokens(context, line, billed, approved: null));
    }

    private static FactValues Tokens(HandlerContext context, BillingLineFacts line, decimal billed, decimal? approved) => new FactValues()
        .Add("line.date", BillingText.Date(line.Date))
        .Add("line.timekeeperName", BillingText.Text(line.TimekeeperName))
        .Add("line.rate", BillingText.Money(billed))
        .Add("approvedRate", BillingText.Money(approved))
        .Add("rule.tolerancePercent", BillingText.Number(context.Rule.TolerancePercent));
}
