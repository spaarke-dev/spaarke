using System.Text.Json;
using System.Text.Json.Nodes;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 191d (#2179 design v2, sections 3.2 and 3.15): <c>config/external-collaborator-role.json</c>
/// is the ONE definition of the 'Spaarke External Collaborator' role that every flagged-external systemuser holds, read by
/// <c>scripts/Set-ExternalCollaboratorRole.ps1</c> (-Apply, -Verify). The role must reach a lineage row only through a share:
/// Basic depth on every lineage table, never Delete, Share or Assign there, and no privilege at all on the "never" tables
/// (Procedure, rule, communication-content and invoice tables). This guard pins the file to the lineage registry
/// (<see cref="SecureChildLineage"/>): every root and child is declared either visible (Basic) or never, so a table added to the
/// registry fails here until someone decides. The live role is checked by the script's -Verify.
/// Enforced by: this guard (the live file gives no finding; each seeded bad document gives its finding).
/// </summary>
public class ExternalCollaboratorRoleGuardTests
{
    internal const string FileName = "external-collaborator-role.json";
    internal const string BusinessUnitName = "External Collaborators";
    internal const string RoleName = "Spaarke External Collaborator";

    /// <summary>THE never tables: no privilege of any kind (task 191d constraint; ebilling 088, D-185, round 131, Q6 = a).</summary>
    internal static readonly string[] NeverTables =
    [
        "sprk_procedure", "sprk_procedureversion", "sprk_procedureassignment", "sprk_storedfile",
        "sprk_ruleset", "sprk_policy", "sprk_policyversion",
        "sprk_emailartifact", "sprk_attachmentartifact", "sprk_emailreviewlog", "sprk_affinity",
        "sprk_invoice", "sprk_invoicesubmission", "sprk_invoiceapproval", "sprk_invoicecorrection",
        "sprk_externalidentifier", "sprk_billingevent",
    ];

    private static readonly HashSet<string> LineageAccess = new(StringComparer.Ordinal) { "Read", "Write", "Append", "AppendTo", "Create" };
    private static readonly HashSet<string> CatalogAccess = new(StringComparer.Ordinal) { "Read", "AppendTo" };
    private static readonly HashSet<string> PlatformDepths = new(StringComparer.Ordinal) { "Basic", "Local", "Global" };

    /// <summary>Every table of the lineage registry: the three roots and every child.</summary>
    internal static IReadOnlySet<string> RegistryTables() =>
        SecureChildLineage.Roots.Concat(SecureChildLineage.Children.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string LiveJson() => File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", FileName));

    private static string? Text(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static IEnumerable<JsonObject> Entries(JsonObject file, string name, List<string> findings)
    {
        if (file[name] is not JsonArray array)
        {
            findings.Add($"{name} is missing or not an array");
            yield break;
        }

        foreach (var node in array)
        {
            if (node is JsonObject entry)
                yield return entry;
            else
                findings.Add($"a {name} entry is not an object");
        }
    }

    private static List<string> Strings(JsonObject entry, string name) =>
        entry[name] is JsonArray a ? a.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "").ToList() : [];

    /// <summary>Every way <paramref name="json"/> breaks the External Collaborator definition; empty when it holds.</summary>
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
        if (file["businessUnit"] is not JsonObject unit || Text(unit, "name") != BusinessUnitName || Text(unit, "parent") != "root")
            findings.Add($"businessUnit is not '{BusinessUnitName}' under the root");
        if (Text(file, "roleName") != RoleName)
            findings.Add($"roleName is '{Text(file, "roleName")}', not the declared '{RoleName}'");
        if (file["isinherited"] is not JsonValue inherited || !inherited.TryGetValue<int>(out var inh) || inh != 1)
            findings.Add("isinherited is not 1 (direct user access)");
        if (Text(file, "lineageDepth") != "Basic")
            findings.Add($"lineageDepth is '{Text(file, "lineageDepth")}', not Basic");

        var registry = RegistryTables();
        var never = NeverTables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Lineage: registry tables only, Basic only (the file's lineageDepth), Read always, no Delete/Share/Assign.
        var lineage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries(file, "lineage", findings))
        {
            var table = Text(entry, "logicalName") ?? "";
            lineage.Add(table);
            if (!seen.Add(table))
                findings.Add($"{table}: listed twice");
            if (never.Contains(table))
                findings.Add($"{table}: is a never table and gets a privilege (lineage)");
            if (!registry.Contains(table))
                findings.Add($"{table}: lineage entry for a table that is not in the lineage registry");
            var access = Strings(entry, "access");
            if (!access.Contains("Read"))
                findings.Add($"{table}: no Read");
            foreach (var a in access.Where(a => !LineageAccess.Contains(a)))
                findings.Add($"{table}: access '{a}' (only Read, Write, Append, AppendTo, Create on a lineage table)");
            if (access.Count != access.Distinct(StringComparer.Ordinal).Count())
                findings.Add($"{table}: an access type listed twice");
            foreach (var key in entry.Select(p => p.Key).Where(k => k is not ("logicalName" or "access" or "reason")))
                findings.Add($"{table}: unexpected field '{key}' (a lineage entry is at lineageDepth, Basic)");
            if (string.IsNullOrWhiteSpace(Text(entry, "reason")))
                findings.Add($"{table}: no reason");
            if (SecureChildLineage.IsRoot(table) && !access.Contains("AppendTo"))
                findings.Add($"{table}: a root without AppendTo (Q3 = a: partners file new rows under a visible root)");
        }

        // Never: exactly the declared set, no other field.
        var declaredNever = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries(file, "never", findings))
        {
            var table = Text(entry, "logicalName") ?? "";
            declaredNever.Add(table);
            if (!seen.Add(table))
                findings.Add($"{table}: listed twice");
            if (!never.Contains(table))
                findings.Add($"{table}: in the file's never list but not a declared never table (the guard's list changes with it)");
            foreach (var key in entry.Select(p => p.Key).Where(k => k is not ("logicalName" or "reason")))
                findings.Add($"{table}: unexpected field '{key}' in a never entry (a never table gets nothing)");
        }

        foreach (var missing in never.Where(t => !declaredNever.Contains(t)).OrderBy(t => t, StringComparer.Ordinal))
            findings.Add($"{missing}: a never table the file does not list as never");

        foreach (var missing in registry.Where(t => !lineage.Contains(t) && !never.Contains(t)).OrderBy(t => t, StringComparer.Ordinal))
            findings.Add($"{missing}: a lineage registry table the file declares neither lineage (Basic) nor never");

        // Catalogs: organization-owned reference tables, Read/AppendTo at Global, never a registry or never table.
        foreach (var entry in Entries(file, "catalogs", findings))
        {
            var table = Text(entry, "logicalName") ?? "";
            if (!seen.Add(table))
                findings.Add($"{table}: listed twice");
            if (never.Contains(table))
                findings.Add($"{table}: is a never table and gets a privilege (catalogs)");
            if (registry.Contains(table))
                findings.Add($"{table}: a lineage registry table declared as a catalog (Global on a lineage table)");
            if (Text(entry, "depth") != "Global")
                findings.Add($"{table}: catalog depth '{Text(entry, "depth")}' (an organization-owned table has Global only)");
            foreach (var a in Strings(entry, "access").Where(a => !CatalogAccess.Contains(a)))
                findings.Add($"{table}: catalog access '{a}' (Read and AppendTo only)");
        }

        // Platform: non-sprk_ privileges, no Share/Assign, no prvWriteUser, Delete at Basic only, no Deep.
        var platform = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in Entries(file, "platform", findings))
        {
            var name = Text(entry, "privilegeName") ?? "";
            var depth = Text(entry, "depth") ?? "";
            if (!platform.TryAdd(name, depth))
                findings.Add($"{name}: platform privilege listed twice");
            if (name.Contains("sprk_", StringComparison.OrdinalIgnoreCase))
                findings.Add($"{name}: a platform entry on a sprk_ table (Spaarke tables go in lineage, never or catalogs)");
            if (name.StartsWith("prvShare", StringComparison.Ordinal) || name.StartsWith("prvAssign", StringComparison.Ordinal))
                findings.Add($"{name}: Share or Assign");
            if (name == "prvWriteUser")
                findings.Add("prvWriteUser: a flagged user could clear its own sprk_isexternal");
            if (!PlatformDepths.Contains(depth))
                findings.Add($"{name}: platform depth '{depth}' (Basic, Local or Global: the lowest each privilege supports)");
            if (name.StartsWith("prvDelete", StringComparison.Ordinal) && depth != "Basic")
                findings.Add($"{name}: Delete above Basic");
        }

        foreach (var (name, depth) in new[] { ("prvReadUser", "Local"), ("prvReadUserSettings", "Basic"), ("prvWriteUserSettings", "Basic") })
        {
            if (!platform.TryGetValue(name, out var held))
                findings.Add($"{name}: missing (design 3.2: {name} at {depth})");
            else if (held != depth)
                findings.Add($"{name}: at {held}, expected {depth} (design 3.2)");
        }

        if (file["defaultTeam"] is not JsonObject team)
            findings.Add("defaultTeam is missing");
        else
        {
            if (!Strings(team, "storedFileTables").Contains("sprk_storedfile"))
                findings.Add("defaultTeam.storedFileTables does not name sprk_storedfile (the STORED FILE PROFILE check)");
            if (team["readerProfiles"] is not JsonArray)
                findings.Add("defaultTeam.readerProfiles is missing");
            foreach (var p in Strings(team, "readerProfiles").Where(p => p.Contains("Writer", StringComparison.OrdinalIgnoreCase)))
                findings.Add($"defaultTeam.readerProfiles names '{p}', a writer profile");
        }

        return findings;
    }

    [Fact]
    public void TheLiveFile_IsBasicOnly_CoversTheRegistry_AndHoldsNothingOnTheNeverTables()
    {
        Assert.Empty(Findings(LiveJson()));
    }

    [Fact]
    public void TheScript_ReadsThisFile_AndKeepsApplyAndVerifySeparate()
    {
        var script = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "scripts", "Set-ExternalCollaboratorRole.ps1"));
        Assert.Contains(FileName, script, StringComparison.Ordinal);
        Assert.Contains("if ($Apply -and $Verify) { throw", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExternalRole_IsNoOtherCodifiedRole()
    {
        foreach (var other in Directory.GetFiles(Path.Combine(SourceScan.RepoRoot, "config"), "*-role.json")
                     .Where(f => !f.EndsWith(FileName, StringComparison.OrdinalIgnoreCase)))
        {
            var o = JsonNode.Parse(File.ReadAllText(other)) as JsonObject;
            Assert.NotEqual(RoleName, o is null ? null : Text(o, "roleName"));
        }
    }

    private static string Mutated(Action<JsonObject> change)
    {
        var file = JsonNode.Parse(LiveJson())!.AsObject();
        change(file);
        return file.ToJsonString();
    }

    private static JsonObject EntryFor(JsonObject file, string list, string key, string value) =>
        file[list]!.AsArray().Single(e => Text(e!.AsObject(), key) == value)!.AsObject();

    private static JsonObject Lineage(string table, params string[] access) => new()
    {
        ["logicalName"] = table,
        ["access"] = new JsonArray(access.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray()),
        ["reason"] = "seeded",
    };

    /// <summary>Each never table, moved into lineage with Read only, must fire.</summary>
    public static TheoryData<string> EachNeverTable()
    {
        var data = new TheoryData<string>();
        foreach (var t in NeverTables)
            data.Add(t);
        return data;
    }

    [Theory]
    [MemberData(nameof(EachNeverTable))]
    public void ANeverTable_GivenAnyPrivilege_GivesItsFinding(string table)
    {
        var asLineage = Findings(Mutated(f =>
        {
            f["never"]!.AsArray().Remove(EntryFor(f, "never", "logicalName", table));
            f["lineage"]!.AsArray().Add(Lineage(table, "Read"));
        }));
        Assert.Contains(asLineage, x => x.Contains($"{table}: is a never table and gets a privilege", StringComparison.Ordinal));

        var asCatalog = Findings(Mutated(f => f["catalogs"]!.AsArray().Add(new JsonObject
        {
            ["logicalName"] = table, ["access"] = new JsonArray("Read"), ["depth"] = "Global", ["reason"] = "seeded",
        })));
        Assert.Contains(asCatalog, x => x.Contains($"{table}: is a never table and gets a privilege (catalogs)", StringComparison.Ordinal));
    }

    public static TheoryData<string, string, string> MustFire() => new()
    {
        { "lineage at Deep", Mutated(f => f["lineageDepth"] = "Deep"), "lineageDepth is 'Deep'" },
        { "lineage at Global", Mutated(f => f["lineageDepth"] = "Global"), "lineageDepth is 'Global'" },
        { "one lineage entry at Global", Mutated(f => EntryFor(f, "lineage", "logicalName", "sprk_matter")["depth"] = "Global"), "sprk_matter: unexpected field 'depth'" },
        { "a lineage table as a Global catalog", Mutated(f => f["catalogs"]!.AsArray().Add(new JsonObject { ["logicalName"] = "sprk_memo", ["access"] = new JsonArray("Read"), ["depth"] = "Global", ["reason"] = "x" })), "sprk_memo: a lineage registry table declared as a catalog" },
        { "Share on a lineage table", Mutated(f => EntryFor(f, "lineage", "logicalName", "sprk_document")["access"]!.AsArray().Add("Share")), "sprk_document: access 'Share'" },
        { "Assign on a lineage table", Mutated(f => EntryFor(f, "lineage", "logicalName", "sprk_event")["access"]!.AsArray().Add("Assign")), "sprk_event: access 'Assign'" },
        { "Delete on a lineage table", Mutated(f => EntryFor(f, "lineage", "logicalName", "sprk_todo")["access"]!.AsArray().Add("Delete")), "sprk_todo: access 'Delete'" },
        { "a registry table left undeclared", Mutated(f => f["lineage"]!.AsArray().Remove(EntryFor(f, "lineage", "logicalName", "sprk_memo"))), "sprk_memo: a lineage registry table the file declares neither" },
        { "a never table dropped from never", Mutated(f => f["never"]!.AsArray().Remove(EntryFor(f, "never", "logicalName", "sprk_affinity"))), "sprk_affinity: a never table the file does not list as never" },
        { "a root without AppendTo", Mutated(f => EntryFor(f, "lineage", "logicalName", "sprk_workassignment")["access"] = new JsonArray("Read", "Write")), "sprk_workassignment: a root without AppendTo" },
        { "a lineage entry without Read", Mutated(f => EntryFor(f, "lineage", "logicalName", "sprk_memo")["access"] = new JsonArray("AppendTo")), "sprk_memo: no Read" },
        { "prvWriteUser", Mutated(f => f["platform"]!.AsArray().Add(new JsonObject { ["privilegeName"] = "prvWriteUser", ["depth"] = "Local", ["reason"] = "x" })), "prvWriteUser: a flagged user could clear" },
        { "prvReadUser at Global", Mutated(f => EntryFor(f, "platform", "privilegeName", "prvReadUser")["depth"] = "Global"), "prvReadUser: at Global, expected Local" },
        { "prvReadUser missing", Mutated(f => f["platform"]!.AsArray().Remove(EntryFor(f, "platform", "privilegeName", "prvReadUser"))), "prvReadUser: missing" },
        { "a sprk_ privilege on the platform list", Mutated(f => f["platform"]!.AsArray().Add(new JsonObject { ["privilegeName"] = "prvReadsprk_Invoice", ["depth"] = "Basic", ["reason"] = "x" })), "prvReadsprk_Invoice: a platform entry on a sprk_ table" },
        { "a platform Share", Mutated(f => f["platform"]!.AsArray().Add(new JsonObject { ["privilegeName"] = "prvShareUserQuery", ["depth"] = "Basic", ["reason"] = "x" })), "prvShareUserQuery: Share or Assign" },
        { "a platform privilege at Deep", Mutated(f => EntryFor(f, "platform", "privilegeName", "prvReadTeam")["depth"] = "Deep"), "prvReadTeam: platform depth 'Deep'" },
        { "a platform Delete above Basic", Mutated(f => EntryFor(f, "platform", "privilegeName", "prvDeleteUserEntityUISettings")["depth"] = "Global"), "prvDeleteUserEntityUISettings: Delete above Basic" },
        { "a catalog at Basic", Mutated(f => EntryFor(f, "catalogs", "logicalName", "sprk_mattertype_ref")["depth"] = "Basic"), "sprk_mattertype_ref: catalog depth 'Basic'" },
        { "a catalog with Write", Mutated(f => EntryFor(f, "catalogs", "logicalName", "sprk_mattertype_ref")["access"]!.AsArray().Add("Write")), "sprk_mattertype_ref: catalog access 'Write'" },
        { "another role name", Mutated(f => f["roleName"] = "Spaarke Core User"), "roleName is 'Spaarke Core User'" },
        { "another business unit", Mutated(f => f["businessUnit"]!["name"] = "Secure Record"), "businessUnit is not" },
        { "team-only inheritance", Mutated(f => f["isinherited"] = 0), "isinherited is not 1" },
        { "a writer profile allowed on the default team", Mutated(f => f["defaultTeam"]!["readerProfiles"]!.AsArray().Add("Spaarke BFF-Managed Field Writers")), "a writer profile" },
        { "no Stored File check", Mutated(f => f["defaultTeam"]!["storedFileTables"] = new JsonArray()), "does not name sprk_storedfile" },
        { "a table listed twice", Mutated(f => f["lineage"]!.AsArray().Add(Lineage("sprk_memo", "Read"))), "sprk_memo: listed twice" },
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
