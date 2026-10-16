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
    }

    public Guid PolicyVersionId { get; }

    public string PolicyCode { get; }

    /// <summary>Always a <see cref="RuleDomain.Billing"/> member.</summary>
    public RuleType RuleType { get; }

    /// <summary>FR-24 / FR-26 position (1-11).</summary>
    public int CataloguePosition => BillingRuleCatalogue.CataloguePosition(RuleType);

    /// <summary>The <c>{{field}}</c> tokens the handler must supply for a finding of this type.</summary>
    public IReadOnlySet<string> TemplateFactFields => BillingRuleCatalogue.TemplateFactFields(RuleType);

    /// <summary>ApprovedRate: percent over the approved rate still allowed (0-100).</summary>
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
