using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 156 — the WRITE side of the core-ancestor stamp (<see cref="CoreAncestorResolver"/>,
/// <see cref="CoreAncestorRestamper"/>, the reconciliation job) and the READ side (<see cref="RecordContainerResolver"/>)
/// must agree on the same topology, or "stale" means two different things.
/// </summary>
/// <remarks>
/// If they drift, either the resolver compares a child's copy with a root the restamper never writes — every upload to
/// that child refuses <c>container_ancestor_stale</c> FOREVER, because the re-stamp it enqueues cannot fix it — or the
/// restamper writes a copy the resolver never checks. Both tables are pinned to each other here.
/// </remarks>
public class CoreAncestorStampTopologyLockstepTests
{
    private const string ServiceRequestColumn = "sprk_regardingservicerequest";

    [Fact(DisplayName = "Task 156 lockstep: every intermediate's root columns in the resolver ARE the columns its copy is derived from")]
    public void IntermediateRootColumns_MatchTheResolversFollowedRoots()
    {
        foreach (var (intermediate, rootColumns) in CoreAncestorResolver.IntermediateRootColumns)
        {
            var links = RecordContainerResolver.ChildAncestorLinks.For(intermediate);
            links.Should().NotBeNull($"the storage resolver must be able to READ a {intermediate} a child is filed under");

            var derived = rootColumns
                .Where(c => c.Column != ServiceRequestColumn) // core: held by the resolver, never compared
                .Select(c => (c.RootEntity, c.Column))
                .ToHashSet();

            links!.FollowedLinks.Select(l => (l.Target, l.LinkColumn)).ToHashSet()
                .Should().BeEquivalentTo(derived,
                    $"the root the resolver compares a child's copy with must be exactly the root {intermediate}'s "
                    + "children are stamped with");
        }
    }

    [Fact(DisplayName = "Task 156 lockstep: on every table that carries a copy, the resolver's compared intermediates ARE the restamper's sources")]
    public void StampSourceColumns_MatchTheResolversIntermediates()
    {
        foreach (var (table, sources) in CoreAncestorResolver.StampSourceColumns)
        {
            var links = RecordContainerResolver.ChildAncestorLinks.For(table);
            links.Should().NotBeNull();

            links!.IntermediateColumns
                .Where(i => i.Column != ServiceRequestColumn)
                .Select(i => (i.Column, i.Target))
                .ToHashSet()
                .Should().BeEquivalentTo(sources.Select(s => (s.Column, s.Intermediate)).ToHashSet(),
                    $"a {table} column the resolver compares must be one the restamper re-stamps from, and back");
        }
    }

    [Fact(DisplayName = "Task 156 lockstep: every child taxonomy entity has resolver links (no child type is refused for unknown links any more)")]
    public void EveryChildTaxonomyEntity_HasResolverLinks()
    {
        CoreAncestorResolver.ChildRecordEntities
            .Should().OnlyContain(e => RecordContainerResolver.ChildAncestorLinks.For(e) != null,
                "since task 156 a child can be filed under any of them, so the resolver reads each one");
    }
}
