using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Membership;

/// <summary>
/// field-mapping-server-write-path-r1 task 032b-1b. <c>MembershipOptionsDefaults.TargetingOnlyColumns</c> names columns that
/// TARGET people (Daily Briefing, notifications) and confer NO access. Two invariants, both pinned here:
/// (1) the list and the access-conferring registry (and the display copies) are disjoint, for the canonical seed and for a
/// bound registry; (2) no access-grant code reads the list: the only source files naming it are its definition and the
/// resolver, and within the resolver only the people-targeting filter does, never the access-conferring filter. Re-pointing a
/// grant path at the list, or listing a registry column in it, fails here.
/// </summary>
public class TargetingOnlyColumnsGuardTests
{
    private const string TargetingList = "TargetingOnlyColumns";

    // The only two source files allowed to name the list: where it is defined, and the resolver's people-targeting filter.
    private static readonly string[] AllowedFiles = ["MembershipOptions.cs", "MembershipResolverService.cs"];

    private const string AccessFilterStart = "private IReadOnlyList<MembershipDescriptor> FilterToAccessConferringRoles(";
    private const string PeopleFilterMarker = "// ── People-targeting surface";

    // ── Invariant 1: disjoint ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The guard: (table, column) pairs that are in the targeting list AND in <paramref name="registry"/> or the display copies.</summary>
    private static IReadOnlyList<(string Table, string Column)> Overlaps(
        IReadOnlyDictionary<string, IReadOnlySet<string>> targeting,
        IEnumerable<(string Table, string Column)> otherColumns)
    {
        var other = otherColumns.ToHashSet();
        return targeting
            .SelectMany(kv => kv.Value.Select(c => (Table: kv.Key.ToLowerInvariant(), Column: c.ToLowerInvariant())))
            .Where(other.Contains)
            .ToList();
    }

    private static IEnumerable<(string Table, string Column)> RegistryColumns(AccessConferringRegistry registry) =>
        registry.Entities.SelectMany(kv => kv.Value.Select(c => (kv.Key.ToLowerInvariant(), c.Field.ToLowerInvariant())));

    private static IEnumerable<(string Table, string Column)> DisplayCopies() =>
        MembershipOptionsDefaults.DisplayCopyColumns
            .SelectMany(kv => kv.Value.Select(c => (kv.Key.ToLowerInvariant(), c.ToLowerInvariant())));

    private static AccessConferringRegistry Bound()
    {
        var options = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(null, options);
        return options.AccessConferringRoles;
    }

    [Fact]
    public void TheTargetingList_IsTheInvoicesTwoAssignedToColumns()
    {
        MembershipOptionsDefaults.TargetingOnlyColumns.Keys.Should().BeEquivalentTo(["sprk_invoice"]);
        MembershipOptionsDefaults.TargetingOnlyColumns["sprk_invoice"].Should().BeEquivalentTo(
            ["sprk_assignedto1", "sprk_assignedto2"]);
    }

    [Fact]
    public void TheTargetingList_IsDisjointFrom_TheCanonicalRegistry_TheBoundRegistry_AndTheDisplayCopies()
    {
        Overlaps(MembershipOptionsDefaults.TargetingOnlyColumns,
            MembershipOptionsDefaults.CanonicalAccessConferringRegistry
                .SelectMany(kv => kv.Value.Select(c => (kv.Key.ToLowerInvariant(), c.Field.ToLowerInvariant()))))
            .Should().BeEmpty("a targeting-only column confers no access, so it is never a registry column");
        Overlaps(MembershipOptionsDefaults.TargetingOnlyColumns, RegistryColumns(Bound())).Should().BeEmpty();
        Overlaps(MembershipOptionsDefaults.TargetingOnlyColumns, DisplayCopies()).Should().BeEmpty(
            "a display copy is not targeted either (U7)");
    }

    /// <summary>Must-fire: a registry seeded with the invoice's Assigned To columns is caught.</summary>
    [Fact]
    public void MustFire_ARegistryThatListsATargetingOnlyColumn_IsCaught()
    {
        var seeded = Bound();
        seeded.Entities["sprk_invoice"] =
        [
            new AccessConferringColumn { Field = "sprk_assignedto1", IdentityType = "Contact" },
            new AccessConferringColumn { Field = "sprk_assignedto2", IdentityType = "Contact" },
        ];

        Overlaps(MembershipOptionsDefaults.TargetingOnlyColumns, RegistryColumns(seeded)).Should().BeEquivalentTo(new[]
        {
            ("sprk_invoice", "sprk_assignedto1"), ("sprk_invoice", "sprk_assignedto2"),
        });
    }

    // ── Invariant 2: no access-grant code reads the list ────────────────────────────────────────────────────────────

    /// <summary>The guard: the file names (not paths) in <paramref name="files"/> that mention the list and are not allowed to.</summary>
    private static IReadOnlyList<string> UnexpectedReaders(IEnumerable<(string Name, string Text)> files) =>
        files
            .Where(f => f.Text.Contains(TargetingList, StringComparison.Ordinal)
                        && !AllowedFiles.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
            .Select(f => f.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>The access-conferring filter's body in the resolver source: from its signature to the people-targeting section.</summary>
    private static string AccessFilterBody(string resolverSource)
    {
        var start = resolverSource.IndexOf(AccessFilterStart, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0,"the access-conferring filter is where this guard expects it");
        var end = resolverSource.IndexOf(PeopleFilterMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the people-targeting section follows the access-conferring filter");
        return resolverSource[start..end];
    }

    [Fact]
    public void OnlyTheDefinitionAndThePeopleTargetingFilter_NameTheTargetingList()
    {
        var api = Path.Combine(RepoRoot(), "src", "server");
        var files = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (Name: Path.GetFileName(p), Text: File.ReadAllText(p)))
            .ToList();

        files.Should().Contain(f => f.Name == "MembershipOptions.cs" && f.Text.Contains(TargetingList), "the definition exists");
        UnexpectedReaders(files).Should().BeEmpty(
            "no grant, share, materializer or access path may read the targeting-only list (032b-1b)");

        var resolver = files.Single(f => f.Name == "MembershipResolverService.cs").Text;
        resolver.Should().Contain(TargetingList, "the people-targeting filter reads it");
        AccessFilterBody(resolver).Should().NotContain(TargetingList, "the access-conferring filter never reads the list");
    }

    /// <summary>Must-fire: an access path that names the list, and an access filter that names it, are both caught.</summary>
    [Fact]
    public void MustFire_AGrantPathOrTheAccessFilter_ThatReadsTheList_IsCaught()
    {
        var seeded = new[]
        {
            ("AssignedAccessMaterializer.cs", "var cols = MembershipOptionsDefaults.TargetingOnlyColumns[\"sprk_invoice\"];"),
            ("MembershipOptions.cs", $"internal static X {TargetingList} {{ get; }}"),
            ("SomethingElse.cs", "no mention"),
        };
        UnexpectedReaders(seeded).Should().BeEquivalentTo(["AssignedAccessMaterializer.cs"]);

        var resolver = $"x {AccessFilterStart} var t = MembershipOptionsDefaults.{TargetingList}; {PeopleFilterMarker}";
        AccessFilterBody(resolver).Should().Contain(TargetingList);
    }

    /// <summary>Must-not-fire: files that never name the list pass.</summary>
    [Fact]
    public void MustNotFire_FilesThatDoNotNameTheList_AreNotFlagged()
    {
        UnexpectedReaders([("AssignedAccessMaterializer.cs", "AccessConferringRoles.Entities"), ("MembershipOptions.cs", TargetingList)])
            .Should().BeEmpty();
        AccessFilterBody($"{AccessFilterStart} registry only {PeopleFilterMarker} {TargetingList}")
            .Should().NotContain(TargetingList);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
