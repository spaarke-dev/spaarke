using System.Collections.Frozen;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>
/// The eleven billing rule types of spec FR-24 / design.md 6.1 (ebilling-r1 task 070): their catalogue order, embedded
/// schema, template fact-field set and describer sentence. Closed: every member is an exhaustive switch over
/// <see cref="RuleType"/>, never a registry, a reflection scan or a data row (D-27).
/// </summary>
/// <remarks>
/// <para><b>Parameters only.</b> A body carries the rule's parameters (a tolerance, an increment, a code list, a number of
/// days). Rates, timekeepers and code descriptions are reference data the evaluator (task 071) reads; the consequence is the
/// <c>sprk_policyversion.sprk_consequence</c> column (task 019), never body content.</para>
/// <para><b>Template fact fields.</b> A billing <c>sprk_messagetemplate</c> may name only the <c>{{field}}</c> tokens in
/// <see cref="TemplateFactFields"/> for its type: the values the type's handler reads for a failing line, plus the rule's own
/// required parameters (<c>rule.*</c>). Task 071's handlers must supply exactly these.</para>
/// </remarks>
public static class BillingRuleCatalogue
{
    /// <summary>FR-24 order. Position N (1-based) is FR-26's numbering; its option value is 100000002 + N.</summary>
    public static IReadOnlyList<RuleType> Types { get; } = new[]
    {
        RuleType.ApprovedTimekeeper,
        RuleType.ApprovedRate,
        RuleType.TimeIncrement,
        RuleType.DailyHoursCap,
        RuleType.NonBillableActivity,
        RuleType.ExpenseRule,
        RuleType.InvoiceAge,
        RuleType.CoreRecordWindow,
        RuleType.Duplicate,
        RuleType.RequiredData,
        RuleType.AiObservation,
    };

    /// <summary>The 1-based FR-24 / FR-26 catalogue position of a billing type.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static int CataloguePosition(RuleType ruleType)
    {
        var index = IndexOf(ruleType);
        return index >= 0
            ? index + 1
            : throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type.");
    }

    /// <summary>The embedded schema resource suffix (flat in <c>Services/Signals/Schemas</c>; the csproj glob is not recursive).</summary>
    internal static string SchemaResourceSuffix(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => Suffix("approved-timekeeper"),
        RuleType.ApprovedRate => Suffix("approved-rate"),
        RuleType.TimeIncrement => Suffix("time-increment"),
        RuleType.DailyHoursCap => Suffix("daily-hours-cap"),
        RuleType.NonBillableActivity => Suffix("non-billable-activity"),
        RuleType.ExpenseRule => Suffix("expense-rule"),
        RuleType.InvoiceAge => Suffix("invoice-age"),
        RuleType.CoreRecordWindow => Suffix("core-record-window"),
        RuleType.Duplicate => Suffix("duplicate"),
        RuleType.RequiredData => Suffix("required-data"),
        RuleType.AiObservation => Suffix("ai-observation"),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    /// <summary>The <c>{{field}}</c> tokens a billing <c>sprk_messagetemplate</c> of this type may name.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static IReadOnlySet<string> TemplateFactFields(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => Fields("line.date", "line.timekeeperName"),
        RuleType.ApprovedRate => Fields("line.date", "line.timekeeperName", "line.rate", "approvedRate", "rule.tolerancePercent"),
        RuleType.TimeIncrement => Fields("line.date", "line.timekeeperName", "line.hours", "rule.incrementHours"),
        RuleType.DailyHoursCap => Fields("line.date", "line.timekeeperName", "dailyHours", "rule.maxHoursPerDay"),
        RuleType.NonBillableActivity => Fields("line.date", "line.timekeeperName", "line.activityCode", "line.taskCode"),
        RuleType.ExpenseRule => Fields("line.date", "line.expenseCode", "line.amount"),
        RuleType.InvoiceAge => Fields("line.date", "invoice.date", "rule.maxAgeDays"),
        RuleType.CoreRecordWindow => Fields("line.date", "coreRecord.openDate", "coreRecord.closeDate", "rule.graceDays"),
        RuleType.Duplicate => Fields("invoice.firmInvoiceNumber", "line.date", "line.timekeeperName", "duplicateOf.trackingNumber"),
        RuleType.RequiredData => Fields("missingField"),
        RuleType.AiObservation => Fields("line.date", "observation", "observationConfidence", "rule.minConfidence"),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    /// <summary>
    /// One deterministic sentence and formal form for a body that <see cref="PolicyVersionValidator.ValidateForSave"/> has
    /// accepted for <paramref name="ruleType"/>. Same body, same text; no lookup is read.
    /// </summary>
    internal static (string Text, string Formal) Describe(RuleType ruleType, JsonElement root) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => (
            "Each line's timekeeper is approved for the firm (and core record) on the line date.",
            "ApprovedTimekeeper()"),
        RuleType.ApprovedRate => (
            $"Each line's billed rate is at most the approved rate for its timekeeper or classification on the line date, plus {Num(root, "tolerancePercent")} percent.",
            $"ApprovedRate(tolerancePercent={Num(root, "tolerancePercent")})"),
        RuleType.TimeIncrement => (
            $"Each line's hours are a multiple of {Num(root, "incrementHours")} hours.",
            $"TimeIncrement(incrementHours={Num(root, "incrementHours")})"),
        RuleType.DailyHoursCap => (
            $"Each timekeeper bills at most {Num(root, "maxHoursPerDay")} hours per day on the invoice.",
            $"DailyHoursCap(maxHoursPerDay={Num(root, "maxHoursPerDay")})"),
        RuleType.NonBillableActivity => (
            $"No line carries a non-billable activity or task code ({List(root, "codes")}).",
            $"NonBillableActivity(codes=[{List(root, "codes")}])"),
        RuleType.ExpenseRule => DescribeExpense(root),
        RuleType.InvoiceAge => (
            $"No line is dated more than {Num(root, "maxAgeDays")} days before the invoice date.",
            $"InvoiceAge(maxAgeDays={Num(root, "maxAgeDays")})"),
        RuleType.CoreRecordWindow => (
            $"Each line is dated within the core record's open and close dates (with {Num(root, "graceDays")} grace days) and the core record is not closed.",
            $"CoreRecordWindow(graceDays={Num(root, "graceDays")})"),
        RuleType.Duplicate => (
            $"No invoice or line duplicates an earlier one from the firm on {List(root, "matchFields")}.",
            $"Duplicate(matchFields=[{List(root, "matchFields")}])"),
        RuleType.RequiredData => (
            $"Each invoice and line has {List(root, "fields")}.",
            $"RequiredData(fields=[{List(root, "fields")}])"),
        RuleType.AiObservation => (
            $"No line has the AI observation '{root.GetProperty("observation").GetString()}' at a confidence of {Num(root, "minConfidence")} or more.",
            $"AiObservation(observation={root.GetProperty("observation").GetString()}, minConfidence={Num(root, "minConfidence")})"),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    private static (string, string) DescribeExpense(JsonElement root)
    {
        var hasCodes = root.TryGetProperty("nonReimbursableCodes", out _);
        var hasCap = root.TryGetProperty("maxAmountPerItem", out _);
        var parts = new List<string>();
        var formal = new List<string>();
        if (hasCodes)
        {
            parts.Add($"carries a non-reimbursable expense code ({List(root, "nonReimbursableCodes")})");
            formal.Add($"nonReimbursableCodes=[{List(root, "nonReimbursableCodes")}]");
        }

        if (hasCap)
        {
            parts.Add($"is over {Num(root, "maxAmountPerItem")} per item");
            formal.Add($"maxAmountPerItem={Num(root, "maxAmountPerItem")}");
        }

        return ($"No expense line {string.Join(" or ", parts)}.", $"ExpenseRule({string.Join(", ", formal)})");
    }

    private static int IndexOf(RuleType ruleType)
    {
        for (var i = 0; i < Types.Count; i++)
        {
            if (Types[i] == ruleType) return i;
        }

        return -1;
    }

    private static string Suffix(string name) => $"Services.Signals.Schemas.billing-{name}.schema.json";

    private static IReadOnlySet<string> Fields(params string[] names) => names.ToFrozenSet(StringComparer.Ordinal);

    private static string Num(JsonElement root, string name) => root.GetProperty(name).GetRawText();

    private static string List(JsonElement root, string name) =>
        string.Join(", ", root.GetProperty(name).EnumerateArray().Select(e => e.GetString()));
}
