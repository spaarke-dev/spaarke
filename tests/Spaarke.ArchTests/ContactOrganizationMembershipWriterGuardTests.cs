using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Contact-organization membership writes belong to the Spaarke Access Administrator role (owner decision 2026-10-28,
/// issue #2382). No other role in the solution may hold Create, Write, Delete, Assign or Append on
/// <c>sprk_contactorganization</c>; reading memberships stays open. The platform roles (System Administrator, System
/// Customizer) are not in the solution and are not checked here. The declared set is
/// <c>config/contact-organization-membership-role.json</c>.
/// </summary>
public class ContactOrganizationMembershipWriterGuardTests
{
    private static string RolesDir => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Roles");
    private static string ConfigPath => Path.Combine(SourceScan.RepoRoot, "config", "contact-organization-membership-role.json");

    private static readonly string[] WriteTypes = ["prvCreate", "prvWrite", "prvDelete", "prvAssign", "prvAppend"];
    private const string Suffix = "sprk_ContactOrganization";

    [Fact(DisplayName = "Only Spaarke Access Administrator writes contact-organization memberships (#2382)")]
    public void OnlyTheWriterRoleWritesMemberships()
    {
        var writer = WriterRoleName();
        var roles = Directory.EnumerateFiles(RolesDir, "*.xml").ToDictionary(f => Path.GetFileNameWithoutExtension(f)!, File.ReadAllText);
        Assert.True(roles.Count >= 10, $"Found only {roles.Count} role files under {RolesDir}.");
        Assert.Contains(writer, roles.Keys);

        var offenders = roles.Where(r => r.Key != writer).SelectMany(r => Writes(r.Value).Select(p => $"{r.Key}: {p}")).ToList();

        Assert.True(offenders.Count == 0,
            "A role other than Spaarke Access Administrator can write contact-organization memberships. Remove the privilege "
            + "(config/contact-organization-membership-role.json):\n  " + string.Join("\n  ", offenders));
    }

    [Fact(DisplayName = "Spaarke Access Administrator holds Create, Read, Write and Append on memberships at Global")]
    public void WriterRoleHoldsTheMembershipPrivileges()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        var required = doc.RootElement.GetProperty("writerRole").GetProperty("privileges").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(4, required.Count);

        var held = Levels(File.ReadAllText(Path.Combine(RolesDir, WriterRoleName() + ".xml")));
        foreach (var p in required)
            Assert.True(held.TryGetValue(p, out var level) && level == "Global", $"Spaarke Access Administrator lacks {p} at Global.");
    }

    [Fact(DisplayName = "Membership writer guard control: Core User with Create or Write re-added is reported; Read and Share are not")]
    public void GuardFiresWhenCoreUserRegainsAWrite()
    {
        var core = File.ReadAllText(Path.Combine(RolesDir, "Spaarke Core User.xml"));
        Assert.Empty(Writes(core));
        Assert.True(Levels(core).ContainsKey("prvReadsprk_ContactOrganization"), "Core User should keep Read on memberships.");

        foreach (var privilege in new[] { "prvCreatesprk_ContactOrganization", "prvWritesprk_ContactOrganization" })
        {
            var doc = XDocument.Parse(core);
            doc.Descendants().First(e => e.Name.LocalName == "RolePrivileges")
                .Add(new XElement("RolePrivilege", new XAttribute("name", privilege), new XAttribute("level", "Global")));
            Assert.Equal([privilege], Writes(doc.ToString()));
        }

        const string readOnly = """
            <Role name="x"><RolePrivileges>
              <RolePrivilege name="prvReadsprk_ContactOrganization" level="Global" />
              <RolePrivilege name="prvSharesprk_ContactOrganization" level="Global" />
              <RolePrivilege name="prvAppendTosprk_ContactOrganization" level="Global" />
              <RolePrivilege name="prvWritesprk_Organization" level="Global" />
            </RolePrivileges></Role>
            """;
        Assert.Empty(Writes(readOnly));
    }

    private static List<string> Writes(string roleXml)
    {
        var writes = WriteTypes.Select(t => t + Suffix).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Levels(roleXml).Keys.Where(writes.Contains).ToList();
    }

    private static Dictionary<string, string> Levels(string roleXml) =>
        XDocument.Parse(roleXml).Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .GroupBy(e => (string?)e.Attribute("name") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (string?)g.First().Attribute("level") ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private static string WriterRoleName()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        return doc.RootElement.GetProperty("writerRole").GetProperty("roleName").GetString()!;
    }
}
