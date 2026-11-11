using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.ContentUnderstanding;

/// <summary>
/// Deterministic completion applied to every read after extraction (ebilling-r1 task 043; task 005 spike conditions
/// 1 and 2). A line-for-line port of the spike's <c>complete()</c>
/// (<c>projects/ebilling-r1/notes/spikes/extraction/score.py</c>), which the D-81 accuracy numbers were measured with,
/// plus the day/month post-check for day-first dates. Pure: no I/O.
/// </summary>
/// <remarks>
/// <para>Fee rows only, in this order (each step sees the previous steps' values):</para>
/// <list type="number">
///   <item>No rate on the row → the rate of the same timekeeper in the invoice's own timekeeper summary (names
///   compared without any "(...)" and as lower-case letters and digits; a summary name "Jane Doe (JD)" also matches
///   rows billed as "JD").</item>
///   <item>Hours × rate differs from the amount by more than 0.02 → read the hours as clock notation (1.30 = 1:30 =
///   1.5); keep that when the fraction is below .60 and the converted hours × rate is within 0.60 of the amount.</item>
///   <item>Rate still missing, hours non-zero → rate = amount / hours. Hours missing, rate non-zero → hours = amount
///   / rate. Amount missing → amount = hours × rate. All rounded to 2 places.</item>
/// </list>
/// <para>
/// Then, on every row: a date outside [BillingPeriodStart, BillingPeriodEnd] whose day and month, swapped, form a
/// date inside it is swapped (a day-first date read month-first). Needs both period ends; changes nothing otherwise.
/// When the analyzer reads the billing period itself month-first, the rows agree with it and nothing is swapped.
/// </para>
/// </remarks>
internal static partial class InvoiceExtractionCompletion
{
    private const decimal ArithmeticTolerance = 0.02m;
    private const decimal ClockTolerance = 0.6m;

    public static ExtractedInvoice Complete(ExtractedInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var summaryRates = SummaryRates(invoice.TimekeeperSummary);
        var start = invoice.Header.BillingPeriodStart.Value;
        var end = invoice.Header.BillingPeriodEnd.Value;

        var lines = invoice.Lines
            .Select(line => line.LineType == InvoiceLineType.Fee ? CompleteFee(line, summaryRates) : line)
            .Select(line => SwapDayMonth(line, start, end))
            .ToList();

        return invoice with { Lines = lines };
    }

    // ── fee arithmetic ────────────────────────────────────────────────────────────────────────────────────

    private static ExtractedInvoiceLine CompleteFee(
        ExtractedInvoiceLine line,
        IReadOnlyDictionary<string, ExtractedInvoiceField<decimal?>> summaryRates)
    {
        var hours = line.Hours;
        var rate = line.Rate;
        var amount = line.Amount;
        decimal? h = Round2(hours.Value), r = Round2(rate.Value), a = Round2(amount.Value);

        // 1. Rate from the invoice's own timekeeper summary.
        var timekeeper = NormaliseTimekeeper(line.TimekeeperAsBilled.Value);
        if (r is null && timekeeper.Length > 0 && summaryRates.TryGetValue(timekeeper, out var summaryRate))
        {
            r = summaryRate.Value;
            rate = summaryRate with { Repair = ExtractedValueRepair.RateFromTimekeeperSummary, ReturnedValue = rate.ReturnedValue };
        }

        // 2. Clock notation read as a decimal.
        if (h is { } hv && r is { } rv && a is { } av && Math.Abs(hv * rv - av) > ArithmeticTolerance)
        {
            var whole = decimal.Truncate(hv);
            var fraction = hv - whole;
            var clock = whole + Math.Round(fraction * 100m, 0, MidpointRounding.ToEven) / 60m;
            var converted = Math.Round(clock, 2, MidpointRounding.ToEven);
            if (fraction < 0.6m && Math.Abs(converted * rv - av) <= ClockTolerance)
            {
                h = converted;
                hours = hours with { Value = converted, Repair = ExtractedValueRepair.ClockHoursConverted };
            }
        }

        // 3. Arithmetic fallbacks.
        if (r is null && h is { } h3 && h3 != 0m && a is { } a3)
        {
            r = Math.Round(a3 / h3, 2, MidpointRounding.ToEven);
            rate = Computed(rate, r);
        }

        if (h is null && r is { } r4 && r4 != 0m && a is { } a4)
        {
            h = Math.Round(a4 / r4, 2, MidpointRounding.ToEven);
            hours = Computed(hours, h);
        }

        if (a is null && h is { } h5 && r is { } r5)
        {
            a = Math.Round(h5 * r5, 2, MidpointRounding.ToEven);
            amount = Computed(amount, a);
        }

        return line with { Hours = hours, Rate = rate, Amount = amount };
    }

    private static ExtractedInvoiceField<decimal?> Computed(ExtractedInvoiceField<decimal?> returned, decimal? value) =>
        new()
        {
            Value = value,
            ReturnedValue = returned.ReturnedValue,
            Repair = ExtractedValueRepair.ComputedFromRow,
        };

    private static Dictionary<string, ExtractedInvoiceField<decimal?>> SummaryRates(IReadOnlyList<ExtractedTimekeeperSummaryRow> summary)
    {
        var rates = new Dictionary<string, ExtractedInvoiceField<decimal?>>(StringComparer.Ordinal);
        foreach (var row in summary)
        {
            var name = row.Name.Value;
            if (string.IsNullOrEmpty(name) || row.Rate.Value is not { } rate || rate == 0m)
            {
                continue;
            }

            var field = row.Rate with { Value = Round2(rate) };
            var key = NormaliseTimekeeper(name);
            if (key.Length > 0)
            {
                // Deviation from the spike: a name that normalises to nothing ("(AB)") would otherwise give its rate
                // to every row with no timekeeper.
                rates[key] = field;
            }

            var initials = Initials().Match(name);
            if (initials.Success)
            {
                rates.TryAdd(NormaliseText(initials.Groups[1].Value), field);
            }
        }

        return rates;
    }

    /// <summary>The spike's <c>norm_tk</c>: drop every "(...)", then <see cref="NormaliseText"/>.</summary>
    internal static string NormaliseTimekeeper(string? name) =>
        string.IsNullOrEmpty(name) ? string.Empty : NormaliseText(Parenthetical().Replace(name, string.Empty));

    /// <summary>The spike's <c>norm_text</c>: lower-case ASCII letters and digits only.</summary>
    internal static string NormaliseText(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : NonAlphanumeric().Replace(text.ToLowerInvariant(), string.Empty);

    private static decimal? Round2(decimal? value) =>
        value is { } v ? Math.Round(v, 2, MidpointRounding.ToEven) : null;

    // ── day/month post-check ──────────────────────────────────────────────────────────────────────────────

    private static ExtractedInvoiceLine SwapDayMonth(ExtractedInvoiceLine line, DateOnly? start, DateOnly? end)
    {
        if (start is not { } from || end is not { } to || from > to || line.Date.Value is not { } date)
        {
            return line;
        }

        if ((date >= from && date <= to) || date.Day > 12 || date.Month > DateTime.DaysInMonth(date.Year, date.Day))
        {
            return line;
        }

        var swapped = new DateOnly(date.Year, date.Day, date.Month);
        return swapped >= from && swapped <= to
            ? line with { Date = line.Date with { Value = swapped, Repair = ExtractedValueRepair.DayMonthSwapped } }
            : line;
    }

    [GeneratedRegex(@"\(.*?\)", RegexOptions.CultureInvariant)]
    private static partial Regex Parenthetical();

    [GeneratedRegex(@"\(([A-Za-z]{1,4})\)", RegexOptions.CultureInvariant)]
    private static partial Regex Initials();

    [GeneratedRegex("[^a-z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphanumeric();
}
