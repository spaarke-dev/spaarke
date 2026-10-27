using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// The Spaarke end-user roles (<c>Spaarke Basic User</c>, <c>Spaarke Core User</c>, <c>Spaarke Console User</c>) must not hold
/// Read on <c>sprk_policy</c> or <c>sprk_policyversion</c> (<c>prvReadsprk_Policy</c>, <c>prvReadsprk_PolicyVersion</c>), at any depth
/// (D-185, #2285; removed on master by task 163, commit <c>accfc2d2f7</c>). Policy rows carry rule configuration that end users must
/// not read directly; the worklist reads the policy columns it needs as the BFF instead. Other roles (Ontology Administrator,
/// Ontology Service, billing admin) may hold the Reads, so this guard asserts nothing about them. AppendTo and Write are not Read
/// and are not reported (Console User holds AppendTo on both tables).
/// Enforced by: this guard (<c>Spaarke.ArchTests</c>, run by the <c>Stop</c> hook and CI).
/// </summary>
/// <remarks>
/// Scans the three named role files under <c>src/dataverse/solutions/SpaarkeMaster/Roles</c>, the only source-controlled role
/// definitions. Names match case-insensitively and exactly, so <c>prvReadsprk_PolicyX</c> is not a match. A missing, unparsable or
/// empty file fails the guard rather than passing it.
/// </remarks>
public class PolicyReadRoleGuardTests
{
    private static readonly string[] GuardedRoleFiles =
        ["Spaarke Basic User.xml", "Spaarke Core User.xml", "Spaarke Console User.xml"];

    internal static readonly string[] PolicyReadPrivileges = ["prvReadsprk_Policy", "prvReadsprk_PolicyVersion"];

    private static string RolesRoot =>
        Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Roles");

    [Fact(DisplayName = "Spaarke Basic/Core/Console User roles: none holds Read on sprk_policy or sprk_policyversion (D-185, #2285)")]
    public void EndUserRolesHoldNoPolicyRead()
    {
        var violations = new List<string>();
        foreach (var file in GuardedRoleFiles)
        {
            var path = Path.Combine(RolesRoot, file);
            Assert.True(File.Exists(path), $"Guarded role file not found: {path}. Repo root resolved to '{SourceScan.RepoRoot}'.");
            violations.AddRange(PolicyReadGrants(File.ReadAllText(path)).Select(v => $"{file}: {v}"));
        }

        Assert.True(violations.Count == 0,
            "An end-user role holds Read on sprk_policy / sprk_policyversion (D-185, #2285). The worklist reads policy columns as the "
            + "BFF, so end users need no Read. Remove the privilege:\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Policy-read role guard non-vacuity: each guarded role file exists, parses and holds privileges")]
    public void GuardedRoleFilesAreNotEmpty()
    {
        foreach (var file in GuardedRoleFiles)
        {
            var path = Path.Combine(RolesRoot, file);
            Assert.True(File.Exists(path), $"Guarded role file not found: {path}. Repo root resolved to '{SourceScan.RepoRoot}'.");
            var count = XDocument.Parse(File.ReadAllText(path)).Descendants()
                .Count(e => e.Name.LocalName == "RolePrivilege");
            Assert.True(count > 0, $"{file} parsed but holds no RolePrivilege, so the policy-read guard would pass vacuously.");
        }
    }

    [Fact(DisplayName = "Policy-read role guard control: Read on sprk_policy or sprk_policyversion is reported, at any depth")]
    public void GuardFiresOnPolicyRead()
    {
        const string role = """
            <Role id="{00000000-0000-0000-0000-000000000001}" name="Spaarke Example">
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_Policy" level="Basic" />
                <RolePrivilege name="prvReadsprk_PolicyVersion" level="Global" />
                <RolePrivilege name="prvReadsprk_signal" level="Basic" />
              </RolePrivileges>
            </Role>
            """;

        var found = PolicyReadGrants(role);

        Assert.Equal(2, found.Count);
        Assert.Contains(found, f => f.Contains("prvReadsprk_Policy (Basic)", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("prvReadsprk_PolicyVersion (Global)", StringComparison.Ordinal));

        const string deepRole = """
            <Role id="{00000000-0000-0000-0000-000000000003}" name="Spaarke Example">
              <RolePrivileges>
                <RolePrivilege name="PRVREADSPRK_POLICY" level="Deep" />
              </RolePrivileges>
            </Role>
            """;
        Assert.Single(PolicyReadGrants(deepRole));
    }

    [Fact(DisplayName = "Policy-read role guard control: Write, AppendTo and look-alike names are not reported")]
    public void GuardIgnoresNonReadAndLookAlikes()
    {
        const string role = """
            <Role id="{00000000-0000-0000-0000-000000000002}" name="Spaarke Console User">
              <!-- prvReadsprk_Policy is deliberately absent -->
              <RolePrivileges>
                <RolePrivilege name="prvWritesprk_Policy" level="Deep" />
                <RolePrivilege name="prvAppendTosprk_Policy" level="Deep" />
                <RolePrivilege name="prvAppendTosprk_PolicyVersion" level="Deep" />
                <RolePrivilege name="prvReadsprk_PolicyX" level="Deep" />
                <RolePrivilege name="xprvReadsprk_Policy" level="Deep" />
                <RolePrivilege name="prvReadsprk_PolicyVersionX" level="Deep" />
              </RolePrivileges>
            </Role>
            """;

        Assert.Empty(PolicyReadGrants(role));
    }

    /// <summary>Every <c>RolePrivilege</c> naming a policy Read exactly (case-insensitive), as "name (level)".</summary>
    internal static List<string> PolicyReadGrants(string roleXml) =>
        XDocument.Parse(roleXml)
            .Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .Select(e => (Name: (string?)e.Attribute("name") ?? string.Empty, Level: (string?)e.Attribute("level") ?? "?"))
            .Where(p => PolicyReadPrivileges.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            .Select(p => $"{p.Name} ({p.Level})")
            .ToList();
}
