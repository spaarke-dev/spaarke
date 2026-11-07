using System.Text.RegularExpressions;
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

    /// <summary>The platform roles on the allow-lists. They are not role files: a repo role file with one of these names would put a role
    /// the repo controls on the allow-list under a platform role's name, so it is a finding (fix round 2).</summary>
    internal static readonly string[] PlatformRoles = ["System Administrator", "System Customizer", "Service Writer"];

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
        // Fix round 2 (a): an element that names the privilege is a grant whatever its level says, an empty level included.
        Fires(committed, r => Grant(r, "Spaarke Core User", "prvCreatesprk_DecisionRecord", ""), "Spaarke Core User", "prvCreatesprk_DecisionRecord");
        // Fix round 2 (b): a repo role file under a platform role name on the allow-lists, holding something or nothing, in any case.
        Fires(committed, r => r["System Customizer"] = new(StringComparer.OrdinalIgnoreCase), "System Customizer", "platform role name");
        Fires(committed, r => r["service writer"] = new(StringComparer.OrdinalIgnoreCase) { ["prvWritesprk_DecisionRecord"] = "Global" },
            "service writer", "platform role name");
    }

    [Theory(DisplayName = "044: a RolePrivilege element that names a guarded privilege is a grant, whatever its level attribute says")]
    [InlineData("level=\"\"")]
    [InlineData("")]
    [InlineData("level=\"   \"")]
    public void AnElementNamingThePrivilege_IsAGrant_WhateverItsLevel(string levelAttribute)
    {
        var findings = FindingsOfOneRoleFile(
            $"<Role name=\"Seeded Role\"><RolePrivileges><RolePrivilege name=\"prvWritesprk_Signal\" {levelAttribute} /></RolePrivileges></Role>");
        Assert.Contains(findings, f => f.StartsWith("Seeded Role:", StringComparison.Ordinal) && f.Contains("prvWritesprk_Signal", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "044: a privilege named twice in one role file is a grant, whichever element carries the level")]
    [InlineData("level=\"\"", "level=\"Global\"")]
    [InlineData("", "level=\"Basic\"")]
    [InlineData("level=\"Deep\"", "")]
    [InlineData("", "")]
    public void APrivilegeNamedTwice_IsAGrant(string first, string second)
    {
        var findings = FindingsOfOneRoleFile(
            "<Role name=\"Seeded Role\"><RolePrivileges>"
            + $"<RolePrivilege name=\"prvCreatesprk_Signal\" {first} /><RolePrivilege name=\"prvCreatesprk_Signal\" {second} />"
            + "</RolePrivileges></Role>");
        Assert.Contains(findings, f => f.StartsWith("Seeded Role:", StringComparison.Ordinal) && f.Contains("prvCreatesprk_Signal", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "044: a role file that holds none of the guarded privileges is not a finding")]
    public void ARoleFileWithoutTheGuardedPrivileges_IsNotAFinding()
    {
        // Must not fire: a Read privilege, and a name that only starts with a guarded one.
        Assert.Empty(FindingsOfOneRoleFile(
            "<Role name=\"Seeded Role\"><RolePrivileges><RolePrivilege name=\"prvReadsprk_Signal\" level=\"Global\" />"
            + "<RolePrivilege name=\"prvWritesprk_SignalFeedback\" level=\"Global\" /></RolePrivileges></Role>"));
    }

    [Fact(DisplayName = "044: no config/*.json names a guarded privilege (the script-provisioned roles hold none of them)")]
    public void NoConfigJsonNamesAGuardedPrivilege()
    {
        var configDirectory = Path.Combine(SourceScan.RepoRoot, "config");
        Assert.NotEmpty(Directory.EnumerateFiles(configDirectory, "*.json"));
        var findings = ConfigPrivilegeFindings(configDirectory);
        Assert.True(findings.Count == 0,
            "A script-provisioned role (config/*.json) names a privilege on the Signal / Decision Record allow-lists (ontology D-204, D-210). "
            + "Raise it with ontology-r1 and unified-access-control-r2:\n  " + string.Join("\n  ", findings));
    }

    [Fact(DisplayName = "044: the config guard fires on each seeded privilege name, in any case, and not on a longer name")]
    public void TheConfigGuardFiresOnASeededPrivilegeName()
    {
        var root = Path.Combine(Path.GetTempPath(), "sprk-044-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "some-role.json"),
                "{ \"privileges\": [ \"prvReadsprk_Signal\", \"prvWritesprk_SignalFeedback\", \"prvCreatesprk_DecisionRecordLine\" ] }");
            Assert.Empty(ConfigPrivilegeFindings(root)); // must not fire

            foreach (var privilege in AllowList.Keys)
            {
                File.WriteAllText(Path.Combine(root, "seeded-role.json"), $"{{ \"privileges\": [ \"{privilege.ToUpperInvariant()}\" ] }}");
                Assert.Contains(ConfigPrivilegeFindings(root), f => f.Contains("seeded-role.json", StringComparison.Ordinal)
                    && f.Contains(privilege, StringComparison.OrdinalIgnoreCase)); // must fire
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
    /// repo defines that does not hold it (missing). A role file holds a privilege when any <c>RolePrivilege</c> element names it, whatever
    /// its level says (empty, missing or repeated). A repo role file under a platform role name on the allow-lists is a finding.</summary>
    internal static List<string> Findings(IReadOnlyDictionary<string, Dictionary<string, string>> roles)
    {
        var findings = new List<string>();
        foreach (var platform in roles.Keys.Where(k => PlatformRoles.Contains(k.Trim(), StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
            findings.Add($"{platform}: a repo role file uses a platform role name on the allow-lists (the platform roles are not role files)");

        foreach (var (privilege, allowed) in AllowList)
        {
            var holders = roles.Where(r => r.Value.ContainsKey(privilege))
                .Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var extra in holders.Where(h => !allowed.Contains(h, StringComparer.Ordinal)).Order(StringComparer.Ordinal))
                findings.Add($"{extra}: holds {privilege} at {LevelText(roles[extra][privilege])}, and is not on its allow-list");
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
            // Every element counts (fix round 2): the name alone is the grant, and a privilege named twice keeps every level it was given.
            roles[name!] = doc.Descendants().Where(e => e.Name.LocalName == "RolePrivilege")
                .GroupBy(e => ((string?)e.Attribute("name") ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key,
                    g => string.Join(",", g.Select(e => ((string?)e.Attribute("level") ?? string.Empty).Trim()).Where(l => l.Length > 0).Distinct()),
                    StringComparer.OrdinalIgnoreCase);
        }

        return roles;
    }

    /// <summary>Each <c>*.json</c> directly in <paramref name="configDirectory"/> that names a guarded privilege (whole name, any case).</summary>
    internal static List<string> ConfigPrivilegeFindings(string configDirectory)
    {
        var findings = new List<string>();
        foreach (var path in Directory.EnumerateFiles(configDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(path);
            foreach (var privilege in AllowList.Keys)
            {
                if (Regex.IsMatch(text, $"(?<![A-Za-z0-9_]){privilege}(?![A-Za-z0-9_])", RegexOptions.IgnoreCase))
                    findings.Add($"{Path.GetFileName(path)}: names {privilege}");
            }
        }

        return findings;
    }

    private static string LevelText(string level) => level.Length == 0 ? "no stated level" : level;

    /// <summary>The findings when <paramref name="roleXml"/> is added, as its own role file, to a copy of the committed Roles folder.</summary>
    private static List<string> FindingsOfOneRoleFile(string roleXml)
    {
        var root = Path.Combine(Path.GetTempPath(), "sprk-044-role-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "SpaarkeMaster", "Roles"));
            Directory.CreateDirectory(Path.Combine(root, "SeededSolution", "Roles"));
            foreach (var file in Directory.EnumerateFiles(Path.Combine(SolutionsDirectory, "SpaarkeMaster", "Roles"), "*.xml"))
                File.Copy(file, Path.Combine(root, "SpaarkeMaster", "Roles", Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(root, "SeededSolution", "Roles", "Seeded Role.xml"), roleXml);
            return Findings(LoadRoleFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
