using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 120: only billing roles read rule sets (owner decision D-185, #2285), and the billing-admin marker (Write on
/// <c>sprk_ruleset</c>, D-76 / D-108, <c>BillingAdminAuthorization</c>) is held by exactly one source-controlled role.
/// <list type="bullet">
/// <item>No Spaarke role other than "Spaarke Billing Administrator" holds any privilege on <c>sprk_ruleset</c> (Core and Basic User lost
/// Read in task 120; Console, Ontology Administrator, Ontology Service and every other role hold none).</item>
/// <item>"Spaarke Billing Administrator" holds exactly Create, Read, Write, Append and AppendTo on <c>sprk_ruleset</c>, <c>sprk_Policy</c>
/// and <c>sprk_PolicyVersion</c> at Global, and nothing else beyond the platform defaults every Spaarke role export carries. No
/// Delete, Assign or Share; nothing on the Procedure tables (task 121 adds those after uac-r2 task 191).</item>
/// </list>
/// </summary>
/// <remarks>
/// Same scan as <see cref="ProcedureRolePrivilegeGuardTests"/>: every <c>src/dataverse/solutions/**/Roles/*.xml</c>, any depth, exact
/// match on <c>prv{Action}sprk_ruleset</c>, case-insensitive. The live half is <c>scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1
/// -Verify</c>. Per <c>tests/CLAUDE.md</c> "Structural fitness functions" this file is MAINTAIN-class.
/// </remarks>
public class RuleSetRolePrivilegeGuardTests
{
    private const string BillingAdministrator = "Spaarke Billing Administrator";

    private static readonly Regex AnyOnRuleSet = new(
        "^prv(Create|Read|Write|Delete|Append|AppendTo|Assign|Share)sprk_ruleset$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>D-76 / D-108: the billing-admin set, in the live privilege-name casing (as <c>BillingAdminAuthorization</c>).</summary>
    private static readonly string[] BillingAdminSet =
    {
        "prvCreatesprk_ruleset", "prvReadsprk_ruleset", "prvWritesprk_ruleset", "prvAppendsprk_ruleset", "prvAppendTosprk_ruleset",
        "prvCreatesprk_Policy", "prvReadsprk_Policy", "prvWritesprk_Policy", "prvAppendsprk_Policy", "prvAppendTosprk_Policy",
        "prvCreatesprk_PolicyVersion", "prvReadsprk_PolicyVersion", "prvWritesprk_PolicyVersion", "prvAppendsprk_PolicyVersion",
        "prvAppendTosprk_PolicyVersion",
    };

    /// <summary>
    /// What Dataverse puts on every role it creates or extends (plugin-metadata reads at create; <c>AddPrivilegesRole</c> re-injects the
    /// SharePoint four): docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md section 5.4, and every Spaarke role export in this folder.
    /// </summary>
    private static readonly string[] PlatformDefaults =
    {
        "prvReadPluginAssembly", "prvReadPluginType", "prvReadSdkMessage", "prvReadSdkMessageProcessingStep",
        "prvReadSdkMessageProcessingStepImage", "prvCreateSharePointData", "prvReadSharePointData", "prvWriteSharePointData",
        "prvReadSharePointDocument",
    };

    [Fact(DisplayName = "120 / D-185: only Spaarke Billing Administrator holds a privilege on sprk_ruleset")]
    public void SourceRolesGrantRuleSetPrivilegesOnlyToTheBillingAdministrator()
    {
        var roles = SourceRoles();
        Assert.Contains(roles, r => r.Name == BillingAdministrator); // non-vacuity: the one allowed holder is scanned

        var violations = roles.SelectMany(r => Violations(r.Xml).Select(v => $"{r.RelativePath}: {v}")).ToList();

        Assert.True(violations.Count == 0,
            "A Spaarke role other than Spaarke Billing Administrator grants a privilege on sprk_ruleset. Owner decision D-185 (#2285): only "
            + "billing roles read rule sets, and Write on sprk_ruleset is the billing-admin marker (D-76). Remove the grant, or take the change "
            + "to the owner:\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "120 / D-76 / D-108: Spaarke Billing Administrator holds exactly the billing-admin set at Global")]
    public void BillingAdministratorHoldsExactlyTheBillingAdminSetAtGlobal()
    {
        var role = Assert.Single(SourceRoles(), r => r.Name == BillingAdministrator);
        var held = Privileges(role.Xml);

        var missing = BillingAdminSet.Where(p => !held.Any(h => h.Name == p && h.Level == "Global")).ToList();
        var extra = held
            .Where(h => !BillingAdminSet.Contains(h.Name, StringComparer.Ordinal) && !PlatformDefaults.Contains(h.Name, StringComparer.Ordinal))
            .Select(h => $"{h.Name} ({h.Level})")
            .ToList();

        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"{role.RelativePath} must hold exactly Create, Read, Write, Append and AppendTo on sprk_ruleset, sprk_Policy and "
            + "sprk_PolicyVersion at Global (live name casing), plus only the platform defaults; no Delete, Assign or Share, and nothing on the "
            + "Procedure tables until task 121.\n  missing or not Global: " + string.Join(", ", missing)
            + "\n  not allowed: " + string.Join(", ", extra));
    }

    [Fact(DisplayName = "120 must-fire: Core User holding Read on sprk_ruleset is reported; a look-alike name is not")]
    public void GuardFiresWhenCoreUserHoldsRuleSetRead()
    {
        const string role = """
            <Role id="{75e1b627-2ab6-f111-aaad-0022482913fc}" name="Spaarke Core User">
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_ruleset" level="Deep" />
                <RolePrivilege name="prvReadsprk_rulesetx" level="Deep" />
                <RolePrivilege name="prvReadsprk_Policy" level="Deep" />
              </RolePrivileges>
            </Role>
            """;

        var found = Violations(role);

        var only = Assert.Single(found);
        Assert.Contains("Spaarke Core User", only, StringComparison.Ordinal);
        Assert.Contains("prvReadsprk_ruleset (Deep)", only, StringComparison.Ordinal);
    }

    private sealed record SourceRole(string RelativePath, string Name, string Xml);

    private static List<SourceRole> SourceRoles()
    {
        var root = Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions");
        var roles = ProcedureRolePrivilegeGuardTests.RoleFiles(root)
            .Select(path =>
            {
                var xml = File.ReadAllText(path);
                return new SourceRole(
                    Path.GetRelativePath(SourceScan.RepoRoot, path), (string?)XDocument.Parse(xml).Root?.Attribute("name") ?? string.Empty, xml);
            })
            .ToList();
        Assert.True(roles.Count >= 13,
            $"Scanner found only {roles.Count} role definitions under {root}/**/Roles (expected >= 13). Repo root resolved to '{SourceScan.RepoRoot}'.");
        return roles;
    }

    private static List<(string Name, string Level)> Privileges(string roleXml) =>
        XDocument.Parse(roleXml).Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .Select(e => (Name: (string?)e.Attribute("name") ?? string.Empty, Level: (string?)e.Attribute("level") ?? "?"))
            .ToList();

    /// <summary>The sprk_ruleset grants in one role definition held by a role other than the Billing Administrator, as "role: name (level)".</summary>
    internal static List<string> Violations(string roleXml)
    {
        var roleName = (string?)XDocument.Parse(roleXml).Root?.Attribute("name") ?? string.Empty;
        if (roleName == BillingAdministrator)
        {
            return new List<string>();
        }

        return Privileges(roleXml)
            .Where(p => AnyOnRuleSet.IsMatch(p.Name))
            .Select(p => $"{roleName}: {p.Name} ({p.Level})")
            .ToList();
    }
}
