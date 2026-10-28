using System.Text.Json;
using System.Text.Json.Nodes;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 194b R4b-3 (owner round 131, the 086 ruling), R4b-2c (owner round 136) and R4b-3b
/// (field-mapping #2399, owner shape A): each customer's top business unit holds two declared, memberless owner teams, each
/// with its own role, both set up per unit by <c>scripts/Set-CustomerOwnerTeams.ps1</c> (<c>businessUnit</c> is
/// <c>customer</c>: never the root, never Secure Record). Each role's ONE definition is its config file, and each must hold
/// Read only, at Basic, on exactly its own declared tables: the rows sit in the customer's top business unit, so any wider
/// grant reaches every one of them there.
/// <list type="bullet">
/// <item><c>config/restricted-content-owner-role.json</c>: the hard-deny tables
/// (<see cref="RecordOwnershipResolver.RestrictedContentTables"/>), under the BFF's compiled team name
/// (<see cref="RestrictedContentOwnerTeam.DefaultOwnerTeamName"/>, a fail-closed lookup key).</item>
/// <item><c>config/customer-configuration-owner-role.json</c>: the customer configuration tables
/// (<see cref="CustomerConfigurationTables"/>), which the BFF never resolves (seeders own the rows to the team). None may be
/// a hard-deny table, an ownership parent or a secure-child table: those are customer records whose owner the resolver
/// decides.</item>
/// </list>
/// The two files declare different teams and roles and no table in common, and the script declares both files.
/// Enforced by: this guard (the live files must give no finding; each seeded bad document must give its finding).
/// </summary>
public class RestrictedContentOwnerRoleGuardTests
{
    /// <summary>
    /// R4b-3b: THE declared customer configuration tables (field-mapping #2399's seeded configuration). A table is added here
    /// and in <c>config/customer-configuration-owner-role.json</c> together, when the owning project names it; the guard
    /// fails while the two differ.
    /// </summary>
    internal static readonly string[] CustomerConfigurationTables =
    [
        "sprk_aisearchindex",
        "sprk_fieldmappingprofile",
        "sprk_fieldmappingrule",
    ];

    internal const string CustomerConfigurationTeamName = "Spaarke Customer Configuration Owners";

    /// <summary>A declared owner role: its file, the team name it must carry, its exact tables, and why a table may never be in it.</summary>
    internal sealed record DeclaredRole(
        string FileName, string TeamName, string TeamNameSource, IReadOnlyCollection<string> Tables, string TablesSource,
        Func<string, string?> NeverListed);

    internal static readonly DeclaredRole RestrictedContent = new(
        "restricted-content-owner-role.json",
        RestrictedContentOwnerTeam.DefaultOwnerTeamName,
        "the BFF's default",
        RecordOwnershipResolver.RestrictedContentTables,
        "a hard-deny table (RecordOwnershipResolver.RestrictedContentTables)",
        _ => null);

    internal static readonly DeclaredRole CustomerConfiguration = new(
        "customer-configuration-owner-role.json",
        CustomerConfigurationTeamName,
        "the declared",
        CustomerConfigurationTables,
        "a declared customer configuration table (CustomerConfigurationTables)",
        table =>
            RecordOwnershipResolver.RestrictedContentTables.Contains(table, StringComparer.OrdinalIgnoreCase)
                ? "a hard-deny table (the restricted-content role's)"
                : RecordOwnershipResolver.OwnershipParentEntities.Contains(table, StringComparer.OrdinalIgnoreCase)
                  || SecureChildLineage.Children.ContainsKey(table)
                    ? "a customer record table (the ownership resolver decides its owner)"
                    : null);

    internal static readonly DeclaredRole[] Declared = [RestrictedContent, CustomerConfiguration];

    private static string LiveJson(DeclaredRole role) =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", role.FileName));

    private static string LiveJson() => LiveJson(RestrictedContent);

    /// <summary>Every way <paramref name="json"/> breaks the restricted-content role's definition; empty when it holds.</summary>
    internal static IReadOnlyList<string> Findings(string json) => Findings(json, RestrictedContent);

    /// <summary>Every way <paramref name="json"/> breaks <paramref name="role"/>'s definition; empty when it holds.</summary>
    internal static IReadOnlyList<string> Findings(string json, DeclaredRole role)
    {
        var findings = new List<string>();
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

        if (file["schemaVersion"] is not JsonValue version || !version.TryGetValue<int>(out var number) || number != 1)
            findings.Add("schemaVersion is not 1");
        if (Text(file, "access") != "Read")
            findings.Add($"access is '{Text(file, "access")}', not Read");
        if (Text(file, "depth") != "Basic")
            findings.Add($"depth is '{Text(file, "depth")}', not Basic");
        if (Text(file, "businessUnit") != "customer")
            findings.Add($"businessUnit is '{Text(file, "businessUnit")}', not customer");
        if (Text(file, "teamName") != role.TeamName)
            findings.Add($"teamName is '{Text(file, "teamName")}', not {role.TeamNameSource} '{role.TeamName}'");
        if (string.IsNullOrWhiteSpace(Text(file, "roleName")))
            findings.Add("roleName is empty");

        if (file["tables"] is not JsonArray tables || tables.Count == 0)
        {
            findings.Add("tables is missing or empty");
            return findings;
        }

        var declared = role.Tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPrivileges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in tables)
        {
            if (node is not JsonObject entry)
            {
                findings.Add("a tables entry is not an object");
                continue;
            }

            var table = Text(entry, "logicalName") ?? "";
            var privilege = Text(entry, "privilegeName") ?? "";
            if (!declared.Contains(table))
                findings.Add($"{table}: not {role.TablesSource}");
            if (role.NeverListed(table) is { } never)
                findings.Add($"{table}: never listed here: {never}");
            if (!seenTables.Add(table))
                findings.Add($"{table}: listed twice");
            if (!seenPrivileges.Add(privilege))
                findings.Add($"{privilege}: duplicate privilege name");
            if (!string.Equals(UserPrivilegeChecker.ReadPrivilegeEntity(privilege), table, StringComparison.OrdinalIgnoreCase))
                findings.Add($"{table}: {privilege} is not this table's Read privilege");
            foreach (var key in entry.Select(p => p.Key))
            {
                if (key is not ("logicalName" or "privilegeName" or "reason" or "evidence" or "addedBy"))
                    findings.Add($"{table}: unexpected field '{key}' (an entry names no depth, access or other grant)");
            }

            foreach (var field in new[] { "reason", "evidence" })
            {
                if (string.IsNullOrWhiteSpace(Text(entry, field)))
                    findings.Add($"{table}: no {field}");
            }
        }

        foreach (var missing in declared.Where(t => !seenTables.Contains(t)).OrderBy(t => t, StringComparer.Ordinal))
            findings.Add($"{missing}: {role.TablesSource} the file does not list");

        return findings;
    }

    private static string? Text(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    [Fact]
    public void TheLiveFile_HoldsReadAtBasic_OnExactlyTheHardDenyTables_UnderTheBffsTeamName()
    {
        Assert.Empty(Findings(LiveJson()));
    }

    [Fact]
    public void TheLiveConfigurationFile_HoldsReadAtBasic_OnExactlyTheDeclaredConfigurationTables_UnderItsTeamName()
    {
        Assert.Empty(Findings(LiveJson(CustomerConfiguration), CustomerConfiguration));
    }

    [Fact]
    public void TheDeclaredConfigurationTables_AreNeverHardDenyOrCustomerRecordTables()
    {
        Assert.All(CustomerConfigurationTables, table => Assert.Null(CustomerConfiguration.NeverListed(table)));
    }

    [Fact]
    public void TheTwoRoles_DeclareDifferentTeamsAndRoles_AndNoTableInCommon()
    {
        var files = Declared.Select(role => JsonNode.Parse(LiveJson(role))!.AsObject()).ToArray();
        Assert.Equal(files.Length, files.Select(f => Text(f, "teamName")).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(files.Length, files.Select(f => Text(f, "roleName")).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var tables = files.SelectMany(f => f["tables"]!.AsArray().Select(e => Text(e!.AsObject(), "logicalName"))).ToArray();
        Assert.Equal(tables.Length, tables.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TheSetupScript_DeclaresEveryRoleFile()
    {
        var script = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "scripts", "Set-CustomerOwnerTeams.ps1"));
        Assert.All(Declared, role => Assert.Contains($"'{role.FileName}'", script, StringComparison.Ordinal));
    }

    private static string Mutated(Action<JsonObject> change) => Mutated(RestrictedContent, change);

    private static string Mutated(DeclaredRole role, Action<JsonObject> change)
    {
        var file = JsonNode.Parse(LiveJson(role))!.AsObject();
        change(file);
        return file.ToJsonString();
    }

    private static JsonObject Entry(string table, string privilege) => new()
    {
        ["logicalName"] = table,
        ["privilegeName"] = privilege,
        ["reason"] = "seeded",
        ["evidence"] = "seeded",
    };

    private static JsonObject EntryFor(JsonObject file, string table) =>
        file["tables"]!.AsArray().Single(e => Text(e!.AsObject(), "logicalName") == table)!.AsObject();

    public static TheoryData<string, string> MustFire() => new()
    {
        {
            "an extra privilege (Write on a listed table)",
            Mutated(f => f["tables"]!.AsArray().Add(Entry("sprk_policy", "prvWritesprk_Policy")))
        },
        {
            "an extra table",
            Mutated(f => f["tables"]!.AsArray().Add(Entry("sprk_document", "prvReadsprk_Document")))
        },
        {
            "a raised depth (the file's)",
            Mutated(f => f["depth"] = "Local")
        },
        {
            "a raised depth (one entry's)",
            Mutated(f => f["tables"]![0]!.AsObject()["depth"] = "Deep")
        },
        {
            "a duplicate privilege name",
            Mutated(f => f["tables"]![1]!.AsObject()["privilegeName"] = f["tables"]![0]!["privilegeName"]!.GetValue<string>())
        },
        {
            "a team name other than the BFF's default",
            Mutated(f => f["teamName"] = "Spaarke Restricted Content Owner")
        },
        {
            "a business unit other than each customer's",
            Mutated(f => f["businessUnit"] = "root")
        },
        {
            "a missing hard-deny table",
            Mutated(f => f["tables"]!.AsArray().RemoveAt(0))
        },
        {
            "another access type",
            Mutated(f => f["access"] = "Write")
        },
        {
            "a configuration table in the restricted-content role",
            Mutated(f => f["tables"]!.AsArray().Add(Entry("sprk_fieldmappingrule", "prvReadsprk_FieldMappingRule")))
        },
    };

    [Theory]
    [MemberData(nameof(MustFire))]
    public void ASeededBadDocument_GivesAFinding(string what, string json)
    {
        Assert.True(Findings(json).Count > 0, $"must fire on {what}");
    }

    public static TheoryData<string, string> ConfigurationMustFire() => new()
    {
        {
            "Write on sprk_fieldmappingprofile (a privilege other than Read)",
            Mutated(CustomerConfiguration, f => f["tables"]!.AsArray().Add(Entry("sprk_fieldmappingprofile", "prvWritesprk_FieldMappingProfile")))
        },
        {
            "Write on sprk_fieldmappingrule (a privilege other than Read)",
            Mutated(CustomerConfiguration, f => f["tables"]!.AsArray().Add(Entry("sprk_fieldmappingrule", "prvWritesprk_FieldMappingRule")))
        },
        {
            "sprk_fieldmappingprofile's entry naming another table's Read privilege",
            Mutated(CustomerConfiguration, f => EntryFor(f, "sprk_fieldmappingprofile")["privilegeName"] = "prvReadsprk_Document")
        },
        {
            "sprk_fieldmappingrule's entry naming another table's Read privilege",
            Mutated(CustomerConfiguration, f => EntryFor(f, "sprk_fieldmappingrule")["privilegeName"] = "prvReadsprk_Policy")
        },
        {
            "a table outside the declared set (a customer record table)",
            Mutated(CustomerConfiguration, f => f["tables"]!.AsArray().Add(Entry("sprk_document", "prvReadsprk_Document")))
        },
        {
            "a table outside the declared set (a hard-deny table)",
            Mutated(CustomerConfiguration, f => f["tables"]!.AsArray().Add(Entry("sprk_policy", "prvReadsprk_Policy")))
        },
        {
            "a table outside the declared set (any other table)",
            Mutated(CustomerConfiguration, f => f["tables"]!.AsArray().Add(Entry("sprk_recordtype_ref", "prvReadsprk_recordtype_ref")))
        },
        {
            "a missing declared table (sprk_fieldmappingprofile)",
            Mutated(CustomerConfiguration, f => f["tables"]!.AsArray().Remove(EntryFor(f, "sprk_fieldmappingprofile")))
        },
        {
            "a missing declared table (sprk_fieldmappingrule)",
            Mutated(CustomerConfiguration, f => f["tables"]!.AsArray().Remove(EntryFor(f, "sprk_fieldmappingrule")))
        },
        {
            "a raised depth (one entry's)",
            Mutated(CustomerConfiguration, f => EntryFor(f, "sprk_fieldmappingrule")["depth"] = "Deep")
        },
        {
            "a raised depth (the file's)",
            Mutated(CustomerConfiguration, f => f["depth"] = "Deep")
        },
        {
            "another access type",
            Mutated(CustomerConfiguration, f => f["access"] = "Write")
        },
        {
            "a team name other than the declared one",
            Mutated(CustomerConfiguration, f => f["teamName"] = RestrictedContentOwnerTeam.DefaultOwnerTeamName)
        },
        {
            "a business unit other than each customer's",
            Mutated(CustomerConfiguration, f => f["businessUnit"] = "root")
        },
    };

    [Theory]
    [MemberData(nameof(ConfigurationMustFire))]
    public void ASeededBadConfigurationDocument_GivesAFinding(string what, string json)
    {
        Assert.True(Findings(json, CustomerConfiguration).Count > 0, $"must fire on {what}");
    }

    [Fact]
    public void TheMustFireControls_EachNameTheirOwnBreak()
    {
        Assert.Contains(Findings(Mutated(f => f["tables"]!.AsArray().Add(Entry("sprk_policy", "prvWritesprk_Policy")))),
            f => f.Contains("is not this table's Read privilege", StringComparison.Ordinal));
        Assert.Contains(Findings(Mutated(f => f["tables"]!.AsArray().Add(Entry("sprk_document", "prvReadsprk_Document")))),
            f => f.Contains("not a hard-deny table", StringComparison.Ordinal));
        Assert.Contains(Findings(Mutated(f => f["depth"] = "Local")), f => f.StartsWith("depth is", StringComparison.Ordinal));
        Assert.Contains(Findings(Mutated(f => f["tables"]![0]!.AsObject()["depth"] = "Deep")),
            f => f.Contains("unexpected field 'depth'", StringComparison.Ordinal));
        Assert.Contains(
            Findings(Mutated(f => f["tables"]![1]!.AsObject()["privilegeName"] = f["tables"]![0]!["privilegeName"]!.GetValue<string>())),
            f => f.Contains("duplicate privilege name", StringComparison.Ordinal));
        Assert.Contains(Findings(Mutated(f => f["teamName"] = "x")), f => f.StartsWith("teamName is", StringComparison.Ordinal));
        Assert.Contains(Findings(Mutated(f => f["businessUnit"] = "root")),
            f => f == "businessUnit is 'root', not customer");
    }

    [Fact]
    public void TheConfigurationMustFireControls_EachNameTheirOwnBreak()
    {
        IReadOnlyList<string> Of(Action<JsonObject> change) => Findings(Mutated(CustomerConfiguration, change), CustomerConfiguration);

        Assert.Contains(Of(f => f["tables"]!.AsArray().Add(Entry("sprk_fieldmappingprofile", "prvWritesprk_FieldMappingProfile"))),
            f => f == "sprk_fieldmappingprofile: prvWritesprk_FieldMappingProfile is not this table's Read privilege");
        Assert.Contains(Of(f => f["tables"]!.AsArray().Add(Entry("sprk_fieldmappingrule", "prvWritesprk_FieldMappingRule"))),
            f => f == "sprk_fieldmappingrule: prvWritesprk_FieldMappingRule is not this table's Read privilege");
        Assert.Contains(Of(f => f["tables"]!.AsArray().Add(Entry("sprk_recordtype_ref", "prvReadsprk_recordtype_ref"))),
            f => f.Contains("not a declared customer configuration table", StringComparison.Ordinal));
        Assert.Contains(Of(f => f["tables"]!.AsArray().Add(Entry("sprk_document", "prvReadsprk_Document"))),
            f => f.Contains("a customer record table", StringComparison.Ordinal));
        Assert.Contains(Of(f => f["tables"]!.AsArray().Add(Entry("sprk_policy", "prvReadsprk_Policy"))),
            f => f.Contains("a hard-deny table (the restricted-content role's)", StringComparison.Ordinal));
        Assert.Contains(Of(f => f["tables"]!.AsArray().Remove(EntryFor(f, "sprk_fieldmappingrule"))),
            f => f.StartsWith("sprk_fieldmappingrule: a declared customer configuration table", StringComparison.Ordinal));
        Assert.Contains(Of(f => f["teamName"] = "x"), f => f.StartsWith("teamName is", StringComparison.Ordinal));
    }
}
