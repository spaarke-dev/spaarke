using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The rules system a <see cref="RuleType"/> belongs to (ebilling-r1 task 070, spec FR-24, D-23 / D-26). Code only: a
/// member's domain is fixed by <see cref="RuleDomains.DomainOf"/>, never read from data.
/// </summary>
public enum RuleDomain
{
    /// <summary>Ontology Signals: compiled to FetchXML by <see cref="PredicateCompiler"/>, evaluated by the Signals passes.</summary>
    Ontology,

    /// <summary>eBilling: evaluated in memory per invoice; obtainable only through
    /// <see cref="PolicyVersionValidator.TryPrepareBillingRule"/>.</summary>
    Billing,
}

/// <summary>
/// The closed rule-type to domain mapping, and the <c>sprk_policy.sprk_domain</c> allowlist every Ontology reader applies
/// (ebilling-r1 task 070; ontology-r1 review of task 019, 2026-10-15).
/// </summary>
/// <remarks>
/// <para><b>Allowlist, not "!= Billing".</b> An Ontology reader loads a policy only when its <c>sprk_domain</c> is empty
/// (no backfill: empty reads as Ontology) or Ontology (100000001). Billing (100000000), the reserved Docketing
/// (100000002) and any value added later are excluded without a code change here.</para>
/// <para><b>Two layers.</b> <see cref="AddOntologyPolicyAllowlist"/> narrows the Dataverse query; <see cref="IsOntologyPolicy"/>
/// re-checks each row read (a reader must select <see cref="PolicyDomainColumn"/>), so a query that lost its filter still
/// cannot hand a Billing row to an Ontology pass.</para>
/// </remarks>
public static class RuleDomains
{
    /// <summary>The <c>sprk_policy</c> column holding the global choice <c>sprk_policydomain</c> (task 019).</summary>
    public const string PolicyDomainColumn = "sprk_domain";

    /// <summary><c>sprk_policydomain</c> Billing (task 019, dev 2026-10-15).</summary>
    public const int PolicyDomainBilling = 100000000;

    /// <summary><c>sprk_policydomain</c> Ontology (task 019, dev 2026-10-15). Empty reads as this value.</summary>
    public const int PolicyDomainOntology = 100000001;

    /// <summary>The domain of every defined <see cref="RuleType"/> member. The switch is NOT compiler-exhaustive (the <c>_</c> arm
    /// satisfies it): a new member that is not added here still compiles and throws <see cref="ArgumentOutOfRangeException"/> at
    /// runtime. Every caller (<see cref="IsBilling"/> / <see cref="IsOntology"/> inside the validator's save and evaluation
    /// gates and the billing gate, and the describer) runs inside a catch that turns the throw into a refusal
    /// (<c>internal_error</c> / a refused description), never a crash; the guard that a member is mapped is a test pinning every
    /// defined member's domain.</summary>
    public static RuleDomain DomainOf(RuleType ruleType) => ruleType switch
    {
        RuleType.Threshold or RuleType.Switch or RuleType.Existence => RuleDomain.Ontology,
        RuleType.ApprovedTimekeeper or RuleType.ApprovedRate or RuleType.TimeIncrement or RuleType.DailyHoursCap
            or RuleType.NonBillableActivity or RuleType.ExpenseRule or RuleType.InvoiceAge or RuleType.CoreRecordWindow
            or RuleType.Duplicate or RuleType.RequiredData or RuleType.AiObservation or RuleType.SubmissionIntegrity
            or RuleType.AddedFee or RuleType.Discount => RuleDomain.Billing,
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a member of the closed RuleType set."),
    };

    /// <summary>True only for a defined member whose domain is <see cref="RuleDomain.Billing"/>.</summary>
    public static bool IsBilling(RuleType ruleType) =>
        Enum.IsDefined(ruleType) && DomainOf(ruleType) == RuleDomain.Billing;

    /// <summary>True only for a defined member whose domain is <see cref="RuleDomain.Ontology"/>.</summary>
    public static bool IsOntology(RuleType ruleType) =>
        Enum.IsDefined(ruleType) && DomainOf(ruleType) == RuleDomain.Ontology;

    /// <summary>Adds <c>(sprk_domain is null OR sprk_domain = Ontology)</c> to <paramref name="query"/>'s criteria (AND).</summary>
    public static void AddOntologyPolicyAllowlist(QueryExpression query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var domain = new FilterExpression(LogicalOperator.Or);
        domain.AddCondition(PolicyDomainColumn, ConditionOperator.Null);
        domain.AddCondition(PolicyDomainColumn, ConditionOperator.Equal, PolicyDomainOntology);
        query.Criteria.AddFilter(domain);
    }

    /// <summary>Adds <c>sprk_domain = Billing</c> to <paramref name="query"/>'s criteria (AND), for a billing reader or writer of
    /// <c>sprk_policy</c> or <c>sprk_ruleset</c> (ebilling-r1 task 076). The mirror of <see cref="AddOntologyPolicyAllowlist"/> with the
    /// opposite empty rule: an EMPTY domain is Ontology, so it never matches here. A top-level condition, so it cannot be bypassed by an
    /// <c>Or</c> sibling.</summary>
    public static void AddBillingFilter(QueryExpression query)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Criteria.AddCondition(PolicyDomainColumn, ConditionOperator.Equal, PolicyDomainBilling);
    }

    /// <summary>True only when the row's <c>sprk_domain</c> is exactly Billing. An empty value (which reads as Ontology), any other
    /// value, or a value of the wrong type is not a billing row (fail closed), so a billing reader whose query lost its filter still
    /// cannot act on an Ontology policy.</summary>
    public static bool IsBillingRow(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Attributes.TryGetValue(PolicyDomainColumn, out var value) && value is OptionSetValue { Value: PolicyDomainBilling };
    }

    /// <summary>True when the row's <c>sprk_domain</c> is empty or Ontology. Any other value, or a value of the wrong
    /// type, is not an Ontology policy (fail closed).</summary>
    public static bool IsOntologyPolicy(Entity policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.Attributes.TryGetValue(PolicyDomainColumn, out var value) || value is null)
        {
            return true;
        }

        return value is OptionSetValue { Value: PolicyDomainOntology };
    }

    /// <summary>Adds <c>(sprk_domain is null OR sprk_domain = Ontology OR sprk_domain = Billing)</c> to <paramref name="query"/>'s criteria
    /// (AND): the query form of <see cref="IsClosableOnRetirement"/>. Used only by the Off/Retired closure read (D-170); every evaluator
    /// reader keeps <see cref="AddOntologyPolicyAllowlist"/>.</summary>
    public static void AddClosableOnRetirementAllowlist(QueryExpression query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var domain = new FilterExpression(LogicalOperator.Or);
        domain.AddCondition(PolicyDomainColumn, ConditionOperator.Null);
        domain.AddCondition(PolicyDomainColumn, ConditionOperator.Equal, PolicyDomainOntology);
        domain.AddCondition(PolicyDomainColumn, ConditionOperator.Equal, PolicyDomainBilling);
        query.Criteria.AddFilter(domain);
    }

    /// <summary>True when an Off or Retired policy's open Signals are closed as <c>PolicyRetired</c>: an Ontology policy (empty or
    /// Ontology) or a Billing one (ebilling-r1 D-132 / ontology-r1 D-170, owner 2026-10-19: a billing policy that is no longer used closes
    /// its open Signals). Docketing (reserved), any later value and a value of the wrong type are not closable (fail closed). This does NOT
    /// widen <see cref="IsOntologyPolicy"/>: that gate stays Ontology-only on every evaluator path, so a Billing policy's Signals are still
    /// never evaluated, superseded or re-raised by the nightly job.</summary>
    public static bool IsClosableOnRetirement(Entity policy) => IsOntologyPolicy(policy) || IsBillingRow(policy);
}
