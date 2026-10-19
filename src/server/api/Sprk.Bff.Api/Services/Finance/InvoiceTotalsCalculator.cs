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
    /// <summary>The approved total would be below zero (FR-32).</summary>
    public const string ApprovedTotalNegative = "invoice-totals.approved-total-negative";

    /// <summary>A line has no usable cost type, so it cannot be placed in the fees, expenses or adjustments.</summary>
    public const string LineCostTypeUnknown = "invoice-totals.line-cost-type-unknown";

    /// <summary>The invoice has no billed total (<c>sprk_totalamount</c>), so no approved total can be derived.</summary>
    public const string BilledTotalMissing = "invoice-totals.billed-total-missing";

    /// <summary>
    /// A stored total (fees, expenses, line adjustments or invoice adjustments) would be below zero, which the platform minimum of 0 on those
    /// columns refuses to store. Credit lines (D-121) make this reachable: a cost type whose credits exceed its charges sums below zero, and so does
    /// a credit line that is Disallowed with nothing to offset it. Replaces the earlier per-line refusal of any negative Fee or Expense amount,
    /// which D-121 lifted.
    /// </summary>
    public const string TotalBelowStorableMinimum = "invoice-totals.total-below-storable-minimum";

    /// <summary>A Reduced line has no approved amount, or one that is negative or above the billed amount.</summary>
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
/// <para><b>Credit lines (D-121).</b> A Fee or Expense line may be negative (a credit or reversal) and is summed as a signed value; the stored
/// <c>sprk_amount</c> accepts it since the column's minimum was lowered. The stored totals still have a minimum of 0, so a result that would put
/// the fees, expenses or either adjustments total below zero is refused (<see cref="InvoiceTotalsRefusalCodes.TotalBelowStorableMinimum"/>), as is a
/// negative approved total.</para>
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
        var percentBase = fees - feeLineAdjustments;

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

        // The four stored totals have a platform minimum of 0. With credit lines (D-121) a sum can fall below it, and the write would fault
        // after the invoice is committed; refuse here instead, as every other unstorable result is (nothing is written).
        if (fees < 0m || expenses < 0m || lineAdjustmentsTotal < 0m || invoiceAdjustments < 0m)
        {
            return Refused(new InvoiceTotalsRefusal(
                InvoiceTotalsRefusalCodes.TotalBelowStorableMinimum,
                "A fees, expenses or adjustments total would be below zero (credit lines exceed the charges they credit), and the platform minimum of 0 would refuse it."));
        }

        var approved = Round(billed - lineAdjustmentsTotal - invoiceAdjustments);

        InvoiceTotalsRefusal? negative = approved < 0m
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
                if (line.ApprovedAmount is not { } approved || approved < 0m || approved > line.Amount)
                {
                    refusal = new InvoiceTotalsRefusal(
                        InvoiceTotalsRefusalCodes.LineApprovedAmountInvalid,
                        "A Reduced line needs an approved amount between 0 and its billed amount.",
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
