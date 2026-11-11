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
/// match without regard to case.</para>
/// <para><b>The code judged is the one the firm billed (D-117).</b> The code check reads <see cref="BillingFieldNames.BilledExpenseCode"/>,
/// the code extracted from the invoice with its D-47 combined confidence, so a rejection never rests on a code the firm did not use.
/// When the line carries no billed code, the AI-assigned expense code (task 047, <see cref="BillingFieldNames.ExpenseCode"/>) is the
/// fallback: that finding reads an AI-assigned field, so the rejection rule only ever holds it as a Flag (073, <c>ai_assigned_input</c>).
/// The cap finding names the same code (billed, else the fallback) as its <c>line.expenseCode</c>.</para>
/// <para><b>Credit lines are skipped (D-105).</b> A line with a negative amount (<see cref="BillingMath.IsCreditLine"/>), for
/// example a credit "E109 -50.00", is judged on neither check: it is a refund of a charge, not a charge.</para>
/// <para><b>Reads:</b> BilledExpenseCode (or ExpenseCode when no code was billed), Amount, Date (extracted), and Hours when the line has them: the credit test reads Hours and
/// Amount (<see cref="HandlerContext.WithCreditTestReads"/>).</para>
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
            // A credit (a refund or reversal of an expense, D-105) is never judged as a charge: a non-reimbursable code on a
            // credit is money coming back, not an expense to refuse, and a negative amount is not over any cap. It stays visible.
            if (line.LineType != InvoiceLineType.Expense || BillingMath.IsCreditLine(line))
            {
                continue;
            }

            // D-117: the firm-billed code; the AI-assigned code only when none was billed (a finding on it can only flag).
            var billed = BillingText.Text(line.BilledExpenseCode);
            var (code, codeField) = billed is not null
                ? (billed, BillingFieldNames.BilledExpenseCode)
                : (BillingText.Text(line.ExpenseCode), BillingFieldNames.ExpenseCode);
            if (code is not null && codes.Contains(code))
            {
                findings.Add(Build(context, line, code, codeField, BillingReasonCodes.ExpenseCodeNotReimbursable));
            }

            if (cap is { } limit && line.Amount is { } amount && amount > limit)
            {
                findings.Add(Build(context, line, code, codeField, BillingReasonCodes.ExpenseAmountOverCap));
            }
        }

        return findings;
    }

    private static BillingFinding Build(HandlerContext context, BillingLineFacts line, string? code, string codeField, string reasonCode)
    {
        var values = new[]
        {
            context.LineField(line, "line.expenseCode", codeField, code),
            context.LineField(line, "line.amount", BillingFieldNames.Amount, BillingText.Money(line.Amount)),
            context.LineDate(line),
        };

        var facts = new FactValues()
            .Add("line.date", BillingText.Date(line.Date))
            .Add("line.expenseCode", code)
            .Add("line.amount", BillingText.Money(line.Amount));

        return context.Finding(BillingSubject.ForLine(line.Index), reasonCode, context.WithCreditTestReads(line, values), facts);
    }
}
