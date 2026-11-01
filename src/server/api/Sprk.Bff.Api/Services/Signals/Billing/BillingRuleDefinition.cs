using System.Text.Json;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>
/// One parsed billing rule (ebilling-r1 task 070, consumed by task 071's handlers). Obtainable only from
/// <see cref="PolicyVersionValidator.TryPrepareBillingRule"/>, which validates the row first: the constructor is private and
/// the one factory is internal to the assembly, called only by that gate (enforced by
/// <c>tests/Spaarke.ArchTests/BillingRuleDefinitionCallerGuardTests.cs</c>).
/// </summary>
/// <remarks>Parameters not used by <see cref="RuleType"/> are null (scalars) or empty (lists). The consequence is not here:
/// it is the policy version's <c>sprk_consequence</c> column (task 019; checked by task 073).</remarks>
public sealed class BillingRuleDefinition
{
    private BillingRuleDefinition(Guid policyVersionId, string policyCode, RuleType ruleType, JsonElement root)
    {
        PolicyVersionId = policyVersionId;
        PolicyCode = policyCode;
        RuleType = ruleType;
        TolerancePercent = Decimal(root, "tolerancePercent");
        IncrementHours = Decimal(root, "incrementHours");
        MaxHoursPerDay = Decimal(root, "maxHoursPerDay");
        Codes = Strings(root, "codes");
        NonReimbursableCodes = Strings(root, "nonReimbursableCodes");
        MaxAmountPerItem = Decimal(root, "maxAmountPerItem");
        MaxAgeDays = Int(root, "maxAgeDays");
        GraceDays = Int(root, "graceDays");
        MatchFields = Strings(root, "matchFields");
        Fields = Strings(root, "fields");
        Observation = root.TryGetProperty("observation", out var o) ? o.GetString() : null;
        MinConfidence = Decimal(root, "minConfidence");
        FeeKind = root.TryGetProperty("feeKind", out var k) ? k.GetString() : null;
        Allowance = root.TryGetProperty("allowance", out var a) ? a.GetString() : null;
        FeeCodes = Strings(root, "feeCodes");
        CapAmount = Decimal(root, "capAmount");
        CapPercent = Decimal(root, "capPercent");
        DiscountPercent = Decimal(root, "discountPercent");
    }

    public Guid PolicyVersionId { get; }

    public string PolicyCode { get; }

    /// <summary>Always a <see cref="RuleDomain.Billing"/> member.</summary>
    public RuleType RuleType { get; }

    /// <summary>FR-24 / FR-26 position (1-13; AddedFee 12 and Discount 13, task 115).</summary>
    public int CataloguePosition => BillingRuleCatalogue.CataloguePosition(RuleType);

    /// <summary>The <c>{{field}}</c> tokens the handler must supply for a finding of this type.</summary>
    public IReadOnlySet<string> TemplateFactFields => BillingRuleCatalogue.TemplateFactFields(RuleType);

    /// <summary>ApprovedRate: percent over the approved rate still allowed (0-100). Discount: percent over the discounted rate (absent = 0).</summary>
    public decimal? TolerancePercent { get; }

    /// <summary>TimeIncrement: the billing increment in hours (e.g. 0.1).</summary>
    public decimal? IncrementHours { get; }

    /// <summary>DailyHoursCap: hours per timekeeper per day.</summary>
    public decimal? MaxHoursPerDay { get; }

    /// <summary>NonBillableActivity: activity / task codes that are not billable.</summary>
    public IReadOnlyList<string> Codes { get; }

    /// <summary>ExpenseRule: expense codes that are not reimbursable (may be empty when a cap is set).</summary>
    public IReadOnlyList<string> NonReimbursableCodes { get; }

    /// <summary>ExpenseRule: the cap per expense item (null when only codes are set).</summary>
    public decimal? MaxAmountPerItem { get; }

    /// <summary>InvoiceAge: the most days a line date may be before the invoice date.</summary>
    public int? MaxAgeDays { get; }

    /// <summary>CoreRecordWindow: days allowed outside the core record's open / close dates.</summary>
    public int? GraceDays { get; }

    /// <summary>Duplicate: the fields compared (closed vocabulary in the schema).</summary>
    public IReadOnlyList<string> MatchFields { get; }

    /// <summary>RequiredData: the fields that must be present (closed vocabulary in the schema).</summary>
    public IReadOnlyList<string> Fields { get; }

    /// <summary>AiObservation: the observation kind (closed vocabulary in the schema).</summary>
    public string? Observation { get; }

    /// <summary>AiObservation: the confidence (0-1] at or above which the observation counts.</summary>
    public decimal? MinConfidence { get; }

    /// <summary>AddedFee: the fee kind (closed vocabulary in the schema). Describes the rule; no fact carries a per-line fee kind.</summary>
    public string? FeeKind { get; }

    /// <summary>
    /// The D-39 replacement key: a more specific rule replaces a broader one with the same key. The rule type, and for AddedFee the
    /// rule type plus <see cref="FeeKind"/> (owner, 2026-10-21), so a firm cap on one fee kind replaces only the client default's cap
    /// on that kind. For the kind <c>other</c> (a courier fee and a postage fee are both <c>other</c>) the key also carries the rule's
    /// fee-code set, so a firm cap replaces only the client default's cap on the same fee (ontology-r1 F3, option a). The set is
    /// compared exactly: {A} and {A,B} are different keys, so a cap on {A,B} never displaces a cap on {A}. The codes are normalised as
    /// the handler matches them (<see cref="Handlers.AddedFeeHandler"/>: a case-insensitive set, so order and duplicates do not
    /// count): upper-cased, de-duplicated, sorted, joined with ',' (a fee code is alphanumeric plus '.', '_' and '-', so ',' cannot
    /// occur in one). The other kinds (lateFee, administrativeFee, interest) key on the kind alone, with no codes. Every other rule
    /// type keys on the rule type alone. The fee kind is compared as stored (ordinal, no trim or case folding): it is a closed
    /// vocabulary the schema enforces before a definition exists, and the handler reads it with the same exact match
    /// (<see cref="BillingRuleCatalogue.FeeKindLabel"/>). A null kind (not reachable through the gate) is a key of its own. An
    /// <c>other</c> rule with no codes is not reachable either (the schema requires at least one code on a cap and the gate refuses
    /// the other allowances; the handler refuses an empty list), but if one existed its empty set would be a key of its own.
    /// </summary>
    internal (RuleType RuleType, string? FeeKind, string FeeCodeSet) PrecedenceKey =>
        RuleType != RuleType.AddedFee
            ? (RuleType, null, string.Empty)
            : (RuleType, FeeKind, FeeKind == OtherFeeKind ? NormalisedFeeCodeSet() : string.Empty);

    /// <summary>The closed-vocabulary value of <see cref="FeeKind"/> that names no particular fee (schema enum <c>other</c>).</summary>
    private const string OtherFeeKind = "other";

    private string NormalisedFeeCodeSet() =>
        string.Join(',', FeeCodes.Select(c => c.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    /// <summary>AddedFee: <c>cap</c> on a definition (the gate refuses <c>notAllowed</c> and <c>afterDays</c>, task 115).</summary>
    public string? Allowance { get; }

    /// <summary>AddedFee (cap): the expense codes of the fee's lines.</summary>
    public IReadOnlyList<string> FeeCodes { get; }

    /// <summary>AddedFee (cap): the most the fee lines may total, in the invoice currency; null when the cap is a percent.</summary>
    public decimal? CapAmount { get; }

    /// <summary>AddedFee (cap): the most the fee lines may total as a percent of the other lines; null when the cap is an amount.</summary>
    public decimal? CapPercent { get; }

    /// <summary>Discount: the agreed discount off the rack rate, in percent (0-100, exclusive). Its tolerance is <see cref="TolerancePercent"/>.</summary>
    public decimal? DiscountPercent { get; }

    /// <summary>Only <see cref="PolicyVersionValidator.TryPrepareBillingRule"/> calls this, after the body passed the type's
    /// schema; it reads the already-validated values and adds no check of its own.</summary>
    internal static BillingRuleDefinition FromValidatedBody(Guid policyVersionId, string policyCode, RuleType ruleType, string ruleBodyJson)
    {
        using var doc = JsonDocument.Parse(ruleBodyJson);
        return new BillingRuleDefinition(policyVersionId, policyCode, ruleType, doc.RootElement);
    }

    private static decimal? Decimal(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? value.GetDecimal() : null;

    private static int? Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? (int)value.GetDecimal() : null; // JSON Schema 'integer' admits 5.0

    private static IReadOnlyList<string> Strings(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
            ? value.EnumerateArray().Select(e => e.GetString()!).ToArray()
            : Array.Empty<string>();
}
