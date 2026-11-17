// §11 justification (CLAUDE.md, ebilling-r1 task 113 fix round):
//   (1) Existing — InvoiceComplianceEvaluator decides which dates an invoice is judged on; ProcedureAssignmentService.JudgedOnAsync (084d-2b) and
//       ProcedureChangeBanner (113) each restated that rule by hand.
//   (2) Extension — the rule is three lines of pure date logic with no state; a shared static is the only form all three callers can use (the
//       evaluator is a public service, the other two are Finance classes that build their inputs from rows). No service, interface or DI entry.
//   (3) Cost of doing nothing — a copy drifts from the evaluator (a line type or a fallback changes in one place), and the banner then compares
//       a resolution built over different dates than the snapshot's, which reads "Changed" or hides a change.
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>
/// The dates an invoice is judged on (ebilling-r1 task 113 fix round, one rule in one place). The evaluator judges the invoice on its invoice
/// date (<see cref="BillingEvaluationRequest.InvoiceDate"/>, else <see cref="BillingEvaluationRequest.ReceivedOn"/>) and each evaluated line on its
/// own date, else the invoice date; each distinct date is resolved once. Pure: no I/O, no clock.
/// </summary>
internal static class BillingJudgedDates
{
    /// <summary>
    /// The distinct dates, ascending: <paramref name="invoiceDate"/> (already resolved by the caller: invoice date, else the evaluator's fallback)
    /// and each evaluated line's date, or <paramref name="invoiceDate"/> when the line has none.
    /// </summary>
    /// <param name="invoiceDate">The date the invoice itself is judged on.</param>
    /// <param name="evaluatedLineDates">The date of each line the evaluator judges (see <see cref="IsEvaluated"/>); null = undated.</param>
    internal static IReadOnlyList<DateOnly> Of(DateOnly invoiceDate, IEnumerable<DateOnly?> evaluatedLineDates)
    {
        ArgumentNullException.ThrowIfNull(evaluatedLineDates);
        return evaluatedLineDates
            .Select(date => date ?? invoiceDate)
            .Append(invoiceDate)
            .Distinct()
            .Order()
            .ToList();
    }

    /// <summary>
    /// Whether the evaluator judges a stored line of <paramref name="costType"/>: Fee and Expense lines only. An Adjustment line (a reviewer's
    /// money change, never extracted) and a row with no cost type are not.
    /// </summary>
    internal static bool IsEvaluated(int? costType) => costType is (int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense;
}
