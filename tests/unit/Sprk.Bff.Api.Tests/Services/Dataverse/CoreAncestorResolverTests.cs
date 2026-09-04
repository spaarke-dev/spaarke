using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

/// <summary>
/// Unit tests for <see cref="CoreAncestorResolver"/> — FR-26 server-side core-ancestor derivation.
/// </summary>
/// <remarks>
/// <para>
/// These guard an ACCESS CONTROL invariant. The stamp this resolver derives is the only thing that lets the
/// evaluator answer "can this principal see this child record?" in one hop; get it wrong and server-created
/// records are silently hidden (under-grant) or silently shared (over-grant).
/// </para>
/// <para>
/// The two rules pinned hardest are the two that are easiest to invert: Matter does NOT inherit from Project,
/// and derivation takes exactly one hop.
/// </para>
/// </remarks>
public class CoreAncestorResolverTests
{
    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CommId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SrId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    /// <summary>All four core-ancestor lookups, as <c>sprk_communication</c> actually carries them.</summary>
    private static readonly string[] CommunicationColumns =
    [
        "sprk_regardingmatter",
        "sprk_regardingproject",
        "sprk_regardingworkassignment",
        "sprk_regardingservicerequest",
    ];

    /// <summary><c>sprk_todo</c>'s real column set — note the ABSENT service-request lookup.</summary>
    private static readonly string[] TodoColumns =
    [
        "sprk_regardingmatter",
        "sprk_regardingproject",
        "sprk_regardingworkassignment",
    ];

    private static CoreAncestorResolver.EntityColumnProbe Probe(params string[] columns) =>
        (_, _) => Task.FromResult<IReadOnlySet<string>>(
            new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase));

    private static CoreAncestorResolver.EntityColumnProbe ThrowingProbe() =>
        (_, _) => throw new InvalidOperationException("metadata unavailable");

    private static CoreAncestorResolver Build(
        Mock<IGenericEntityService> entityService,
        CoreAncestorResolver.EntityColumnProbe probe) =>
        new(entityService.Object, probe, NullLogger<CoreAncestorResolver>.Instance);

    private static Mock<IGenericEntityService> EntityServiceReturning(Entity? row)
    {
        var mock = new Mock<IGenericEntityService>(MockBehavior.Loose);
        mock.Setup(s => s.RetrieveAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row!);
        return mock;
    }

    // ---------------------------------------------------------------------
    // Taxonomy — pinned literally, and pinned to the TypeScript side
    // ---------------------------------------------------------------------

    [Fact]
    public void CoreRecordEntities_ArePinnedLiterally()
    {
        // Changing this set changes who can see what. It must fail a test, loudly.
        CoreAncestorResolver.CoreRecordEntities.Should().Equal(
            "sprk_project", "sprk_matter", "sprk_workassignment", "sprk_servicerequest");
    }

    [Fact]
    public void ChildRecordEntities_ArePinnedLiterally()
    {
        CoreAncestorResolver.ChildRecordEntities.Should().Equal(
            "sprk_invoice", "sprk_communication", "sprk_document", "sprk_event", "sprk_todo", "sprk_analysis");
    }

    [Fact]
    public void CoreAndChildSets_AreDisjoint()
    {
        CoreAncestorResolver.CoreRecordEntities
            .Intersect(CoreAncestorResolver.ChildRecordEntities)
            .Should().BeEmpty();
    }

    [Fact]
    public void EveryCoreEntity_HasExactlyOneAncestorLookup()
    {
        // The taxonomy and the lookup table must not drift — the CoreTarget branch indexes into the table.
        CoreAncestorResolver.CoreAncestorLookups.Select(c => c.EntityType)
            .Should().BeEquivalentTo(CoreAncestorResolver.CoreRecordEntities);
    }

    [Fact]
    public void Matter_IsCoreNotChild()
    {
        // If this flips, every Project holder silently gains every Matter beneath it.
        CoreAncestorResolver.IsCoreRecordEntity("sprk_matter").Should().BeTrue();
        CoreAncestorResolver.IsChildRecordEntity("sprk_matter").Should().BeFalse();
    }

    [Theory]
    [InlineData("sprk_budget")]
    [InlineData("sprk_organization")]
    [InlineData("contact")]
    [InlineData("account")]
    [InlineData("sprk_reportcard")]
    public void NonAccessConferringTargets_AreUnclassified(string entity)
    {
        CoreAncestorResolver.IsCoreRecordEntity(entity).Should().BeFalse();
        CoreAncestorResolver.IsChildRecordEntity(entity).Should().BeFalse();
    }

    /// <summary>
    /// Cross-language parity: the C# taxonomy MUST equal the TypeScript taxonomy in
    /// <c>PolymorphicResolverService.ts</c>. Two implementations of one access model drift silently
    /// otherwise — the client stamps one set, the server another, and only some chains resolve.
    /// </summary>
    [Fact]
    public void Taxonomy_MatchesTheTypeScriptSide()
    {
        var tsPath = FindRepoFile(
            "src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts");
        tsPath.Should().NotBeNull(
            "the TypeScript resolver is the parity source; if it moved, this test must be updated, not deleted");

        var ts = File.ReadAllText(tsPath!);

        ParseTsStringArray(ts, "CORE_RECORD_ENTITIES")
            .Should().Equal(CoreAncestorResolver.CoreRecordEntities);
        ParseTsStringArray(ts, "CHILD_RECORD_ENTITIES")
            .Should().Equal(CoreAncestorResolver.ChildRecordEntities);
    }

    // ---------------------------------------------------------------------
    // Derivation
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CoreTarget_StampsItselfWithoutAnyRead()
    {
        var entityService = EntityServiceReturning(null);
        var resolver = Build(entityService, Probe());

        var result = await resolver.ResolveStampsAsync("sprk_matter", MatterId);

        result.Status.Should().Be(CoreAncestorStatus.CoreTarget);
        result.Stamps.Should().ContainSingle()
            .Which.Should().Be(new CoreAncestorStamp("sprk_matter", "sprk_regardingmatter", MatterId));

        // A core target is terminal — no hop is taken at all.
        entityService.Verify(s => s.RetrieveAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MatterTarget_DoesNotStampItsOwnProject()
    {
        // Matter does NOT inherit from Project (design.md §4.3). Even if the matter row carried a project
        // association, derivation must never read it.
        var row = new Entity("sprk_matter");
        row["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId);
        var resolver = Build(EntityServiceReturning(row), Probe(CommunicationColumns));

        var result = await resolver.ResolveStampsAsync("sprk_matter", MatterId);

        result.Stamps.Select(s => s.EntityType).Should().Equal("sprk_matter");
        result.Stamps.Should().NotContain(s => s.EntityType == "sprk_project");
    }

    [Fact]
    public async Task ChildOfChild_DerivesTheMatterAncestor()
    {
        // FR-26 acceptance: a To Do regarding a Communication regarding Matter M must carry M.
        var row = new Entity("sprk_communication");
        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId);
        var resolver = Build(EntityServiceReturning(row), Probe(CommunicationColumns));

        var result = await resolver.ResolveStampsAsync("sprk_communication", CommId);

        result.Status.Should().Be(CoreAncestorStatus.Derived);
        result.Stamps.Should().ContainSingle()
            .Which.Should().Be(new CoreAncestorStamp("sprk_matter", "sprk_regardingmatter", MatterId));
    }

    [Fact]
    public async Task Derivation_TakesExactlyOneHop()
    {
        // Reads the communication once and stops — never follows the matter (ADR-034 1-hop cap).
        var row = new Entity("sprk_communication");
        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId);
        var entityService = EntityServiceReturning(row);
        var resolver = Build(entityService, Probe(CommunicationColumns));

        await resolver.ResolveStampsAsync("sprk_communication", CommId);

        entityService.Verify(s => s.RetrieveAsync(
            "sprk_communication", CommId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
        entityService.Verify(s => s.RetrieveAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnlySelectsAncestorColumnsThatExistOnTheTarget()
    {
        // sprk_todo has no service-request lookup. Requesting it would fault and turn a schema gap into a
        // blocked write.
        string[]? requested = null;
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService.Setup(s => s.RetrieveAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string[], CancellationToken>((_, _, cols, _) => requested = cols)
            .ReturnsAsync(new Entity("sprk_todo"));

        var resolver = Build(entityService, Probe(TodoColumns));
        await resolver.ResolveStampsAsync("sprk_todo", CommId);

        requested.Should().NotBeNull();
        requested.Should().Contain("sprk_regardingmatter");
        requested.Should().NotContain("sprk_regardingservicerequest");
    }

    [Fact]
    public async Task AllCoreLookupsNull_IsNoAncestorNotError()
    {
        // An orphan communication is a legitimate record; it simply confers nothing. This must NOT collapse
        // into Error, which would block the write.
        var resolver = Build(EntityServiceReturning(new Entity("sprk_communication")), Probe(CommunicationColumns));

        var result = await resolver.ResolveStampsAsync("sprk_communication", CommId);

        result.Status.Should().Be(CoreAncestorStatus.NoAncestor);
        result.Stamps.Should().BeEmpty();
        result.Error.Should().BeNull();
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task UnclassifiedTarget_TakesNoReadAndIsNotAnError()
    {
        var entityService = EntityServiceReturning(null);
        var resolver = Build(entityService, Probe(CommunicationColumns));

        var result = await resolver.ResolveStampsAsync("sprk_organization", ProjectId);

        result.Status.Should().Be(CoreAncestorStatus.Unclassified);
        result.Succeeded.Should().BeTrue();
        entityService.Verify(s => s.RetrieveAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadFailure_FailsClosed()
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService.Setup(s => s.RetrieveAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse 503"));

        var result = await Build(entityService, Probe(CommunicationColumns))
            .ResolveStampsAsync("sprk_communication", CommId);

        result.Status.Should().Be(CoreAncestorStatus.Error);
        result.Succeeded.Should().BeFalse();
        result.Stamps.Should().BeEmpty();
        result.Error.Should().Contain("Dataverse 503");
    }

    [Fact]
    public async Task MetadataFailure_FailsClosed()
    {
        // "no core-ancestor columns" and "could not read the metadata" are indistinguishable from an empty
        // column set, so we must not take the optimistic branch.
        var result = await Build(EntityServiceReturning(new Entity("sprk_communication")), ThrowingProbe())
            .ResolveStampsAsync("sprk_communication", CommId);

        result.Status.Should().Be(CoreAncestorStatus.Error);
    }

    [Fact]
    public async Task EmptyTargetId_FailsClosed()
    {
        var result = await Build(EntityServiceReturning(null), Probe(CommunicationColumns))
            .ResolveStampsAsync("sprk_communication", Guid.Empty);

        result.Status.Should().Be(CoreAncestorStatus.Error);
    }

    // ---------------------------------------------------------------------
    // ApplyStamps
    // ---------------------------------------------------------------------

    [Fact]
    public void ApplyStamps_WritesTheDerivedAncestorOntoTheChild()
    {
        var resolver = Build(EntityServiceReturning(null), Probe(TodoColumns));
        var child = new Entity("sprk_todo");
        var result = new CoreAncestorResult(
            CoreAncestorStatus.Derived,
            [new CoreAncestorStamp("sprk_matter", "sprk_regardingmatter", MatterId)],
            null);

        var unstampable = resolver.ApplyStamps(
            child, result, new HashSet<string>(TodoColumns, StringComparer.OrdinalIgnoreCase));

        unstampable.Should().BeEmpty();
        child.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
    }

    [Fact]
    public void ApplyStamps_SurfacesAnAncestorTheHostCannotStoreInsteadOfSwallowingIt()
    {
        // sprk_todo has no sprk_regardingservicerequest, so a service-request ancestor cannot be stamped.
        // That is a real hole in child inheritance and must be reported, not silently dropped.
        var resolver = Build(EntityServiceReturning(null), Probe(TodoColumns));
        var child = new Entity("sprk_todo");
        var result = new CoreAncestorResult(
            CoreAncestorStatus.Derived,
            [new CoreAncestorStamp("sprk_servicerequest", "sprk_regardingservicerequest", SrId)],
            null);

        var unstampable = resolver.ApplyStamps(
            child, result, new HashSet<string>(TodoColumns, StringComparer.OrdinalIgnoreCase));

        unstampable.Should().Equal("sprk_regardingservicerequest");
        child.Contains("sprk_regardingservicerequest").Should().BeFalse();
    }

    [Fact]
    public void ApplyStamps_SkipsTheDirectlyBoundTarget()
    {
        // The caller already wrote the chosen target's own lookup; re-writing it is redundant.
        var resolver = Build(EntityServiceReturning(null), Probe(TodoColumns));
        var child = new Entity("sprk_todo");
        var result = new CoreAncestorResult(
            CoreAncestorStatus.CoreTarget,
            [new CoreAncestorStamp("sprk_matter", "sprk_regardingmatter", MatterId)],
            null);

        resolver.ApplyStamps(
            child, result, new HashSet<string>(TodoColumns, StringComparer.OrdinalIgnoreCase), "sprk_matter");

        child.Contains("sprk_regardingmatter").Should().BeFalse();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Walk up from the test assembly to the repo root and resolve a repo-relative path.</summary>
    private static string? FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Extract the string literals from an exported TS `const NAME: ... = [ 'a', 'b' ]` array.</summary>
    private static IReadOnlyList<string> ParseTsStringArray(string source, string constName)
    {
        var match = Regex.Match(
            source,
            $@"export\s+const\s+{Regex.Escape(constName)}\s*:[^=]*=\s*\[(?<body>[^\]]*)\]",
            RegexOptions.Singleline);
        match.Success.Should().BeTrue($"{constName} must exist in the TypeScript resolver");

        return Regex.Matches(match.Groups["body"].Value, @"'([^']+)'")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }
}
