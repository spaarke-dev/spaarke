using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Access;

/// <summary>
/// unified-access-control-r2 task 149 — the two tables the secure-child share synchronizer decides from: which tables hold
/// a secure record's children (and how they are filed), and what a root's share carries onto a child.
/// </summary>
public class SecureChildLineageTests
{
    /// <summary>
    /// Every child table the Secure Record Owner role covers (config/secure-record-owner-role.json — the ONE codified set,
    /// task 145/146) is a table the synchronizer mirrors, and nothing else is. A child table added to the role set without
    /// a lineage entry would be owned by the Secure team and shared with nobody.
    /// </summary>
    [Fact]
    public void TheLineageMapCoversExactlyTheCodifiedChildTables()
    {
        var codified = SecureRecordOwnerRoleSet.Embedded.Tables
            .Where(t => t.Kind == "child")
            .Select(t => t.LogicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        SecureChildLineage.Children.Keys.Should().BeEquivalentTo(codified);
        SecureRecordOwnerRoleSet.Embedded.Tables.Where(t => t.Kind == "root").Select(t => t.LogicalName)
            .Should().BeEquivalentTo(SecureChildLineage.Roots);
    }

    /// <summary>Every lookup files a row under a root or another mirrored child — never an identity or a service request.</summary>
    [Fact]
    public void EveryLookupTargetsARootOrAMirroredChild()
    {
        foreach (var table in SecureChildLineage.Children.Values)
        {
            table.Lookups.Should().NotBeEmpty($"{table.LogicalName} must be fileable under something");
            foreach (var (column, target) in table.Lookups)
            {
                (SecureChildLineage.IsRoot(target) || SecureChildLineage.IsChild(target))
                    .Should().BeTrue($"{table.LogicalName}.{column} targets {target}");
                target.Should().NotBe("sprk_servicerequest",
                    "the ownership resolver never looks through a service request, so nothing under one is secure");
            }
        }
    }

    /// <summary>
    /// The mirror follows the ownership resolver: every table it treats as a reparentable child is mirrored, except
    /// <c>sprk_servicerequest</c>, which the resolver treats as a root of its own.
    /// </summary>
    [Fact]
    public void EveryReparentableChildOfTheResolverIsMirrored()
    {
        foreach (var table in RecordOwnershipResolver.OwnershipParentEntities.Where(RecordOwnershipResolver.IsReparentableChild))
            SecureChildLineage.IsChild(table).Should().BeTrue($"{table} is owned by the Secure team under a secure parent");
    }

    // ── What a child receives (Dataverse AccessRights values as literals) ────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1)]               // View Only
    [InlineData(262167, 23)]         // Collaborate → no Share
    [InlineData(327703, 65559)]      // Full Access → Delete kept, no Share
    [InlineData(23, 23)]             // legacy Collaborate
    [InlineData(1 | 524288, 1)]      // a UI share carrying Assign → no Assign
    [InlineData(1 | 32, 1)]          // Create means nothing on a share
    public void ChildMirrorMask_KeepsOnlyReadWriteAppendAppendToAndDelete(int rootMask, int childMask)
        => RecordShareLevels.ChildMirrorMask(rootMask).Should().Be(childMask);

    [Fact]
    public void ChildMirrorRights_WritesTheCanonicalLiteral_AndRefusesShare()
    {
        RecordShareLevels.ChildMirrorRights(65559).AccessRightsCsv
            .Should().Be("ReadAccess,WriteAccess,AppendAccess,AppendToAccess,DeleteAccess");
        RecordShareLevels.ChildMirrorRights(1).AccessRightsCsv.Should().Be("ReadAccess");

        var withShare = () => RecordShareLevels.ChildMirrorRights(262167);
        withShare.Should().Throw<ArgumentOutOfRangeException>("a child mirror never carries Share");
    }
}
