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
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> / spec FR-09, FR-10) for
/// <see cref="PolicyScopeResolver"/>. Exercises the REAL scope-match + ordering + fail-closed evaluation over
/// <c>sprk_policy</c>, doubling only the Dataverse module boundary (<see cref="IGenericEntityService"/>) —
/// same placement rationale as <c>CommunicationRuleGateSeamTests</c> (the component composes a table read
/// boundary + branchy evaluation), whose scope semantics this resolver copies verbatim.
/// </summary>
public sealed class PolicyScopeResolverSeamTests
{
    private static (PolicyScopeResolver Resolver, Mock<IGenericEntityService> EntityMock) Resolver(
        params DataverseEntity[] policies)
    {
        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entity
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_policy"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(policies.ToList()));

        var resolver = new PolicyScopeResolver(entity.Object, NullLogger<PolicyScopeResolver>.Instance);
        return (resolver, entity);
    }

    private static DataverseEntity Policy(
        Guid id, string? tenant = null, Guid? matterId = null, int? priority = null, string code = "POL-TEST")
    {
        var e = new DataverseEntity("sprk_policy", id)
        {
            ["sprk_policycode"] = code,
        };
        if (tenant is not null)
        {
            e["sprk_tenant"] = tenant;
        }
        if (matterId.HasValue)
        {
            e["sprk_matter"] = new EntityReference("sprk_matter", matterId.Value);
        }
        if (priority.HasValue)
        {
            e["sprk_priority"] = priority.Value;
        }
        return e;
    }

    // Blank sprk_tenant matches every tenant (verbatim CommunicationRuleGate.cs:175-181).
    [Fact]
    public async Task ResolveAsync_PolicyHasBlankTenant_MatchesEveryTenant()
    {
        var policyId = Guid.NewGuid();
        var (resolver, _) = Resolver(Policy(policyId, tenant: null));

        var result = await resolver.ResolveAsync(new PolicyScopeRequest(Tenant: "tenant-a", MatterId: null));

        result.ReadFailed.Should().BeFalse();
        result.MatchedPolicies.Should().ContainSingle(m => m.PolicyId == policyId);
    }

    // Empty sprk_matter matches every matter (verbatim CommunicationRuleGate.cs:183-188).
    [Fact]
    public async Task ResolveAsync_PolicyHasEmptyMatter_MatchesEveryMatter()
    {
        var policyId = Guid.NewGuid();
        var (resolver, _) = Resolver(Policy(policyId, matterId: null));

        var result = await resolver.ResolveAsync(
            new PolicyScopeRequest(Tenant: null, MatterId: Guid.NewGuid()));

        result.ReadFailed.Should().BeFalse();
        result.MatchedPolicies.Should().ContainSingle(m => m.PolicyId == policyId);
    }

    // A non-blank tenant on the policy that does NOT equal the request tenant must NOT match — the inverse of
    // the blank-means-all case, and the one most likely to be silently broken by an inverted condition.
    [Fact]
    public async Task ResolveAsync_PolicyTenantDiffersFromRequestTenant_DoesNotMatch()
    {
        var (resolver, _) = Resolver(Policy(Guid.NewGuid(), tenant: "tenant-a"));

        var result = await resolver.ResolveAsync(new PolicyScopeRequest(Tenant: "tenant-b", MatterId: null));

        result.ReadFailed.Should().BeFalse();
        result.MatchedPolicies.Should().BeEmpty();
        result.Reason.Should().Be("no-matching-policy");
    }

    // OrderBy(sprk_priority ?? 500).ThenBy(Id) — equal priority ties broken deterministically by record id.
    [Fact]
    public async Task ResolveAsync_TwoPoliciesWithEqualPriority_OrdersByRecordIdAscending()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var (first, second) = idA.CompareTo(idB) < 0 ? (idA, idB) : (idB, idA);

        var (resolver, _) = Resolver(
            Policy(idA, priority: 100),
            Policy(idB, priority: 100));

        var result = await resolver.ResolveAsync(new PolicyScopeRequest(Tenant: null, MatterId: null));

        result.MatchedPolicies.Should().HaveCount(2);
        result.MatchedPolicies[0].PolicyId.Should().Be(first);
        result.MatchedPolicies[1].PolicyId.Should().Be(second);
    }

    // A policy with no sprk_priority set is treated as 500 — both in ordering and in the mapped value.
    [Fact]
    public async Task ResolveAsync_PolicyHasNoPriority_TreatedAs500()
    {
        var noPriorityId = Guid.NewGuid();
        var explicitHigherId = Guid.NewGuid();
        var (resolver, _) = Resolver(
            Policy(noPriorityId, priority: null),
            Policy(explicitHigherId, priority: 600)); // 600 > 500, so the null-priority policy wins (lower sorts first)

        var result = await resolver.ResolveAsync(new PolicyScopeRequest(Tenant: null, MatterId: null));

        result.MatchedPolicies.Should().HaveCount(2);
        result.MatchedPolicies[0].PolicyId.Should().Be(noPriorityId, "500 (the default) sorts before 600");
        result.MatchedPolicies[0].Priority.Should().Be(500);
        result.MatchedPolicies[1].Priority.Should().Be(600);
    }

    // Negative: an unavailable policy store FAILS CLOSED with a stated reason — it must NOT look like
    // "no-matching-policy" (which would read as a working quiet system over a broken read path, spec NFR-03).
    [Fact]
    public async Task ResolveAsync_PolicyStoreUnavailable_FailsClosedWithStatedReason()
    {
        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entity
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_policy"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse outage"));

        var resolver = new PolicyScopeResolver(entity.Object, NullLogger<PolicyScopeResolver>.Instance);

        var result = await resolver.ResolveAsync(new PolicyScopeRequest(Tenant: null, MatterId: null));

        result.ReadFailed.Should().BeTrue("a store-read failure must fail closed, never proceed with an empty set");
        result.Reason.Should().Be("policy-store-read-failed");
        result.Reason.Should().NotBe("no-matching-policy", "the failure reason must be distinguishable from a genuine zero-match result");
        result.MatchedPolicies.Should().BeEmpty();
    }

    // Policy authoring defaults (FR-10): the query sent to the store asks Dataverse to filter to
    // sprk_enabled = true, so a newly authored (disabled) policy is excluded before it ever reaches scope
    // matching — inert until explicitly enabled.
    [Fact]
    public async Task ResolveAsync_Always_QueriesOnlyEnabledActivePolicies()
    {
        QueryExpression? captured = null;
        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entity
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_policy"),
                It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(new EntityCollection());

        var resolver = new PolicyScopeResolver(entity.Object, NullLogger<PolicyScopeResolver>.Instance);
        await resolver.ResolveAsync(new PolicyScopeRequest(Tenant: null, MatterId: null));

        captured.Should().NotBeNull();
        captured!.Criteria.Conditions.Should().Contain(c =>
            c.AttributeName == "sprk_enabled"
            && c.Operator == ConditionOperator.Equal
            && c.Values.Count == 1
            && Equals(c.Values[0], true));
    }
}
