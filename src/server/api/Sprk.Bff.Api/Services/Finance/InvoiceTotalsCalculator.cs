using System.Globalization;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Invoice Line cost type (<c>sprk_billingevent.sprk_costtype</c>, global choice; task 010 notes section 4).</summary>
public enum InvoiceLineCostType
{
    Fee = 100000000,
    Expense = 100000001,
    Adjustment = 100000002,
}

/// <summary>Invoice Line status (<c>sprk_billingevent.sprk_linestatus</c>; task 010 notes section 4). Only Reduced and Disallowed change a total.</summary>
public enum InvoiceLineStatus
{
    Pending = 100000000,
    Clean = 100000001,
    Flagged = 100000002,
    Accepted = 100000003,
    Reduced = 100000004,
    Disallowed = 100000005,
}

/// <summary>Method of an invoice-level Adjustment line (<c>sprk_billingevent.sprk_adjustmentmethod</c>; task 010 notes section 4).</summary>
public enum InvoiceAdjustmentMethod
{
    Percent = 100000000,
    Fixed = 100000001,
}

/// <summary>One Invoice Line as the calculator sees it. <see cref="Amount"/> is <c>sprk_amount</c> (the billed value; for an Adjustment line it is the
/// COMPUTED adjustment, which the calculator overwrites); <see cref="ApprovedAmount"/> is <c>sprk_approvedamount</c>.</summary>
public sealed record InvoiceLineInput(
    Guid Id,
    InvoiceLineCostType? CostType,
    decimal Amount,
    decimal? ApprovedAmount,
    InvoiceLineStatus? Status,
    InvoiceAdjustmentMethod? AdjustmentMethod,
    decimal? AdjustmentValue);

/// <summary>Stable reason codes of a refusal (FR-32). Callers branch on these; never on the message.</summary>
public static class InvoiceTotalsRefusalCodes
{
    /// <summary>
    /// The approved total would be below zero on an invoice whose billed total is not (FR-32): the adjustments exceed what was billed. Not raised
    /// for a credit memo (a negative billed total, D-127), whose approved total is legitimately negative.
    /// </summary>
    public const string ApprovedTotalNegative = "invoice-totals.approved-total-negative";

    /// <summary>A line has no usable cost type, so it cannot be placed in the fees, expenses or adjustments.</summary>
    public const string LineCostTypeUnknown = "invoice-totals.line-cost-type-unknown";

    /// <summary>The invoice has no billed total (<c>sprk_totalamount</c>), so no approved total can be derived.</summary>
    public const string BilledTotalMissing = "invoice-totals.billed-total-missing";

    /// <summary>A Reduced line has no approved amount, or one outside 0 .. the billed amount (for a credit: outside the billed credit .. 0, D-129 (3)).</summary>
    public const string LineApprovedAmountInvalid = "invoice-totals.line-approved-amount-invalid";

    /// <summary>An Adjustment line has no method or value, a negative value, or a percent above 100.</summary>
    public const string AdjustmentInvalid = "invoice-totals.adjustment-invalid";
}

/// <summary>Why the totals were refused. <see cref="LineId"/> names the offending line when one line is the cause.</summary>
public sealed record InvoiceTotalsRefusal(string Code, string Message, Guid? LineId = null);

/// <summary>
/// The five totals of an invoice plus the computed amount of each Adjustment line. When <see cref="Refusal"/> is set the figures must
/// not be stored: for <see cref="InvoiceTotalsRefusalCodes.ApprovedTotalNegative"/> they are what the inputs would give (so a caller can say
/// how far below zero the adjustments go); for every other code they are zero.
/// </summary>
public sealed record InvoiceTotalsCalculation(
    decimal FeesTotal,
    decimal ExpensesTotal,
    decimal LineAdjustmentsTotal,
    decimal InvoiceAdjustmentsTotal,
    decimal ApprovedTotal,
    IReadOnlyDictionary<Guid, decimal> AdjustmentLineAmounts,
    InvoiceTotalsRefusal? Refusal)
{
    public bool IsRefused => Refusal is not null;
}

/// <summary>
/// The single definition of the invoice totals (FR-07, invariant I-1, D-42). Pure: no I/O, no clock, no currency conversion (USD only in R1).
/// </summary>
/// <remarks>
/// <para><b>Definitions</b> (exact):
/// fees total = sum of <c>sprk_amount</c> of Fee lines; expenses total = the same for Expense lines;
/// a Fee or Expense line's adjustment = amount - approved amount when Reduced, the whole amount when Disallowed, 0 otherwise;
/// line-adjustments total = sum of those; a Percent Adjustment line = value% x (fees total - sum of the Fee lines' adjustments)
/// (expenses are never in the base; every percent line uses the same base, they do not compound); a Fixed Adjustment line = its value;
/// invoice-adjustments total = sum of the Adjustment-line amounts;
/// approved total = billed total - line-adjustments total - invoice-adjustments total.</para>
/// <para><b>Rounding.</b> <see cref="decimal"/> throughout. Each computed adjustment and each total is rounded to 2 decimals with
/// <see cref="MidpointRounding.AwayFromZero"/> (the owner-facing choice recorded in <c>docs/data-model/sprk_invoice.md</c>).
/// The billed total is taken as given (it is the stored <c>sprk_totalamount</c>).</para>
/// <para><b>Fail closed.</b> Input that the formula cannot place (a line with no cost type, a Reduced line with no usable approved amount,
/// an Adjustment line with no method/value, a missing billed total) refuses the whole calculation rather than being skipped, because a
/// skipped line silently changes what the firm is paid. An Adjustment line's own status is never read: only Fee and Expense lines are decided.</para>
/// <para><b>Credit lines and credit memos (D-121, D-127, D-128, D-129).</b> A Fee or Expense line may be negative (a credit or reversal) and is
/// summed as a signed value, so a cost type's total, the billed total and the approved total may all be negative (an all-credit invoice is a credit
/// memo); the stored columns accept it since their minimums were lowered. The one floor is FR-32: an approved total below zero is refused only when
/// the billed total is zero or more (D-129 (1)); a credit memo has no extra limit. A Disallowed credit is summed signed: its adjustment is the
/// negative amount, so disallowing a credit raises the approved total. A Reduced credit accepts part of it: the approved amount lies between the
/// billed credit and 0 (-100 reduced to -40 raises the payable by 60); more credit than billed, or the opposite sign, is refused (D-129 (3)).</para>
/// <para><b>Percent adjustments (D-129 (2)).</b> A percent adjustment is <c>value% x</c> the fee base, with the base clamped at zero: on a net-negative
/// fee base it adjusts nothing (10% of -700 is 0, the invoice stays -700); on a positive base it is unchanged. Expenses are never in the base.</para>
/// </remarks>
public static class InvoiceTotalsCalculator
{
    private const int Decimals = 2;

    public static InvoiceTotalsCalculation Calculate(decimal? billedTotal, IReadOnlyList<InvoiceLineInput> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (billedTotal is not { } billed)
        {
            return Refused(new InvoiceTotalsRefusal(
                InvoiceTotalsRefusalCodes.BilledTotalMissing, "The invoice has no billed total, so the approved total cannot be derived."));
        }

        decimal fees = 0m, expenses = 0m, feeLineAdjustments = 0m, expenseLineAdjustments = 0m;
        var adjustmentLines = new List<InvoiceLineInput>();

        foreach (var line in lines)
        {
            switch (line.CostType)
            {
                case InvoiceLineCostType.Fee:
                case InvoiceLineCostType.Expense:
                    // D-121: a credit line keeps its negative amount and is summed as a signed value. A Reduced credit has no valid
                    // approved amount (0 .. a negative billed amount is empty), so TryLineAdjustment refuses it, fail closed.
                    if (!TryLineAdjustment(line, out var lineAdjustment, out var refusal))
                    {
                        return Refused(refusal!);
                    }

                    if (line.CostType == InvoiceLineCostType.Fee)
                    {
                        fees += line.Amount;
                        feeLineAdjustments += lineAdjustment;
                    }
                    else
                    {
                        expenses += line.Amount;
                        expenseLineAdjustments += lineAdjustment;
                    }

                    break;

                case InvoiceLineCostType.Adjustment:
                    adjustmentLines.Add(line);
                    break;

                default:
                    return Refused(new InvoiceTotalsRefusal(
                        InvoiceTotalsRefusalCodes.LineCostTypeUnknown,
                        "A line has no recognised cost type (Fee, Expense or Adjustment), so the totals cannot be computed.",
                        line.Id));
            }
        }

        fees = Round(fees);
        expenses = Round(expenses);
        feeLineAdjustments = Round(feeLineAdjustments);
        expenseLineAdjustments = Round(expenseLineAdjustments);
        var lineAdjustmentsTotal = Round(feeLineAdjustments + expenseLineAdjustments);

        // D-42: a percent adjustment is taken on fees AFTER the fee lines' own adjustments; expenses never enter the base.
        // D-129 (2): the base is clamped at zero. A percent adjustment on a net-negative fee base adjusts nothing (10% of -700 is 0).
        var percentBase = Math.Max(0m, fees - feeLineAdjustments);

        var adjustmentAmounts = new Dictionary<Guid, decimal>(adjustmentLines.Count);
        decimal invoiceAdjustments = 0m;
        foreach (var line in adjustmentLines)
        {
            if (!TryAdjustmentAmount(line, percentBase, out var amount, out var refusal))
            {
                return Refused(refusal!);
            }

            adjustmentAmounts[line.Id] = amount;
            invoiceAdjustments += amount;
        }

        invoiceAdjustments = Round(invoiceAdjustments);

        var approved = Round(billed - lineAdjustmentsTotal - invoiceAdjustments);

        // FR-32 refuses adjustments that exceed the billed total. D-129 (1): the floor applies only when the billed total is zero or more; a credit
        // memo (billed total below zero, D-127) legitimately has a negative approved total and has no extra limit.
        InvoiceTotalsRefusal? negative = approved < 0m && billed >= 0m
            ? new InvoiceTotalsRefusal(
                InvoiceTotalsRefusalCodes.ApprovedTotalNegative,
                "The adjustments exceed the billed total: the approved total would be "
                + approved.ToString("0.00", CultureInfo.InvariantCulture) + ".")
            : null;

        return new InvoiceTotalsCalculation(fees, expenses, lineAdjustmentsTotal, invoiceAdjustments, approved, adjustmentAmounts, negative);
    }

    private static bool TryLineAdjustment(InvoiceLineInput line, out decimal adjustment, out InvoiceTotalsRefusal? refusal)
    {
        refusal = null;
        adjustment = 0m;

        switch (line.Status)
        {
            case InvoiceLineStatus.Disallowed:
                adjustment = Round(line.Amount);
                return true;

            case InvoiceLineStatus.Reduced:
                // D-129 (3): the approved amount lies between 0 and the billed amount, whichever sign the line has. A charge is reduced
                // towards 0 from above; a credit is reduced (part of it accepted) towards 0 from below: -100 reduced to -40 is valid.
                if (line.ApprovedAmount is not { } approved
                    || approved < Math.Min(0m, line.Amount) || approved > Math.Max(0m, line.Amount))
                {
                    refusal = new InvoiceTotalsRefusal(
                        InvoiceTotalsRefusalCodes.LineApprovedAmountInvalid,
                        "A Reduced line needs an approved amount between 0 and its billed amount (a credit: between the billed credit and 0).",
                        line.Id);
                    return false;
                }

                adjustment = Round(line.Amount - approved);
                return true;

            default:
                return true;
        }
    }

    private static bool TryAdjustmentAmount(InvoiceLineInput line, decimal percentBase, out decimal amount, out InvoiceTotalsRefusal? refusal)
    {
        amount = 0m;
        refusal = null;

        if (line.AdjustmentValue is not { } value || value < 0m || line.AdjustmentMethod is null)
        {
            refusal = Invalid(line, "An Adjustment line needs a method and a value of zero or more.");
            return false;
        }

        switch (line.AdjustmentMethod)
        {
            case InvoiceAdjustmentMethod.Percent:
                if (value > 100m)
                {
                    refusal = Invalid(line, "A percent adjustment cannot exceed 100.");
                    return false;
                }

                amount = Round(percentBase * value / 100m);
                return true;

            case InvoiceAdjustmentMethod.Fixed:
                amount = Round(value);
                return true;

            default:
                refusal = Invalid(line, "An Adjustment line has an unknown method.");
                return false;
        }
    }

    private static InvoiceTotalsRefusal Invalid(InvoiceLineInput line, string message) =>
        new(InvoiceTotalsRefusalCodes.AdjustmentInvalid, message, line.Id);

    private static InvoiceTotalsCalculation Refused(InvoiceTotalsRefusal refusal) =>
        new(0m, 0m, 0m, 0m, 0m, new Dictionary<Guid, decimal>(), refusal);

    private static decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
}
