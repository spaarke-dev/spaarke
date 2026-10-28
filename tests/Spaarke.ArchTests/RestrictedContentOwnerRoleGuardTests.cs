using System.Text.Json;
using System.Text.Json.Nodes;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 194b R4b-3 (owner round 131, the 086 ruling) and R4b-2c (owner round 136):
/// <c>config/restricted-content-owner-role.json</c> is the ONE definition of the role of the memberless owner team each
/// customer's top business unit holds, which <c>scripts/Set-RestrictedContentOwnerTeam.ps1</c> applies and verifies per unit
/// (<c>businessUnit</c> is <c>customer</c>: never the root, never Secure Record). The role must hold Read only, at Basic, and
/// only on the hard-deny tables (<see cref="RecordOwnershipResolver.RestrictedContentTables"/>): these rows sit in the
/// customer's top business unit, so any wider grant reaches every one of them there. The file's team name is the BFF's compiled default
/// (<see cref="RestrictedContentOwnerTeam.DefaultOwnerTeamName"/>), a fail-closed lookup key.
/// Enforced by: this guard (the live file must give no finding; each seeded bad document must give its finding).
/// </summary>
public class RestrictedContentOwnerRoleGuardTests
{
    private static string LiveJson() =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", "restricted-content-owner-role.json"));

    /// <summary>Every way <paramref name="json"/> breaks the role's definition; empty when it holds.</summary>
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

        string? Text(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        if (file["schemaVersion"] is not JsonValue version || !version.TryGetValue<int>(out var number) || number != 1)
            findings.Add("schemaVersion is not 1");
        if (Text(file, "access") != "Read")
            findings.Add($"access is '{Text(file, "access")}', not Read");
        if (Text(file, "depth") != "Basic")
            findings.Add($"depth is '{Text(file, "depth")}', not Basic");
        if (Text(file, "businessUnit") != "customer")
            findings.Add($"businessUnit is '{Text(file, "businessUnit")}', not customer");
        if (Text(file, "teamName") != RestrictedContentOwnerTeam.DefaultOwnerTeamName)
            findings.Add($"teamName is '{Text(file, "teamName")}', not the BFF's default '{RestrictedContentOwnerTeam.DefaultOwnerTeamName}'");
        if (string.IsNullOrWhiteSpace(Text(file, "roleName")))
            findings.Add("roleName is empty");

        if (file["tables"] is not JsonArray tables || tables.Count == 0)
        {
            findings.Add("tables is missing or empty");
            return findings;
        }

        var hardDeny = RecordOwnershipResolver.RestrictedContentTables.ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            if (!hardDeny.Contains(table))
                findings.Add($"{table}: not a hard-deny table (RecordOwnershipResolver.RestrictedContentTables)");
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

        foreach (var missing in hardDeny.Where(t => !seenTables.Contains(t)).OrderBy(t => t, StringComparer.Ordinal))
            findings.Add($"{missing}: a hard-deny table the file does not list");

        return findings;
    }

    [Fact]
    public void TheLiveFile_HoldsReadAtBasic_OnExactlyTheHardDenyTables_UnderTheBffsTeamName()
    {
        Assert.Empty(Findings(LiveJson()));
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
    };

    [Theory]
    [MemberData(nameof(MustFire))]
    public void ASeededBadDocument_GivesAFinding(string what, string json)
    {
        Assert.True(Findings(json).Count > 0, $"must fire on {what}");
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
}
