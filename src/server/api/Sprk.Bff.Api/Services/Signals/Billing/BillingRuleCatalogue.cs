using System.Collections.Frozen;
using System.Text.Json;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>
/// The eleven billing rule types of spec FR-24 / design.md 6.1 (ebilling-r1 task 070): their catalogue order, embedded
/// schema, template fact-field set and describer sentence. Closed: every member is a switch over
/// <see cref="RuleType"/> (a throwing default for a non-billing type), never a registry, a reflection scan or a data row (D-27).
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

    /// <summary>
    /// The text of the embedded body schema of a billing type (what <see cref="RuleBodySchemaValidator"/> validates against), for a caller that
    /// must show a type's parameters to someone else (the rule drafting Action, task 075).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    /// <exception cref="InvalidOperationException">The embedded resource is missing.</exception>
    internal static string SchemaText(RuleType ruleType)
    {
        var suffix = SchemaResourceSuffix(ruleType);
        var assembly = typeof(BillingRuleCatalogue).Assembly;
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded resource ending in '{suffix}' was not found in assembly '{assembly.FullName}'.");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource stream '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

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
    /// The message of a Duplicate finding whose matching prior is across the secure wall, or whose wall could not be established
    /// (D-112, <see cref="BillingFinding.DetailWithheld"/>). It names no token: the finding carries no tracking number, no
    /// invoice or line detail. The variant of the Duplicate template (see <c>BillingRuleDraftTranslator.Wording</c>), which names
    /// <c>{{duplicateOf.trackingNumber}}</c>; a message composer uses this text, not the policy version's template, for such a finding.
    /// </summary>
    public const string WithheldDuplicateTemplate = "This matches an earlier submission.";

    /// <summary>
    /// The <c>{{field}}</c> tokens of a withheld finding's message (D-112): none. A withheld Duplicate finding is built against this
    /// set, not <see cref="TemplateFactFields"/>, so it cannot name the tracking number or a line value.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a type with a withheld variant (only Duplicate has one).</exception>
    public static IReadOnlySet<string> WithheldTemplateFactFields(RuleType ruleType) => ruleType switch
    {
        RuleType.Duplicate => Fields(),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Only Duplicate has a withheld-detail variant."),
    };

    /// <summary>The message of RequiredData's <see cref="BillingReasonCodes.CurrencyNotUsd"/> finding (ontology-r1 072 review F1): the
    /// type's default wording ("The required data {{missingField}} is missing.") would tell the firm a currency it read was missing.</summary>
    public const string CurrencyNotUsdTemplate = "The invoice currency is not USD.";

    /// <summary>The message of CoreRecordWindow's <see cref="BillingReasonCodes.CoreRecordClosedToBilling"/> finding (D-116; 072 review
    /// F1-a). The version's template and the type's default wording name the open and close dates ("outside the open date ... and
    /// close date ..."), which is false for a Closed record whose dates the line is inside; this names no date.</summary>
    public const string ClosedToBillingTemplate = "The matter, project or work assignment this line is billed to is closed to billing.";

    /// <summary>The message of CoreRecordWindow's <see cref="BillingReasonCodes.CoreRecordClosed"/> finding (D-111, D-116; 072 review
    /// F1-a): a Final record with no close date, or (billing status empty) an inactive one with none. There is no date the line is
    /// outside of, so the date wording would be false; this names none.</summary>
    public const string NoCloseDateTemplate = "This line cannot be checked against a close date: the record is closed or final but has no close date.";

    /// <summary>
    /// The reason's own message, used instead of the policy version's template and the type's default wording, or <c>null</c> when the
    /// reason has none. Names no token, so it always renders.
    /// </summary>
    public static string? ReasonTemplate(string reasonCode) => reasonCode switch
    {
        BillingReasonCodes.CurrencyNotUsd => CurrencyNotUsdTemplate,
        BillingReasonCodes.CoreRecordClosedToBilling => ClosedToBillingTemplate,
        BillingReasonCodes.CoreRecordClosed => NoCloseDateTemplate,
        BillingEvaluationReasons.CurrencyMissing => RatesNotCheckedPrefix + "missing.",
        BillingEvaluationReasons.CurrencyUnreliable => RatesNotCheckedPrefix + "unclear.",
        BillingEvaluationReasons.CurrencyNotUsd => RatesNotCheckedPrefix + "not USD.",
        _ => null,
    };

    /// <summary>D-115: the start of the message of the evaluator's "money not judged" finding; the reason completes it.</summary>
    private const string RatesNotCheckedPrefix = "Rates and expense caps were not checked: the invoice currency is ";

    /// <summary>
    /// A reason that never rejects or adjusts, whatever consequence its policy version carries (D-115; ontology-r1 072 review F1):
    /// the currency is a fact about the invoice, not a breach the rule's author priced. Task 073 holds such a finding as a Flag, and
    /// <see cref="BillingFindingSignalMapper"/> counts it as a Flag when it merges reasons.
    /// </summary>
    public static bool IsFlagOnlyReason(string reasonCode) => reasonCode switch
    {
        BillingReasonCodes.CurrencyNotUsd => true,
        BillingEvaluationReasons.CurrencyMissing or BillingEvaluationReasons.CurrencyUnreliable or BillingEvaluationReasons.CurrencyNotUsd => true,
        _ => false,
    };

    /// <summary>
    /// FR-26 reject-eligibility (ebilling-r1 task 073): catalogue positions 1, 2 and 6 to 10, that is ApprovedTimekeeper, ApprovedRate,
    /// ExpenseRule, InvoiceAge, CoreRecordWindow, Duplicate and RequiredData. TimeIncrement, DailyHoursCap, NonBillableActivity and
    /// AiObservation (I-5) are not. Eligibility is one of the three conditions of a rejection; it is not the whole of "may reject"
    /// (see <see cref="IsHoldOnly"/>, and Duplicate rejects at invoice level only).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static bool IsRejectEligible(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedTimekeeper or RuleType.ApprovedRate or RuleType.ExpenseRule or RuleType.InvoiceAge
            or RuleType.CoreRecordWindow or RuleType.Duplicate or RuleType.RequiredData => true,
        RuleType.TimeIncrement or RuleType.DailyHoursCap or RuleType.NonBillableActivity or RuleType.AiObservation => false,
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    /// <summary>
    /// D-89 ("ApprovedTimekeeper holds, never auto-rejects"): a type that is reject-eligible under FR-26 but whose findings only ever
    /// send the invoice to review. A Reject consequence is refused for it at publish and at the evaluation gate, and the rejection rule
    /// never rejects on its findings. Enforced by name here, not by 046's confidence cap staying below the threshold.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static bool IsHoldOnly(RuleType ruleType) => IsRejectEligible(ruleType) && ruleType == RuleType.ApprovedTimekeeper;

    /// <summary>
    /// The consequences a billing admin may choose for a rule of this type (design.md 6.1 "Allowed consequences", narrowed by D-89 and
    /// I-5): Reject only where the type is reject-eligible and not hold-only; Suggest adjustment only where a handler suggests one
    /// (ApprovedRate, NonBillableActivity); Flag always.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static IReadOnlySet<BillingConsequence> AllowedConsequences(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => Consequences(BillingConsequence.Flag),
        RuleType.ApprovedRate => Consequences(BillingConsequence.Reject, BillingConsequence.Flag, BillingConsequence.SuggestAdjustment),
        RuleType.TimeIncrement => Consequences(BillingConsequence.Flag),
        RuleType.DailyHoursCap => Consequences(BillingConsequence.Flag),
        RuleType.NonBillableActivity => Consequences(BillingConsequence.Flag, BillingConsequence.SuggestAdjustment),
        RuleType.ExpenseRule => Consequences(BillingConsequence.Reject, BillingConsequence.Flag),
        RuleType.InvoiceAge => Consequences(BillingConsequence.Reject, BillingConsequence.Flag),
        RuleType.CoreRecordWindow => Consequences(BillingConsequence.Reject, BillingConsequence.Flag),
        RuleType.Duplicate => Consequences(BillingConsequence.Reject, BillingConsequence.Flag),
        RuleType.RequiredData => Consequences(BillingConsequence.Reject, BillingConsequence.Flag),
        RuleType.AiObservation => Consequences(BillingConsequence.Flag),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    /// <summary>True when <paramref name="consequence"/> is one of the three defined values and <see cref="AllowedConsequences"/> lists it for the type.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static bool IsConsequenceAllowed(RuleType ruleType, int consequence) =>
        Enum.IsDefined(typeof(BillingConsequence), consequence) && AllowedConsequences(ruleType).Contains((BillingConsequence)consequence);

    /// <summary>The sentence a save-time caller shows when <see cref="IsConsequenceAllowed"/> is false. Names no rule content.</summary>
    internal static string ConsequenceRefusal(RuleType ruleType, int consequence)
    {
        var allowed = string.Join(", ", AllowedConsequences(ruleType).OrderBy(c => (int)c).Select(c => c.ToString()));
        if (!Enum.IsDefined(typeof(BillingConsequence), consequence))
        {
            return $"consequence {consequence} is not one of Reject, Flag or SuggestAdjustment. A {ruleType} rule may use: {allowed}.";
        }

        var chosen = (BillingConsequence)consequence;
        var why = ruleType switch
        {
            RuleType.AiObservation => "an AI observation never rejects a submission (I-5)",
            RuleType.ApprovedTimekeeper when chosen == BillingConsequence.Reject => "an unapproved timekeeper holds the invoice for review and never rejects it (D-89)",
            _ when chosen == BillingConsequence.Reject => "this rule type may not reject a submission (FR-26)",
            _ => "this rule type does not suggest adjustments (design.md 6.1)",
        };
        return $"A {ruleType} rule may not have the consequence {chosen}: {why}. Allowed: {allowed}.";
    }

    // ── Reject-capable reason codes (ebilling-r1 task 073 review, F-ALLOW) ──────────────────────────────────────────────────────

    /// <summary>
    /// The finding reason codes (<see cref="BillingReasonCodes"/>) on which a rule of this type may reject a submission: an allowlist, so
    /// a reason code added later (a new sub-reason, a currency finding) only ever holds or flags until it is listed here on purpose. The
    /// rejection rule checks it as part of eligibility. Empty for a type that never rejects (TimeIncrement, DailyHoursCap,
    /// NonBillableActivity, AiObservation) or holds only (ApprovedTimekeeper, D-89). Not listed on purpose:
    /// the ApprovedRate reference-missing reasons (071 judgment call 2), the line-level Duplicate reasons (design.md 6.1) and
    /// <see cref="BillingReasonCodes.CurrencyNotUsd"/> (flag-only, D-115).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static IReadOnlySet<string> RejectCapableReasons(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedRate => RejectCapable(BillingReasonCodes.RateAboveApproved),
        RuleType.ExpenseRule => RejectCapable(BillingReasonCodes.ExpenseCodeNotReimbursable, BillingReasonCodes.ExpenseAmountOverCap),
        RuleType.InvoiceAge => RejectCapable(BillingReasonCodes.InvoiceTooOld),
        RuleType.CoreRecordWindow => RejectCapable(
            BillingReasonCodes.BeforeCoreRecordOpened, BillingReasonCodes.AfterCoreRecordClosed, BillingReasonCodes.CoreRecordClosed,
            BillingReasonCodes.CoreRecordClosedToBilling),
        RuleType.Duplicate => RejectCapable(BillingReasonCodes.DuplicateInvoiceNumber),
        RuleType.RequiredData => RejectCapable(BillingReasonCodes.RequiredFieldMissing),
        RuleType.ApprovedTimekeeper or RuleType.TimeIncrement or RuleType.DailyHoursCap or RuleType.NonBillableActivity
            or RuleType.AiObservation => RejectCapable(),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    /// <summary>True when <see cref="RejectCapableReasons"/> lists <paramref name="reasonCode"/> (ordinal) for the type.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static bool IsRejectCapableReason(RuleType ruleType, string? reasonCode) =>
        reasonCode is not null && RejectCapableReasons(ruleType).Contains(reasonCode);

    private static IReadOnlySet<string> RejectCapable(params string[] reasonCodes) => reasonCodes.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// One deterministic sentence and formal form for a body that <see cref="PolicyVersionValidator.ValidateForSave"/> has
    /// accepted for <paramref name="ruleType"/>. Same body, same text; no lookup is read.
    /// </summary>
    internal static (string Text, string Formal) Describe(RuleType ruleType, JsonElement root) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => (
            "Each line's timekeeper is approved for the firm on the line date.",
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
            $"Judged by the core record's billing status, with {Num(root, "graceDays")} grace days on each date; credit lines are never flagged. Open: a line dated before the core record's open date is flagged; the close date and closed state are ignored. Final: also a line dated after its close date is flagged, and work dated on or before the close date and billed later is not; with no close date, every charge line (credits excluded) is flagged. Closed: every charge line (credits excluded) is flagged as closed to billing. No billing status: a line dated before the open date or after the close date is flagged, and a closed core record with no close date flags every charge line (credits excluded).",
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

    private static IReadOnlySet<BillingConsequence> Consequences(params BillingConsequence[] values) => values.ToFrozenSet();

    private static string Num(JsonElement root, string name) => root.GetProperty(name).GetRawText();

    private static string List(JsonElement root, string name) =>
        string.Join(", ", root.GetProperty(name).EnumerateArray().Select(e => e.GetString()));
}
