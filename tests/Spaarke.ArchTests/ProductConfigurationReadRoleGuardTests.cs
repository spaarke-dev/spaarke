using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
/// (<c>config/customer-configuration-owner-role.json</c>) or platform service configuration (<see cref="PlatformServiceTables"/>);
/// no listed Read is one of <see cref="SecureBuRoleDepthAssertion.GuardedPrivileges"/>;</item>
/// <item>each entry's privilege is exactly the table's own Read (<c>prvRead</c> + logical name, any letter case);</item>
/// <item>both role XML files (the source of truth a SpaarkeMaster import ships) hold each listed table's own Read at Global,
/// and no Create on a <see cref="NoCreateTables"/> table.</item>
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
        Add(PlatformServiceTables, "platform service configuration (PlatformServiceTables), not product configuration");
        return forbidden;
    }

    /// <summary>
    /// Platform service configuration: external service connections, Dataverse environment registrations and AI model
    /// deployments. Not product configuration, and never on this list. The ONE list is <c>$PlatformServiceTables</c> in
    /// <c>scripts/Set-ProductConfigurationReadDepth.ps1</c>, which refuses these tables in every mode; this guard reads it
    /// from there (<see cref="ParsePlatformServiceTables"/>), so the build and the script cannot disagree.
    /// </summary>
    internal static readonly string[] PlatformServiceTables =
        ParsePlatformServiceTables(File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "scripts", "Set-ProductConfigurationReadDepth.ps1")));

    /// <summary>
    /// The tables of the script's one <c>$PlatformServiceTables = @('a', 'b', ...)</c> assignment, lower case. Throws when
    /// there is not exactly one such assignment or it names no table, so a renamed or emptied list fails the build.
    /// </summary>
    internal static string[] ParsePlatformServiceTables(string scriptText)
    {
        var matches = Regex.Matches(scriptText, @"(?m)^\s*\$PlatformServiceTables\s*=\s*@\(([^)]*)\)");
        if (matches.Count != 1)
            throw new InvalidOperationException($"Set-ProductConfigurationReadDepth.ps1 must assign $PlatformServiceTables exactly once; found {matches.Count}.");

        var tables = Regex.Matches(matches[0].Groups[1].Value, @"'([^']+)'")
            .Select(m => m.Groups[1].Value.Trim().ToLowerInvariant())
            .ToArray();
        if (tables.Length == 0)
            throw new InvalidOperationException("Set-ProductConfigurationReadDepth.ps1 assigns $PlatformServiceTables no table.");
        return tables;
    }

    /// <summary>
    /// Tables the two roles read at Global through this list and must hold NO Create privilege on, at any depth: a row a
    /// user created there would be readable by every user.
    /// </summary>
    internal static readonly string[] NoCreateTables = ["sprk_gridconfiguration"];

    /// <summary>The Read privilege of <paramref name="table"/>: <c>prvRead</c> + the logical name (compared case-insensitively).</summary>
    internal static string ReadPrivilegeOf(string table) => "prvRead" + table;

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
        var tables = ListedTables(ReadConfig(ConfigFile));
        Assert.NotEmpty(tables);

        var violations = new List<string>();
        foreach (var role in Roles)
        {
            var path = Path.Combine(RolesRoot, $"{role}.xml");
            Assert.True(File.Exists(path), $"Role file not found: {path}. Repo root resolved to '{SourceScan.RepoRoot}'.");
            var xml = File.ReadAllText(path);
            Assert.True(XDocument.Parse(xml).Descendants().Count(e => e.Name.LocalName == "RolePrivilege") > 0,
                $"{role}.xml parsed but holds no RolePrivilege, so this guard would pass vacuously.");
            violations.AddRange(XmlFindings(xml, tables).Select(v => $"{role}.xml: {v}"));
        }

        Assert.True(violations.Count == 0,
            $"config/{ConfigFile} lists a table the role XML does not read at Global. Change the XML in the same commit:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Product configuration read list: Core and Basic User hold no Create on a Global-read table that forbids it")]
    public void RoleXmlHoldsNoCreateOnNoCreateTables()
    {
        var violations = new List<string>();
        foreach (var role in Roles)
        {
            var path = Path.Combine(RolesRoot, $"{role}.xml");
            Assert.True(File.Exists(path), $"Role file not found: {path}. Repo root resolved to '{SourceScan.RepoRoot}'.");
            violations.AddRange(CreateFindings(File.ReadAllText(path), NoCreateTables).Select(v => $"{role}.xml: {v}"));
        }

        Assert.True(violations.Count == 0,
            "A role reads these tables at Global, so it must not create rows in them:\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Product configuration read guard non-vacuity: every exclusion source contributes tables")]
    public void ForbiddenSetIsNotVacuous()
    {
        var forbidden = ForbiddenTables();
        foreach (var table in new[]
                 {
                     "sprk_policy", "sprk_storedfile", "sprk_matter", "sprk_document", "sprk_analysisoutput", "sprk_navitem",
                     "sprk_externalrecordaccess", "sprk_fieldmappingrule",
                     "sprk_externalserviceconfig", "sprk_dataverseenvironment", "sprk_aimodeldeployment",
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
            "sprk_externalserviceconfig",  // platform service configuration
            "sprk_dataverseenvironment",   // platform service configuration
            "SPRK_AIMODELDEPLOYMENT",      // platform service configuration, any letter case
        ];
        var json = Config(tables: seeded.Select(t => Table(t, $"prvRead{t}")).ToArray());

        var findings = ConfigFindings(json, ForbiddenTables());

        foreach (var table in seeded)
            Assert.Contains(findings, f => f.StartsWith($"{table}:", StringComparison.Ordinal) && f.Contains("never gets Global Read", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Product configuration read guard: the platform service list is read from the script, and holds the three tables")]
    public void PlatformServiceListComesFromTheScript()
    {
        Assert.Equal(
            new[] { "sprk_aimodeldeployment", "sprk_dataverseenvironment", "sprk_externalserviceconfig" },
            PlatformServiceTables.OrderBy(t => t, StringComparer.Ordinal).ToArray());

        // Controls: the parse reads the one live assignment, ignores a commented one, and refuses none, two or an empty list.
        Assert.Equal(new[] { "sprk_a", "sprk_b" }, ParsePlatformServiceTables("# $PlatformServiceTables = @('sprk_x')\n$PlatformServiceTables = @('SPRK_A', 'sprk_b')\n"));
        Assert.Throws<InvalidOperationException>(() => ParsePlatformServiceTables("$Other = @('sprk_a')"));
        Assert.Throws<InvalidOperationException>(() => ParsePlatformServiceTables("$PlatformServiceTables = @('sprk_a')\n$PlatformServiceTables = @('sprk_b')"));
        Assert.Throws<InvalidOperationException>(() => ParsePlatformServiceTables("$PlatformServiceTables = @()"));
    }

    [Fact(DisplayName = "Product configuration read guard control: a privilegeName that is not prvRead + the table's own name is reported")]
    public void GuardFiresOnAnotherTablesOrANonReadPrivilege()
    {
        var none = new Dictionary<string, string>();

        // Another table's Read.
        Assert.Contains(ConfigFindings(Config(tables: [Table("sprk_aitooltype", "prvReadsprk_GridConfiguration")]), none),
            f => f.StartsWith("sprk_aitooltype:", StringComparison.Ordinal) && f.Contains("not this table's Read privilege", StringComparison.Ordinal));
        // A prefix of the table's name, or the name with a suffix, is not its Read.
        Assert.Contains(ConfigFindings(Config(tables: [Table("sprk_aitooltype", "prvReadsprk_AIToolTypeX")]), none),
            f => f.Contains("not this table's Read privilege", StringComparison.Ordinal));
        // A non-Read privilege on the table itself.
        Assert.Contains(ConfigFindings(Config(tables: [Table("sprk_aitooltype", "prvWritesprk_AIToolType")]), none),
            f => f.Contains("not a Read privilege", StringComparison.Ordinal));
        // Must not fire: the table's own Read in another letter case.
        Assert.Empty(ConfigFindings(Config(tables: [Table("sprk_aitooltype", "PRVREADSPRK_AITOOLTYPE")]), none));
    }

    [Fact(DisplayName = "Product configuration read guard control: a Global Write, or another table's Global Read, does not satisfy a table's Read")]
    public void XmlGuardIsNotSatisfiedByAnotherPrivilege()
    {
        const string role = """
            <Role id="{00000000-0000-0000-0000-000000000002}" name="Spaarke Example">
              <RolePrivileges>
                <RolePrivilege name="prvWritesprk_AIToolType" level="Global" />
                <RolePrivilege name="prvReadsprk_GridConfiguration" level="Global" />
                <RolePrivilege name="prvReadsprk_AIOutputTypeArchive" level="Global" />
              </RolePrivileges>
            </Role>
            """;

        var findings = XmlFindings(role, ["sprk_aitooltype", "sprk_aioutputtype"]);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.StartsWith("sprk_aitooltype:", StringComparison.Ordinal) && f.Contains("'absent'", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.StartsWith("sprk_aioutputtype:", StringComparison.Ordinal) && f.Contains("'absent'", StringComparison.Ordinal));
        // Must not fire: the table whose own Read is at Global.
        Assert.Empty(XmlFindings(role, ["sprk_gridconfiguration"]));
    }

    [Fact(DisplayName = "Product configuration read guard control: a Create on a no-create table is reported at any depth")]
    public void CreateGuardFiresOnCreatePrivilege()
    {
        const string withCreate = """
            <Role id="{00000000-0000-0000-0000-000000000003}" name="Spaarke Example">
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_GridConfiguration" level="Global" />
                <RolePrivilege name="PRVCREATESPRK_GRIDCONFIGURATION" level="Basic" />
              </RolePrivileges>
            </Role>
            """;
        const string readOnly = """
            <Role id="{00000000-0000-0000-0000-000000000004}" name="Spaarke Example">
              <RolePrivileges>
                <RolePrivilege name="prvReadsprk_GridConfiguration" level="Global" />
                <RolePrivilege name="prvWritesprk_GridConfiguration" level="Basic" />
                <RolePrivilege name="prvCreatesprk_GridConfigurationArchive" level="Global" />
              </RolePrivileges>
            </Role>
            """;

        var finding = Assert.Single(CreateFindings(withCreate, NoCreateTables));
        Assert.Contains("sprk_gridconfiguration", finding, StringComparison.Ordinal);
        Assert.Contains("'Basic'", finding, StringComparison.Ordinal);
        // Must not fire: Read and Write only, and a Create on a different table whose name starts the same.
        Assert.Empty(CreateFindings(readOnly, NoCreateTables));
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

        var findings = XmlFindings(role, ["sprk_aitooltype", "sprk_gridconfiguration", "sprk_aioutputtype"]);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.StartsWith("sprk_aitooltype:", StringComparison.Ordinal) && f.Contains("'Deep'", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.StartsWith("sprk_aioutputtype:", StringComparison.Ordinal) && f.Contains("'absent'", StringComparison.Ordinal));
        Assert.Empty(XmlFindings(role, ["sprk_gridconfiguration"]));
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

            // The table's OWN Read, exactly: another table's Read or a non-Read privilege never stands in for it.
            if (!privilege.StartsWith("prvRead", StringComparison.OrdinalIgnoreCase))
                findings.Add($"{table}: '{privilege}' is not a Read privilege");
            else if (!string.Equals(privilege, ReadPrivilegeOf(table), StringComparison.OrdinalIgnoreCase))
                findings.Add($"{table}: '{privilege}' is not this table's Read privilege ({ReadPrivilegeOf(table)})");
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

    /// <summary>
    /// Each listed table whose OWN Read (<see cref="ReadPrivilegeOf"/>, exact name, case-insensitive) the role XML does not
    /// hold at Global. Only that privilege counts: a Global Write on the table, or a Global Read on another table, does not.
    /// </summary>
    internal static List<string> XmlFindings(string roleXml, IEnumerable<string> tables)
    {
        var levels = PrivilegeLevels(roleXml);
        return tables
            .Select(t => (Table: t, Read: ReadPrivilegeOf(t), Level: levels.TryGetValue(ReadPrivilegeOf(t), out var l) ? l : "absent"))
            .Where(p => p.Level != "Global")
            .Select(p => $"{p.Table}: {p.Read} is '{p.Level}', not Global")
            .ToList();
    }

    /// <summary>Each table in <paramref name="tables"/> the role XML holds a Create privilege on (<c>prvCreate</c> + the
    /// table's name, exact, case-insensitive), at any depth.</summary>
    internal static List<string> CreateFindings(string roleXml, IEnumerable<string> tables)
    {
        var levels = PrivilegeLevels(roleXml);
        return tables
            .Where(t => levels.ContainsKey("prvCreate" + t))
            .Select(t => $"{t}: prvCreate{t} is held at '{levels["prvCreate" + t]}'; this role reads the table at Global and must not create in it")
            .ToList();
    }

    private static Dictionary<string, string> PrivilegeLevels(string roleXml) =>
        XDocument.Parse(roleXml).Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .GroupBy(e => (string?)e.Attribute("name") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (string?)g.Last().Attribute("level") ?? "?", StringComparer.OrdinalIgnoreCase);

    private static List<string> ListedTables(string json) =>
        (JsonNode.Parse(json)?["tables"] as JsonArray ?? [])
            .Select(n => (string?)n?["logicalName"])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
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
