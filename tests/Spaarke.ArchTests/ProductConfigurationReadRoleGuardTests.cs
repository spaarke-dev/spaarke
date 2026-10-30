using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Owner decision 2026-10-29 (dev BU1 move plan Q1): <c>Spaarke Core User</c> and <c>Spaarke Basic User</c> read the Spaarke
/// PRODUCT configuration tables at Global, because product data is owned in the root business unit (owner round 136) and a
/// Read at Deep does not reach it from a customer unit. <c>config/product-configuration-read-role.json</c> is the one declared
/// list. This guard keeps it from drifting:
/// <list type="bullet">
/// <item>the file asks for Read at Global for exactly those two roles, and every table carries a reason and evidence;</item>
/// <item>no listed table is customer data or restricted content: not a hard-deny table
/// (<see cref="RecordOwnershipResolver.RestrictedContentTables"/>), an ownership parent
/// (<see cref="RecordOwnershipResolver.OwnershipParentEntities"/>), a secure root or secure-child table
/// (<see cref="SecureChildLineage"/>), a per-user table (<see cref="OwnedChildWrite.PerUserTables"/>), a table the Secure
/// Record census guards (<c>config/secure-record-owner-role.json</c>) or a customer configuration table
/// (<c>config/customer-configuration-owner-role.json</c>); no listed Read is one of
/// <see cref="SecureBuRoleDepthAssertion.GuardedPrivileges"/>;</item>
/// <item>both role XML files (the source of truth a SpaarkeMaster import ships) hold each listed Read at Global.</item>
/// </list>
/// Enforced by: this guard (<c>Spaarke.ArchTests</c>, run by the <c>Stop</c> hook and CI).
/// </summary>
public class ProductConfigurationReadRoleGuardTests
{
    private const string ConfigFile = "product-configuration-read-role.json";

    internal static readonly string[] Roles = ["Spaarke Core User", "Spaarke Basic User"];

    private static string RolesRoot =>
        Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Roles");

    private static string ReadConfig(string file) => File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", file));

    /// <summary>Every table that must never get Global Read through this list, with the reason, from the BFF's own sets
    /// and the declared role files (never copied).</summary>
    internal static IReadOnlyDictionary<string, string> ForbiddenTables()
    {
        var forbidden = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(IEnumerable<string> tables, string why)
        {
            foreach (var t in tables)
                forbidden.TryAdd(t, why);
        }

        Add(RecordOwnershipResolver.RestrictedContentTables, "a hard-deny restricted-content table (RecordOwnershipResolver.RestrictedContentTables)");
        Add(SecureChildLineage.Roots, "a secure root (SecureChildLineage.Roots)");
        Add(RecordOwnershipResolver.OwnershipParentEntities, "a customer record table (RecordOwnershipResolver.OwnershipParentEntities)");
        Add(SecureChildLineage.Children.Keys, "a secure-child table (SecureChildLineage.Children)");
        Add(OwnedChildWrite.PerUserTables, "a per-user table (OwnedChildWrite.PerUserTables)");
        Add(DeclaredTables(ReadConfig("secure-record-owner-role.json")), "a table the Secure Record census guards (config/secure-record-owner-role.json)");
        Add(DeclaredTables(ReadConfig("customer-configuration-owner-role.json")), "a customer configuration table (config/customer-configuration-owner-role.json)");
        return forbidden;
    }

    private static IEnumerable<string> DeclaredTables(string json) =>
        (JsonNode.Parse(json)?["tables"] as JsonArray ?? [])
            .Select(n => (string?)n?["logicalName"])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!);

    [Fact(DisplayName = "Product configuration read list: the live file is Read at Global, for Core and Basic User, with no customer or restricted table")]
    public void LiveConfigHasNoFindings()
    {
        var findings = ConfigFindings(ReadConfig(ConfigFile), ForbiddenTables());

        Assert.True(findings.Count == 0,
            $"config/{ConfigFile} breaks the product configuration read rule (owner decision 2026-10-29, BU1 plan Q1):\n  "
            + string.Join("\n  ", findings));
    }

    [Fact(DisplayName = "Product configuration read list: both role XML files hold every listed Read at Global")]
    public void RoleXmlHoldsEveryListedReadAtGlobal()
    {
        var privileges = ListedPrivileges(ReadConfig(ConfigFile));
        Assert.NotEmpty(privileges);

        var violations = new List<string>();
        foreach (var role in Roles)
        {
            var path = Path.Combine(RolesRoot, $"{role}.xml");
            Assert.True(File.Exists(path), $"Role file not found: {path}. Repo root resolved to '{SourceScan.RepoRoot}'.");
            var xml = File.ReadAllText(path);
            Assert.True(XDocument.Parse(xml).Descendants().Count(e => e.Name.LocalName == "RolePrivilege") > 0,
                $"{role}.xml parsed but holds no RolePrivilege, so this guard would pass vacuously.");
            violations.AddRange(XmlFindings(xml, privileges).Select(v => $"{role}.xml: {v}"));
        }

        Assert.True(violations.Count == 0,
            $"config/{ConfigFile} lists a table the role XML does not read at Global. Change the XML in the same commit:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Product configuration read guard non-vacuity: every exclusion source contributes tables")]
    public void ForbiddenSetIsNotVacuous()
    {
        var forbidden = ForbiddenTables();
        foreach (var table in new[]
                 {
                     "sprk_policy", "sprk_storedfile", "sprk_matter", "sprk_document", "sprk_analysisoutput", "sprk_navitem",
                     "sprk_externalrecordaccess", "sprk_fieldmappingrule",
                 })
        {
            Assert.True(forbidden.ContainsKey(table), $"The forbidden set lost '{table}'; a source it is built from is empty or renamed.");
        }
    }

    [Fact(DisplayName = "Product configuration read guard control: customer, restricted and per-user tables are reported")]
    public void GuardFiresOnForbiddenTables()
    {
        string[] seeded =
        [
            "sprk_policy",                 // hard-deny
            "sprk_storedfile",             // hard-deny
            "sprk_matter",                 // secure root
            "sprk_document",               // ownership parent
            "sprk_analysisoutput",         // secure child only
            "sprk_navitem",                // per-user
            "sprk_externalrecordaccess",   // census-guarded
            "sprk_fieldmappingrule",       // customer configuration
        ];
        var json = Config(tables: seeded.Select(t => Table(t, $"prvRead{t}")).ToArray());

        var findings = ConfigFindings(json, ForbiddenTables());

        foreach (var table in seeded)
            Assert.Contains(findings, f => f.StartsWith($"{table}:", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Product configuration read guard control: a census-guarded Read privilege is reported")]
    public void GuardFiresOnGuardedPrivilege()
    {
        var json = Config(tables: [Table("sprk_example", "prvReadsprk_Matter")]);

        var findings = ConfigFindings(json, new Dictionary<string, string>());

        Assert.Contains(findings, f => f.Contains("prvReadsprk_Matter", StringComparison.Ordinal) && f.Contains("census", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Product configuration read guard control: a wrong depth, access, role set or entry shape is reported")]
    public void GuardFiresOnWrongShape()
    {
        var none = new Dictionary<string, string>();

        Assert.Contains(ConfigFindings(Config(depth: "Deep"), none), f => f.Contains("depth", StringComparison.Ordinal));
        Assert.Contains(ConfigFindings(Config(access: "Write"), none), f => f.Contains("access", StringComparison.Ordinal));
        Assert.Contains(ConfigFindings(Config(roles: ["Spaarke Core User", "Spaarke Basic User", "Spaarke Console User"]), none),
            f => f.Contains("roles", StringComparison.Ordinal));
        Assert.Contains(ConfigFindings(Config(roles: ["Spaarke Core User"]), none), f => f.Contains("roles", StringComparison.Ordinal));
        Assert.Contains(ConfigFindings(Config(tables: [Table("sprk_aitooltype", "prvReadsprk_AIToolType", reason: "")]), none),
            f => f.Contains("reason", StringComparison.Ordinal));
        Assert.Contains(ConfigFindings(Config(tables: [Table("sprk_aitooltype", "prvWritesprk_AIToolType")]), none),
            f => f.Contains("not a Read privilege", StringComparison.Ordinal));
        Assert.Contains(ConfigFindings(Config(tables: [Table("sprk_aitooltype", "prvReadsprk_AIToolType"), Table("SPRK_AITOOLTYPE", "prvReadsprk_AIToolType")]), none),
            f => f.Contains("twice", StringComparison.Ordinal));
        Assert.Contains(ConfigFindings(Config(excluded: ["sprk_aitooltype"]), none), f => f.Contains("excluded", StringComparison.Ordinal));
        Assert.Contains(ConfigFindings(Config(tables: []), none), f => f.Contains("tables", StringComparison.Ordinal));
        Assert.StartsWith("not JSON", Assert.Single(ConfigFindings("{", none)), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Product configuration read guard control: a valid file passes (must not fire)")]
    public void GuardPassesValidConfig()
    {
        Assert.Empty(ConfigFindings(Config(), ForbiddenTables()));
    }

    [Fact(DisplayName = "Product configuration read guard control: role XML at Deep or without the Read is reported; Global passes")]
    public void XmlGuardFiresBelowGlobal()
    {
        const string role = """
            <Role id="{00000000-0000-0000-0000-000000000001}" name="Spaarke Example">
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_AIToolType" level="Deep" />
                <RolePrivilege name="PRVREADSPRK_GRIDCONFIGURATION" level="Global" />
                <RolePrivilege name="prvReadsprk_AIOutputTypeX" level="Global" />
              </RolePrivileges>
            </Role>
            """;

        var findings = XmlFindings(role, ["prvReadsprk_AIToolType", "prvReadsprk_GridConfiguration", "prvReadsprk_AIOutputType"]);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Contains("prvReadsprk_AIToolType is 'Deep'", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Contains("prvReadsprk_AIOutputType is 'absent'", StringComparison.Ordinal));
        Assert.Empty(XmlFindings(role, ["prvReadsprk_GridConfiguration"]));
    }

    /// <summary>Every way <paramref name="json"/> breaks the rule; empty when it holds.</summary>
    internal static List<string> ConfigFindings(string json, IReadOnlyDictionary<string, string> forbidden)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return [$"not JSON: {ex.Message}"];
        }

        if (root is not JsonObject file)
            return ["the document is not an object"];

        var findings = new List<string>();
        if (file["schemaVersion"] is not JsonValue version || !version.TryGetValue<int>(out var number) || number != 1)
            findings.Add("schemaVersion is not 1");
        if ((string?)file["access"] != "Read")
            findings.Add($"access is '{(string?)file["access"]}', not Read");
        if ((string?)file["depth"] != "Global")
            findings.Add($"depth is '{(string?)file["depth"]}', not Global");

        var roles = (file["roles"] as JsonArray ?? []).Select(r => (string?)r ?? string.Empty).ToList();
        if (roles.Count != Roles.Length || !Roles.All(roles.Contains))
            findings.Add($"roles is [{string.Join(", ", roles)}], not exactly [{string.Join(", ", Roles)}]");

        if (file["tables"] is not JsonArray tables || tables.Count == 0)
        {
            findings.Add("tables is missing or empty");
            return findings;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in tables)
        {
            var table = (string?)node?["logicalName"] ?? string.Empty;
            var privilege = (string?)node?["privilegeName"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(table))
            {
                findings.Add("a tables entry has no logicalName");
                continue;
            }

            if (!seen.Add(table))
                findings.Add($"{table}: listed twice");
            foreach (var field in new[] { "privilegeName", "reason", "evidence" })
            {
                if (string.IsNullOrWhiteSpace((string?)node?[field]))
                    findings.Add($"{table}: no {field}");
            }

            if (!privilege.StartsWith("prvRead", StringComparison.OrdinalIgnoreCase))
                findings.Add($"{table}: '{privilege}' is not a Read privilege");
            if (forbidden.TryGetValue(table, out var why))
                findings.Add($"{table}: {why}; it never gets Global Read");
            if (SecureBuRoleDepthAssertion.GuardedPrivileges.Contains(privilege, StringComparer.OrdinalIgnoreCase))
                findings.Add($"{table}: {privilege} is a privilege the Secure Record census guards (SecureBuRoleDepthAssertion.GuardedPrivileges)");
        }

        foreach (var node in file["excluded"] as JsonArray ?? [])
        {
            var table = (string?)node?["logicalName"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace((string?)node?["reason"]))
                findings.Add($"an excluded entry ('{table}') has no logicalName or no reason");
            else if (seen.Contains(table))
                findings.Add($"{table}: both listed and excluded");
        }

        return findings;
    }

    /// <summary>Each listed privilege the role XML does not hold at Global (exact, case-insensitive name).</summary>
    internal static List<string> XmlFindings(string roleXml, IEnumerable<string> privileges)
    {
        var levels = XDocument.Parse(roleXml).Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .GroupBy(e => (string?)e.Attribute("name") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (string?)g.Last().Attribute("level") ?? "?", StringComparer.OrdinalIgnoreCase);
        return privileges
            .Select(p => (Name: p, Level: levels.TryGetValue(p, out var l) ? l : "absent"))
            .Where(p => p.Level != "Global")
            .Select(p => $"{p.Name} is '{p.Level}', not Global")
            .ToList();
    }

    private static List<string> ListedPrivileges(string json) =>
        (JsonNode.Parse(json)?["tables"] as JsonArray ?? [])
            .Select(n => (string?)n?["privilegeName"])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

    private static JsonObject Table(string table, string privilege, string reason = "a reason", string evidence = "evidence") =>
        new() { ["logicalName"] = table, ["privilegeName"] = privilege, ["reason"] = reason, ["evidence"] = evidence };

    private static string Config(
        string access = "Read",
        string depth = "Global",
        string[]? roles = null,
        JsonObject[]? tables = null,
        string[]? excluded = null)
    {
        var file = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["roles"] = new JsonArray((roles ?? Roles).Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
            ["access"] = access,
            ["depth"] = depth,
            ["tables"] = new JsonArray((tables ?? [Table("sprk_aitooltype", "prvReadsprk_AIToolType")]).Select(t => (JsonNode)t).ToArray()),
            ["excluded"] = new JsonArray((excluded ?? ["sprk_container"])
                .Select(e => (JsonNode)new JsonObject { ["logicalName"] = e, ["reason"] = "customer data" }).ToArray()),
        };
        return file.ToJsonString();
    }
}
