using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// ebilling-r1 task 070 (ontology-r1 review of task 019): <see cref="PolicyScopeResolver"/> returns Ontology policies only,
/// so a Billing rule never gates a communication. Doubles only the Dataverse boundary, like
/// <c>PolicyScopeResolverSeamTests</c>; the mock returns every row regardless of the query, so the per-row re-check is
/// what these tests exercise, and the captured query proves the server-side allowlist.
/// </summary>
public sealed class PolicyDomainAllowlistSeamTests
{
    private static DataverseEntity Policy(string code, object? domain)
    {
        var e = new DataverseEntity("sprk_policy", Guid.NewGuid()) { ["sprk_policycode"] = code };
        if (domain is not null) e[RuleDomains.PolicyDomainColumn] = domain;
        return e;
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNullAndOntologyDomainPolicies_NeverBillingDocketingOrMalformed()
    {
        QueryExpression? captured = null;
        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entity
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_policy"), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity>
            {
                Policy("POL-NULL", null),
                Policy("POL-ONT", new OptionSetValue(RuleDomains.PolicyDomainOntology)),
                Policy("BILL-001", new OptionSetValue(RuleDomains.PolicyDomainBilling)),
                Policy("DOCK-001", new OptionSetValue(100000002)),
                Policy("POL-ODD", RuleDomains.PolicyDomainOntology), // a raw int, not an OptionSetValue: not trusted
            }));
        var resolver = new PolicyScopeResolver(entity.Object, NullLogger<PolicyScopeResolver>.Instance);

        var result = await resolver.ResolveAsync(new PolicyScopeRequest("tenant-a", null));

        result.MatchedPolicies.Select(p => p.PolicyCode).Should().BeEquivalentTo("POL-NULL", "POL-ONT");
        captured!.ColumnSet.Columns.Should().Contain(RuleDomains.PolicyDomainColumn);
        captured.Criteria.Filters.Should().ContainSingle(f => f.FilterOperator == LogicalOperator.Or)
            .Which.Conditions.Select(c => (c.AttributeName, c.Operator)).Should().BeEquivalentTo(new[]
            {
                (RuleDomains.PolicyDomainColumn, ConditionOperator.Null),
                (RuleDomains.PolicyDomainColumn, ConditionOperator.Equal),
            });
    }

    [Fact]
    public void AddOntologyPolicyAllowlist_IsAnAllowlist_NotANotEqualBilling()
    {
        var query = new QueryExpression("sprk_policy");

        RuleDomains.AddOntologyPolicyAllowlist(query);

        var filter = query.Criteria.Filters.Should().ContainSingle().Subject;
        filter.FilterOperator.Should().Be(LogicalOperator.Or);
        filter.Conditions.Should().NotContain(c => c.Operator == ConditionOperator.NotEqual);
        filter.Conditions.Single(c => c.Operator == ConditionOperator.Equal).Values.Should().Equal(RuleDomains.PolicyDomainOntology);
    }
}
