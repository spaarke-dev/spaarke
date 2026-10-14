using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Ontology task 144 (unified-access-control-r2's option A, conditions 2 and 3) — the pins every guard over the stamp
/// topology applies to <see cref="CoreAncestorResolver.StampedOutsideAccessTaxonomy"/>. One implementation, so the three
/// guards that read the exemption (<c>CoreAncestorResolverTests.StampSourceTopology_IsClosed</c>,
/// <c>CoreAncestorStampTopologyLockstepTests.StampSourceColumns_MatchTheResolversIntermediates</c> and the
/// <c>StampFreshness</c> theory) cannot pin it differently.
/// </summary>
internal static class StampedOutsideAccessTaxonomyPin
{
    /// <summary>The exemption, written out once more on purpose: widening it must change this line too.</summary>
    internal static readonly string[] Expected = ["sprk_signal", "sprk_decisionrecord"];

    /// <summary>
    /// The exemption is exactly <see cref="Expected"/>; every member is a stamped table (a stale exemption fails); and no
    /// member is in the CHILD access taxonomy, on either side of the C# / TypeScript pair.
    /// </summary>
    internal static void AssertPinned()
    {
        CoreAncestorResolver.StampedOutsideAccessTaxonomy.Should().BeEquivalentTo(Expected,
            "the exemption names exactly the ontology Signal and Decision Record tables (task 144, uac-r2 option A)");
        CoreAncestorResolver.StampedOutsideAccessTaxonomyReason.Should().NotBeNullOrWhiteSpace();

        foreach (var member in CoreAncestorResolver.StampedOutsideAccessTaxonomy)
        {
            CoreAncestorResolver.StampSourceColumns.Should().ContainKey(member,
                $"{member} is exempted as a STAMPED table, so it must carry the stamp; an exemption for a table that is not "
                + "stamped is stale");
            CoreAncestorResolver.IsChildRecordEntity(member).Should().BeFalse(
                $"{member} is outside the access taxonomy: {CoreAncestorResolver.StampedOutsideAccessTaxonomyReason}");
            CoreAncestorResolver.IntermediateRootColumns.Should().NotContainKey(member,
                $"{member} is a stamped child, never an intermediate (nothing is filed under it)");
        }

        TypeScriptChildRecordEntities().Should().NotIntersectWith(CoreAncestorResolver.StampedOutsideAccessTaxonomy,
            "the TypeScript CHILD_RECORD_ENTITIES is the browser's copy of the access taxonomy");
    }

    /// <summary>The TypeScript <c>CHILD_RECORD_ENTITIES</c> literals.</summary>
    internal static IReadOnlyList<string> TypeScriptChildRecordEntities()
    {
        const string relative = "src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts";
        string? path = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && path is null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) path = candidate;
        }

        path.Should().NotBeNull("the TypeScript resolver is the parity source; if it moved, update this pin, never drop it");
        var match = Regex.Match(File.ReadAllText(path!),
            @"export\s+const\s+CHILD_RECORD_ENTITIES\s*:[^=]*=\s*\[(?<body>[^\]]*)\]", RegexOptions.Singleline);
        match.Success.Should().BeTrue("CHILD_RECORD_ENTITIES must exist in the TypeScript resolver");
        return Regex.Matches(match.Groups["body"].Value, @"'([^']+)'").Select(m => m.Groups[1].Value).ToList();
    }
}

/// <summary>
/// Ontology task 144 (unified-access-control-r2's option A, condition 3): the exemption's SAFETY claim — "nothing is filed
/// under a Signal or a Decision Record, so the storage resolver never reads one and no child inherits through one" — is a
/// guard, not prose. It fails the build when any stamp source, any storage-resolver link, or any lineage lookup of a CHILD
/// taxonomy table TARGETS a member of <see cref="CoreAncestorResolver.StampedOutsideAccessTaxonomy"/>.
/// </summary>
public class StampedOutsideAccessTaxonomyGuardTests
{
    /// <summary>One link of the topology: the table that holds it, its column, and the table it names.</summary>
    internal sealed record Link(string Owner, string Column, string Target, string Topology);

    /// <summary>
    /// Every link the stamp and storage topology declares: each stamp source (a child → the intermediate its copy comes from),
    /// each intermediate's root column, each storage-resolver link (followed, intermediate, carrier), and each lineage lookup
    /// of a CHILD taxonomy table.
    /// </summary>
    internal static IReadOnlyList<Link> RealTopology()
    {
        var links = new List<Link>();

        foreach (var (table, sources) in CoreAncestorResolver.StampSourceColumns)
            links.AddRange(sources.Select(s => new Link(table, s.Column, s.Intermediate, "StampSourceColumns")));

        foreach (var (intermediate, roots) in CoreAncestorResolver.IntermediateRootColumns)
            links.AddRange(roots.Select(r => new Link(intermediate, r.Column, r.RootEntity, "IntermediateRootColumns")));

        foreach (var entity in RecordContainerResolver.ChildAncestorLinks.KnownChildEntities)
        {
            var resolverLinks = RecordContainerResolver.ChildAncestorLinks.For(entity)!;
            links.AddRange(resolverLinks.FollowedLinks.Select(l => new Link(entity, l.LinkColumn, l.Target, "ChildAncestorLinks.Followed")));
            links.AddRange(resolverLinks.IntermediateColumns.Select(l => new Link(entity, l.Column, l.Target, "ChildAncestorLinks.Intermediate")));
            links.AddRange(resolverLinks.CarrierColumns.Select(l => new Link(entity, l.Column, l.Target, "ChildAncestorLinks.Carrier")));
        }

        foreach (var child in CoreAncestorResolver.ChildRecordEntities)
        {
            SecureChildLineage.Children.Should().ContainKey(child, $"every CHILD taxonomy table has a lineage row ({child})");
            links.AddRange(SecureChildLineage.Children[child].Lookups
                .Select(l => new Link(child, l.Key, l.Value, "SecureChildLineage(CHILD taxonomy)")));
        }

        return links;
    }

    /// <summary>The links that name a member of <paramref name="members"/> — what the guard refuses.</summary>
    internal static IReadOnlyList<Link> LinksTargeting(IEnumerable<Link> links, IReadOnlyCollection<string> members) =>
        links.Where(l => members.Contains(l.Target, StringComparer.OrdinalIgnoreCase)).ToList();

    [Fact(DisplayName = "Task 144 (uac-r2 condition 2): the exemption is exactly Signal and Decision Record, each one stamped, neither in the C# or TypeScript CHILD taxonomy")]
    public void Exemption_IsPinnedBothWays() => StampedOutsideAccessTaxonomyPin.AssertPinned();

    [Fact(DisplayName = "Task 144 (uac-r2 condition 3): no stamp source, storage-resolver link or CHILD lineage lookup targets a Signal or a Decision Record")]
    public void NothingIsFiledUnderAnExemptedTable()
    {
        var topology = RealTopology();
        topology.Should().NotBeEmpty("the guard must read the real topology, or it proves nothing");

        LinksTargeting(topology, CoreAncestorResolver.StampedOutsideAccessTaxonomy).Should().BeEmpty(
            "a table filed under a Signal or a Decision Record would make the storage resolver read one, and a child could "
            + "inherit through its stamp — the exemption's safety claim: " + CoreAncestorResolver.StampedOutsideAccessTaxonomyReason);

        foreach (var member in CoreAncestorResolver.StampedOutsideAccessTaxonomy)
        {
            RecordContainerResolver.ChildAncestorLinks.For(member).Should().BeNull(
                $"the storage resolver has no links for {member}: it is never asked about one");
            RecordContainerResolver.ChildAncestorLinks.KindOf(member).Should().BeNull(
                $"the storage resolver classifies no record kind for {member}, so a regarding pair naming one is refused");
        }
    }

    [Fact(DisplayName = "Task 144 (uac-r2 condition 3, must fire): a link injected to a Signal is caught")]
    public void Guard_Fires_OnAnInjectedLink()
    {
        var injected = new Link("sprk_todo", "sprk_regardingsignal", "sprk_signal", "injected");
        var injectedDr = new Link("sprk_event", "sprk_regardingdecisionrecord", "sprk_decisionrecord", "injected");

        LinksTargeting(RealTopology().Append(injected).Append(injectedDr), CoreAncestorResolver.StampedOutsideAccessTaxonomy)
            .Should().BeEquivalentTo([injected, injectedDr]);
    }

    [Fact(DisplayName = "Task 144 (uac-r2 condition 3, must not fire): links between ordinary tables, and a link FROM a Signal, are not caught")]
    public void Guard_DoesNotFire_OnLinksThatDoNotTargetAMember()
    {
        var clean = new[]
        {
            new Link("sprk_todo", "sprk_regardingcommunication", "sprk_communication", "synthetic"),
            new Link("sprk_signal", "sprk_regardingcommunication", "sprk_communication", "synthetic"),
            new Link("sprk_decisionrecord", "sprk_regardingmatter", "sprk_matter", "synthetic"),
        };

        LinksTargeting(clean, CoreAncestorResolver.StampedOutsideAccessTaxonomy).Should().BeEmpty(
            "a Signal or a Decision Record naming its own subject is the stamp's input, not something filed under it");
    }
}
