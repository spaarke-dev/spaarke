using System.Text.Json;
using System.Text.Json.Nodes;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 198 (Part A; fm task 054a): <c>config/direct-message-owner-role.json</c> is the ONE definition
/// of the Direct-message owner role, read by <c>scripts/Set-CustomerOwnerTeams.ps1 -DirectMessageBusinessUnitName</c>. The team
/// it is assigned to owns unfiled Direct 1:1 messages and the rows that follow them, so the role must hold Read only, at Basic,
/// on exactly the six tables fm's writer assigns to that team, under the declared team and role names, in a <c>direct</c> unit.
/// Modelled on <see cref="RestrictedContentOwnerRoleGuardTests"/>; a separate guard because that one's declared set and
/// <c>businessUnit: customer</c> rule must stay exact.
/// Enforced by: this guard (the live file gives no finding; each seeded bad document gives its finding).
/// </summary>
public class DirectMessageOwnerRoleGuardTests
{
    internal const string FileName = "direct-message-owner-role.json";
    internal const string TeamName = "Spaarke Direct Message Owners";
    internal const string RoleName = "Direct Message Owner";

    /// <summary>THE tables fm 054a assigns to the Direct team (confirmed by fm 2026-11-09). Changed only with fm's writer.</summary>
    internal static readonly string[] Tables =
    [
        "sprk_communication",
        "sprk_communicationattachment",
        "sprk_communicationparticipant",
        "sprk_emailreviewlog",
        "sprk_document",
        "sprk_decisionrecord",
    ];

    private static string LiveJson() => File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", FileName));

    /// <summary>Every way <paramref name="json"/> breaks the Direct-message owner role's definition; empty when it holds.</summary>
    internal static IReadOnlyList<string> Findings(string json)
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
        if (Text(file, "businessUnit") != "direct")
            findings.Add($"businessUnit is '{Text(file, "businessUnit")}', not direct");
        if (Text(file, "teamName") != TeamName)
            findings.Add($"teamName is '{Text(file, "teamName")}', not the declared '{TeamName}'");
        if (Text(file, "roleName") != RoleName)
            findings.Add($"roleName is '{Text(file, "roleName")}', not the declared '{RoleName}'");

        if (file["tables"] is not JsonArray tables || tables.Count == 0)
        {
            findings.Add("tables is missing or empty");
            return findings;
        }

        var declared = Tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                findings.Add($"{table}: not a table fm 054a assigns to the Direct team");
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
            findings.Add($"{missing}: a table fm 054a assigns to the Direct team the file does not list");

        return findings;
    }

    private static string? Text(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    [Fact]
    public void TheLiveFile_HoldsReadAtBasic_OnExactlyTheSixDirectTables_UnderItsTeamAndRoleNames()
    {
        Assert.Empty(Findings(LiveJson()));
    }

    [Fact]
    public void TheDirectRole_IsNeverACustomerRoleOrTheSecureRecordOwnerRole()
    {
        Assert.All(RestrictedContentOwnerRoleGuardTests.Declared, role =>
        {
            Assert.NotEqual(RoleName, role.RoleName, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual(TeamName, role.TeamName, StringComparer.OrdinalIgnoreCase);
        });
        var secure = JsonNode.Parse(File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", "secure-record-owner-role.json")))!
            .AsObject();
        Assert.NotEqual(RoleName, Text(secure, "roleName"), StringComparer.OrdinalIgnoreCase);
    }

    private static string Mutated(Action<JsonObject> change)
    {
        var file = JsonNode.Parse(LiveJson())!.AsObject();
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

    public static TheoryData<string, string, string> MustFire() => new()
    {
        { "a seventh table", Mutated(f => f["tables"]!.AsArray().Add(Entry("sprk_matter", "prvReadsprk_Matter"))), "not a table fm 054a assigns" },
        { "Write on a listed table", Mutated(f => f["tables"]!.AsArray().Add(Entry("sprk_communication", "prvWritesprk_Communication"))), "is not this table's Read privilege" },
        { "a missing table", Mutated(f => f["tables"]!.AsArray().Remove(EntryFor(f, "sprk_emailreviewlog"))), "sprk_emailreviewlog: a table fm 054a assigns" },
        { "another table's Read privilege", Mutated(f => EntryFor(f, "sprk_document")["privilegeName"] = "prvReadsprk_Matter"), "is not this table's Read privilege" },
        { "a raised depth (the file's)", Mutated(f => f["depth"] = "Local"), "depth is" },
        { "a raised depth (one entry's)", Mutated(f => EntryFor(f, "sprk_document")["depth"] = "Global"), "unexpected field 'depth'" },
        { "another access type", Mutated(f => f["access"] = "Write"), "access is" },
        { "a customer business unit", Mutated(f => f["businessUnit"] = "customer"), "businessUnit is 'customer'" },
        { "another team name", Mutated(f => f["teamName"] = "Spaarke Restricted Content Owners"), "teamName is" },
        { "another role name", Mutated(f => f["roleName"] = "System Administrator"), "roleName is" },
        { "a table listed twice", Mutated(f => f["tables"]!.AsArray().Add(Entry("sprk_document", "prvReadsprk_Document"))), "listed twice" },
    };

    [Theory]
    [MemberData(nameof(MustFire))]
    public void ASeededBadDocument_GivesItsOwnFinding(string what, string json, string expected)
    {
        var findings = Findings(json);
        Assert.True(findings.Any(f => f.Contains(expected, StringComparison.Ordinal)),
            $"must fire on {what}; findings: {string.Join(" | ", findings)}");
    }
}
