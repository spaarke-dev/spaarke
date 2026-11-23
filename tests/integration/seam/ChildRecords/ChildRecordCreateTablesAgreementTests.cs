using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.ChildRecords;

/// <summary>
/// field-mapping-server-write-path-r1 task 020 (spec FR-15): the server's create table set
/// (<see cref="ChildRecordEndpoints.CreateTables"/>) and the client's (<c>BFF_CHILD_CREATE_TABLES</c> in
/// <c>bffChildWriteAdapter.ts</c>) agree: the server's set is the client's plus the ribbon-only tables, exactly. A table added
/// to one side only fails here, so a writer is never routed to a table the server refuses, and the server never accepts a
/// browser table the client still writes through <c>Xrm.WebApi</c> unnoticed.
/// </summary>
[Trait("status", "new")]
public sealed partial class ChildRecordCreateTablesAgreementTests
{
    /// <summary>
    /// The tables the server creates through the route but the client adapter does not route: the secure-record ribbon's
    /// "New Budget" / "New KPI Assessment" (unified-access-control-r2 task 147, owner round 28 item 2)
    /// post to the route from their own ribbon script, not through <c>withBffChildWrites</c>. Whether they should join the
    /// client set is a separate decision (task 020 notes, deferred item). ebilling-r1 task 164a-1 (uac-r2 Q6): "New Billing
    /// Event" is gone, and the route no longer creates an invoice line.
    /// </summary>
    internal static readonly IReadOnlySet<string> RibbonOnlyCreateTables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_budget", "sprk_kpiassessment" };

    private static readonly string AdapterFile = Path.Combine(
        RepoRoot(), "src", "client", "shared", "Spaarke.UI.Components", "src", "utils", "adapters", "bffChildWriteAdapter.ts");

    [GeneratedRegex(@"BFF_CHILD_CREATE_TABLES[^=]*=\s*new Set\(\[(?<list>[^\]]*)\]\)")]
    private static partial Regex ClientSet();

    [GeneratedRegex(@"'(?<t>[a-z_]+)'")]
    private static partial Regex Quoted();

    /// <summary>The client's set, read from the adapter's source (comments inside the list are ignored).</summary>
    internal static IReadOnlyList<string> ClientCreateTables()
    {
        var match = ClientSet().Match(File.ReadAllText(AdapterFile));
        match.Success.Should().BeTrue("BFF_CHILD_CREATE_TABLES is declared as `new Set([...])` in " + AdapterFile);
        var withoutComments = Regex.Replace(match.Groups["list"].Value, @"//[^\n]*", string.Empty);
        return Quoted().Matches(withoutComments).Select(m => m.Groups["t"].Value).ToList();
    }

    /// <summary>
    /// What breaks the agreement: every server table that is neither a client table nor ribbon-only, every client table the
    /// server does not create, and every ribbon-only table the client also routes or the server does not create.
    /// </summary>
    internal static IReadOnlyList<string> Disagreements(
        IEnumerable<string> server, IEnumerable<string> client, IEnumerable<string> ribbonOnly)
    {
        var s = new HashSet<string>(server, StringComparer.OrdinalIgnoreCase);
        var c = new HashSet<string>(client, StringComparer.OrdinalIgnoreCase);
        var r = new HashSet<string>(ribbonOnly, StringComparer.OrdinalIgnoreCase);
        return s.Where(t => !c.Contains(t) && !r.Contains(t)).Select(t => $"{t}: server only (not in the client set, not ribbon-only)")
            .Concat(c.Where(t => !s.Contains(t)).Select(t => $"{t}: client only (the server refuses it)"))
            .Concat(r.Where(c.Contains).Select(t => $"{t}: ribbon-only AND in the client set"))
            .Concat(r.Where(t => !s.Contains(t)).Select(t => $"{t}: ribbon-only but not a server create table"))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void TheServerCreateTables_AreTheClientCreateTables_PlusTheRibbonOnlyTables()
    {
        var client = ClientCreateTables();

        client.Should().Contain(["sprk_matter", "sprk_project"], "task 020 adds the two roots on both sides");
        client.Should().OnlyHaveUniqueItems();
        Disagreements(ChildRecordEndpoints.CreateTables, client, RibbonOnlyCreateTables).Should().BeEmpty();
    }

    /// <summary>Must-fire: a table added to the server set that is in neither the client set nor the ribbon-only list.</summary>
    [Fact]
    public void Disagreements_ATableOnlyTheServerCreates_AndNotRibbonOnly_IsReported()
    {
        var server = ChildRecordEndpoints.CreateTables.Append("sprk_fixturetable");

        Disagreements(server, ClientCreateTables(), RibbonOnlyCreateTables)
            .Should().ContainSingle().Which.Should().StartWith("sprk_fixturetable: server only");
    }

    /// <summary>Must-fire: a table the client routes that the server refuses; and a ribbon-only table the client also routes.</summary>
    [Fact]
    public void Disagreements_AClientOnlyTable_OrARibbonOnlyTableOnTheClient_IsReported()
    {
        var client = ClientCreateTables().Append("sprk_fixturetable").Append("sprk_budget");

        Disagreements(ChildRecordEndpoints.CreateTables, client, RibbonOnlyCreateTables).Should().BeEquivalentTo(
            "sprk_budget: ribbon-only AND in the client set", "sprk_fixturetable: client only (the server refuses it)");
    }

    /// <summary>
    /// Must-fire for the census filter (SecureChildOwnershipAiToolTests.ChildRecordRoutes.cs, <c>TheCensusTheory_CoversEveryCreateTable</c>):
    /// sprk_matter is a create table and a TOP root (a parent that inherits from nothing), so the census of secure CHILDREN
    /// must exclude it — the filter before task 020 (<c>!Inherits</c> only) would have kept it, the filter now
    /// (<c>!Inherits &amp;&amp; !IsParent</c>) drops exactly it. Its own cases are ChildRecordMatterProjectCreateSeamTests'.
    /// </summary>
    [Fact]
    public void TheCensusFilter_ExcludesTheMatter_ATopRootOnTheRoute_AndNothingElseTheOldFilterKept()
    {
        ChildRecordEndpoints.CreateTables.Should().Contain("sprk_matter");

        var before = ChildRecordEndpoints.CreateTables.Where(t => !SecureRootInheritance.Inherits(t)).ToHashSet();
        var now = ChildRecordEndpoints.CreateTables
            .Where(t => !SecureRootInheritance.Inherits(t) && !SecureRootInheritance.IsParent(t)).ToHashSet();

        before.Should().Contain("sprk_matter", "without the IsParent clause the census would demand secure-child cases for a matter");
        now.Should().NotContain("sprk_matter");
        before.Except(now).Should().BeEquivalentTo(["sprk_matter"], "the new clause removes the top root and nothing else");
    }

    /// <summary>
    /// Guard for <c>SecureChildOwnershipAiToolTests.ChildCreate_OfATableOutsideTheCensus_IsRefused400</c>: every table that
    /// theory names (read from its own <c>[InlineData]</c>, so the two cannot drift) is absent from the route's create tables.
    /// Task 020 moved sprk_project onto the route and the theory's sprk_project row silently changed meaning; a table added
    /// to the route later now fails HERE, by name, instead.
    /// </summary>
    [Fact]
    public void TheOutsideTheCensusTheory_NamesOnlyTablesTheRouteDoesNotCreate()
    {
        var theory = typeof(SecureChildOwnershipAiToolTests).GetMethod(
            nameof(SecureChildOwnershipAiToolTests.ChildCreate_OfATableOutsideTheCensus_IsRefused400));
        theory.Should().NotBeNull();

        var tables = theory!.GetCustomAttributes(typeof(InlineDataAttribute), inherit: false)
            .Cast<InlineDataAttribute>()
            .SelectMany(a => a.GetData(theory))
            .Select(row => (string)row[0])
            .ToList();

        tables.Should().NotBeEmpty();
        tables.Where(ChildRecordEndpoints.CreateTables.Contains).Should().BeEmpty(
            "a table the route creates is not 'outside the census'; pick another real table for that theory");
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (System.IO.Directory.Exists(git) || File.Exists(git))
                return dir;

            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
