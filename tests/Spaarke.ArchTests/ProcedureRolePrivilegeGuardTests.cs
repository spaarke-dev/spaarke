using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 081: the Procedure tables are written only by the BFF (ProcedureService), so no source-controlled Spaarke role may grant
/// a person a way around it.
/// <list type="bullet">
/// <item><b>Permanent</b> (ontology-r1 080 review, 2026-10-21): no role holds Create or Write on <c>sprk_procedureversion</c> or
/// <c>sprk_procedureassignment</c>. <c>sprk_procedureversion.sprk_procedure</c> is an alternate-key column and cannot be field-secured, so a
/// holder could re-parent or create versions directly in Dataverse, outside I-P1 (no overlap) and I-P5 (billing admin + Write).</item>
/// <item><b>Until uac-r2 task 191</b> (uac-r2 2026-10-21, round 113 item 3): no role holds ANY privilege on <c>sprk_procedure</c>,
/// <c>sprk_procedureversion</c> or <c>sprk_procedureassignment</c>. Flagged-external systemusers hold the same roles as internal users
/// through their business unit's default team, so any grant would reach them; Procedures are internal-only. Task 191 replaces this rule
/// (delete <see cref="UntilTask191"/> and its control then, with uac-r2's agreement).</item>
/// </list>
/// </summary>
/// <remarks>
/// Scans every role definition in the repo: <c>src/dataverse/solutions/**/Roles/*.xml</c>, recursively, so a role added to any solution
/// (not only SpaarkeMaster) is covered (uac-r2 review item C, 2026-10-22). The BFF application user's role is not among them. Any depth counts. The live half is task 080's verify (it fails if a role a flagged-external user holds
/// gains a privilege on the three tables). Matching is exact on <c>prv{Action}{table}</c>, case-insensitive (Dataverse spells the table by
/// its schema name).
/// </remarks>
public class ProcedureRolePrivilegeGuardTests
{
    /// <summary>The temporary uac-r2 rule (see the class summary). Set to false only when uac-r2 task 191 lands, with uac-r2's agreement.</summary>
    private const bool UntilTask191 = true;

    private static readonly Regex CreateOrWriteOnVersionOrAssignment = new(
        "^prv(Create|Write)(sprk_procedureversion|sprk_procedureassignment)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AnyOnProcedureTables = new(
        "^prv(Create|Read|Write|Delete|Append|AppendTo|Assign|Share)(sprk_procedure|sprk_procedureversion|sprk_procedureassignment)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact(DisplayName = "081: no Spaarke role grants a privilege on the Procedure tables (Create/Write on versions and assignments never; any, until uac-r2 191)")]
    public void SourceRolesGrantNothingOnTheProcedureTables()
    {
        var solutionsRoot = Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions");
        var roles = RoleFiles(solutionsRoot);
        Assert.True(roles.Count >= 12,
            $"Scanner found only {roles.Count} role definitions under {solutionsRoot}/**/Roles (expected >= 12). Repo root resolved to '{SourceScan.RepoRoot}'.");
        Assert.Contains(roles, r => r.Contains(Path.Combine("SpaarkeMaster", "Roles"), StringComparison.OrdinalIgnoreCase)); // non-vacuity
        // Non-vacuity: the files parse and carry privileges (a renamed element would otherwise make every role look clean).
        Assert.True(roles.Sum(r => XDocument.Parse(File.ReadAllText(r)).Descendants().Count(e => e.Name.LocalName == "RolePrivilege")) >= 100,
            "The role definitions carry fewer than 100 RolePrivilege elements in all; the scanner is not reading what it should.");

        var violations = roles
            .SelectMany(path => Violations(File.ReadAllText(path)).Select(v => $"{Path.GetRelativePath(SourceScan.RepoRoot, path)}: {v}"))
            .ToList();

        Assert.True(violations.Count == 0,
            "A Spaarke role grants a privilege on a Procedure table. Procedures are written only by the BFF (ebilling-r1 081; ontology-r1 080 "
            + "review: sprk_procedureversion.sprk_procedure cannot be field-secured), and until uac-r2 task 191 no role may hold any privilege "
            + "on them (flagged-external users hold the same roles). Remove the grant or agree it with uac-r2:\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "081 must-fire: Create/Write on a version or an assignment, and (until 191) any privilege on the three tables, are reported")]
    public void GuardFiresOnTheForbiddenGrants()
    {
        const string role = """
            <Role id="{00000000-0000-0000-0000-000000000001}" name="Spaarke Basic User">
              <RolePrivileges>
                <RolePrivilege name="prvCreatesprk_ProcedureVersion" level="Deep" />
                <RolePrivilege name="prvWritesprk_procedureassignment" level="Basic" />
                <RolePrivilege name="prvReadsprk_procedure" level="Global" />
                <RolePrivilege name="prvAppendTosprk_procedureversion" level="Deep" />
              </RolePrivileges>
            </Role>
            """;

        var found = Violations(role);

        Assert.Equal(4, found.Count);
        Assert.Contains(found, f => f.Contains("prvCreatesprk_ProcedureVersion", StringComparison.Ordinal) && f.Contains("never", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("prvWritesprk_procedureassignment", StringComparison.Ordinal) && f.Contains("never", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("prvReadsprk_procedure", StringComparison.Ordinal) && f.Contains("191", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("prvAppendTosprk_procedureversion", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "081 must-not-fire: other tables, look-alike names and comments are not reported")]
    public void GuardIgnoresOtherGrants()
    {
        const string role = """
            <Role id="{00000000-0000-0000-0000-000000000002}" name="Spaarke Console User">
              <!-- prvWritesprk_procedureversion is deliberately absent -->
              <RolePrivileges>
                <RolePrivilege name="prvWritesprk_ruleset" level="Deep" />
                <RolePrivilege name="prvWritesprk_procedureversionx" level="Deep" />
                <RolePrivilege name="xprvWritesprk_procedure" level="Deep" />
                <RolePrivilege name="prvWritesprk_policyversion" level="Deep" />
                <RolePrivilege name="prvReadsprk_proceduresource" level="Deep" />
              </RolePrivileges>
            </Role>
            """;

        Assert.Empty(Violations(role));
    }

    [Fact(DisplayName = "081 controls: the role-file selector finds a Roles/*.xml at any depth and nothing outside a Roles folder")]
    public void RoleFileSelector_FindsNestedRolesOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "sprk-roles-guard-" + Guid.NewGuid().ToString("N"));
        try
        {
            var nested = Path.Combine(root, "SomeOtherSolution", "src", "Roles");
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(Path.Combine(root, "SpaarkeMaster", "Entities"));
            File.WriteAllText(Path.Combine(nested, "Role.xml"), "<Role />");                               // must fire
            File.WriteAllText(Path.Combine(root, "SpaarkeMaster", "Entities", "Entity.xml"), "<Entity />");  // must not
            File.WriteAllText(Path.Combine(nested, "Role.txt"), "x");                                       // must not

            var found = RoleFiles(root);

            Assert.Single(found);
            Assert.EndsWith(Path.Combine("Roles", "Role.xml"), found[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Every <c>*.xml</c> directly in a folder named <c>Roles</c>, at any depth under <paramref name="root"/>.</summary>
    internal static List<string> RoleFiles(string root) =>
        Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories)
            .Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "Roles", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>The forbidden grants in one role definition, as "role: name (level) — rule".</summary>
    internal static List<string> Violations(string roleXml)
    {
        var doc = XDocument.Parse(roleXml);
        var roleName = (string?)doc.Root?.Attribute("name") ?? string.Empty;
        var found = new List<string>();
        foreach (var (name, level) in doc.Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .Select(e => (Name: (string?)e.Attribute("name") ?? string.Empty, Level: (string?)e.Attribute("level") ?? "?")))
        {
            if (CreateOrWriteOnVersionOrAssignment.IsMatch(name))
            {
                found.Add($"{roleName}: {name} ({level}) — never (the version's Procedure link cannot be field-secured)");
            }
            else if (UntilTask191 && AnyOnProcedureTables.IsMatch(name))
            {
                found.Add($"{roleName}: {name} ({level}) — not until uac-r2 task 191");
            }
        }

        return found;
    }
}
