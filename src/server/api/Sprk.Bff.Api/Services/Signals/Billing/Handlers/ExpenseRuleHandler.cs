using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 6, ExpenseRule (design.md 6.1): an expense line whose expense code is not reimbursable, or whose amount is over the
/// cap per item. The rule has either parameter or both (070 guarantees at least one).
/// </summary>
/// <remarks>
/// <para><b>Two checks, two findings.</b> A line that breaks both gets both: the code finding first, then the cap finding. They
/// have different reasons and a firm fixes them differently.</para>
/// <para><b>Item = expense line.</b> The cap applies to the line's amount (an expense line is one item in LEDES terms); it is not
/// divided by a quantity. An amount equal to the cap holds; a line with no amount is not checked against the cap. The listed codes
/// match without regard to case, and the code read is the AI's validated expense code (task 047), carried with its coding
/// confidence (NFR-05).</para>
/// <para><b>Reads:</b> ExpenseCode, Amount, Date (extracted).</para>
/// </remarks>
internal static class ExpenseRuleHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var codes = new HashSet<string>(context.Rule.NonReimbursableCodes, StringComparer.OrdinalIgnoreCase);
        var cap = context.Rule.MaxAmountPerItem;
        if (codes.Count == 0 && cap is null)
        {
            throw new BillingRuleRefusedException(
                BillingRuleRefusedException.ParameterMissing,
                context.Rule.RuleType,
                "An ExpenseRule has neither 'nonReimbursableCodes' nor 'maxAmountPerItem'; it is refused, not evaluated.");
        }

        var findings = new List<BillingFinding>();
        foreach (var line in context.Facts.Lines)
        {
            if (line.LineType != InvoiceLineType.Expense)
            {
                continue;
            }

            var code = BillingText.Text(line.ExpenseCode);
            if (code is not null && codes.Contains(code))
            {
                findings.Add(Build(context, line, code, BillingReasonCodes.ExpenseCodeNotReimbursable));
            }

            if (cap is { } limit && line.Amount is { } amount && amount > limit)
            {
                findings.Add(Build(context, line, code, BillingReasonCodes.ExpenseAmountOverCap));
            }
        }

        return findings;
    }

    private static BillingFinding Build(HandlerContext context, BillingLineFacts line, string? code, string reasonCode)
    {
        var values = new[]
        {
            context.LineField(line, "line.expenseCode", BillingFieldNames.ExpenseCode, code),
            context.LineField(line, "line.amount", BillingFieldNames.Amount, BillingText.Money(line.Amount)),
            context.LineDate(line),
        };

        var facts = new FactValues()
            .Add("line.date", BillingText.Date(line.Date))
            .Add("line.expenseCode", code)
            .Add("line.amount", BillingText.Money(line.Amount));

        return context.Finding(BillingSubject.ForLine(line.Index), reasonCode, values, facts);
    }
}
