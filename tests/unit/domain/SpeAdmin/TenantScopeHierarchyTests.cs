using FluentAssertions;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.SpeAdmin;

/// <summary>
/// Pure-logic tests (ADR-038 §2 path #6 — no mocks, no DI, no I/O) for the business-unit hierarchy
/// walk that decides which customers an SPE Admin caller may reach.
/// </summary>
/// <remarks>
/// <para>
/// What breaks if these are deleted: in the shared-BFF deployment model this traversal IS the
/// cross-customer boundary. The BFF app registration can reach every container type Graph has
/// registered against it, so nothing outside this codebase prevents customer A's admin from reading
/// customer B's containers. A bug that returns one business unit too many is a data disclosure, and
/// it would look exactly like a working system.
/// </para>
/// <para>
/// The direction of failure matters more than the fact of failure: returning too FEW units shows up
/// immediately as "I can't see my own configs", while returning too MANY is silent. These lean on the
/// second case.
/// </para>
/// </remarks>
public class TenantScopeHierarchyTests
{
    private static readonly Guid Root = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CustomerA = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CustomerB = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CustomerASub = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Orphan = new("55555555-5555-5555-5555-555555555555");

    /// <summary>Root → {A → A-sub, B}, plus an unrelated top-level unit.</summary>
    private static Dictionary<Guid, Guid?> Hierarchy() => new()
    {
        [Root] = null,
        [CustomerA] = Root,
        [CustomerB] = Root,
        [CustomerASub] = CustomerA,
        [Orphan] = null,
    };

    [Fact]
    public void CollectSelfAndDescendants_FromRoot_ReturnsEveryUnitBeneathIt()
    {
        // A Spaarke operator sits above the customers they support.
        SpeAdminTenantScope.CollectSelfAndDescendants(Root, Hierarchy())
            .Should().BeEquivalentTo(new[] { Root, CustomerA, CustomerB, CustomerASub });
    }

    [Fact]
    public void CollectSelfAndDescendants_FromRoot_DoesNotReachAnUnrelatedTopLevelUnit()
    {
        // Orphan has no parent, so it is not beneath Root. This is the disclosure case: a traversal
        // that treated "no parent" as "child of root" would hand every customer to every operator.
        SpeAdminTenantScope.CollectSelfAndDescendants(Root, Hierarchy())
            .Should().NotContain(Orphan);
    }

    [Fact]
    public void CollectSelfAndDescendants_FromACustomerUnit_ExcludesSiblingCustomers()
    {
        // THE cross-customer assertion. Customer A's administrator must never see customer B.
        var accessible = SpeAdminTenantScope.CollectSelfAndDescendants(CustomerA, Hierarchy());

        accessible.Should().BeEquivalentTo(new[] { CustomerA, CustomerASub });
        accessible.Should().NotContain(CustomerB);
        accessible.Should().NotContain(Root, "a child must not reach its parent");
    }

    [Fact]
    public void CollectSelfAndDescendants_FromALeaf_ReturnsOnlyItself()
    {
        SpeAdminTenantScope.CollectSelfAndDescendants(CustomerASub, Hierarchy())
            .Should().BeEquivalentTo(new[] { CustomerASub });
    }

    [Fact]
    public void CollectSelfAndDescendants_AlwaysIncludesTheCallersOwnUnit()
    {
        // Even when the unit is absent from the map — a config referencing a business unit that was
        // deleted must not silently widen the caller's reach to everything.
        var unknown = Guid.NewGuid();

        SpeAdminTenantScope.CollectSelfAndDescendants(unknown, Hierarchy())
            .Should().BeEquivalentTo(new[] { unknown });
    }

    [Fact]
    public void CollectSelfAndDescendants_GivenAnEmptyHierarchy_ReturnsOnlyTheCaller()
    {
        SpeAdminTenantScope.CollectSelfAndDescendants(CustomerA, new Dictionary<Guid, Guid?>())
            .Should().BeEquivalentTo(new[] { CustomerA });
    }

    [Fact]
    public void CollectSelfAndDescendants_WhenAUnitIsItsOwnParent_Terminates()
    {
        // Dataverse should not permit this, but the traversal loops until no new unit is added, so a
        // malformed row must not hang a request. Asserting termination, not tolerating the data.
        var selfParented = new Dictionary<Guid, Guid?>
        {
            [Root] = null,
            [CustomerA] = CustomerA,
        };

        SpeAdminTenantScope.CollectSelfAndDescendants(Root, selfParented)
            .Should().BeEquivalentTo(new[] { Root });
    }

    [Fact]
    public void CollectSelfAndDescendants_WhenTwoUnitsParentEachOther_Terminates()
    {
        var cyclic = new Dictionary<Guid, Guid?>
        {
            [CustomerA] = CustomerB,
            [CustomerB] = CustomerA,
        };

        SpeAdminTenantScope.CollectSelfAndDescendants(CustomerA, cyclic)
            .Should().BeEquivalentTo(new[] { CustomerA, CustomerB });
    }

    [Fact]
    public void CollectSelfAndDescendants_ReachesDescendantsRegardlessOfMapOrdering()
    {
        // The walk repeats until it stops finding new units, so a grandchild listed before its parent
        // is still reached. A single ordered pass would miss it — and would do so only for certain
        // hierarchies, which is the worst kind of intermittent.
        var reversed = new Dictionary<Guid, Guid?>
        {
            [CustomerASub] = CustomerA,
            [CustomerA] = Root,
            [Root] = null,
        };

        SpeAdminTenantScope.CollectSelfAndDescendants(Root, reversed)
            .Should().Contain(CustomerASub);
    }
}
