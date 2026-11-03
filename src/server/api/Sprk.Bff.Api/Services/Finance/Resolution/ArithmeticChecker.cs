using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Finance.Resolution;

/// <summary>The arithmetic checks of one invoice and the outcome per field.</summary>
public sealed class ArithmeticReport
{
    private readonly Dictionary<FieldRef, ArithmeticOutcome> _outcomes;

    internal ArithmeticReport(IReadOnlyList<ArithmeticCheckResult> checks, Dictionary<FieldRef, ArithmeticOutcome> outcomes)
    {
        Checks = checks;
        _outcomes = outcomes;
    }

    public IReadOnlyList<ArithmeticCheckResult> Checks { get; }

    /// <summary>The outcome for a field: Failed when any check involving it failed, Passed when it was in at least one check and all passed, otherwise NotApplicable.</summary>
    public ArithmeticOutcome OutcomeOf(int? lineIndex, string field) =>
        _outcomes.TryGetValue(new FieldRef(lineIndex, field), out var outcome) ? outcome : ArithmeticOutcome.NotApplicable;
}

/// <summary>
/// The arithmetic consistency signal of D-47 (Processing Pipeline step 2): per-row products, rows to section totals, sections to total due.
/// Pure and deterministic.
/// </summary>
/// <remarks>
/// <para><b>Checks.</b> (1) Per row: fee hours x rate, expense quantity x unit cost, each equal to the amount; runs only when all three values
/// are present. (2) Fee rows add up to the fees total; expense rows add up to the expenses total; runs when the section total is present
/// (a row without an amount counts as 0, so a lost amount fails the sum rather than slipping through). (3) Fees + expenses + adjustments =
/// total due; runs when the total due and at least one component are present, an absent component counts as 0.</para>
/// <para><b>Tolerance.</b> A check passes when the two sides differ by at most <see cref="Tolerance"/> (0.01): the product is rounded to 2
/// decimals (away from zero, as the invoice totals are) before it is compared.</para>
/// <para><b>Which fields a failure marks.</b> Every field the check involved: the row's three values; for a section sum the section total and
/// every row amount of that section; for the total-due check the total due and the components. Arithmetic cannot say which of the values
/// is wrong, so none is spared, and a field in several checks is Failed if any failed. That is deliberately conservative: a failed field
/// is capped below the reliability threshold (D-47), which can only stop an automatic rejection, never cause one.</para>
/// <para><b>Known limit.</b> An invoice whose total due includes a prior balance, tax or a previous payment (not fields in R1) fails the
/// total-due check and its fields stay capped; the invoice is then reviewed by a person instead of rejected automatically.</para>
/// </remarks>
public static class ArithmeticChecker
{
    /// <summary>Largest difference between the two sides of a check that still passes.</summary>
    public const decimal Tolerance = 0.01m;

    private const int Decimals = 2;

    public static ArithmeticReport Check(InvoiceResolutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var checks = new List<ArithmeticCheckResult>();
        var outcomes = new Dictionary<FieldRef, ArithmeticOutcome>();

        void Record(ArithmeticCheckKind kind, decimal expected, decimal actual, List<FieldRef> fields)
        {
            var passed = Math.Abs(expected - actual) <= Tolerance;
            checks.Add(new ArithmeticCheckResult(kind, passed, expected, actual, fields));

            foreach (var field in fields)
            {
                // Failed wins; Passed only fills an empty slot.
                if (!passed)
                {
                    outcomes[field] = ArithmeticOutcome.Failed;
                }
                else if (!outcomes.ContainsKey(field))
                {
                    outcomes[field] = ArithmeticOutcome.Passed;
                }
            }
        }

        var lines = input.Lines;

        // (1) Rows: quantity x unit price = amount.
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var (quantity, quantityName, price, priceName) = line.LineType == InvoiceLineType.Fee
                ? (line.Hours.Value, ResolutionFieldNames.Hours, line.Rate.Value, ResolutionFieldNames.Rate)
                : (line.Quantity.Value, ResolutionFieldNames.Quantity, line.UnitCost.Value, ResolutionFieldNames.UnitCost);

            if (quantity is { } q && price is { } p && line.Amount.Value is { } amount)
            {
                Record(
                    ArithmeticCheckKind.LineProduct,
                    Math.Round(q * p, Decimals, MidpointRounding.AwayFromZero),
                    amount,
                    [new FieldRef(i, quantityName), new FieldRef(i, priceName), new FieldRef(i, ResolutionFieldNames.Amount)]);
            }
        }

        // (2) Rows add up to the section totals.
        CheckSection(InvoiceLineType.Fee, input.Header.FeesTotal.Value, ResolutionFieldNames.FeesTotal, ArithmeticCheckKind.FeesSection);
        CheckSection(InvoiceLineType.Expense, input.Header.ExpensesTotal.Value, ResolutionFieldNames.ExpensesTotal, ArithmeticCheckKind.ExpensesSection);

        // (3) Sections add up to the total due.
        var fees = input.Header.FeesTotal.Value;
        var expenses = input.Header.ExpensesTotal.Value;
        var adjustments = input.Header.AdjustmentsTotal.Value;
        if (input.Header.TotalDue.Value is { } totalDue && (fees.HasValue || expenses.HasValue || adjustments.HasValue))
        {
            var fields = new List<FieldRef> { new(null, ResolutionFieldNames.TotalDue) };
            if (fees.HasValue)
            {
                fields.Add(new FieldRef(null, ResolutionFieldNames.FeesTotal));
            }

            if (expenses.HasValue)
            {
                fields.Add(new FieldRef(null, ResolutionFieldNames.ExpensesTotal));
            }

            if (adjustments.HasValue)
            {
                fields.Add(new FieldRef(null, ResolutionFieldNames.AdjustmentsTotal));
            }

            Record(ArithmeticCheckKind.TotalDue, (fees ?? 0m) + (expenses ?? 0m) + (adjustments ?? 0m), totalDue, fields);
        }

        return new ArithmeticReport(checks, outcomes);

        void CheckSection(InvoiceLineType type, decimal? sectionTotal, string totalField, ArithmeticCheckKind kind)
        {
            if (sectionTotal is not { } total)
            {
                return;
            }

            var fields = new List<FieldRef> { new(null, totalField) };
            decimal sum = 0m;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].LineType != type)
                {
                    continue;
                }

                if (lines[i].Amount.Value is { } amount)
                {
                    sum += amount;
                    fields.Add(new FieldRef(i, ResolutionFieldNames.Amount));
                }
            }

            Record(kind, sum, total, fields);
        }
    }
}
