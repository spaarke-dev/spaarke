using System.Globalization;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// The one rounding rule of the billing handlers (task 071): money is rounded to the cent, a midpoint away from zero
/// (<c>2.345</c> becomes <c>2.35</c>, <c>-2.345</c> becomes <c>-2.35</c>). Every computed amount (the rate limit with its tolerance, a
/// suggested reduction) goes through <see cref="ToCent"/>; nothing else rounds. Money and hours are <c>decimal</c> throughout.
/// It also holds the one definition of a credit line (<see cref="IsCreditLine"/>, D-105), the other piece of arithmetic every
/// charge rule shares.
/// </summary>
internal static class BillingMath
{
    internal static decimal ToCent(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The one definition of a credit line (D-105): a line with negative hours or a negative amount, a reversal or a write-off
    /// of an earlier charge. A charge rule never judges it as a charge (ApprovedRate, ExpenseRule, InvoiceAge, CoreRecordWindow
    /// skip it; NonBillableActivity never suggests disallowing it), so a credit cannot be rejected or reduced as if it were
    /// billed. The credit stays on the invoice, visible to the reviewer. A line with no hours and no amount is not a credit, but
    /// nor is it shown not to be one: its findings list both as unknown reads (<see cref="HandlerContext.WithCreditTestReads"/>).
    /// Every handler asks this method; none repeats the sign test.
    /// </summary>
    internal static bool IsCreditLine(BillingLineFacts line) => line.Hours < 0m || line.Amount < 0m;
}

/// <summary>
/// The one currency normaliser of billing (D-119): used by the evaluator's money skip (D-20) and its "not checked" finding (D-115),
/// and by RequiredData's currency check, so the two can never disagree about an invoice. R1 judges US dollars only.
/// </summary>
internal static class BillingCurrency
{
    internal const string UsDollars = "USD";

    /// <summary>
    /// The currency as an upper-case code, or <c>null</c> when none was read (absent or blank). A bare <c>"$"</c> is
    /// <see cref="UsDollars"/> (D-119, owner), and so are <c>"US$"</c> and <c>"USD$"</c> (unambiguous; 072 W review); anything else
    /// is the trimmed text upper-cased with the invariant culture (so <c>"CA$"</c> stays a foreign currency).
    /// </summary>
    internal static string? Normalise(string? currency)
    {
        var text = BillingText.Text(currency)?.ToUpperInvariant();
        return text switch
        {
            null => null,
            "$" or "US$" or "USD$" => UsDollars,
            _ => text,
        };
    }

    /// <summary>True when the currency read is US dollars (<c>USD</c> in any case, a bare <c>"$"</c>, <c>"US$"</c> or <c>"USD$"</c>). A currency that was not
    /// read is not USD here; whether that is "missing" is the caller's question (D-115).</summary>
    internal static bool IsUsd(string? currency) => Normalise(currency) == UsDollars;
}

/// <summary>Invariant text for the values a finding records, so a finding does not depend on the machine's culture.</summary>
internal static class BillingText
{
    internal static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A number without trailing zeros: <c>0.10</c> is <c>0.1</c>, <c>5.0</c> is <c>5</c>.</summary>
    internal static string? Number(decimal? value) =>
        value?.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>Money to the cent, two decimals.</summary>
    internal static string? Money(decimal? value) =>
        value is null ? null : BillingMath.ToCent(value.Value).ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>The text trimmed, or <c>null</c> when it is absent or blank.</summary>
    internal static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A comparison key for names, narratives and invoice numbers: trimmed, runs of whitespace collapsed, upper-cased with the
    /// invariant culture. <c>null</c> when nothing is left.
    /// </summary>
    internal static string? Key(string? value)
    {
        var trimmed = Text(value);
        if (trimmed is null) return null;

        var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToUpperInvariant();
    }
}

/// <summary>
/// One handler call: the rule, where it came from, the fact set, and the helpers that build a finding. Building every finding
/// here means every finding carries the policy, version and clause, and every <c>{{token}}</c> value comes from the type's
/// template fact-field set (<see cref="BillingRuleCatalogue.TemplateFactFields"/>): a handler that names a token outside it
/// throws, which is a programming error found by the tests, never something an invoice can cause.
/// </summary>
internal sealed class HandlerContext
{
    private readonly IReadOnlySet<string> _tokens;

    internal HandlerContext(BillingRuleDefinition rule, BillingRuleOrigin origin, BillingFactSet facts)
    {
        Rule = rule;
        Origin = origin;
        Facts = facts;
        _tokens = BillingRuleCatalogue.TemplateFactFields(rule.RuleType);
    }

    internal BillingRuleDefinition Rule { get; }

    internal BillingRuleOrigin Origin { get; }

    internal BillingFactSet Facts { get; }

    internal BillingFinding Finding(
        BillingSubject subject,
        string reasonCode,
        IReadOnlyList<BillingValueRead> values,
        FactValues factValues,
        BillingSuggestedAdjustment? adjustment = null,
        bool detailWithheld = false)
    {
        // A withheld finding (D-112) is built against the withheld variant's token set, which names no detail at all.
        var allowed = detailWithheld ? BillingRuleCatalogue.WithheldTemplateFactFields(Rule.RuleType) : _tokens;
        foreach (var token in factValues.Tokens)
        {
            if (!allowed.Contains(token))
            {
                throw new InvalidOperationException(
                    $"Handler for {Rule.RuleType} used template token '{token}', which is not in its {(detailWithheld ? "withheld " : string.Empty)}fact-field set (task 070).");
            }
        }

        return new BillingFinding
        {
            RuleType = Rule.RuleType,
            PolicyId = Origin.PolicyId,
            PolicyCode = Rule.PolicyCode,
            PolicyVersionId = Rule.PolicyVersionId,
            ClauseReference = Origin.ClauseReference,
            ClauseLocation = Origin.ClauseLocation,
            Subject = subject,
            ReasonCode = reasonCode,
            ValuesRead = values,
            SuggestedAdjustment = adjustment,
            FactValues = factValues.ToDictionary(),
            DetailWithheld = detailWithheld,
        };
    }

    /// <summary>An extracted value of a line, with its confidence from the line's own confidence map.</summary>
    internal BillingValueRead LineField(BillingLineFacts line, string name, string fieldName, string? value) =>
        Extracted(name, new FieldRef(line.Index, fieldName), value, line.FieldConfidence);

    /// <summary>The line date (<c>line.date</c>), an extracted value.</summary>
    internal BillingValueRead LineDate(BillingLineFacts line) =>
        LineField(line, "line.date", BillingFieldNames.Date, BillingText.Date(line.Date));

    /// <summary>The timekeeper as billed (<c>line.timekeeperName</c>), an extracted value.</summary>
    internal BillingValueRead LineTimekeeper(BillingLineFacts line) =>
        LineField(line, "line.timekeeperName", BillingFieldNames.TimekeeperAsBilled, BillingText.Text(line.TimekeeperName));

    /// <summary>The hours (<c>line.hours</c>), an extracted value.</summary>
    internal BillingValueRead LineHours(BillingLineFacts line) =>
        LineField(line, "line.hours", BillingFieldNames.Hours, BillingText.Number(line.Hours));

    /// <summary>
    /// The values a finding read, plus the ones the credit test read (D-105). <see cref="BillingMath.IsCreditLine"/> decides on the
    /// extracted Hours and Amount, so a finding built after that test depends on them whether or not its own check uses them: a
    /// reversal printed "(1,437.50)" and extracted as +1437.50 at confidence 0.50 is judged as a charge only because the amount
    /// could not be trusted. Task 073 checks the confidence of the read fields and never infers them, so every such finding lists
    /// them. A value already in <paramref name="values"/> is not repeated; a field the line does not have was not read.
    /// Appended in a fixed order (Hours, then Amount) after the finding's own values.
    /// <para><b>Both absent (071 round 3, F4): a deliberate exception to "an absent field is not invented".</b> When the line has
    /// NEITHER Hours NOR Amount, the credit test had nothing to judge, so "not a credit" is not evidence: "(2.5) @ 650 = (1,625.00)"
    /// whose bracketed numbers did not parse would otherwise be judged as a charge with every read reliable, and 073 could reject a
    /// credit. Both are then listed as null-valued reads with unknown confidence (below the floor), so 073 holds the finding. One
    /// absent is normal (an expense line has no hours) and is not listed.</para>
    /// <para><b>Hours present, Amount absent (072 W review, A7 extension).</b> A fee line whose hours parsed (not negative) but whose
    /// amount did not is judged a charge on the hours alone, and the amount may have been the negative one. The missing Amount is
    /// listed as an unknown read (null value, unknown confidence, below the floor) so 073 holds the finding, as in the both-absent case.
    /// A line with an Amount and no hours (an expense line) is unchanged.</para>
    /// </summary>
    internal IReadOnlyList<BillingValueRead> WithCreditTestReads(BillingLineFacts line, IEnumerable<BillingValueRead> values)
    {
        var all = values.ToList();
        var hours = new FieldRef(line.Index, BillingFieldNames.Hours);
        var amount = new FieldRef(line.Index, BillingFieldNames.Amount);

        if (line.Hours is null && line.Amount is null)
        {
            // Added unless the finding already lists the field with unknown confidence: a read of it with a known confidence (the
            // line's map can hold one for a value that did not parse) must not make the credit test look reliable.
            if (!all.Any(v => v.ExtractedField == hours && v.Confidence is null))
            {
                all.Add(Extracted("line.hours", hours, null, (decimal?)null));
            }

            if (!all.Any(v => v.ExtractedField == amount && v.Confidence is null))
            {
                all.Add(Extracted("line.amount", amount, null, (decimal?)null));
            }

            return all;
        }

        if (line.Hours is not null && !all.Any(v => v.ExtractedField == hours))
        {
            all.Add(LineHours(line));
        }

        if (line.Amount is not null && !all.Any(v => v.ExtractedField == amount))
        {
            all.Add(LineField(line, "line.amount", BillingFieldNames.Amount, BillingText.Money(line.Amount)));
        }
        else if (line.Amount is null && line.Hours >= 0m && !all.Any(v => v.ExtractedField == amount && v.Confidence is null))
        {
            // A fee line whose hours parsed but whose amount did not (072 W review, A7 extension): nothing shows the amount is not
            // the negative one, so it is listed as an unknown read as well. An expense line (no hours, amount present) is unchanged.
            all.Add(Extracted("line.amount", amount, null, (decimal?)null));
        }

        return all;
    }

    /// <summary>An extracted value of the header.</summary>
    internal BillingValueRead HeaderField(string name, string fieldName, string? value) =>
        Extracted(name, new FieldRef(null, fieldName), value, Facts.Invoice.FieldConfidence);

    private BillingValueRead Extracted(string name, FieldRef field, string? value, IReadOnlyDictionary<string, decimal> confidences)
    {
        decimal? confidence = confidences.TryGetValue(field.Field, out var c) ? c : null;
        return Extracted(name, field, value, confidence);
    }

    /// <summary>An extracted value whose confidence is already known. Unknown (<c>null</c>) counts as below the threshold.</summary>
    internal BillingValueRead Extracted(string name, FieldRef field, string? value, decimal? confidence) => new()
    {
        Name = name,
        Value = value,
        ExtractedField = field,
        Confidence = confidence,
        BelowReliabilityThreshold = confidence is null || confidence.Value < Facts.ReliabilityThreshold,
    };

    /// <summary>A value from the system's own reference data: no field reference, no confidence.</summary>
    internal static BillingValueRead Reference(string name, string? value) => new() { Name = name, Value = value };
}

/// <summary>The template token values of one finding. A token without a value is simply not added.</summary>
internal sealed class FactValues
{
    private readonly List<KeyValuePair<string, string>> _values = [];

    internal IEnumerable<string> Tokens => _values.Select(v => v.Key);

    internal FactValues Add(string token, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _values.Add(new KeyValuePair<string, string>(token, value));
        }

        return this;
    }

    internal IReadOnlyDictionary<string, string> ToDictionary()
    {
        var map = new Dictionary<string, string>(_values.Count, StringComparer.Ordinal);
        foreach (var (key, value) in _values)
        {
            map[key] = value;
        }

        return map;
    }
}
