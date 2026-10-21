using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 12, AddedFee (ebilling-r1 task 115; procedure-and-file-model.md 6.3, O-16): the lines that carry an added fee (a late
/// fee, an administrative fee, interest) total at most the rule's cap, an amount or a percent of the invoice's other lines. Flag only.
/// </summary>
/// <remarks>
/// <para><b>Cap form only.</b> The gate refuses the "not allowed" and "after N days" allowances (<c>rule_form_not_supported</c>) until
/// extraction returns a per-line fee kind and the due date, so a definition reaching this handler is a cap; anything else is refused
/// here too (<see cref="BillingRuleRefusedException.ParameterMissing"/>), never judged as "holds".</para>
/// <para><b>Fee lines.</b> Expense lines whose code is in <c>feeCodes</c> (case ignored): the code the firm billed, else the AI-assigned
/// expense code (D-117, as <see cref="ExpenseRuleHandler"/>). The fee kind is not a fact of a line, so it is not read.</para>
/// <para><b>The check.</b> The fee total is the sum of the fee lines' amounts, credits included (a credited fee lowers it); a line
/// with no amount is left out. The limit is <c>capAmount</c>, or <c>capPercent</c> of the sum of every other line's amount (credits
/// included, never below 0), both to the cent. The rule fires when the total, to the cent, is <b>above</b> the limit; equal holds,
/// and a total of 0 or less never fires.</para>
/// <para><b>One finding per invoice</b>, on the first fee line with an amount above 0: the cap is a property of the invoice. It reads
/// every counted fee line's code, amount and date; the totals are reference values (<c>feeTotal</c>, <c>feeLimit</c>, and for a
/// percent cap <c>feeBase</c>, whose lines are not listed one by one so the finding stays bounded; AddedFee never rejects).</para>
/// </remarks>
internal static class AddedFeeHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var rule = context.Rule;
        if (rule.Allowance != BillingRuleCatalogue.AllowanceCap)
        {
            throw new BillingRuleRefusedException(
                BillingRuleRefusedException.ParameterMissing,
                rule.RuleType,
                $"An AddedFee rule with allowance '{rule.Allowance}' cannot be evaluated; only '{BillingRuleCatalogue.AllowanceCap}' is. It is refused, not evaluated.");
        }

        var codes = new HashSet<string>(BillingRuleHandlers.RequireList(rule.FeeCodes, context, "feeCodes"), StringComparer.OrdinalIgnoreCase);
        if ((rule.CapAmount is null) == (rule.CapPercent is null))
        {
            throw new BillingRuleRefusedException(
                BillingRuleRefusedException.ParameterMissing,
                rule.RuleType,
                "An AddedFee cap needs exactly one of 'capAmount' and 'capPercent'; it is refused, not evaluated.");
        }

        var fees = new List<(BillingLineFacts Line, string Code, string CodeField)>();
        var otherTotal = 0m;
        foreach (var line in context.Facts.Lines)
        {
            if (line.Amount is not { } amount)
            {
                continue;
            }

            var (code, codeField) = FeeCode(line);
            if (line.LineType == InvoiceLineType.Expense && code is not null && codes.Contains(code))
            {
                fees.Add((line, code, codeField));
            }
            else
            {
                otherTotal += amount;
            }
        }

        if (fees.Count == 0)
        {
            return [];
        }

        var total = BillingMath.ToCent(fees.Sum(f => f.Line.Amount!.Value));
        decimal? feeBase = rule.CapPercent is null ? null : BillingMath.ToCent(Math.Max(otherTotal, 0m));
        var limit = rule.CapAmount is { } capAmount
            ? BillingMath.ToCent(capAmount)
            : BillingMath.ToCent(feeBase!.Value * rule.CapPercent!.Value / 100m);
        if (total <= 0m || total <= limit)
        {
            return [];
        }

        var values = new List<BillingValueRead>();
        foreach (var (line, code, codeField) in fees)
        {
            values.Add(context.LineField(line, "line.expenseCode", codeField, code));
            values.Add(context.LineField(line, "line.amount", BillingFieldNames.Amount, BillingText.Money(line.Amount)));
            values.Add(context.LineDate(line));
        }

        values.Add(HandlerContext.Reference("feeTotal", BillingText.Money(total)));
        values.Add(HandlerContext.Reference("feeLimit", BillingText.Money(limit)));
        if (feeBase is not null)
        {
            values.Add(HandlerContext.Reference("feeBase", BillingText.Money(feeBase)));
        }

        // The first fee CHARGE: a total above 0 has one, and a credit line never carries a finding as a charge (D-105).
        var first = fees.First(f => f.Line.Amount > 0m).Line;
        var facts = new FactValues()
            .Add("line.date", BillingText.Date(first.Date))
            .Add("feeTotal", BillingText.Money(total))
            .Add("feeLimit", BillingText.Money(limit))
            .Add("rule.feeKind", rule.FeeKind);

        return [context.Finding(BillingSubject.ForLine(first.Index), BillingReasonCodes.AddedFeeOverCap, values, facts)];
    }

    /// <summary>D-117: the firm-billed expense code, else the AI-assigned one (a finding on it reads an AI field).</summary>
    private static (string? Code, string CodeField) FeeCode(BillingLineFacts line)
    {
        var billed = BillingText.Text(line.BilledExpenseCode);
        return billed is not null
            ? (billed, BillingFieldNames.BilledExpenseCode)
            : (BillingText.Text(line.ExpenseCode), BillingFieldNames.ExpenseCode);
    }
}
