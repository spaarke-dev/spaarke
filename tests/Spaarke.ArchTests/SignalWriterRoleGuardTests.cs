using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 044 (owner ruling 2026-11-06; ontology D-204, D-210): an exact allow-list of roles that may
/// create or write Signals and Decision Records. The core record pair carries no field security, so write integrity rests on the roles,
/// the writers' refusal (D-204), column auditing and task 052's report. Create and Write are separate sets, each checked for EQUALITY
/// with its allow-list (an extra role fails; an allow-listed role defined in the repo that no longer holds the privilege fails), at any
/// depth, over every role file in any <c>Roles</c> folder of any solution, whoever holds the role (users or teams).
/// <para>Only roles the repo defines can be checked here: the platform roles on the allow-lists are not role files, and who may hold
/// the service roles is live state that is not in the repo, so both are checked read-only against the live environment.</para>
/// <para>Complements <see cref="FeatureRolePrivilegeGuardTests.MustNotHold"/> (046b), which forbids Create, Write and Delete on these
/// tables to every person role except the cleanup role: this guard also covers the app users' role (so its missing Write on Decision
/// Records, on which append-only depends, is pinned) and does not except the cleanup role.</para>
/// </summary>
public class SignalWriterRoleGuardTests
{
    /// <summary>The allow-lists (role names appear only here). Agreed by ontology-r1 and unified-access-control-r2, 2026-11-06.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> AllowList = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["prvCreatesprk_Signal"] = ["System Administrator", "System Customizer", "Service Writer", "Spaarke Ontology Service"],
        ["prvWritesprk_Signal"] = ["System Administrator", "System Customizer", "Service Writer", "Spaarke Ontology Service"],
        ["prvCreatesprk_DecisionRecord"] = ["System Administrator", "System Customizer", "Service Writer", "Spaarke Ontology Service"],
        ["prvWritesprk_DecisionRecord"] = ["System Administrator", "System Customizer", "Service Writer"],
    };

    private const string OntologyService = "Spaarke Ontology Service";

    private static string SolutionsDirectory => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions");

    [Fact(DisplayName = "044: the roles holding Create or Write on Signals and Decision Records equal their allow-lists (D-204, D-210)")]
    public void TheRoleFilesHoldExactlyTheAllowLists()
    {
        var roles = LoadRoleFiles(SolutionsDirectory);
        Assert.Contains(OntologyService, roles.Keys);
        var findings = Findings(roles);
        Assert.True(findings.Count == 0,
            "The roles that may create or write Signals and Decision Records are an exact allow-list (ontology D-204, D-210). Raise any "
            + "change with ontology-r1 and unified-access-control-r2:\n  " + string.Join("\n  ", findings));
    }

    [Fact(DisplayName = "044: the allow-list guard fires on each seeded drift and not on the committed role files")]
    public void TheGuardFiresOnEachSeededDrift()
    {
        var committed = LoadRoleFiles(SolutionsDirectory);
        Assert.Empty(Findings(committed)); // must not fire: today's state

        Fires(committed, r => Grant(r, "Spaarke Core User", "prvCreatesprk_Signal", "Basic"), "Spaarke Core User", "prvCreatesprk_Signal");
        Fires(committed, r => Grant(r, "Spaarke Console User", "prvWritesprk_Signal", "Deep"), "Spaarke Console User", "prvWritesprk_Signal");
        Fires(committed, r => Grant(r, "Spaarke Basic User", "prvCreatesprk_DecisionRecord", "Global"), "Spaarke Basic User", "prvCreatesprk_DecisionRecord");
        Fires(committed, r => Grant(r, "Spaarke Ontology Administrator", "prvWritesprk_Signal", "Global"), "Spaarke Ontology Administrator", "prvWritesprk_Signal");
        // Specifically: the writer gaining Write on Decision Records (append-only depends on its absence).
        Fires(committed, r => Grant(r, OntologyService, "prvWritesprk_DecisionRecord", "Global"), OntologyService, "prvWritesprk_DecisionRecord");
        // A role defined in another solution's Roles folder (held by a team or a user alike: the file does not say, so both are covered).
        Fires(committed, r => r["Spaarke Added Elsewhere"] = new(StringComparer.OrdinalIgnoreCase) { ["prvWritesprk_DecisionRecord"] = "Basic" },
            "Spaarke Added Elsewhere", "prvWritesprk_DecisionRecord");
        // An allow-listed role the repo defines that no longer holds its privilege.
        Fires(committed, r => r[OntologyService].Remove("prvCreatesprk_Signal"), OntologyService, "prvCreatesprk_Signal");
        // Case and depth do not matter: any depth is a grant.
        Fires(committed, r => Grant(r, "Spaarke Core User", "PRVWRITESPRK_SIGNAL", "Local"), "Spaarke Core User", "prvWritesprk_Signal");
    }

    [Fact(DisplayName = "044: the guard reads every Roles folder of every solution")]
    public void TheGuardReadsEveryRolesFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "sprk-044-roles-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "SpaarkeMaster", "Roles"));
            Directory.CreateDirectory(Path.Combine(root, "SomeOtherSolution", "Roles"));
            Directory.CreateDirectory(Path.Combine(root, "SomeOtherSolution", "Other"));
            foreach (var file in Directory.EnumerateFiles(Path.Combine(SolutionsDirectory, "SpaarkeMaster", "Roles"), "*.xml"))
                File.Copy(file, Path.Combine(root, "SpaarkeMaster", "Roles", Path.GetFileName(file)));
            Assert.Empty(Findings(LoadRoleFiles(root))); // must not fire

            File.WriteAllText(Path.Combine(root, "SomeOtherSolution", "Other", "Not A Role.xml"), // must not fire: not in a Roles folder
                "<Role name=\"Not A Role\"><RolePrivileges><RolePrivilege name=\"prvWritesprk_Signal\" level=\"Global\" /></RolePrivileges></Role>");
            Assert.Empty(Findings(LoadRoleFiles(root)));

            File.WriteAllText(Path.Combine(root, "SomeOtherSolution", "Roles", "Team Role.xml"), // must fire
                "<Role name=\"Team Role\"><RolePrivileges><RolePrivilege name=\"prvCreatesprk_Signal\" level=\"Basic\" /></RolePrivileges></Role>");
            Assert.Contains(Findings(LoadRoleFiles(root)), f => f.Contains("Team Role", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── The check ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>For each guarded privilege: a role file holding it that is not on its allow-list (extra), and an allow-listed role the
    /// repo defines that does not hold it (missing).</summary>
    internal static List<string> Findings(IReadOnlyDictionary<string, Dictionary<string, string>> roles)
    {
        var findings = new List<string>();
        foreach (var (privilege, allowed) in AllowList)
        {
            var holders = roles.Where(r => r.Value.TryGetValue(privilege, out var level) && !string.IsNullOrWhiteSpace(level))
                .Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var extra in holders.Where(h => !allowed.Contains(h, StringComparer.Ordinal)).Order(StringComparer.Ordinal))
                findings.Add($"{extra}: holds {privilege} at {roles[extra][privilege]}, and is not on its allow-list");
            foreach (var missing in allowed.Where(a => roles.ContainsKey(a) && !holders.Contains(a)))
                findings.Add($"{missing}: is on the {privilege} allow-list and defined in the repo, but does not hold it");
        }

        return findings;
    }

    /// <summary>Every role file in any <c>Roles</c> folder under <paramref name="root"/>: role name → privilege (case-insensitive) → level.
    /// A file that is unparsable, has no role name, or defines a role a second time fails (never read as "holds nothing").</summary>
    internal static Dictionary<string, Dictionary<string, string>> LoadRoleFiles(string root)
    {
        var roles = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var path in ProcedureRolePrivilegeGuardTests.RoleFiles(root).Order(StringComparer.Ordinal))
        {
            var doc = XDocument.Parse(File.ReadAllText(path));
            var name = (string?)doc.Root?.Attribute("name");
            Assert.False(string.IsNullOrWhiteSpace(name), $"Role file {path} has no role name.");
            Assert.False(roles.ContainsKey(name!), $"Role '{name}' is defined twice (second: {path}).");
            roles[name!] = doc.Descendants().Where(e => e.Name.LocalName == "RolePrivilege")
                .GroupBy(e => (string?)e.Attribute("name") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (string?)g.First().Attribute("level") ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        return roles;
    }

    private static void Fires(
        IReadOnlyDictionary<string, Dictionary<string, string>> committed, Action<Dictionary<string, Dictionary<string, string>>> seed,
        string role, string privilege)
    {
        var copy = committed.ToDictionary(r => r.Key, r => new Dictionary<string, string>(r.Value, StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);
        seed(copy);
        var findings = Findings(copy);
        Assert.True(findings.Any(f => f.StartsWith(role + ":", StringComparison.Ordinal) && f.Contains(privilege, StringComparison.OrdinalIgnoreCase)),
            $"Seeding {role} / {privilege} must fire. Findings: {string.Join("; ", findings)}");
    }

    private static void Grant(Dictionary<string, Dictionary<string, string>> roles, string role, string privilege, string level)
    {
        Assert.True(roles.ContainsKey(role), $"The seeded role '{role}' is not a role file.");
        roles[role][privilege] = level;
    }
}
