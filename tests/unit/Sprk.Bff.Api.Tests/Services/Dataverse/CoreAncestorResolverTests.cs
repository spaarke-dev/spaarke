using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
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
            "sprk_invoice", "sprk_communication", "sprk_document", "sprk_event", "sprk_todo", "sprk_analysis", "sprk_memo");
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

    /// <summary>
    /// Task 147: the stamp backfill script carries a THIRD copy of the taxonomy (it says it "MUST mirror" the resolvers).
    /// Nothing pinned it, so the memo could have joined both resolvers and stayed out of the backfill. The script's
    /// <c>$CoreEntities</c> / <c>$ChildEntities</c> literals and its default <c>-Entities</c> (every child) are pinned here.
    /// </summary>
    [Fact]
    public void Taxonomy_MatchesTheStampBackfillScript()
    {
        var scriptPath = FindRepoFile("scripts/Backfill-CoreAncestorStamps.ps1");
        scriptPath.Should().NotBeNull("the stamp backfill is the third copy of the taxonomy; if it moved, update this test");
        var script = File.ReadAllText(scriptPath!);

        ParsePsStringArray(script, @"\$CoreEntities\s*=\s*@\(([^)]*)\)")
            .Should().Equal(CoreAncestorResolver.CoreRecordEntities);
        ParsePsStringArray(script, @"\$ChildEntities\s*=\s*@\(([^)]*)\)")
            .Should().Equal(CoreAncestorResolver.ChildRecordEntities);
        ParsePsStringArray(script, @"\[string\[\]\]\$Entities\s*=\s*@\(([^)]*)\)")
            .Should().BeEquivalentTo(CoreAncestorResolver.ChildRecordEntities, "a dry run with no -Entities covers every child");
    }

    private static IReadOnlyList<string> ParsePsStringArray(string source, string pattern)
    {
        var match = Regex.Match(source, pattern);
        match.Success.Should().BeTrue($"the script must declare {pattern}");
        return Regex.Matches(match.Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>
    /// Cross-language parity (task 169): the TypeScript <c>INTERMEDIATE_ROOT_COLUMNS</c>, which the
    /// RegardingResolver PCF's <c>deriveCoreAncestorStamps</c> reads to stamp a child at save time, MUST equal
    /// <see cref="CoreAncestorResolver.IntermediateRootColumns"/> row for row. If they drift, the client previews
    /// a different access answer from the one the server owns (ADR-002 WP-2), and nothing else fails.
    /// </summary>
    /// <remarks>
    /// The TypeScript table is PARSED, so its shape is pinned too: a flat array of single-quoted object literals.
    /// A row the parser cannot see (a spread, a referenced constant, a helper call, a row hidden in a comment) would
    /// let the runtime table differ from what this test compares, so any such content fails the test.
    /// </remarks>
    [Fact]
    public void IntermediateRootColumns_MatchTheTypeScriptSide()
    {
        var ts = ReadTypeScriptResolver();
        var body = ParseTsArrayBody(ts, "INTERMEDIATE_ROOT_COLUMNS");

        AssertPlainLiteralArray(body, "INTERMEDIATE_ROOT_COLUMNS");

        var rowPattern = new Regex(
            @"\{\s*intermediate\s*:\s*'(?<intermediate>[^']*)'\s*,\s*column\s*:\s*'(?<column>[^']*)'\s*,"
            + @"\s*rootEntity\s*:\s*'(?<root>[^']*)'\s*,?\s*\}",
            RegexOptions.Singleline);
        var matches = rowPattern.Matches(body);

        body.Count(c => c == '{').Should().Be(matches.Count,
            "every object literal in INTERMEDIATE_ROOT_COLUMNS must be a row the parser reads "
            + "({ intermediate: '…', column: '…', rootEntity: '…' }, in that order)");
        Regex.Replace(rowPattern.Replace(body, string.Empty), @"[\s,]", string.Empty)
            .Should().BeEmpty(
                "INTERMEDIATE_ROOT_COLUMNS may contain only literal rows; anything else (a referenced constant, a "
                + "helper call) is a row the runtime has and this test cannot see");

        var tsRows = matches
            .Select(m => (
                Intermediate: m.Groups["intermediate"].Value.ToLowerInvariant(),
                Column: m.Groups["column"].Value.ToLowerInvariant(),
                Root: m.Groups["root"].Value.ToLowerInvariant()))
            .ToList();
        var csRows = CoreAncestorResolver.IntermediateRootColumns
            .SelectMany(kv => kv.Value.Select(r => (
                Intermediate: kv.Key.ToLowerInvariant(),
                Column: r.Column.ToLowerInvariant(),
                Root: r.RootEntity.ToLowerInvariant())))
            .ToList();

        tsRows.Should().OnlyHaveUniqueItems("a duplicated TypeScript row hides a missing one from a count check");
        tsRows.Should().HaveCount(csRows.Count);
        tsRows.Should().BeEquivalentTo(csRows, options => options.WithStrictOrdering(),
            "the TypeScript stamp derivation must read exactly the server's intermediate table, in the same order "
            + "(ebilling-r1 026 fix-round-1 item 6: each intermediate's root columns are read and reported in this order "
            + "on both sides, so a reordering on one side is a drift too)");
    }

    /// <summary>
    /// Cross-language parity (task 169): the TypeScript <c>CORE_ANCESTOR_LOOKUPS</c> root → stamp-column pairs MUST
    /// equal <see cref="CoreAncestorResolver.CoreAncestorLookups"/>. The client mirror writes every derived stamp
    /// through that table, so a drift there stamps the right root onto the wrong column.
    /// </summary>
    [Fact]
    public void CoreAncestorLookups_MatchTheTypeScriptSide()
    {
        var ts = ReadTypeScriptResolver();
        var body = ParseTsArrayBody(ts, "CORE_ANCESTOR_LOOKUPS");

        var rows = Regex.Matches(body, @"\{(?<row>[^{}]*)\}", RegexOptions.Singleline)
            .Select(m => m.Groups["row"].Value)
            .ToList();
        body.Count(c => c == '{').Should().Be(rows.Count, "every CORE_ANCESTOR_LOOKUPS entry must be an object literal");
        body.Should().NotContain("...", "a spread entry is invisible to this parser");

        var tsPairs = rows
            .Select(row =>
            {
                var entityType = Regex.Match(row, @"entityType\s*:\s*'(?<v>[^']*)'");
                var lookupAttribute = Regex.Match(row, @"lookupAttribute\s*:\s*'(?<v>[^']*)'");
                entityType.Success.Should().BeTrue($"every CORE_ANCESTOR_LOOKUPS row needs a literal entityType: {row}");
                lookupAttribute.Success.Should().BeTrue(
                    $"every CORE_ANCESTOR_LOOKUPS row needs a literal lookupAttribute: {row}");
                return (EntityType: entityType.Groups["v"].Value.ToLowerInvariant(),
                    LookupAttribute: lookupAttribute.Groups["v"].Value.ToLowerInvariant());
            })
            .ToList();
        var csPairs = CoreAncestorResolver.CoreAncestorLookups
            .Select(l => (EntityType: l.EntityType.ToLowerInvariant(), LookupAttribute: l.LookupAttribute.ToLowerInvariant()))
            .ToList();

        tsPairs.Should().HaveCount(csPairs.Count);
        tsPairs.Should().BeEquivalentTo(csPairs);
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

    /// <summary>
    /// Task 147 (owner round 2 item 6): a record filed under a MEMO derives the memo's own core ancestor. Before
    /// <c>sprk_memo</c> joined the CHILD set, a memo target read as Unclassified and its project was lost.
    /// </summary>
    [Fact]
    public async Task MemoTarget_DerivesTheMemosOwnProjectAncestor()
    {
        var row = new Entity("sprk_memo");
        row["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId);
        var resolver = Build(EntityServiceReturning(row), Probe(CommunicationColumns));

        var result = await resolver.ResolveStampsAsync("sprk_memo", CommId);

        result.Status.Should().Be(CoreAncestorStatus.Derived);
        result.Stamps.Should().ContainSingle()
            .Which.Should().Be(new CoreAncestorStamp("sprk_project", "sprk_regardingproject", ProjectId));
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

    // GitHub #2002 — "carries no core-ancestor stamp; a record written against it will inherit no access" is a Warning only
    // when a record is written against the target. A read-only resolution (a reconciliation scan) writes nothing.

    [Theory]
    [InlineData(CoreAncestorIntent.Write, true)]
    [InlineData(CoreAncestorIntent.ReadOnly, false)]
    public async Task NoAncestor_WarnsOnlyWhenARecordIsWritten(CoreAncestorIntent intent, bool warns)
    {
        var log = new CapturingLogger();
        var resolver = new CoreAncestorResolver(
            EntityServiceReturning(new Entity("sprk_communication")).Object, Probe(CommunicationColumns), log);

        var result = await resolver.ResolveStampsAsync("sprk_communication", CommId, intent: intent);

        result.Status.Should().Be(CoreAncestorStatus.NoAncestor, "the intent never changes the answer");
        result.Succeeded.Should().BeTrue();
        log.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("carries no core-ancestor stamp"))
            .Should().Be(warns);
        log.Entries.Any(e => e.Level == LogLevel.Debug && e.Message.Contains("carries no core-ancestor stamp"))
            .Should().Be(!warns, "the same condition is still recorded, at Debug, on a read-only resolution");
    }

    [Fact]
    public async Task NoAncestor_DefaultIntentIsWrite_SoAnUpdatedCallerKeepsTheWarning()
    {
        var log = new CapturingLogger();
        var resolver = new CoreAncestorResolver(
            EntityServiceReturning(new Entity("sprk_communication")).Object, Probe(CommunicationColumns), log);

        await resolver.ResolveStampsAsync("sprk_communication", CommId);

        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("carries no core-ancestor stamp"));
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
    // Task 156 — a child's stamp is the root of WHATEVER it is filed under
    // ---------------------------------------------------------------------

    [Theory(DisplayName = "Task 156: invoice / budget / document derive their root from their TYPED root columns, not sprk_regarding{core}")]
    [InlineData("sprk_invoice", "sprk_matter")]
    [InlineData("sprk_budget", "sprk_matter")]
    [InlineData("sprk_document", "sprk_matter")]
    [InlineData("sprk_document", "sprk_relatedmatter")]
    public async Task TypedRootIntermediates_DeriveTheirRoot(string target, string matterColumn)
    {
        // Until task 156 a to-do filed under one of these carried NO stamp (none of them has the four
        // sprk_regarding{core} columns), so it inherited nothing from the matter above it — and the storage resolver had
        // nothing to compare. Owner round 4 item 5: the copy must equal the root of whatever the child is filed under.
        var row = new Entity(target, CommId) { [matterColumn] = new EntityReference("sprk_matter", MatterId) };
        var columns = CoreAncestorResolver.IntermediateRootColumns[target].Select(c => c.Column).ToArray();

        var result = await Build(EntityServiceReturning(row), Probe(columns)).ResolveStampsAsync(target, CommId);

        result.Status.Should().Be(CoreAncestorStatus.Derived);
        result.Stamps.Should().ContainSingle()
            .Which.Should().Be(new CoreAncestorStamp("sprk_matter", "sprk_regardingmatter", MatterId),
                "the stamp lands on the CHILD's sprk_regardingmatter, whatever the intermediate's own column is called");
    }

    [Fact(DisplayName = "ebilling-r1 task 013: an invoice filed under a work assignment (sprk_regardingworkassignment) is an intermediate root column")]
    public async Task InvoiceFiledUnderAWorkAssignment_DerivesTheWorkAssignment()
    {
        // Task 012 added sprk_invoice.sprk_regardingworkassignment (FR-05). Without it in the invoice's root columns a to-do
        // filed under a work-assignment invoice carries no stamp (a silent FR-26 under-grant).
        var waId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var row = new Entity("sprk_invoice", CommId)
        {
            ["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", waId),
        };

        CoreAncestorResolver.IntermediateRootColumns["sprk_invoice"].Should().BeEquivalentTo(
            new[]
            {
                ("sprk_project", "sprk_project"), ("sprk_matter", "sprk_matter"),
                ("sprk_regardingworkassignment", "sprk_workassignment"),
            });

        var result = await Build(EntityServiceReturning(row), Probe("sprk_project", "sprk_matter", "sprk_regardingworkassignment"))
            .ResolveStampsAsync("sprk_invoice", CommId);

        result.Status.Should().Be(CoreAncestorStatus.Derived);
        result.Stamps.Should().ContainSingle()
            .Which.Should().Be(new CoreAncestorStamp("sprk_workassignment", "sprk_regardingworkassignment", waId));
    }

    // ---------------------------------------------------------------------
    // ebilling-r1 task 026 (FR-05, FR-15, D-75, D-94) — the Invoice Line is an intermediate with ONE root
    // ---------------------------------------------------------------------

    private static readonly Guid LineId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly string[] LineColumns =
        ["sprk_project", "sprk_matter", "sprk_regardingworkassignment", "sprk_invoice", "sprk_resolvedtimekeeper"];

    [Fact(DisplayName = "ebilling-r1 task 026 (D-94): the line's root columns are exactly the guard's core lookups, never sprk_invoice")]
    public void InvoiceLine_RootColumns_AreItsOwnCoreLookups()
    {
        CoreAncestorResolver.IntermediateRootColumns["sprk_billingevent"].Should().BeEquivalentTo(
            InvoiceFamilyCoreRecordGuard.CoreRecordLookups["sprk_billingevent"].Select(l => (l.Column, l.CoreEntity)),
            "the stamp and the FR-05 guard read the same columns, so a line's Signal groups under the core record the guard accepted");
        CoreAncestorResolver.IntermediateRootColumns["sprk_billingevent"].Select(c => c.Column)
            .Should().NotContain(["sprk_invoice", "sprk_resolvedtimekeeper"]);
        CoreAncestorResolver.SingleRootIntermediates.Should().BeEquivalentTo(["sprk_billingevent"]);
        CoreAncestorResolver.IsChildRecordEntity("sprk_billingevent").Should().BeFalse("the access taxonomy is untouched");
    }

    [Theory(DisplayName = "ebilling-r1 task 026: a line derives the root its OWN lookup names (project, matter or work assignment)")]
    [InlineData("sprk_project", "sprk_project", "sprk_regardingproject")]
    [InlineData("sprk_matter", "sprk_matter", "sprk_regardingmatter")]
    [InlineData("sprk_regardingworkassignment", "sprk_workassignment", "sprk_regardingworkassignment")]
    public async Task InvoiceLine_DerivesItsOwnRoot(string column, string target, string stampColumn)
    {
        var rootId = Guid.NewGuid();
        var row = new Entity("sprk_billingevent", LineId) { [column] = new EntityReference(target, rootId) };
        var service = EntityServiceReturning(row);

        var result = await Build(service, Probe(LineColumns)).ResolveStampsAsync("sprk_billingevent", LineId);

        result.Status.Should().Be(CoreAncestorStatus.Derived);
        result.Stamps.Should().ContainSingle().Which.Should().Be(new CoreAncestorStamp(target, stampColumn, rootId));
        service.Verify(s => s.RetrieveAsync("sprk_billingevent", LineId,
            It.Is<string[]>(c => !c.Contains("sprk_invoice") && !c.Contains("sprk_resolvedtimekeeper")),
            It.IsAny<CancellationToken>()), Times.Once, "the line's invoice is never read for its root (D-75)");
    }

    [Fact(DisplayName = "ebilling-r1 task 026: a line whose only lookup is its invoice has NO ancestor (the invoice's matter is never borrowed)")]
    public async Task InvoiceLine_WithOnlyItsInvoice_IsNoAncestor()
    {
        var row = new Entity("sprk_billingevent", LineId) { ["sprk_invoice"] = new EntityReference("sprk_invoice", CommId) };

        var result = await Build(EntityServiceReturning(row), Probe(LineColumns)).ResolveStampsAsync("sprk_billingevent", LineId);

        result.Status.Should().Be(CoreAncestorStatus.NoAncestor);
        result.Stamps.Should().BeEmpty();
    }

    [Fact(DisplayName = "ebilling-r1 task 026 (D-94 condition 3): a line naming a matter AND a project is a permanent derivation error, never two stamps")]
    public async Task InvoiceLine_WithTwoRoots_IsRefused()
    {
        var row = new Entity("sprk_billingevent", LineId)
        {
            ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_project"] = new EntityReference("sprk_project", ProjectId),
        };

        var result = await Build(EntityServiceReturning(row), Probe(LineColumns)).ResolveStampsAsync("sprk_billingevent", LineId);

        result.Status.Should().Be(CoreAncestorStatus.Error);
        result.IsPermanent.Should().BeTrue("a two-root line is a data condition, not a fault to retry");
        result.Stamps.Should().BeEmpty();
    }

    [Fact(DisplayName = "ebilling-r1 task 026: the single-root rule is the line's only; an invoice naming a matter and a project still derives both")]
    public async Task Invoice_WithTwoRoots_StillDerivesBoth()
    {
        var row = new Entity("sprk_invoice", CommId)
        {
            ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_project"] = new EntityReference("sprk_project", ProjectId),
        };

        var result = await Build(EntityServiceReturning(row), Probe("sprk_project", "sprk_matter", "sprk_regardingworkassignment"))
            .ResolveStampsAsync("sprk_invoice", CommId);

        result.Status.Should().Be(CoreAncestorStatus.Derived);
        result.Stamps.Should().HaveCount(2);
    }

    [Fact(DisplayName = "ebilling-r1 task 026: a Signal / Decision Record filed under a line takes its stamp from sprk_regardinginvoiceline")]
    public void InvoiceLine_IsAStampSourceOfTheOntologyRecords()
    {
        foreach (var host in new[] { "sprk_signal", "sprk_decisionrecord" })
        {
            CoreAncestorResolver.StampSourceColumns[host]
                .Should().Contain(("sprk_regardinginvoiceline", "sprk_billingevent"), host);
        }
    }

    // ── ebilling-r1 task 026 fix-round-1: the refusal kind (additive; every existing caller's result is unchanged) ──

    private static Mock<IGenericEntityService> EntityServiceThrowing(Exception ex)
    {
        var mock = new Mock<IGenericEntityService>(MockBehavior.Loose);
        mock.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);
        return mock;
    }

    private static Exception NotFoundFault() =>
        new System.ServiceModel.FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) }, "Does Not Exist");

    [Fact(DisplayName = "ebilling-r1 task 026 fix-round-1: each refusal names its kind — MultipleRoots / NotFound permanent, Transient not")]
    public async Task Refusals_CarryTheirKind_AndTheSamePermanenceAsBefore()
    {
        var lineTwoRoots = new Entity("sprk_billingevent", LineId)
        {
            ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_project"] = new EntityReference("sprk_project", ProjectId),
        };
        var documentTwoMatters = new Entity("sprk_document", CommId)
        {
            ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_relatedmatter"] = new EntityReference("sprk_matter", Guid.NewGuid()),
        };

        var cases = new (string Name, CoreAncestorResult Result, CoreAncestorRefusalKind Kind, bool Permanent)[]
        {
            ("line two roots", await Build(EntityServiceReturning(lineTwoRoots), Probe(LineColumns)).ResolveStampsAsync("sprk_billingevent", LineId),
                CoreAncestorRefusalKind.MultipleRoots, true),
            ("document two matters", await Build(EntityServiceReturning(documentTwoMatters), Probe("sprk_matter", "sprk_relatedmatter"))
                .ResolveStampsAsync("sprk_document", CommId), CoreAncestorRefusalKind.MultipleRoots, true),
            ("not found", await Build(EntityServiceThrowing(NotFoundFault()), Probe(CommunicationColumns)).ResolveStampsAsync("sprk_communication", CommId),
                CoreAncestorRefusalKind.NotFound, true),
            ("null row", await Build(EntityServiceReturning(null), Probe(CommunicationColumns)).ResolveStampsAsync("sprk_communication", CommId),
                CoreAncestorRefusalKind.NotFound, true),
            ("read fault", await Build(EntityServiceThrowing(new InvalidOperationException("Dataverse 503")), Probe(CommunicationColumns))
                .ResolveStampsAsync("sprk_communication", CommId), CoreAncestorRefusalKind.Transient, false),
            ("metadata fault", await Build(EntityServiceReturning(null), ThrowingProbe()).ResolveStampsAsync("sprk_communication", CommId),
                CoreAncestorRefusalKind.Transient, false),
            ("empty id", await Build(EntityServiceReturning(null), Probe(CommunicationColumns)).ResolveStampsAsync("sprk_communication", Guid.Empty),
                CoreAncestorRefusalKind.Transient, false),
        };

        foreach (var (name, result, kind, permanent) in cases)
        {
            result.Status.Should().Be(CoreAncestorStatus.Error, name);
            result.Stamps.Should().BeEmpty(name);
            result.IsPermanent.Should().Be(permanent, name);
            result.RefusalKind.Should().Be(kind, name);
        }
    }

    [Theory(DisplayName = "ebilling-r1 task 026 fix-round-1: a successful derivation carries no refusal kind (core, intermediate, no-ancestor, unclassified)")]
    [InlineData("sprk_matter", CoreAncestorStatus.CoreTarget)]
    [InlineData("sprk_project", CoreAncestorStatus.CoreTarget)]
    [InlineData("sprk_workassignment", CoreAncestorStatus.CoreTarget)]
    [InlineData("sprk_communication", CoreAncestorStatus.Derived)]
    [InlineData("sprk_todo", CoreAncestorStatus.NoAncestor)]
    [InlineData("contact", CoreAncestorStatus.Unclassified)]
    public async Task Successes_CarryNoRefusalKind(string target, CoreAncestorStatus status)
    {
        var row = new Entity(target, CommId);
        if (status == CoreAncestorStatus.Derived) row["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId);

        var result = await Build(EntityServiceReturning(row), Probe(CommunicationColumns)).ResolveStampsAsync(target, CommId);

        result.Status.Should().Be(status);
        result.IsPermanent.Should().BeFalse();
        result.RefusalKind.Should().BeNull();
    }

    [Fact(DisplayName = "ebilling-r1 task 026 fix-round-2 (uac-r2 K4): a permanent error with no kind is never labelled Transient")]
    public void StampFailureOf_APermanentErrorWithNoKind_IsNeverTransient_ATransientOneIs_AKindIsKept()
    {
        // Built with the public constructor: no RefusalKind. Permanent means a retry does not change it.
        var permanent = CoreAncestorResolver.StampFailureOf(
            new CoreAncestorResult(CoreAncestorStatus.Error, [], "gone") { IsPermanent = true });
        var transient = CoreAncestorResolver.StampFailureOf(new CoreAncestorResult(CoreAncestorStatus.Error, [], "timeout"));
        var kinded = CoreAncestorResolver.StampFailureOf(CoreAncestorResult.Refused("two roots", CoreAncestorRefusalKind.MultipleRoots));

        permanent.IsPermanent.Should().BeTrue();
        permanent.RefusalKind.Should().BeNull("a permanent failure is not a transient fault; its kind is unknown");
        permanent.Error.Should().Be("gone");
        transient.IsPermanent.Should().BeFalse();
        transient.RefusalKind.Should().Be(CoreAncestorRefusalKind.Transient);
        kinded.RefusalKind.Should().Be(CoreAncestorRefusalKind.MultipleRoots);
        kinded.IsPermanent.Should().BeTrue();
    }

    [Fact(DisplayName = "ebilling-r1 task 026 fix-round-1: StampAsync carries the refusal kind and permanence through to the writer")]
    public async Task StampAsync_CarriesTheRefusalKind()
    {
        var lineTwoRoots = new Entity("sprk_billingevent", LineId)
        {
            ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", Guid.NewGuid()),
        };

        var outcome = await Build(EntityServiceReturning(lineTwoRoots), Probe(LineColumns))
            .StampAsync(new Entity("sprk_signal"), "sprk_billingevent", LineId, default);
        var fault = await Build(EntityServiceThrowing(new InvalidOperationException("503")), Probe(LineColumns))
            .StampAsync(new Entity("sprk_signal"), "sprk_billingevent", LineId, default);

        outcome.Succeeded.Should().BeFalse();
        outcome.IsPermanent.Should().BeTrue();
        outcome.RefusalKind.Should().Be(CoreAncestorRefusalKind.MultipleRoots);
        fault.IsPermanent.Should().BeFalse();
        fault.RefusalKind.Should().Be(CoreAncestorRefusalKind.Transient);
    }

    [Theory(DisplayName = "Task 156: agreement and report card derive from their own sprk_regardingmatter / sprk_regardingproject (they were Unclassified)")]
    [InlineData("sprk_agreement")]
    [InlineData("sprk_reportcard")]
    public async Task AgreementAndReportCard_DeriveTheirRoot(string target)
    {
        var row = new Entity(target, CommId) { ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId) };

        var result = await Build(EntityServiceReturning(row), Probe("sprk_regardingmatter", "sprk_regardingproject"))
            .ResolveStampsAsync(target, CommId);

        result.Status.Should().Be(CoreAncestorStatus.Derived);
        result.Stamps.Should().ContainSingle()
            .Which.Should().Be(new CoreAncestorStamp("sprk_project", "sprk_regardingproject", ProjectId));
        CoreAncestorResolver.IsChildRecordEntity(target).Should().BeFalse(
            "the ACCESS taxonomy is untouched — only what a child filed under it is stamped with changes");
    }

    [Fact(DisplayName = "Task 156: a document naming two DIFFERENT matters (sprk_matter and sprk_relatedmatter) is a derivation ERROR — never a guess")]
    public async Task TwoDifferentRootsOfOneType_FailClosed()
    {
        var row = new Entity("sprk_document", CommId)
        {
            ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_relatedmatter"] = new EntityReference("sprk_matter", Guid.NewGuid()),
        };

        var result = await Build(EntityServiceReturning(row), Probe("sprk_matter", "sprk_relatedmatter"))
            .ResolveStampsAsync("sprk_document", CommId);

        result.Status.Should().Be(CoreAncestorStatus.Error);
        result.Stamps.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 156: the stamp-source topology names only tables that carry the stamp, and only intermediates with known root columns")]
    public void StampSourceTopology_IsClosed()
    {
        // Ontology task 144 (uac-r2 option A): the one named exemption, pinned both ways.
        Sprk.Bff.Api.Tests.AccessControl.StampedOutsideAccessTaxonomyPin.AssertPinned();

        foreach (var (table, sources) in CoreAncestorResolver.StampSourceColumns)
        {
            CoreAncestorResolver.IsChildRecordEntity(table).Should().Be(!CoreAncestorResolver.IsStampedOutsideAccessTaxonomy(table),
                $"{table} carries a stamp, so it is a child — unless CoreAncestorResolver.StampedOutsideAccessTaxonomy names it");
            sources.Select(s => s.Intermediate).Should().OnlyContain(i => CoreAncestorResolver.IsStampSourceEntity(i),
                $"every record {table} can be filed under must have known root columns, or its copy cannot be checked");
        }
    }

    /// <summary>
    /// Task 147 r1 (verifier item 1): since task 156 C# derivation keys on <see cref="CoreAncestorResolver.IntermediateRootColumns"/>,
    /// while the TypeScript side (until task 169 lands) derives every <c>CHILD_RECORD_ENTITIES</c> target through the four
    /// <c>sprk_regarding{core}</c> columns. A CHILD type missing from the C# map reads Unclassified on the server and is
    /// derived in the browser — the two sides disagree and no taxonomy parity test sees it. This pins the C# precondition:
    /// every CHILD is an intermediate here, and every CHILD whose root columns are the four stamp columns carries exactly
    /// the four the TypeScript side reads.
    /// </summary>
    [Fact(DisplayName = "Task 147 r1: every CHILD taxonomy entity is a C# intermediate, so no CHILD target reads Unclassified on the server while the browser derives it")]
    public void EveryChildTaxonomyEntity_IsAnIntermediate()
    {
        CoreAncestorResolver.ChildRecordEntities
            .Should().OnlyContain(e => CoreAncestorResolver.IsStampSourceEntity(e),
                "a record filed under any CHILD type must be derived on the server, as it is in the browser");
    }

    [Fact(DisplayName = "Task 147 r1: a memo's root columns are exactly the four stamp columns (what the TypeScript side reads for a CHILD target)")]
    public void Memo_RootColumns_AreTheFourStampColumns()
    {
        CoreAncestorResolver.IntermediateRootColumns["sprk_memo"]
            .Should().BeEquivalentTo(CoreAncestorResolver.CoreAncestorLookups.Select(l => (l.LookupAttribute, l.EntityType)));
    }

    // ClassifyStampSource — the ONE rule the cascade, the reconciliation job and the storage resolver share.

    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ContactId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Entity Todo(string? pairId, params (string Column, string Target, Guid Id)[] lookups)
    {
        var row = new Entity("sprk_todo", Guid.NewGuid());
        foreach (var (column, target, id) in lookups) row[column] = new EntityReference(target, id);
        if (pairId is not null) row["sprk_regardingrecordid"] = pairId;
        return row;
    }

    [Fact(DisplayName = "Task 156 classify: no intermediate set → not filed under one (its root columns are its own)")]
    public void Classify_NoIntermediate()
        => CoreAncestorResolver.ClassifyStampSource("sprk_todo", Todo(null, ("sprk_regardingmatter", "sprk_matter", MatterId)))
            .Kind.Should().Be(StampSourceKind.NotFiledUnderAnIntermediate);

    [Fact(DisplayName = "Task 156 classify: the pair names the intermediate → it is the source")]
    public void Classify_PairNamesTheIntermediate()
    {
        var decision = CoreAncestorResolver.ClassifyStampSource("sprk_todo", Todo(CommId.ToString(),
            ("sprk_regardingcommunication", "sprk_communication", CommId), ("sprk_regardingmatter", "sprk_matter", MatterId)));

        decision.Kind.Should().Be(StampSourceKind.Source);
        decision.Source!.Id.Should().Be(CommId);
    }

    [Fact(DisplayName = "Task 156 classify: the pair names the ROOT → a direct, user-chosen link; the intermediate is a carrier")]
    public void Classify_PairNamesTheRoot()
    {
        var decision = CoreAncestorResolver.ClassifyStampSource("sprk_todo", Todo(MatterId.ToString(),
            ("sprk_regardingcommunication", "sprk_communication", CommId), ("sprk_regardingmatter", "sprk_matter", MatterId)));

        decision.Kind.Should().Be(StampSourceKind.DirectRootLink);
        decision.Carriers.Should().ContainSingle().Which.Id.Should().Be(CommId);
    }

    [Fact(DisplayName = "Task 156 classify: no pair and ONE intermediate → it is the source (TaskActionCore writes no pair)")]
    public void Classify_NoPairOneIntermediate()
        => CoreAncestorResolver.ClassifyStampSource("sprk_todo", Todo(null,
                ("sprk_regardingcommunication", "sprk_communication", CommId), ("sprk_regardingmatter", "sprk_matter", MatterId)))
            .Kind.Should().Be(StampSourceKind.Source);

    [Fact(DisplayName = "Task 156 classify: no pair and TWO intermediates → ambiguous; a pair naming its own typed party counts as no pair")]
    public void Classify_NoPairTwoIntermediates()
    {
        var row = Todo(ContactId.ToString(),
            ("sprk_regardingcommunication", "sprk_communication", CommId), ("sprk_regardingevent", "sprk_event", EventId),
            ("sprk_regardingcontact", "contact", ContactId));

        CoreAncestorResolver.ClassifyStampSource("sprk_todo", row, ["sprk_regardingcontact", "sprk_regardingorganization"])
            .Kind.Should().Be(StampSourceKind.AmbiguousSource);
    }

    [Theory(DisplayName = "Task 156 classify: a pair naming nothing on the row, or not a GUID → inconsistent (nothing is written from it)")]
    [InlineData("4f4f4f4f-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    public void Classify_InconsistentPair(string pair)
        => CoreAncestorResolver.ClassifyStampSource("sprk_todo", Todo(pair,
                ("sprk_regardingcommunication", "sprk_communication", CommId)))
            .Kind.Should().Be(StampSourceKind.InconsistentPair);

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
    // StampAsync — the single call every converged writer makes (task 052)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task StampAsync_WhenTheHostColumnProbeFails_FailsClosedRatherThanReportingEverythingUnstampable()
    {
        // The subtle one. Derivation SUCCEEDS here — the ancestor is known. If an unreadable host column
        // set were treated as "no columns", every stamp would fall into `unstampable`, which is a warning,
        // not a failure — and the writer would cheerfully create a child with no inherited access while
        // logging that it could not store it. That is a total inheritance loss disguised as a schema gap,
        // so the host probe fails the operation exactly like the target probe does.
        var row = new Entity("sprk_communication", CommId);
        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId);

        var calls = 0;
        CoreAncestorResolver.EntityColumnProbe probe = (entity, _) =>
        {
            // First call is the TARGET (sprk_communication) and succeeds; the second is the HOST.
            calls++;
            return calls == 1
                ? Task.FromResult<IReadOnlySet<string>>(
                    new HashSet<string>(CommunicationColumns, StringComparer.OrdinalIgnoreCase))
                : throw new InvalidOperationException($"metadata unavailable for {entity}");
        };

        var resolver = Build(EntityServiceReturning(row), probe);
        var child = new Entity("sprk_todo");

        var outcome = await resolver.StampAsync(child, "sprk_communication", CommId);

        outcome.Succeeded.Should().BeFalse();
        outcome.Status.Should().Be(CoreAncestorStatus.Error);
        child.Attributes.Should().BeEmpty("a failed stamp must leave the payload untouched");
    }

    [Fact]
    public async Task StampAsync_WhenTheHostCannotStoreADerivedAncestor_ReportsItUnstampableAndStillSucceeds()
    {
        // F-050-2, server side. sprk_todo has no sprk_regardingservicerequest column, so a to-do filed
        // under a communication anchored to a Service Request cannot inherit that access. This is a SCHEMA
        // gap, not a runtime failure: surface it (so the owner can close it by adding the column) and let
        // the write proceed. Failing here would turn a known, bounded hole into an outage.
        var row = new Entity("sprk_communication", CommId);
        row["sprk_regardingservicerequest"] = new EntityReference("sprk_servicerequest", SrId);

        var calls = 0;
        CoreAncestorResolver.EntityColumnProbe probe = (_, _) =>
        {
            calls++;
            var columns = calls == 1 ? CommunicationColumns : TodoColumns; // target, then host
            return Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase));
        };

        var resolver = Build(EntityServiceReturning(row), probe);
        var child = new Entity("sprk_todo");

        var outcome = await resolver.StampAsync(child, "sprk_communication", CommId);

        outcome.Succeeded.Should().BeTrue();
        outcome.Unstampable.Should().ContainSingle().Which.Should().Be("sprk_regardingservicerequest");
        child.Contains("sprk_regardingservicerequest").Should().BeFalse(
            "writing a column sprk_todo does not have would fault the whole create");
    }

    // ---------------------------------------------------------------------
    // StampAsync(..., alreadyDerived) — ontology task 042 (K2 reuse; uac-r2 re-check F1/F2)
    // ---------------------------------------------------------------------

    private static Entity CommunicationFiledUnderTheMatter()
    {
        var row = new Entity("sprk_communication", CommId);
        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId);
        return row;
    }

    private static int Reads(Mock<IGenericEntityService> service) =>
        service.Invocations.Count(i => i.Method.Name == nameof(IGenericEntityService.RetrieveAsync));

    [Fact]
    public async Task StampAsync_WithAFailedAlreadyDerived_DerivesItself_ReadsOnce_AndAppliesTheStamps()
    {
        var service = EntityServiceReturning(CommunicationFiledUnderTheMatter());
        var resolver = Build(service, Probe([.. CommunicationColumns, .. TodoColumns]));
        var child = new Entity("sprk_todo");

        var outcome = await resolver.StampAsync(child, "sprk_communication", CommId, default, CoreAncestorResult.Failed("x"));

        outcome.Succeeded.Should().BeTrue();
        Reads(service).Should().Be(1, "a failed hand-in is not reused: the stamps are derived here");
        child.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
    }

    [Fact]
    public async Task StampAsync_WithASucceededAlreadyDerivedForTheSameTarget_ReadsNothing_AndAppliesTheStamps()
    {
        var service = EntityServiceReturning(CommunicationFiledUnderTheMatter());
        var resolver = Build(service, Probe([.. CommunicationColumns, .. TodoColumns]));
        var derived = await resolver.ResolveStampsAsync("sprk_communication", CommId);
        service.Invocations.Clear();
        var child = new Entity("sprk_todo");

        // The entity name is compared case-insensitively.
        var outcome = await resolver.StampAsync(child, "SPRK_Communication", CommId, default, derived);

        outcome.Succeeded.Should().BeTrue();
        Reads(service).Should().Be(0, "the hand-in was derived for this target and is reused");
        child.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
    }

    [Fact]
    public async Task StampAsync_WithAnAlreadyDerivedForAnotherTarget_OrWithNoTarget_DerivesAgain()
    {
        var service = EntityServiceReturning(CommunicationFiledUnderTheMatter());
        var resolver = Build(service, Probe([.. CommunicationColumns, .. TodoColumns]));
        var forAnotherRecord = await resolver.ResolveStampsAsync("sprk_communication", Guid.NewGuid());
        // Built with the public constructor: it names no target, so it can never be matched to one.
        var noTarget = new CoreAncestorResult(
            CoreAncestorStatus.CoreTarget, [new CoreAncestorStamp("sprk_project", "sprk_regardingproject", ProjectId)], null);

        foreach (var handedIn in new[] { forAnotherRecord, noTarget })
        {
            service.Invocations.Clear();
            var child = new Entity("sprk_todo");

            var outcome = await resolver.StampAsync(child, "sprk_communication", CommId, default, handedIn);

            outcome.Succeeded.Should().BeTrue();
            Reads(service).Should().Be(1, "a result for another target is not this target's stamps: derive again");
            child.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
            child.Contains("sprk_regardingproject").Should().BeFalse("the handed-in project stamp belongs to no row of this target");
        }
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

    /// <summary>The TypeScript resolver source (the parity source for the lock-step tests).</summary>
    private static string ReadTypeScriptResolver()
    {
        var tsPath = FindRepoFile(
            "src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts");
        tsPath.Should().NotBeNull(
            "the TypeScript resolver is the parity source; if it moved, this test must be updated, not deleted");
        return File.ReadAllText(tsPath!);
    }

    /// <summary>
    /// The raw body of an exported TS `const NAME: ... = [ ... ]` array, up to its first <c>]</c> (the same shape
    /// <see cref="ParseTsStringArray"/> matches).
    /// </summary>
    private static string ParseTsArrayBody(string source, string constName)
    {
        var match = Regex.Match(
            source,
            $@"export\s+const\s+{Regex.Escape(constName)}\s*:[^=]*=\s*\[(?<body>[^\]]*)\]",
            RegexOptions.Singleline);
        match.Success.Should().BeTrue($"{constName} must exist in the TypeScript resolver");
        return match.Groups["body"].Value;
    }

    /// <summary>A parsed array body may hold no spread and no comment: either can hide a row from the parser.</summary>
    private static void AssertPlainLiteralArray(string body, string constName)
    {
        body.Should().NotContain("...", $"{constName} must not use a spread (the parser cannot see spread rows)");
        body.Should().NotContain("//", $"{constName} must not contain a comment (explanations go in its JSDoc)");
        body.Should().NotContain("/*", $"{constName} must not contain a comment (explanations go in its JSDoc)");
    }

    private sealed class CapturingLogger : ILogger<CoreAncestorResolver>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
