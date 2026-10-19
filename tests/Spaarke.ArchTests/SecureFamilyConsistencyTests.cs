using System.Text.Json.Nodes;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 182 (ontology child-record contract C7, audit F4) — the three lists that name a secure
/// record's child tables agree, so a new family table cannot ship with only some of them:
/// <list type="bullet">
/// <item><see cref="SecureChildLineage.Children"/> — the tables the reconciler re-owns and the share synchronizer mirrors;</item>
/// <item><c>config/secure-record-owner-role.json</c>, <c>tables[]</c> of kind <c>child</c> — the tables the Secure Record
/// Owners team holds Read on, without which Dataverse refuses it as the owner;</item>
/// <item><see cref="RecordOwnerAssignmentCensusTests.ChildTables"/> — the tables whose server creates are censused.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>The rules.</b> A lineage table missing from the role set fails; a role-set child missing from the lineage
/// fails; a lineage table missing from the census fails. The census also lists tables that are not lineage children (a
/// root, a service request, transport and job rows): only the lineage children of the census are compared. A table that
/// belongs in one list and not another is allow-listed below with its reason; an allow-list entry that no longer
/// describes a gap fails, so the lists cannot go stale. All three allow-lists are empty at task 182.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing: <c>SecureChildLineageTests</c> (unit tests) pins the
/// lineage to the role set, strictly. Extension: the census list lives in this assembly, so the tie that includes it lives
/// here; the unit test stays (it also pins the roots). An allow-list entry for a lineage/role gap must therefore also be
/// reflected there. Cost of doing nothing: a table added to the lineage and the role set but not the census would have its
/// creates go unscanned, so an ordinary-owned create of a secure root's child could ship unnoticed (audit F4).</para>
/// <para><b>Source only</b> (ADR-038 ban B3): the lineage and the census are static tables, the role set is read from the
/// file; no DI, no network.</para>
/// </remarks>
public class SecureFamilyConsistencyTests
{
    /// <summary>Tables with a lineage row that are deliberately not in the role set, with the reason. Empty.</summary>
    private static readonly IReadOnlyDictionary<string, string> LineageNotInRoleSet =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Role-set child tables that deliberately have no lineage row, with the reason. Empty.</summary>
    private static readonly IReadOnlyDictionary<string, string> RoleSetNotInLineage =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tables with a lineage row that are deliberately not in the census, with the reason. Empty.</summary>
    private static readonly IReadOnlyDictionary<string, string> LineageNotInCensus =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly FamilyAllowLists AllowLists = new(LineageNotInRoleSet, RoleSetNotInLineage, LineageNotInCensus);

    private static string RoleConfigPath => Path.Combine(SourceScan.RepoRoot, "config", "secure-record-owner-role.json");

    [Fact(DisplayName = "Task 182: the lineage, the codified role set and the census name the same secure child tables")]
    public void TheLineageTheRoleSetAndTheCensusAgree()
    {
        var problems = FamilyProblems(
            SecureChildLineage.Children.Keys,
            RoleSetChildTables(File.ReadAllText(RoleConfigPath)),
            RecordOwnerAssignmentCensusTests.ChildTables,
            AllowLists);

        Assert.True(
            problems.Count == 0,
            "The secure child tables disagree (task 182, contract C7). Every family table needs a lineage row "
            + "(SecureChildLineage), a role entry (config/secure-record-owner-role.json, refusal recorded first per "
            + "howToExtend — never added to turn this green) and a census entry (ChildTables):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 182: negative control — each of the three gaps fails, an allow-listed gap passes, a stale allow-list entry fails")]
    public void FamilyProblems_NegativeControl()
    {
        var lineage = new[] { "t_all", "t_no_role", "t_no_census", "t_allowed" };
        var role = new[] { "t_all", "t_no_census", "t_no_lineage", "t_allowed", "t_stale" };
        var census = new[] { "t_all", "t_no_role", "t_no_lineage", "t_census_only" };
        var allow = new FamilyAllowLists(
            new Dictionary<string, string> { ["t_stale"] = "seeded: not a lineage table, so this entry describes no gap" },
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["t_allowed"] = "seeded: allow-listed" });

        var problems = FamilyProblems(lineage, role, census, allow);

        Assert.Equal(5, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("t_no_role: has a lineage row but no kind \"child\" entry", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("t_no_lineage: is a kind \"child\" table", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("t_stale: is a kind \"child\" table", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("t_no_census: has a lineage row but is not in", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("t_stale: allow-listed", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.StartsWith("t_allowed", StringComparison.Ordinal));     // allow-listed
        Assert.DoesNotContain(problems, p => p.StartsWith("t_census_only", StringComparison.Ordinal)); // census-only is not compared
    }

    [Fact(DisplayName = "Task 182: negative control — removing a lineage table from the role config fails the guard")]
    public void RemovingALineageTableFromTheRoleConfig_Fails()
    {
        var config = JsonNode.Parse(File.ReadAllText(RoleConfigPath))!;
        var tables = config["tables"]!.AsArray();
        var signal = tables.Single(t => (string?)t!["logicalName"] == "sprk_signal");
        tables.Remove(signal);

        var problems = FamilyProblems(
            SecureChildLineage.Children.Keys,
            RoleSetChildTables(config.ToJsonString()),
            RecordOwnerAssignmentCensusTests.ChildTables,
            AllowLists);

        Assert.StartsWith("sprk_signal: has a lineage row but no kind \"child\" entry", Assert.Single(problems), StringComparison.Ordinal);
    }

    /// <summary>
    /// The kinds a role-config entry may carry, with what each means. Only <c>child</c> takes part in the three-list
    /// comparison above, so an entry of any other kind is outside it: an unknown or misspelt kind would drop a table out of
    /// the comparison unseen. <c>grant</c> (UAC task 099b review round 3, F4): <c>sprk_externalrecordaccess</c>, owned by its
    /// root's team (D-11) — a row the Secure team owns, but not a lineage child (nothing is filed under a grant, and the share
    /// mirror never covers it), so it has no lineage or census-child row.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RoleSetKinds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["root"] = "carries sprk_issecure; the Secure team owns the record itself",
        ["child"] = "a secure root's child: a lineage row, a census entry, re-owned and mirrored",
        ["grant"] = "an external-access grant row: owned by its root's team (D-11), moved with the root, never mirrored",
    };

    [Fact(DisplayName = "UAC 099b r3 F4: every role-config entry has a known kind")]
    public void EveryRoleConfigEntry_HasAKnownKind()
    {
        var unknown = UnknownKinds(File.ReadAllText(RoleConfigPath));

        Assert.True(unknown.Count == 0,
            "Role-config entries of a kind this guard does not know (only 'child' is compared with the lineage and the census, "
            + "so any other kind must be listed in RoleSetKinds with its meaning): " + string.Join(", ", unknown));
    }

    [Fact(DisplayName = "UAC 099b r3 F4: negative control — an unknown kind fails, the grant kind passes")]
    public void UnknownKinds_NegativeControl()
    {
        const string Json = """
            {"tables":[
              {"logicalName":"t_root","kind":"root"},{"logicalName":"t_child","kind":"child"},
              {"logicalName":"t_grant","kind":"grant"},{"logicalName":"t_typo","kind":"chlid"},{"logicalName":"t_none"}]}
            """;

        Assert.Equal(new[] { "t_typo (kind 'chlid')", "t_none (kind '')" }, UnknownKinds(Json));
    }

    /// <summary>Every role-config entry whose kind is not in <see cref="RoleSetKinds"/>.</summary>
    private static List<string> UnknownKinds(string json) =>
        JsonNode.Parse(json)!["tables"]!.AsArray()
            .Select(t => (Table: (string)t!["logicalName"]!, Kind: (string?)t["kind"] ?? string.Empty))
            .Where(t => !RoleSetKinds.ContainsKey(t.Kind))
            .Select(t => $"{t.Table} (kind '{t.Kind}')")
            .ToList();

    /// <summary>The <c>logicalName</c> of every <c>tables[]</c> entry of kind <c>child</c> in the role config.</summary>
    private static IReadOnlyList<string> RoleSetChildTables(string json) =>
        JsonNode.Parse(json)!["tables"]!.AsArray()
            .Where(t => (string?)t!["kind"] == "child")
            .Select(t => (string)t!["logicalName"]!)
            .ToList();

    internal sealed record FamilyAllowLists(
        IReadOnlyDictionary<string, string> LineageNotInRoleSet,
        IReadOnlyDictionary<string, string> RoleSetNotInLineage,
        IReadOnlyDictionary<string, string> LineageNotInCensus);

    /// <summary>Every disagreement between the three lists that the allow-lists do not explain, and every stale allow-list entry.</summary>
    internal static List<string> FamilyProblems(
        IEnumerable<string> lineage, IEnumerable<string> roleSetChildren, IEnumerable<string> census, FamilyAllowLists allow)
    {
        var inLineage = lineage.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inRoleSet = roleSetChildren.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inCensus = census.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        Gaps(inLineage, inRoleSet, allow.LineageNotInRoleSet, problems,
            "has a lineage row but no kind \"child\" entry in config/secure-record-owner-role.json",
            "The reconciler hands its rows to the Secure Record Owners team, and Dataverse refuses that owner without Read: "
            + "add the role entry through the file's howToExtend procedure (refusal recorded first).");
        Gaps(inRoleSet, inLineage, allow.RoleSetNotInLineage, problems,
            "is a kind \"child\" table of config/secure-record-owner-role.json with no SecureChildLineage row",
            "Its rows can be owned by the Secure team but are never reconciled or mirrored to the root's sharees: add its "
            + "filing lookups to SecureChildLineage.");
        Gaps(inLineage, inCensus, allow.LineageNotInCensus, problems,
            "has a lineage row but is not in RecordOwnerAssignmentCensusTests.ChildTables",
            "Its creates are never censused, so an ordinary-owned create of a secure root's child could ship unseen: add it "
            + "to ChildTables and census every create the scanner then finds.");

        return problems;
    }

    /// <summary>The tables of <paramref name="from"/> missing from <paramref name="to"/> and not allow-listed; then every
    /// allow-list entry that no longer describes such a gap.</summary>
    private static void Gaps(
        IReadOnlySet<string> from, IReadOnlySet<string> to, IReadOnlyDictionary<string, string> allowed, List<string> problems,
        string gap, string fix)
    {
        foreach (var table in from.Where(t => !to.Contains(t)).OrderBy(t => t, StringComparer.Ordinal))
        {
            if (!allowed.TryGetValue(table, out var reason) || string.IsNullOrWhiteSpace(reason))
                problems.Add($"{table}: {gap}. {fix}");
        }

        foreach (var table in allowed.Keys.OrderBy(t => t, StringComparer.Ordinal))
        {
            if (!from.Contains(table) || to.Contains(table))
                problems.Add($"{table}: allow-listed because it {gap}, but it no longer does: delete the stale allow-list entry.");
        }
    }
}
