using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// ebilling-r1 task 070 (spec FR-24; ontology-r1 review of task 019, 2026-10-15): the Signals passes load Ontology policies
/// only (<c>sprk_domain</c> empty or Ontology). A Billing-domain policy is neither evaluated nor counted as invalid; a
/// billing-typed version on an Ontology policy is refused by the validator gate and counted; a re-evaluation by id of a
/// Billing policy is refused. The fake World ignores nested query filters, so these tests prove the per-row re-check
/// (<see cref="RuleDomains.IsOntologyPolicy"/>); <see cref="Reader_QueriesCarryTheOntologyAllowlist"/> proves the query filter.
/// </summary>
public partial class SignalReevaluationJobTests
{
    private const string BillingCode = "BILL-RATE-001";
    private const int ApprovedRateOption = 100000004;
    private const string ApprovedRateBody = """{"type":"ApprovedRate","tolerancePercent":5}""";

    private static void SetDomain(Harness h, string code, int? domain)
    {
        var policy = new Entity("sprk_policy", h.Sysadmin.PolicyIdOf(code));
        policy[RuleDomains.PolicyDomainColumn] = domain is { } d ? new OptionSetValue(d) : null;
        h.Sysadmin.Stub(policy);
    }

    [Fact]
    public async Task Run_EnabledBillingDomainPolicy_IsNeitherEvaluatedNorCountedInvalid_OntologyPolicyStillEvaluated()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Sysadmin.AddPolicy(BillingCode, SignalWriter.LaneDecide, true, ApprovedRateBody, "Billed {{line.rate}}.", ruleType: ApprovedRateOption);
        SetDomain(h, BillingCode, RuleDomains.PolicyDomainBilling);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ResultJson.Should().Contain("\"policiesEvaluated\":1", "only the null-domain Ontology policy is evaluated")
            .And.Contain("\"invalidPolicies\":0").And.Contain("\"policyFailures\":0");
        h.Org.CreatedPayloads.Should().ContainSingle()
            .Which.GetAttributeValue<string>("sprk_policycode").Should().Be(PolicyCode);
    }

    [Theory]
    [InlineData(RuleDomains.PolicyDomainOntology)]
    [InlineData(null)]
    public async Task Run_OntologyPolicy_ByNullOrExplicitDomain_IsEvaluated(int? domain)
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        SetDomain(h, PolicyCode, domain);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ResultJson.Should().Contain("\"policiesEvaluated\":1");
        h.Org.CreatedPayloads.Should().ContainSingle();
    }

    [Fact]
    public async Task Run_DocketingDomainPolicy_IsExcludedByTheAllowlist()
    {
        // An allowlist, not "!= Billing": the reserved Docketing value (100000002) is excluded too.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        SetDomain(h, PolicyCode, 100000002);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ResultJson.Should().Contain("\"policiesEvaluated\":0");
        h.Org.CreatedPayloads.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_BillingTypedVersionOnAnOntologyPolicy_IsRefusedAndCountedAsAFailure()
    {
        var h = new Harness();
        h.Sysadmin.AddPolicy(BillingCode, SignalWriter.LaneDecide, true, ApprovedRateBody, "Billed {{line.rate}}.", ruleType: ApprovedRateOption);

        var result = await h.RunAsync();

        result.Success.Should().BeFalse("a billing rule in an Ontology policy is an invalid Ontology policy, visibly");
        result.ResultJson.Should().Contain("\"invalidPolicies\":1");
        h.Org.CreatedPayloads.Should().BeEmpty("a billing type is never treated as true");
    }

    [Fact]
    public async Task ReevaluatePolicy_BillingDomainPolicyId_IsRefused_BeforeAnyEvaluation()
    {
        var h = new Harness();
        h.Sysadmin.AddPolicy(BillingCode, SignalWriter.LaneDecide, true, ApprovedRateBody, "Billed {{line.rate}}.", ruleType: ApprovedRateOption);
        SetDomain(h, BillingCode, RuleDomains.PolicyDomainBilling);

        var act = () => h.Job.ReevaluatePolicyAsync(h.Sysadmin.PolicyIdOf(BillingCode));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not an Ontology policy*");
        h.Org.CreatedPayloads.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateSubject_EnabledBillingDomainPolicy_IsNotEvaluatedByTheEventTrigger()
    {
        var h = new Harness();
        h.Sysadmin.AddPolicy(BillingCode, SignalWriter.LaneDecide, true, ApprovedRateBody, "Billed {{line.rate}}.", ruleType: ApprovedRateOption);
        SetDomain(h, BillingCode, RuleDomains.PolicyDomainBilling);

        var result = await h.Job.EvaluateSubjectAsync("sprk_matter", MatterId, default);

        result.PoliciesEvaluated.Should().Be(0);
        result.InvalidPolicies.Should().Be(0);
    }

    [Fact]
    public async Task PoliciesNotInForce_ExcludesABillingDomainPolicy()
    {
        var h = new Harness().WithPathBPolicy();
        h.Sysadmin.AddPolicy(BillingCode, SignalWriter.LaneDecide, false, ApprovedRateBody, "Billed {{line.rate}}.", ruleType: ApprovedRateOption);
        SetDomain(h, BillingCode, RuleDomains.PolicyDomainBilling);
        h.Sysadmin.SetPolicyState(PolicyCode, enabled: false);

        var notInForce = await h.Closure.PoliciesNotInForceAsync(default);

        notInForce.Values.Should().Equal(PolicyCode);
    }

    [Fact]
    public async Task Reader_QueriesCarryTheOntologyAllowlist()
    {
        var h = new Harness().WithPathBPolicy();
        var policyQueries = new List<QueryExpression>();
        h.Sysadmin.FaultOn = q =>
        {
            if (q.EntityName == "sprk_policy") policyQueries.Add(q);
            return null;
        };

        await h.RunAsync();

        policyQueries.Should().NotBeEmpty();
        policyQueries.Should().OnlyContain(q => HasOntologyAllowlist(q),
            "the enabled read and both not-in-force reads filter on sprk_domain null OR Ontology");
        policyQueries.Should().OnlyContain(q => q.ColumnSet.Columns.Contains(RuleDomains.PolicyDomainColumn));
    }

    internal static bool HasOntologyAllowlist(QueryExpression query) =>
        query.Criteria.FilterOperator == LogicalOperator.And
        && query.Criteria.Filters.Any(f =>
            f.FilterOperator == LogicalOperator.Or
            && f.Conditions.Count == 2
            && f.Conditions.Any(c => c.AttributeName == RuleDomains.PolicyDomainColumn && c.Operator == ConditionOperator.Null)
            && f.Conditions.Any(c => c.AttributeName == RuleDomains.PolicyDomainColumn && c.Operator == ConditionOperator.Equal
                                     && c.Values.Count == 1 && (int)c.Values[0] == RuleDomains.PolicyDomainOntology));
}
