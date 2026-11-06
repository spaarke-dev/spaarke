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
/// <item>"Spaarke Billing Administrator" holds exactly Read at Deep (EB-D-162) and Create, Write, Append and AppendTo at Basic on
/// <c>sprk_ruleset</c>, <c>sprk_Policy</c> and <c>sprk_PolicyVersion</c>, and nothing else beyond the platform defaults every Spaarke role
/// export carries. No Delete, Assign or Share; nothing on the Procedure tables (task 121 adds those after uac-r2 task 191). Basic, not
/// Global (uac-r2 review of task 120): the BFF asks for Create / Write only as markers at any depth (D-108) and writes these tables
/// app-only (D-173); at Global a billing admin could create or edit any rule set or policy natively, past RuleSetService (ADR-002).</item>
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

    /// <summary>
    /// D-76 / D-108 with uac-r2's depths: name (live privilege-name casing, as <c>BillingAdminAuthorization</c>) -> level. Read is Deep;
    /// the markers are Basic. Global Read (task 120, kept by EB-D-150) reached the Secure Record business unit under every layout, and with
    /// task 089 a Secure record's rule set and record rules live there; the owner lowered it to Deep (EB-D-162, 2026-10-27), which stops at
    /// the admin's business-unit subtree once UAC design section 5.2 makes Secure a sibling.
    /// </summary>
    private static readonly Dictionary<string, string> BillingAdminSet = new(StringComparer.Ordinal)
    {
        ["prvReadsprk_ruleset"] = "Deep", ["prvReadsprk_Policy"] = "Deep", ["prvReadsprk_PolicyVersion"] = "Deep",
        ["prvCreatesprk_ruleset"] = "Basic", ["prvWritesprk_ruleset"] = "Basic", ["prvAppendsprk_ruleset"] = "Basic", ["prvAppendTosprk_ruleset"] = "Basic",
        ["prvCreatesprk_Policy"] = "Basic", ["prvWritesprk_Policy"] = "Basic", ["prvAppendsprk_Policy"] = "Basic", ["prvAppendTosprk_Policy"] = "Basic",
        ["prvCreatesprk_PolicyVersion"] = "Basic", ["prvWritesprk_PolicyVersion"] = "Basic", ["prvAppendsprk_PolicyVersion"] = "Basic",
        ["prvAppendTosprk_PolicyVersion"] = "Basic",
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

    [Fact(DisplayName = "120 / D-76 / D-108: Spaarke Billing Administrator holds exactly the billing-admin set (Read Deep, markers Basic)")]
    public void BillingAdministratorHoldsExactlyTheBillingAdminSetAtItsDepths()
    {
        var role = Assert.Single(SourceRoles(), r => r.Name == BillingAdministrator);
        var (missing, extra) = ExactSetFindings(role.Xml);

        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"{role.RelativePath} must hold exactly Read at Deep (EB-D-162) and Create, Write, Append and AppendTo at Basic on sprk_ruleset, sprk_Policy "
            + "and sprk_PolicyVersion (live name casing; uac-r2: Global markers would let a billing admin write past RuleSetService), plus only "
            + "the platform defaults; no Delete, Assign or Share, and nothing on the Procedure tables until task 121.\n  missing or at another "
            + "depth: " + string.Join(", ", missing)
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

    [Fact(DisplayName = "120 must-fire: the Billing Administrator exact set reports a wrong depth and an extra privilege; the exact set does not")]
    public void ExactSetCheckFiresOnAWrongDepthAndAnExtraPrivilege()
    {
        var exact = BillingAdminSet.Select(p => (p.Key, p.Value)).Concat(PlatformDefaults.Select(d => (d, "Global"))).ToList();

        var clean = ExactSetFindings(BillingAdminRole(exact));
        Assert.Empty(clean.Missing);
        Assert.Empty(clean.Extra);

        var wrongDepth = ExactSetFindings(BillingAdminRole(exact.Select(p => p.Item1 == "prvReadsprk_Policy" ? (p.Item1, "Global") : p).ToList()));
        Assert.Equal("prvReadsprk_Policy at Deep (held at Global)", Assert.Single(wrongDepth.Missing));
        Assert.Empty(wrongDepth.Extra);

        var extraPrivilege = ExactSetFindings(BillingAdminRole(exact.Append(("prvDeletesprk_ruleset", "Basic")).ToList()));
        Assert.Empty(extraPrivilege.Missing);
        Assert.Equal("prvDeletesprk_ruleset (Basic)", Assert.Single(extraPrivilege.Extra));
    }

    [Fact(DisplayName = "120 must-not-fire: the holder check skips the Billing Administrator role by name")]
    public void HolderCheckSkipsTheBillingAdministrator()
    {
        var role = BillingAdminRole(new List<(string, string)> { ("prvReadsprk_ruleset", "Global"), ("prvWritesprk_ruleset", "Basic") });

        Assert.Empty(Violations(role));
        Assert.Equal(2, Violations(role.Replace(BillingAdministrator, "Spaarke Billing Administrator Copy", StringComparison.Ordinal)).Count);
    }

    private static string BillingAdminRole(IEnumerable<(string Name, string Level)> privileges) =>
        new XElement("Role", new XAttribute("name", BillingAdministrator),
            new XElement("RolePrivileges", privileges.Select(p =>
                new XElement("RolePrivilege", new XAttribute("name", p.Name), new XAttribute("level", p.Level))))).ToString();

    /// <summary>The Billing Administrator exact-set check on one role definition: set members missing or at another depth, and privileges
    /// outside the set and the platform defaults.</summary>
    private static (List<string> Missing, List<string> Extra) ExactSetFindings(string roleXml)
    {
        var held = Privileges(roleXml);
        var missing = BillingAdminSet
            .Where(p => !held.Any(h => h.Name == p.Key && h.Level == p.Value))
            .Select(p => $"{p.Key} at {p.Value}{string.Concat(held.Where(h => h.Name == p.Key).Select(h => $" (held at {h.Level})"))}")
            .ToList();
        var extra = held
            .Where(h => !BillingAdminSet.ContainsKey(h.Name) && !PlatformDefaults.Contains(h.Name, StringComparer.Ordinal))
            .Select(h => $"{h.Name} ({h.Level})")
            .ToList();
        return (missing, extra);
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
