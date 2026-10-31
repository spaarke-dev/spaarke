using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 2, ApprovedRate (design.md 6.1): a fee line's billed rate must not exceed the approved rate in force on its date
/// for its timekeeper, else for the timekeeper's classification (task 046 resolves which rate that is), plus the rule's tolerance.
/// </summary>
/// <remarks>
/// <para><b>The check.</b> The limit is <c>approved x (1 + tolerancePercent / 100)</c>, rounded to the cent (midpoint away from
/// zero, <see cref="BillingMath.ToCent"/>). The rule fires when the billed rate is <b>above</b> the limit; a rate equal to it holds.
/// The billed rate is itself rounded to the cent before the comparison (and before the reduction is computed), so the rate judged
/// is the rate the finding shows: a sub-cent extraction such as 575.004 against 575.00 holds.</para>
/// <para><b>The suggested reduction</b> is the whole excess over the <b>approved</b> rate, not over the limit:
/// <c>(billed - approved) x hours</c>, rounded to the cent. The tolerance is the slack before the rule fires, not a rate the firm
/// is entitled to; the research note names the consequence "adjust to approved". Hours that are missing or zero leave
/// the amount <c>null</c> (a reduction is suggested, its size is not known). This is an owner-visible reading of "excess times
/// hours": the alternative (reduce to the limit) is one line here.</para>
/// <para><b>Credit lines are skipped (D-105).</b> A line with negative hours or a negative amount
/// (<see cref="BillingMath.IsCreditLine"/>) is never judged: a reversal is not billed at a rate, so there is nothing to reduce.
/// Hours of zero are not a credit and are still judged (the reduction is then unknown).</para>
/// <para><b>No approved rate.</b> When the billed rate cannot be compared because 046 found no approved rate in force, found
/// several that disagree, or could not look one up, the rule fires with a reason that says so and suggests nothing. The Rate field's
/// reference-missing cap (D-47) exists for this case: such a finding holds and cannot reject. A line that bills no hourly rate is
/// not judged (nothing to approve); a missing rate is a RequiredData matter.</para>
/// <para><b>Reads:</b> Rate, Date, TimekeeperAsBilled (all extracted), and Hours and Amount whenever the line has them: the
/// credit test that precedes every finding reads both (<see cref="HandlerContext.WithCreditTestReads"/>), and the reduction
/// uses Hours; the approved rate, its limit, basis, scope and ids are reference values.</para>
/// </remarks>
internal static class ApprovedRateHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var tolerance = BillingRuleHandlers.Require(context.Rule.TolerancePercent, context, "tolerancePercent");

        var findings = new List<BillingFinding>();
        foreach (var line in context.Facts.Lines)
        {
            // A credit line (negative hours or amount, D-105) is never judged as a charge: a reversal at the old rate is not a
            // new overcharge. It stays on the invoice for the reviewer; this rule just says nothing about it.
            if (line.LineType != InvoiceLineType.Fee || line.BilledRate is not { } billed || BillingMath.IsCreditLine(line))
            {
                continue;
            }

            var resolution = line.ApprovedRate;
            if (resolution is { Status: RateResolutionStatus.Found, ApprovedRate: { } approved })
            {
                // The billed rate is compared at the precision it is shown and billed at, the cent, like the limit it is held
                // against: an extracted 575.004 is 575.00 and holds, instead of firing "above" a limit the finding shows as equal.
                var billedCent = BillingMath.ToCent(billed);
                var limit = BillingMath.ToCent(approved * (1m + (tolerance / 100m)));
                if (billedCent > limit)
                {
                    findings.Add(AboveApproved(context, line, billedCent, approved, limit, resolution));
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
        var values = new List<BillingValueRead>
        {
            context.LineField(line, "line.rate", BillingFieldNames.Rate, BillingText.Money(billed)),
        };

        // The hours are read only when the reduction is computed from them (the read-field contract for task 073).
        if (line.Hours is > 0)
        {
            reduction = BillingMath.ToCent((billed - approved) * line.Hours.Value);
            values.Add(context.LineHours(line));
        }

        values.AddRange(
        [
            context.LineDate(line),
            context.LineTimekeeper(line),
            HandlerContext.Reference("approvedRate", BillingText.Money(approved)),
            HandlerContext.Reference("approvedRate.limit", BillingText.Money(limit)),
            HandlerContext.Reference("approvedRate.basis", resolution.Basis?.ToString()),
            HandlerContext.Reference("approvedRate.scope", resolution.Scope?.ToString()),
            HandlerContext.Reference("rateCard.id", resolution.RateCardId?.ToString("D")),
            HandlerContext.Reference("rateCard.rateId", resolution.RateId?.ToString("D")),
        ]);

        return context.Finding(
            BillingSubject.ForLine(line.Index),
            BillingReasonCodes.RateAboveApproved,
            context.WithCreditTestReads(line, values),
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

        return context.Finding(
            BillingSubject.ForLine(line.Index), reasonCode, context.WithCreditTestReads(line, values), Tokens(context, line, billed, approved: null));
    }

    private static FactValues Tokens(HandlerContext context, BillingLineFacts line, decimal billed, decimal? approved) => new FactValues()
        .Add("line.date", BillingText.Date(line.Date))
        .Add("line.timekeeperName", BillingText.Text(line.TimekeeperName))
        .Add("line.rate", BillingText.Money(billed))
        .Add("approvedRate", BillingText.Money(approved))
        .Add("rule.tolerancePercent", BillingText.Number(context.Rule.TolerancePercent));
}
