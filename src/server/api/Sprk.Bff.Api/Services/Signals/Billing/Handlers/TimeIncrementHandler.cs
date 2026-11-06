using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 3, TimeIncrement (design.md 6.1): a fee line's hours must be a multiple of the billing increment (for example 0.1).
/// Flag only (never reject-eligible).
/// </summary>
/// <remarks>
/// <b>Exact.</b> The test is the decimal remainder <c>hours % increment</c>, which is exact for decimals (0.3 % 0.1 = 0, where a
/// double gives 0.0999...). Zero hours and negative hours that are a multiple (a credit) hold. A line with no hours is not judged.
/// <para><b>Reads:</b> Hours, Date, TimekeeperAsBilled (extracted).</para>
/// </remarks>
internal static class TimeIncrementHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var increment = BillingRuleHandlers.Require(context.Rule.IncrementHours, context, "incrementHours");
        if (increment <= 0m)
        {
            // The schema allows (0, 1]; a zero would divide by zero. Refuse rather than crash or pass.
            throw new BillingRuleRefusedException(
                BillingRuleRefusedException.ParameterMissing,
                context.Rule.RuleType,
                "A TimeIncrement rule needs a positive 'incrementHours'; it is refused, not evaluated.");
        }

        var findings = new List<BillingFinding>();
        foreach (var line in context.Facts.Lines)
        {
            if (line.LineType != InvoiceLineType.Fee || line.Hours is not { } hours || hours % increment == 0m)
            {
                continue;
            }

            var values = new[] { context.LineHours(line), context.LineDate(line), context.LineTimekeeper(line) };
            var facts = new FactValues()
                .Add("line.date", BillingText.Date(line.Date))
                .Add("line.timekeeperName", BillingText.Text(line.TimekeeperName))
                .Add("line.hours", BillingText.Number(hours))
                .Add("rule.incrementHours", BillingText.Number(increment));

            findings.Add(context.Finding(BillingSubject.ForLine(line.Index), BillingReasonCodes.TimeNotAMultipleOfIncrement, values, facts));
        }

        return findings;
    }
}
