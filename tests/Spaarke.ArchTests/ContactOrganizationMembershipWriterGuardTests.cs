using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Contact-organization membership writes belong to the Spaarke Access Administrator role (owner decision 2026-10-28,
/// issue #2382). No other role in the solution may hold Create, Write, Delete, Assign or Append on
/// <c>sprk_contactorganization</c>, and the read-only roles (Core User, Basic User, Internal Contact User (Workforce)) hold
/// ONLY Read and Share on it (owner rounds 132 and 134): AppendTo is disallowed for them too, matching
/// <c>removedFromReadOnlyRoles</c> in the config. The platform roles (System Administrator, System Customizer, Service
/// Writer) are not in the solution and are not checked here; the dev script checks them live against the config's
/// allowlist. The declared set is <c>config/contact-organization-membership-role.json</c>.
/// </summary>
public class ContactOrganizationMembershipWriterGuardTests
{
    private static string RolesDir => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Roles");
    private static string ConfigPath => Path.Combine(SourceScan.RepoRoot, "config", "contact-organization-membership-role.json");

    private static readonly string[] WriteTypes = ["prvCreate", "prvWrite", "prvDelete", "prvAssign", "prvAppend"];
    private static readonly string[] ReadOnlyAllowed = ["prvRead", "prvShare"];
    private const string Suffix = "sprk_ContactOrganization";

    [Fact(DisplayName = "Only Spaarke Access Administrator writes contact-organization memberships (#2382)")]
    public void OnlyTheWriterRoleWritesMemberships()
    {
        var writer = WriterRoleName();
        var roles = Directory.EnumerateFiles(RolesDir, "*.xml").ToDictionary(f => Path.GetFileNameWithoutExtension(f)!, File.ReadAllText);
        Assert.True(roles.Count >= 10, $"Found only {roles.Count} role files under {RolesDir}.");
        Assert.Contains(writer, roles.Keys);

        var readOnly = ReadOnlyRoleNames();
        var offenders = roles.Where(r => r.Key != writer)
            .SelectMany(r => Offences(r.Key, r.Value, readOnly).Select(p => $"{r.Key}: {p}")).ToList();

        Assert.True(offenders.Count == 0,
            "A role other than Spaarke Access Administrator holds a membership privilege it may not hold (a read-only role "
            + "keeps only Read and Share). Remove the privilege (config/contact-organization-membership-role.json):\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact(DisplayName = "The config's removedFromReadOnlyRoles is every membership privilege except Read and Share")]
    public void ConfigRemovesEverythingButReadAndShareFromTheReadOnlyRoles()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        var removed = doc.RootElement.GetProperty("removedFromReadOnlyRoles").EnumerateArray().Select(e => e.GetString()!)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        var expected = new[] { "prvCreate", "prvWrite", "prvDelete", "prvAssign", "prvAppend", "prvAppendTo" }.Select(t => t + Suffix)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(expected, removed, StringComparer.OrdinalIgnoreCase);
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

    [Fact(DisplayName = "Membership writer guard control: Core User with Create, Write or AppendTo re-added is reported; Read and Share are not")]
    public void GuardFiresWhenCoreUserRegainsAWrite()
    {
        var readOnlyRoles = ReadOnlyRoleNames();
        const string coreName = "Spaarke Core User";
        Assert.Contains(coreName, readOnlyRoles);
        var core = File.ReadAllText(Path.Combine(RolesDir, coreName + ".xml"));
        Assert.Empty(Offences(coreName, core, readOnlyRoles));
        Assert.True(Levels(core).ContainsKey("prvReadsprk_ContactOrganization"), "Core User should keep Read on memberships.");

        foreach (var privilege in new[] { "prvCreatesprk_ContactOrganization", "prvWritesprk_ContactOrganization", "prvAppendTosprk_ContactOrganization" })
        {
            var doc = XDocument.Parse(core);
            doc.Descendants().First(e => e.Name.LocalName == "RolePrivileges")
                .Add(new XElement("RolePrivilege", new XAttribute("name", privilege), new XAttribute("level", "Global")));
            Assert.Equal([privilege], Offences(coreName, doc.ToString(), readOnlyRoles));
        }

        const string readOnly = """
            <Role name="x"><RolePrivileges>
              <RolePrivilege name="prvReadsprk_ContactOrganization" level="Global" />
              <RolePrivilege name="prvSharesprk_ContactOrganization" level="Global" />
              <RolePrivilege name="prvAppendTosprk_ContactOrganization" level="Global" />
              <RolePrivilege name="prvWritesprk_Organization" level="Global" />
            </RolePrivileges></Role>
            """;
        // A read-only role: AppendTo on memberships is reported; Read, Share and another table's Write are not.
        Assert.Equal(["prvAppendTosprk_ContactOrganization"], Offences(coreName, readOnly, readOnlyRoles));
        // Any other non-writer role: only the write privileges are checked (AppendTo alone confers no membership write).
        Assert.Empty(Offences("Spaarke Some Other Role", readOnly, readOnlyRoles));
    }

    /// <summary>
    /// The membership privileges a non-writer role may not hold: for a read-only role, everything but Read and Share; for any
    /// other role, Create, Write, Delete, Assign and Append.
    /// </summary>
    private static List<string> Offences(string roleName, string roleXml, IReadOnlyCollection<string> readOnlyRoles)
    {
        var held = Levels(roleXml).Keys.Where(k => k.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (readOnlyRoles.Contains(roleName, StringComparer.Ordinal))
        {
            var allowed = ReadOnlyAllowed.Select(t => t + Suffix).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return held.Where(k => !allowed.Contains(k)).ToList();
        }

        var writes = WriteTypes.Select(t => t + Suffix).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return held.Where(writes.Contains).ToList();
    }

    private static List<string> ReadOnlyRoleNames()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        var names = doc.RootElement.GetProperty("readOnlyRoles").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(3, names.Count);
        return names;
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
