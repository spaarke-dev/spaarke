using FluentAssertions;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;
using static Sprk.Bff.Api.Services.SpeAdmin.SpeDashboardSyncService;

namespace Sprk.Bff.Api.Tests.Domain.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165 (owner round 20; round 25 item 5) — the pure rules of the container → business-unit
/// binding: reading the stamp, deciding a caller's reach, and attributing a container to the config that owns it for the
/// dashboard. ADR-038 §2 path #6 (pure domain logic; no I/O).
/// </summary>
public sealed class ContainerBindingRuleTests
{
    private static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitA = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitASub = Guid.Parse("1a100000-0000-0000-0000-000000000000");
    private static readonly Guid UnitASubSub = Guid.Parse("1a110000-0000-0000-0000-000000000000");
    private static readonly Guid UnitB = Guid.Parse("1b000000-0000-0000-0000-000000000000");

    private const string TypeT = "77777777-0000-0000-0000-000000000077";
    private const string TypeX = "88888888-0000-0000-0000-000000000088";

    private static readonly Guid ConfigRoot = Guid.Parse("c1000000-0000-0000-0000-000000000000");
    private static readonly Guid ConfigA = Guid.Parse("ca000000-0000-0000-0000-00000000000a");
    private static readonly Guid ConfigA2 = Guid.Parse("ca200000-0000-0000-0000-00000000000a");
    private static readonly Guid ConfigB = Guid.Parse("cb000000-0000-0000-0000-00000000000b");
    private static readonly Guid ConfigN = Guid.Parse("c0000000-0000-0000-0000-00000000000e");

    private static readonly IReadOnlyDictionary<Guid, Guid?> Hierarchy = new Dictionary<Guid, Guid?>
    {
        [Root] = null,
        [UnitA] = Root,
        [UnitASub] = UnitA,
        [UnitASubSub] = UnitASub,
        [UnitB] = Root,
    };

    // ── reading the stamp ────────────────────────────────────────────────────

    [Fact]
    public void Read_TheStamp_IsBoundToItsUnit_WhateverTheNamesCase()
    {
        SpeContainerBusinessUnitStamp.Read(new[] { new CustomPropertyDto("SPAARKEBUSINESSUNITID", UnitA.ToString("N"), false) })
            .Should().Be(SpeContainerBinding.BoundTo(UnitA));
    }

    [Fact]
    public void Read_NoStamp_IsUnbound()
    {
        SpeContainerBusinessUnitStamp.Read(new[] { new CustomPropertyDto("Region", "EU", true) })
            .Should().Be(SpeContainerBinding.Unbound);
        SpeContainerBusinessUnitStamp.Read(Array.Empty<CustomPropertyDto>()).Should().Be(SpeContainerBinding.Unbound);
        SpeContainerBusinessUnitStamp.Read(null).Should().Be(SpeContainerBinding.Unbound);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Read_AStampThatIsNotOneBusinessUnit_IsMalformed(string value)
    {
        SpeContainerBusinessUnitStamp.Read(new[] { new CustomPropertyDto(SpeContainerBusinessUnitStamp.PropertyName, value, false) })
            .Should().Be(SpeContainerBinding.Malformed);
    }

    [Fact]
    public void Read_TwoSpellingsOfTheNameWithDifferentUnits_IsMalformed()
    {
        SpeContainerBusinessUnitStamp.Read(new[]
        {
            new CustomPropertyDto("spaarkeBusinessUnitId", UnitA.ToString(), false),
            new CustomPropertyDto("SpaarkeBusinessUnitID", UnitB.ToString(), false),
        }).Should().Be(SpeContainerBinding.Malformed);
    }

    [Fact]
    public void TheStampItWrites_ReadsBackAsItsUnit()
    {
        SpeContainerBusinessUnitStamp.Read(new[] { SpeContainerBusinessUnitStamp.ToProperty(UnitA) })
            .Should().Be(SpeContainerBinding.BoundTo(UnitA));
        SpeContainerBusinessUnitStamp.ToProperty(UnitA).IsSearchable.Should().BeFalse();
    }

    // ── the caller's reach ───────────────────────────────────────────────────

    [Fact]
    public void ALeafAdmin_ReachesItsOwnSubtree_Only()
    {
        var leaf = new SpeAdminCallerScope(UnitA, IsPlatformOperator: false, new HashSet<Guid> { UnitA, UnitASub });

        leaf.CanReach(SpeContainerBinding.BoundTo(UnitA)).Should().BeTrue();
        leaf.CanReach(SpeContainerBinding.BoundTo(UnitASub)).Should().BeTrue();
        leaf.CanReach(SpeContainerBinding.BoundTo(UnitB)).Should().BeFalse("another customer's container");
        leaf.CanReach(SpeContainerBinding.BoundTo(Root)).Should().BeFalse("its parent's container");
        leaf.CanReach(SpeContainerBinding.Unbound).Should().BeFalse("an unbound container is a root-unit admin's only");
        leaf.CanReach(SpeContainerBinding.Malformed).Should().BeFalse();
    }

    [Fact]
    public void ARootAdmin_ReachesItsUnitsAndUnboundContainers_ButNotAUnitItDoesNotKnow()
    {
        var root = new SpeAdminCallerScope(Root, IsPlatformOperator: true, new HashSet<Guid>(Hierarchy.Keys));

        root.CanReach(SpeContainerBinding.BoundTo(UnitB)).Should().BeTrue();
        root.CanReach(SpeContainerBinding.Unbound).Should().BeTrue();
        root.CanReach(SpeContainerBinding.Malformed).Should().BeTrue();
        root.CanReach(SpeContainerBinding.BoundTo(Guid.Parse("f0000000-0000-0000-0000-0000000000ff"))).Should().BeFalse(
            "another environment's container in a shared Model 1 consuming tenant");
    }

    [Fact]
    public void Nobody_ReachesNothing()
    {
        SpeAdminCallerScope.Nobody.CanReach(SpeContainerBinding.BoundTo(UnitA)).Should().BeFalse();
        SpeAdminCallerScope.Nobody.CanReach(SpeContainerBinding.Unbound).Should().BeFalse();
    }

    [Fact]
    public void DecideContainer_RefusesAReportedTypeThatIsNotTheConfigs_AndAnAbsentContainer()
    {
        var root = new SpeAdminCallerScope(Root, true, new HashSet<Guid>(Hierarchy.Keys));

        SpeAdminTenantScope.DecideContainer(root, TypeT, new SpeContainerBindingRead("c", TypeX, SpeContainerBinding.BoundTo(UnitA)))
            .Should().Be(SpeAdminScopeDecision.NotFoundOrOutOfScope);
        SpeAdminTenantScope.DecideContainer(root, TypeT, null).Should().Be(SpeAdminScopeDecision.NotFoundOrOutOfScope);
        SpeAdminTenantScope.DecideContainer(root, TypeT, new SpeContainerBindingRead("c", TypeT.ToUpperInvariant(), SpeContainerBinding.BoundTo(UnitA)))
            .Should().Be(SpeAdminScopeDecision.Permitted);
    }

    // ── dashboard attribution ────────────────────────────────────────────────

    private static readonly IReadOnlyList<AttributableConfig> Configs = new[]
    {
        new AttributableConfig(ConfigRoot, TypeT, Root),
        new AttributableConfig(ConfigA, TypeT, UnitA),
        new AttributableConfig(ConfigB, TypeT, UnitB),
        new AttributableConfig(ConfigN, TypeT, null),
    };

    [Fact]
    public void AContainer_GoesToTheConfigOfItsOwnUnit()
    {
        AttributeContainer(SpeContainerBinding.BoundTo(UnitA), TypeT, Configs, Hierarchy)
            .Should().Be(ContainerAttribution.To(ConfigA));
        AttributeContainer(SpeContainerBinding.BoundTo(UnitB), TypeT, Configs, Hierarchy)
            .Should().Be(ContainerAttribution.To(ConfigB));
    }

    [Fact]
    public void AContainerOfADescendantUnit_GoesToTheNearestAncestorsConfig()
    {
        AttributeContainer(SpeContainerBinding.BoundTo(UnitASubSub), TypeT, Configs, Hierarchy)
            .Should().Be(ContainerAttribution.To(ConfigA), "Unit A is the nearest unit above A-sub-sub with a config of the type");
    }

    [Fact]
    public void ARootUnitsContainer_GoesToTheRootConfig_NotToTheUnitLessOne()
    {
        AttributeContainer(SpeContainerBinding.BoundTo(Root), TypeT, Configs, Hierarchy)
            .Should().Be(ContainerAttribution.To(ConfigRoot));
    }

    [Fact]
    public void OnlyAConfigOfTheContainersOwnType_CanOwnIt()
    {
        AttributeContainer(SpeContainerBinding.BoundTo(UnitA), TypeX, Configs, Hierarchy)
            .Should().Be(ContainerAttribution.Unattributed, "no config of type X sits at or above Unit A");
    }

    [Fact]
    public void AUnitLessConfig_NeverReceivesABoundContainer()
    {
        var onlyUnitLess = new[] { new AttributableConfig(ConfigN, TypeT, null) };

        AttributeContainer(SpeContainerBinding.BoundTo(UnitA), TypeT, onlyUnitLess, Hierarchy)
            .Should().Be(ContainerAttribution.Unattributed, "a unit-less config is visible to every admin");
    }

    [Fact]
    public void UnboundAndMalformedContainers_AreUnattributed()
    {
        AttributeContainer(SpeContainerBinding.Unbound, TypeT, Configs, Hierarchy).Should().Be(ContainerAttribution.Unattributed);
        AttributeContainer(SpeContainerBinding.Malformed, TypeT, Configs, Hierarchy).Should().Be(ContainerAttribution.Unattributed);
    }

    [Fact]
    public void AContainerBoundToAUnitTheHierarchyDoesNotHold_IsExcluded()
    {
        AttributeContainer(SpeContainerBinding.BoundTo(Guid.Parse("f0000000-0000-0000-0000-0000000000ff")), TypeT, Configs, Hierarchy)
            .Should().Be(ContainerAttribution.Excluded);
    }

    [Fact]
    public void WithoutAHierarchy_OnlyAnExactUnitMatchIsAttributed_EverythingElseIsExcluded()
    {
        AttributeContainer(SpeContainerBinding.BoundTo(UnitA), TypeT, Configs, hierarchy: null)
            .Should().Be(ContainerAttribution.To(ConfigA));
        AttributeContainer(SpeContainerBinding.BoundTo(UnitASub), TypeT, Configs, hierarchy: null)
            .Should().Be(ContainerAttribution.Excluded, "without the hierarchy the nearest owner cannot be proven");
    }

    [Fact]
    public void TwoConfigsOfOneUnitAndType_TheLowestIdOwnsTheContainer_Deterministically()
    {
        var twins = new[] { new AttributableConfig(ConfigA2, TypeT, UnitA), new AttributableConfig(ConfigA, TypeT, UnitA) };

        AttributeContainer(SpeContainerBinding.BoundTo(UnitA), TypeT, twins, Hierarchy)
            .Should().Be(ContainerAttribution.To(ConfigA));
    }

    [Fact]
    public void ACyclicHierarchy_Terminates()
    {
        var cyclic = new Dictionary<Guid, Guid?> { [UnitA] = UnitB, [UnitB] = UnitA };

        AttributeContainer(SpeContainerBinding.BoundTo(UnitA), TypeX, Configs, cyclic)
            .Should().Be(ContainerAttribution.Unattributed);
    }
}
