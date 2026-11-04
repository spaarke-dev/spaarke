using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Api.Finance;
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
/// (<see cref="CustomerConfigurationTables"/>), which the BFF never resolves (seeders will own new rows to the team). None may be
/// a hard-deny table, an ownership parent or a secure-child table: those are customer records whose owner the resolver
/// decides.</item>
/// </list>
/// <para>ebilling-r1 task 089d (option R, owner 2026-11-04 "use direct role approach") adds a third declared role that is NOT an
/// owner role and that NO team holds: <c>config/billing-rule-readers-role.json</c>, the <c>Billing Rule Reader</c> role, Read at
/// Deep on <c>sprk_policy</c>, <c>sprk_ruleset</c> and <c>sprk_procedure</c> only (the last two: task 089e fix round 2), assigned
/// directly to each child-unit billing admin from the customer top unit's copy. Deep is
/// measured from the unit of the copy a principal holds, so the holder reaches the top unit and its children and never Secure
/// Record (a sibling); Global, or Deep from the root's copy, would reach Secure Record (D-123). Its file must say <c>Deep</c>,
/// <c>customer</c>, <c>assignment: users</c>, <c>isinherited: 0</c>, the billing-admin marker as <c>holderPrivilege</c> and the
/// org setting it needs, name no team, and list no other table. The script's write paths are scanned too: no path that sets up
/// this role assigns it to a team.</para>
/// The files declare different teams and roles (each pinned by name, none the Secure Record owner role), the owner roles no
/// table in common, and the script's <c>$DeclaredRoleFiles</c> equals the declared file set (R4b-4: none missing, none extra).
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

    /// <summary>R4b-4 (B-K1): the restricted-content owner role's name, pinned as the team name is.</summary>
    internal const string RestrictedContentRoleName = "Restricted Content Owner";

    /// <summary>R4b-4 (B-K1): the customer configuration owner role's name, pinned as the team name is.</summary>
    internal const string CustomerConfigurationRoleName = "Customer Configuration Owner";

    /// <summary>089d: the Billing Rule Reader role's name, pinned as the owner roles' are. No team carries it.</summary>
    internal const string BillingRuleReaderRoleName = "Billing Rule Reader";

    /// <summary>089d (option R): the org setting a role assigned from another unit's copy needs (matrix business units).</summary>
    internal const string CrossUnitOrgSetting = "EnableOwnershipAcrossBusinessUnits";

    /// <summary>
    /// A declared role: its file, the team (<c>null</c>: no team, the role is assigned to users directly) and role names it must
    /// carry, its exact tables, why a table may never be in it, its one depth, and, for a directly assigned role, the privilege
    /// every holder must hold (<c>null</c>: a memberless owner team).
    /// </summary>
    internal sealed record DeclaredRole(
        string FileName, string? TeamName, string TeamNameSource, IReadOnlyCollection<string> Tables, string TablesSource,
        Func<string, string?> NeverListed, string RoleName, string Depth = "Basic", string? HolderPrivilege = null)
    {
        /// <summary>A memberless owner team (it owns rows); otherwise a role assigned directly to users (it only reads).</summary>
        public bool IsOwnerTeam => HolderPrivilege is null;
    }

    internal static readonly DeclaredRole RestrictedContent = new(
        "restricted-content-owner-role.json",
        RestrictedContentOwnerTeam.DefaultOwnerTeamName,
        "the BFF's default",
        RecordOwnershipResolver.RestrictedContentTables,
        "a hard-deny table (RecordOwnershipResolver.RestrictedContentTables)",
        _ => null,
        RestrictedContentRoleName);

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
                    : null,
        CustomerConfigurationRoleName);

    /// <summary>
    /// 089d: Read at Deep on <c>sprk_policy</c> (the Exclude route's caller check; <c>sprk_policyversion</c> is read there as the
    /// application, so it is not listed). 089e fix round 2 (owner 2026-11-04, "Approve role + dev apply"): and on <c>sprk_ruleset</c>
    /// (the rule-set by-id routes and list ask the caller's Read on the set) and <c>sprk_procedure</c> (Procedure override removal and
    /// preview ask the caller's Read on the Procedure). No team; every holder must hold the billing-admin marker the BFF checks.
    /// </summary>
    internal static readonly DeclaredRole BillingRuleReaders = new(
        "billing-rule-readers-role.json",
        null,
        "no team (the role is assigned to users directly)",
        ["sprk_policy", "sprk_ruleset", "sprk_procedure"],
        "a table the billing routes check as the caller (sprk_policy, sprk_ruleset, sprk_procedure)",
        _ => null,
        BillingRuleReaderRoleName,
        Depth: "Deep",
        HolderPrivilege: BillingAdminAuthorization.WriteRuleSetPrivilege);

    internal static readonly DeclaredRole[] Declared = [RestrictedContent, CustomerConfiguration, BillingRuleReaders];

    private static string LiveJson(DeclaredRole role) =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", role.FileName));

    private static string LiveJson() => LiveJson(RestrictedContent);

    /// <summary>The Secure Record owner role's name (<c>config/secure-record-owner-role.json</c>): never a declared role's.</summary>
    private static string SecureRecordOwnerRoleName() =>
        Text(JsonNode.Parse(File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", "secure-record-owner-role.json")))!
            .AsObject(), "roleName") ?? throw new InvalidOperationException("config/secure-record-owner-role.json names no roleName");

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
        if (Text(file, "depth") != role.Depth)
            findings.Add($"depth is '{Text(file, "depth")}', not {role.Depth}");
        // 089d (option R): no declared team has members, so no file names a membership or a member privilege.
        foreach (var gone in new[] { "membership", "memberPrivilege" })
        {
            if (file[gone] is not null)
                findings.Add($"{gone} is set (no declared team has members; 089d option R)");
        }

        // An owner team names no assignment (or 'team') and none of a directly assigned role's fields; a directly assigned role
        // names 'users', isinherited 0, its holder privilege and the org setting, and no team.
        if (role.IsOwnerTeam)
        {
            if (file["assignment"] is not null && Text(file, "assignment") != "team")
                findings.Add($"assignment is '{Text(file, "assignment")}', not team (an owner role is held by its owner team)");
            foreach (var field in new[] { "holderPrivilege", "isinherited", "requiresOrgSetting" })
            {
                if (file[field] is not null)
                    findings.Add($"{field} is set on a memberless owner team");
            }
        }
        else
        {
            if (Text(file, "assignment") != "users")
                findings.Add($"assignment is '{Text(file, "assignment")}', not users (no team holds this role)");
            if (file["isinherited"] is not JsonValue inherited || !inherited.TryGetValue<int>(out var inheritedValue) || inheritedValue != 0)
                findings.Add($"isinherited is '{file["isinherited"]?.ToJsonString()}', not 0 (Team privileges only)");
            if (Text(file, "holderPrivilege") != role.HolderPrivilege)
                findings.Add($"holderPrivilege is '{Text(file, "holderPrivilege")}', not the billing-admin marker '{role.HolderPrivilege}'");
            if (Text(file, "requiresOrgSetting") != CrossUnitOrgSetting)
                findings.Add($"requiresOrgSetting is '{Text(file, "requiresOrgSetting")}', not {CrossUnitOrgSetting}");
        }
        if (Text(file, "businessUnit") != "customer")
            findings.Add($"businessUnit is '{Text(file, "businessUnit")}', not customer");
        if (role.TeamName is null)
        {
            if (file["teamName"] is not null)
                findings.Add($"teamName is '{Text(file, "teamName")}': a team in the file; {role.TeamNameSource}");
        }
        else if (Text(file, "teamName") != role.TeamName)
            findings.Add($"teamName is '{Text(file, "teamName")}', not {role.TeamNameSource} '{role.TeamName}'");
        if (string.IsNullOrWhiteSpace(Text(file, "roleName")))
            findings.Add("roleName is empty");
        else if (Text(file, "roleName") != role.RoleName)
            findings.Add($"roleName is '{Text(file, "roleName")}', not the declared '{role.RoleName}'");

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
    public void TheLiveBillingRuleReaderFile_HoldsReadAtDeep_OnTheRuleRuleSetAndProcedureTablesOnly_AssignedToBillingAdminsWithNoTeam()
    {
        Assert.Empty(Findings(LiveJson(BillingRuleReaders), BillingRuleReaders));
    }

    /// <summary>
    /// The declared roles name different teams and roles; the OWNER roles share no table (whose would the row be?). A directly
    /// assigned role only reads, so its table may also be an owner team's (089d: sprk_policy is the restricted-content team's
    /// too); the script applies the same rule.
    /// </summary>
    [Fact]
    public void TheRoles_DeclareDifferentTeamsAndRoles_AndTheOwnerRolesNoTableInCommon()
    {
        var files = Declared.Select(role => JsonNode.Parse(LiveJson(role))!.AsObject()).ToArray();
        var owners = Declared.Where(r => r.IsOwnerTeam).Select(role => JsonNode.Parse(LiveJson(role))!.AsObject()).ToArray();
        Assert.Equal(owners.Length, owners.Select(f => Text(f, "teamName")).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(files.Length, files.Select(f => Text(f, "roleName")).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var tables = Declared.Where(r => r.IsOwnerTeam)
            .SelectMany(r => JsonNode.Parse(LiveJson(r))!["tables"]!.AsArray().Select(e => Text(e!.AsObject(), "logicalName")))
            .ToArray();
        Assert.Equal(tables.Length, tables.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(2, Declared.Count(r => r.IsOwnerTeam)); // non-vacuity: the owner-table check covers both owner roles
    }

    [Fact]
    public void TheDeclaredRoleNames_AreNeverTheSecureRecordOwnerRole()
    {
        var secure = SecureRecordOwnerRoleName();
        Assert.False(string.IsNullOrWhiteSpace(secure));
        Assert.All(Declared, role => Assert.NotEqual(secure, role.RoleName, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(Declared.Length, Declared.Select(r => r.RoleName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static string LiveScript() =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "scripts", "Set-CustomerOwnerTeams.ps1"));

    private static readonly Regex DeclaredRoleFilesAssignment =
        new(@"^[ \t]*\$DeclaredRoleFiles[ \t]*=[ \t]*@\(", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static string WithoutBlockComments(string script) => Regex.Replace(script, @"<#[\s\S]*?#>", "");

    /// <summary>The script's code: block comments dropped, and each line cut at its first <c>#</c>.</summary>
    private static string CodeOnly(string script) =>
        string.Join('\n', WithoutBlockComments(script).Split('\n').Select(line => line.Split('#')[0]));

    /// <summary>
    /// R4b-4b: the forms that WRITE <c>$DeclaredRoleFiles</c> by name — an assignment of any form (<c>=</c>, <c>+=</c>,
    /// <c>-=</c>, <c>??=</c>, a scoped or braced name), an element assignment (<c>$DeclaredRoleFiles[1] = …</c>),
    /// <c>Set-Variable</c>/<c>New-Variable</c>, a common parameter that writes a variable (<c>-OutVariable</c> and its kin),
    /// the <c>variable:</c> drive, a collection method that changes it, or a <c>[ref]</c>. Only the one array literal may
    /// write it. A write through an alias (<c>$x = $DeclaredRoleFiles; $x[0] = …</c>) is beyond a text scan.
    /// </summary>
    private static readonly Regex DeclaredRoleFilesWrite = new(
        @"\$\{?(?:(?:script|global|local|private|variable):)?DeclaredRoleFiles\}?\s*(?:\[[^\]]*\]\s*)?(?:[-+*/%]|\?\?)?=(?!=)"
        + @"|-(?:OutVariable|ov|PipelineVariable|pv|ErrorVariable|ev|WarningVariable|wv|InformationVariable|iv)\b\s*:?\s*['""]?\+?DeclaredRoleFiles\b"
        + @"|\b(?:Set|New)-Variable\b[^\n]*\bDeclaredRoleFiles\b"
        + @"|\bvariable:\s*DeclaredRoleFiles\b"
        + @"|\$\{?DeclaredRoleFiles\}?\s*\.\s*(?:Add|AddRange|Insert|InsertRange|Clear|Remove\w*|Set\w*)\s*\("
        + @"|\[ref\]\s*\$\{?DeclaredRoleFiles\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// R4b-4 (B-F3): the script's <c>$DeclaredRoleFiles</c> array as the script runs it — block comments and the comment part
    /// of each line dropped, so a commented-out entry is not declared — up to the array's closing parenthesis.
    /// </summary>
    internal static IReadOnlyList<string> ScriptDeclaredRoleFiles(string script)
    {
        var code = WithoutBlockComments(script);
        var start = DeclaredRoleFilesAssignment.Match(code);
        if (!start.Success)
            return [];
        var files = new List<string>();
        foreach (var raw in code[(start.Index + start.Length)..].Split('\n'))
        {
            var line = raw.Split('#')[0];
            var close = line.IndexOf(')');
            files.AddRange(Regex.Matches(close >= 0 ? line[..close] : line, "'([^']*)'").Select(m => m.Groups[1].Value));
            if (close >= 0)
                break;
        }

        return files;
    }

    /// <summary>Every way the script's declared files differ from <see cref="Declared"/>; empty when they are equal.</summary>
    internal static IReadOnlyList<string> ScriptFindings(string script)
    {
        var assignments = DeclaredRoleFilesAssignment.Matches(WithoutBlockComments(script)).Count;
        if (assignments != 1)
            return [$"$DeclaredRoleFiles is assigned {assignments} time(s), not once"];

        // R4b-4b: the one array literal is the ONLY write; an appended or reassigned file would bypass the parse below.
        var writes = DeclaredRoleFilesWrite.Matches(CodeOnly(script)).Select(m => m.Value.Trim()).ToList();
        if (writes.Count != 1)
            return [$"$DeclaredRoleFiles is written {writes.Count} time(s) ({string.Join("; ", writes)}); only its one array literal may write it"];

        var findings = new List<string>();
        var files = ScriptDeclaredRoleFiles(script);
        var declared = Declared.Select(r => r.FileName).ToHashSet(StringComparer.Ordinal);
        foreach (var dupe in files.GroupBy(f => f, StringComparer.Ordinal).Where(g => g.Count() > 1))
            findings.Add($"'{dupe.Key}' is listed {dupe.Count()} times");
        foreach (var extra in files.Where(f => !declared.Contains(f)).Distinct(StringComparer.Ordinal))
            findings.Add($"'{extra}' is not a declared role file");
        foreach (var missing in declared.Where(f => !files.Contains(f, StringComparer.Ordinal)).OrderBy(f => f, StringComparer.Ordinal))
            findings.Add($"'{missing}' is declared but not in $DeclaredRoleFiles");
        return findings;
    }

    [Fact]
    public void TheSetupScript_DeclaresExactlyTheDeclaredRoleFiles()
    {
        Assert.Equal(
            Declared.Select(r => r.FileName).OrderBy(f => f, StringComparer.Ordinal),
            ScriptDeclaredRoleFiles(LiveScript()).OrderBy(f => f, StringComparer.Ordinal));
        Assert.Empty(ScriptFindings(LiveScript()));
    }

    private const string ConfigurationEntry = "    'customer-configuration-owner-role.json'";
    private const string RestrictedEntry = "    'restricted-content-owner-role.json',";
    private const string BillingEntry = "    'billing-rule-readers-role.json'";

    public static TheoryData<string, string> ScriptMustFire() => new()
    {
        {
            "(A) an extra file in the array",
            LiveScript().Replace(ConfigurationEntry, ConfigurationEntry + ", 'secure-record-owner-role.json'", StringComparison.Ordinal)
        },
        {
            "(E) the configuration entry commented out",
            LiveScript().Replace(ConfigurationEntry, "#" + ConfigurationEntry, StringComparison.Ordinal)
        },
        {
            "(089d) the billing rule readers entry commented out",
            LiveScript().Replace(BillingEntry, "#" + BillingEntry, StringComparison.Ordinal)
        },
        {
            "the restricted-content entry commented out after its indent",
            LiveScript().Replace(RestrictedEntry, "    # 'restricted-content-owner-role.json',", StringComparison.Ordinal)
        },
        {
            "a file named twice",
            LiveScript().Replace(RestrictedEntry, RestrictedEntry + " 'restricted-content-owner-role.json',", StringComparison.Ordinal)
        },
        {
            "the assignment itself commented out",
            LiveScript().Replace("$DeclaredRoleFiles = @(", "# $DeclaredRoleFiles = @(", StringComparison.Ordinal)
        },
        {
            "(R4b-4b) a file appended with +=",
            AfterTheArray("$DeclaredRoleFiles += 'secure-record-owner-role.json'")
        },
        {
            "(R4b-4b) the array reassigned from itself",
            AfterTheArray("$DeclaredRoleFiles = $DeclaredRoleFiles + @('secure-record-owner-role.json')")
        },
        {
            "(R4b-4b) a scoped reassignment",
            AfterTheArray("$script:DeclaredRoleFiles = @('secure-record-owner-role.json')")
        },
        {
            "(R4b-4b) Set-Variable",
            AfterTheArray("Set-Variable -Name DeclaredRoleFiles -Value @('secure-record-owner-role.json')")
        },
        {
            "(R4b-4b) a collection method",
            AfterTheArray("$DeclaredRoleFiles.Add('secure-record-owner-role.json')")
        },
        {
            "an element assigned",
            AfterTheArray("$DeclaredRoleFiles[1] = 'secure-record-owner-role.json'")
        },
        {
            "an element appended to",
            AfterTheArray("$DeclaredRoleFiles[0] += 'x'")
        },
        {
            "a common parameter that writes the variable",
            AfterTheArray("Get-ChildItem config -Filter '*.json' -OutVariable DeclaredRoleFiles | Out-Null")
        },
    };

    /// <summary>The live script with <paramref name="line"/> added right after the <c>$DeclaredRoleFiles</c> array.</summary>
    private static string AfterTheArray(string line)
    {
        var script = LiveScript();
        var start = script.IndexOf("$DeclaredRoleFiles = @(", StringComparison.Ordinal);
        var close = script.IndexOf("\n)", start, StringComparison.Ordinal) + 2;
        return script[..close] + "\n" + line + script[close..];
    }

    [Theory]
    [InlineData("# $DeclaredRoleFiles += 'secure-record-owner-role.json'")]
    [InlineData("<# $DeclaredRoleFiles += 'secure-record-owner-role.json' #>")]
    [InlineData("$copy = $DeclaredRoleFiles")]
    [InlineData("if ($DeclaredRoleFiles -eq $null) { throw 'none' }")]
    public void AReadOrACommentedWrite_GivesNoFinding(string line)
    {
        Assert.Empty(ScriptFindings(AfterTheArray(line)));
    }

    [Theory]
    [MemberData(nameof(ScriptMustFire))]
    public void ASeededBadScript_GivesAFinding(string what, string script)
    {
        Assert.NotEqual(LiveScript(), script); // the seeded change landed
        Assert.True(ScriptFindings(script).Count > 0, $"must fire on {what}");
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
            "a role name other than the declared one (B-K1)",
            Mutated(f => f["roleName"] = "System Administrator")
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
        AssertTheSeededChangeLanded(RestrictedContent, json);
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
        {
            "a role name other than the declared one (B-K1)",
            Mutated(CustomerConfiguration, f => f["roleName"] = "System Administrator")
        },
    };

    [Theory]
    [MemberData(nameof(ConfigurationMustFire))]
    public void ASeededBadConfigurationDocument_GivesAFinding(string what, string json)
    {
        AssertTheSeededChangeLanded(CustomerConfiguration, json);
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

    // ── 089d (option R): the directly assigned Billing Rule Reader role ────────────────────────────────────────────────────────

    public static TheoryData<string, string> BillingRuleReadersMustFire() => new()
    {
        { "a team in the config", Mutated(BillingRuleReaders, f => f["teamName"] = "Billing Rule Readers") },
        { "assignment to a team", Mutated(BillingRuleReaders, f => f["assignment"] = "team") },
        { "assignment missing", Mutated(BillingRuleReaders, f => f.Remove("assignment")) },
        { "members (the replaced team design)", Mutated(BillingRuleReaders, f => f["membership"] = "members") },
        { "a member privilege (the replaced team design)", Mutated(BillingRuleReaders, f => f["memberPrivilege"] = BillingAdminAuthorization.WriteRuleSetPrivilege) },
        { "the root as the assignment unit", Mutated(BillingRuleReaders, f => f["businessUnit"] = "root") },
        { "Secure Record as the assignment unit", Mutated(BillingRuleReaders, f => f["businessUnit"] = "secure") },
        { "Global depth (the file's)", Mutated(BillingRuleReaders, f => f["depth"] = "Global") },
        { "Business Unit depth (misses the child units)", Mutated(BillingRuleReaders, f => f["depth"] = "Local") },
        { "Basic depth", Mutated(BillingRuleReaders, f => f["depth"] = "Basic") },
        { "a depth on one entry", Mutated(BillingRuleReaders, f => EntryFor(f, "sprk_policy")["depth"] = "Global") },
        { "isinherited 1", Mutated(BillingRuleReaders, f => f["isinherited"] = 1) },
        { "isinherited missing", Mutated(BillingRuleReaders, f => f.Remove("isinherited")) },
        { "isinherited as text", Mutated(BillingRuleReaders, f => f["isinherited"] = "0") },
        { "an extra privilege (Read on sprk_policyversion)", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Add(Entry("sprk_policyversion", "prvReadsprk_PolicyVersion"))) },
        { "an extra privilege (Write on sprk_policy)", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Add(Entry("sprk_policy", "prvWritesprk_Policy"))) },
        { "an extra table (sprk_procedureassignment)", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Add(Entry("sprk_procedureassignment", "prvReadsprk_procedureassignment"))) },
        { "an extra table (sprk_procedureversion)", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Add(Entry("sprk_procedureversion", "prvReadsprk_procedureversion"))) },
        { "an extra privilege (Write on sprk_ruleset)", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Add(Entry("sprk_ruleset", "prvWritesprk_ruleset"))) },
        { "sprk_ruleset missing (089e)", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Remove(EntryFor(f, "sprk_ruleset"))) },
        { "sprk_procedure missing (089e)", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Remove(EntryFor(f, "sprk_procedure"))) },
        { "a depth on the sprk_ruleset entry", Mutated(BillingRuleReaders, f => EntryFor(f, "sprk_ruleset")["depth"] = "Global") },
        { "sprk_procedure's entry naming another table's Read", Mutated(BillingRuleReaders, f => EntryFor(f, "sprk_procedure")["privilegeName"] = "prvReadsprk_procedureversion") },
        { "another access type", Mutated(BillingRuleReaders, f => f["access"] = "Write") },
        { "sprk_policy's entry naming another table's Read", Mutated(BillingRuleReaders, f => EntryFor(f, "sprk_policy")["privilegeName"] = "prvReadsprk_PolicyVersion") },
        { "sprk_policy missing", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Remove(EntryFor(f, "sprk_policy"))) },
        { "every table missing", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Clear()) },
        { "holderPrivilege missing (any user could hold it)", Mutated(BillingRuleReaders, f => f.Remove("holderPrivilege")) },
        { "holderPrivilege other than the billing-admin marker", Mutated(BillingRuleReaders, f => f["holderPrivilege"] = "prvReadsprk_Policy") },
        { "the org setting missing", Mutated(BillingRuleReaders, f => f.Remove("requiresOrgSetting")) },
        { "another org setting", Mutated(BillingRuleReaders, f => f["requiresOrgSetting"] = "AlwaysMoveRecordToOwnerBusinessUnit") },
        { "a role name other than the declared one", Mutated(BillingRuleReaders, f => f["roleName"] = "Spaarke Billing Administrator") },
    };

    [Theory]
    [MemberData(nameof(BillingRuleReadersMustFire))]
    public void ASeededBadBillingRuleReaderDocument_GivesAFinding(string what, string json)
    {
        AssertTheSeededChangeLanded(BillingRuleReaders, json);
        Assert.True(Findings(json, BillingRuleReaders).Count > 0, $"must fire on {what}");
    }

    /// <summary>
    /// A seeded document must differ from the live file AS PARSED. Comparing the live file's text with <c>Mutated</c>'s
    /// output cannot fail: <c>Mutated</c> re-serialises with <c>ToJsonString()</c> (compact), which never equals the
    /// file's own text, even when the change did nothing. Comparing the nodes can.
    /// </summary>
    private static void AssertTheSeededChangeLanded(DeclaredRole role, string json) =>
        Assert.False(
            JsonNode.DeepEquals(JsonNode.Parse(LiveJson(role)), JsonNode.Parse(json)),
            "the seeded change did not change the document");

    [Fact]
    public void TheSeededChangeCheck_CanFail_OnAChangeThatChangesNothing()
    {
        var noChange = Mutated(BillingRuleReaders, _ => { });
        // The old check passed here (re-serialised text never equals the file's text), so it could not catch a no-op change.
        Assert.NotEqual(LiveJson(BillingRuleReaders), noChange);
        // The node comparison sees the no-op, so the check fails on it.
        Assert.Throws<Xunit.Sdk.FalseException>(() => AssertTheSeededChangeLanded(BillingRuleReaders, noChange));
        // ... and holds on a real change.
        AssertTheSeededChangeLanded(BillingRuleReaders, Mutated(BillingRuleReaders, f => f["isinherited"] = 1));
    }

    [Fact]
    public void TheBillingRuleReaderMustFireControls_EachNameTheirOwnBreak()
    {
        IReadOnlyList<string> Of(Action<JsonObject> change) => Findings(Mutated(BillingRuleReaders, change), BillingRuleReaders);

        Assert.Contains(Of(f => f["teamName"] = "Billing Rule Readers"), f => f.StartsWith("teamName is 'Billing Rule Readers': a team in the file", StringComparison.Ordinal));
        Assert.Contains(Of(f => f["businessUnit"] = "root"), f => f == "businessUnit is 'root', not customer");
        Assert.Contains(Of(f => f["depth"] = "Global"), f => f == "depth is 'Global', not Deep");
        Assert.Contains(Of(f => f["isinherited"] = 1), f => f == "isinherited is '1', not 0 (Team privileges only)");
        Assert.Contains(Of(f => f["tables"]!.AsArray().Add(Entry("sprk_policyversion", "prvReadsprk_PolicyVersion"))),
            f => f.StartsWith("sprk_policyversion: not a table the billing routes check as the caller", StringComparison.Ordinal));
        Assert.Contains(Of(f => f["tables"]!.AsArray().Remove(EntryFor(f, "sprk_ruleset"))),
            f => f.StartsWith("sprk_ruleset: a table the billing routes check as the caller", StringComparison.Ordinal));
        Assert.Contains(Of(f => f["tables"]!.AsArray().Remove(EntryFor(f, "sprk_procedure"))),
            f => f.StartsWith("sprk_procedure: a table the billing routes check as the caller", StringComparison.Ordinal));
        Assert.Contains(Of(f => f["tables"]!.AsArray().Add(Entry("sprk_policy", "prvWritesprk_Policy"))),
            f => f == "sprk_policy: prvWritesprk_Policy is not this table's Read privilege");
        Assert.Contains(Of(f => f["assignment"] = "team"), f => f == "assignment is 'team', not users (no team holds this role)");
        Assert.Contains(Of(f => f.Remove("holderPrivilege")), f => f.StartsWith("holderPrivilege is ''", StringComparison.Ordinal));
        Assert.Contains(Of(f => f.Remove("requiresOrgSetting")), f => f.StartsWith("requiresOrgSetting is ''", StringComparison.Ordinal));
        // The owner teams' rule in the other direction: an owner file that gains a directly assigned role's field is refused.
        Assert.Contains(Findings(Mutated(f => f["assignment"] = "users")), f => f.StartsWith("assignment is 'users', not team", StringComparison.Ordinal));
        Assert.Contains(Findings(Mutated(f => f["isinherited"] = 0)), f => f == "isinherited is set on a memberless owner team");
        Assert.Contains(Findings(Mutated(f => f["membership"] = "members")), f => f.StartsWith("membership is set", StringComparison.Ordinal));
    }

    /// <summary>The holder privilege is the one the BFF's billing-admin marker checks (Exclude's second filter), not a copy.</summary>
    [Fact]
    public void TheBillingRuleReaderHolderPrivilege_IsTheBffsBillingAdminMarker()
    {
        Assert.Equal("prvWritesprk_ruleset", BillingRuleReaders.HolderPrivilege);
        Assert.Equal(BillingAdminAuthorization.WriteRuleSetPrivilege, Text(JsonNode.Parse(LiveJson(BillingRuleReaders))!.AsObject(), "holderPrivilege"));
    }

    // ── 089d: no team holds the role, on any script path (uac-r2 condition 5) ──────────────────────────────────────────────────

    /// <summary>The live script with LF line ends, so a seeded multi-line change lands on a CRLF checkout too.</summary>
    private static string LiveScriptLf() => LiveScript().Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// The text of one top-level function of the script's main block (block comments dropped, line comments kept, so the
    /// section rules still end it), up to the next function or section rule.
    /// </summary>
    private static string? FunctionBody(string script, string name)
    {
        var start = script.IndexOf($"function {name}(", StringComparison.Ordinal);
        if (start < 0)
            return null;
        var next = Regex.Match(script[(start + 1)..], @"\n    (?:function |# ──)");
        return next.Success ? script.Substring(start, next.Index + 1) : script[start..];
    }

    private static readonly Regex TeamRoleWrite = new(
        @"\bteamroles_association/`\$ref|AddMembersTeam|Invoke-DvPost\s+'teams'", RegexOptions.CultureInvariant);

    /// <summary>
    /// Every way the script could assign the directly assigned role to a team: the function that sets it up
    /// (<c>Invoke-ApplyRole</c>) writes a team role or creates a team; a team-role write sits outside the owner-team setup
    /// (<c>Invoke-ApplyTeam</c>); or the owner-team setup runs for anything but the owner-team configs.
    /// </summary>
    internal static IReadOnlyList<string> ScriptTeamHolderFindings(string script)
    {
        var raw = WithoutBlockComments(script);
        var findings = new List<string>();
        var applyRole = FunctionBody(raw, "Invoke-ApplyRole");
        var applyTeam = FunctionBody(raw, "Invoke-ApplyTeam");
        if (applyRole is null || applyTeam is null)
            return ["Invoke-ApplyRole or Invoke-ApplyTeam is missing (the scan cannot place the writes)"];

        foreach (Match m in TeamRoleWrite.Matches(CodeOnly(applyRole)))
            findings.Add($"Invoke-ApplyRole writes a team ({m.Value}); no team may hold the directly assigned role");

        var outside = CodeOnly(raw.Replace(applyTeam, "", StringComparison.Ordinal));
        foreach (Match m in TeamRoleWrite.Matches(outside))
            findings.Add($"a team write ({m.Value}) outside Invoke-ApplyTeam");

        var calls = Regex.Matches(outside, @"[^\n]*\bInvoke-ApplyTeam\b[^\n]*").Select(m => m.Value).ToList();
        if (calls.Count == 0)
            findings.Add("Invoke-ApplyTeam is never called (the scan cannot see where it runs)");
        foreach (var call in calls.Where(c => !c.Contains("Kind -eq 'team'", StringComparison.Ordinal)))
            findings.Add($"Invoke-ApplyTeam runs without the owner-team filter: {call.Trim()}");
        return findings;
    }

    [Fact]
    public void TheSetupScript_NeverAssignsTheDirectlyAssignedRoleToATeam()
    {
        Assert.Empty(ScriptTeamHolderFindings(LiveScript()));
    }

    public static TheoryData<string, string> ScriptTeamHolderMustFire() => new()
    {
        {
            "Invoke-ApplyRole assigns the copy to a team",
            LiveScriptLf().Replace(
                "        Set-RolePrivileges $d $roleId $s.Held\n        $s = Get-RoleState $d",
                "        Set-RolePrivileges $d $roleId $s.Held\n        Invoke-DvPost \"teams($tid)/teamroles_association/`$ref\" @{ '@odata.id' = \"$Api/roles($roleId)\" } | Out-Null\n        $s = Get-RoleState $d",
                StringComparison.Ordinal)
        },
        {
            "Invoke-ApplyRole creates a team",
            LiveScriptLf().Replace(
                "        Set-RolePrivileges $d $roleId $s.Held\n        $s = Get-RoleState $d",
                "        Set-RolePrivileges $d $roleId $s.Held\n        Invoke-DvPost 'teams' @{ name = 'Billing Rule Readers' } | Out-Null\n        $s = Get-RoleState $d",
                StringComparison.Ordinal)
        },
        {
            "a team-role write after the apply loops",
            LiveScriptLf().Replace(
                "    $left = 0\n",
                "    Invoke-DvPost \"teams($tid)/teamroles_association/`$ref\" $ref | Out-Null\n    $left = 0\n",
                StringComparison.Ordinal)
        },
        {
            "the owner-team setup run for every config",
            LiveScriptLf().Replace("$_.Kind -eq 'team' -and $_.Drift.Count -gt 0", "$_.Drift.Count -gt 0", StringComparison.Ordinal)
        },
    };

    [Theory]
    [MemberData(nameof(ScriptTeamHolderMustFire))]
    public void ASeededScriptThatGivesTheRoleToATeam_GivesAFinding(string what, string script)
    {
        Assert.NotEqual(LiveScriptLf(), script); // the seeded change landed
        Assert.True(ScriptTeamHolderFindings(script).Count > 0, $"must fire on {what}");
    }

    // ── 089d: the assignment writes the customer top unit's copy, never the root's (pass-1 F3) ────────────────────────────────
    // The write that decides isolation is the user-role association in Invoke-ApplyRole. A copy other than the customer top
    // unit's (the root role: Deep from the root reaches Secure Record) would break isolation. The Pester suite proves the
    // behaviour against a mocked Dataverse; this scan keeps the source from changing quietly.

    /// <summary>Every <c>@odata.id</c> the assign path writes names <c>$s.Copy.roleid</c> (the customer top unit's copy), and there is a user-role write.</summary>
    internal static IReadOnlyList<string> ScriptAssignCopyFindings(string script)
    {
        var applyRole = FunctionBody(WithoutBlockComments(script), "Invoke-ApplyRole");
        if (applyRole is null)
            return ["Invoke-ApplyRole is missing (the scan cannot place the user-role write)"];
        var code = CodeOnly(applyRole);
        var findings = new List<string>();
        if (!code.Contains("systemuserroles_association", StringComparison.Ordinal))
            findings.Add("Invoke-ApplyRole has no systemuserroles_association write (the scan cannot see the assignment)");
        var refs = Regex.Matches(code, @"'@odata\.id'\s*=[^\r\n]*").Select(m => m.Value.Trim()).ToList();
        if (refs.Count == 0)
            findings.Add("Invoke-ApplyRole names no @odata.id (the scan cannot see which copy is assigned)");
        foreach (var r in refs.Where(r => !r.Contains("$s.Copy.roleid", StringComparison.Ordinal)))
            findings.Add($"the user-role write does not name $s.Copy.roleid (the customer top unit's copy): {r}");
        return findings;
    }

    [Fact]
    public void TheSetupScript_AssignsOnlyTheCustomerTopUnitsCopy()
    {
        Assert.Empty(ScriptAssignCopyFindings(LiveScript()));
    }

    public static TheoryData<string, string> ScriptAssignCopyMustFire() => new()
    {
        {
            "the root role is assigned",
            LiveScriptLf().Replace(
                "'@odata.id' = \"$Api/roles($($s.Copy.roleid))\" }\n            Invoke-DvPost \"systemusers(",
                "'@odata.id' = \"$Api/roles($roleId)\" }\n            Invoke-DvPost \"systemusers(",
                StringComparison.Ordinal)
        },
        {
            "the role as read (the root) is assigned",
            LiveScriptLf().Replace(
                "'@odata.id' = \"$Api/roles($($s.Copy.roleid))\" }\n            Invoke-DvPost \"systemusers(",
                "'@odata.id' = \"$Api/roles($($s.Role.roleid))\" }\n            Invoke-DvPost \"systemusers(",
                StringComparison.Ordinal)
        },
        {
            "the user-role write is gone",
            LiveScriptLf().Replace("systemuserroles_association", "systemuserroles_x", StringComparison.Ordinal)
        },
    };

    [Theory]
    [MemberData(nameof(ScriptAssignCopyMustFire))]
    public void ASeededScriptThatAssignsAnotherCopy_GivesAFinding(string what, string script)
    {
        Assert.NotEqual(LiveScriptLf(), script); // the seeded change landed
        Assert.True(ScriptAssignCopyFindings(script).Count > 0, $"must fire on {what}");
    }

    // ── 089d: the reach model (uac-r2's census) ────────────────────────────────────────────────────────────────────────────────

    private static readonly Guid Root = Guid.NewGuid();
    private static readonly Guid Secure = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Child = Guid.NewGuid();
    private static readonly Guid Grandchild = Guid.NewGuid();
    private static readonly Guid OtherCustomer = Guid.NewGuid();

    private static readonly BusinessUnitNode[] Tree =
    [
        new(Root, "Spaarke", null),
        new(Secure, "Secure Record", Root),
        new(Customer, "Customer One", Root),
        new(Child, "Customer One / Litigation", Customer),
        new(Grandchild, "Customer One / Litigation / West", Child),
        new(OtherCustomer, "Customer Two", Root),
    ];

    /// <summary>
    /// 089d negative case, through the Secure census's own depth model (<see cref="SecureBuRoleDepthAssertion.Reaches(PrivilegeDepth, Guid, Guid, IReadOnlyList{BusinessUnitNode})"/>,
    /// a directly assigned grant anchored at the ROLE COPY's unit): Deep from the customer top unit's copy reaches a rule owned in
    /// a child unit of it and never one owned in Secure Record, a sibling. The two shapes ruled out (D-123), Global and Deep from
    /// the root's copy, both reach Secure Record, so the census would report either.
    /// </summary>
    [Fact]
    public void TheBillingRuleReader_DeepFromTheCustomerTopUnitsCopy_ReachesItsChildUnits_AndNeverSecureRecord()
    {
        var depth = Enum.Parse<PrivilegeDepth>(Text(JsonNode.Parse(LiveJson(BillingRuleReaders))!.AsObject(), "depth")!);
        bool Reaches(PrivilegeDepth d, Guid anchor, Guid owner) => SecureBuRoleDepthAssertion.Reaches(d, anchor, owner, Tree);

        Assert.Equal(PrivilegeDepth.Deep, depth);
        Assert.True(Reaches(depth, Customer, Customer));    // a rule owned in the customer's top unit
        Assert.True(Reaches(depth, Customer, Child));       // ... in a child unit (the child-unit admin's 403 goes away)
        Assert.True(Reaches(depth, Customer, Grandchild));  // ... and below
        Assert.False(Reaches(depth, Customer, Secure));     // never a rule owned by the Secure Record Owners team
        Assert.False(Reaches(depth, Customer, Root));       // never the root's product rules
        Assert.False(Reaches(depth, Customer, OtherCustomer)); // never another customer's unit
        Assert.False(Reaches(PrivilegeDepth.Deep, Child, Customer)); // why the child unit's own copy is not enough
        Assert.True(Reaches(PrivilegeDepth.Deep, Root, Secure));     // ruled out: Deep from the root's copy
        Assert.True(Reaches(PrivilegeDepth.Global, Customer, Secure)); // ruled out: Global
    }

    /// <summary>
    /// The census already models option R (no assertion change): <see cref="SecureBuRoleDepthCensusBuilder.Build"/> anchors a
    /// user's direct grant at the business unit of the role COPY the user holds, not at the user's home unit. A human homed in a
    /// child unit holding the customer top unit's copy of Read at Deep on <c>sprk_policy</c> gives no clause-1 finding; the same
    /// user holding the ROOT copy does, and so does the top unit's copy at Global.
    /// </summary>
    [Fact]
    public void TheSecureCensus_AnchorsTheCrossUnitAssignmentAtTheCopysUnit_AndReportsOnlyTheRuledOutShapes()
    {
        var rootRole = Guid.NewGuid();
        var adminRole = Guid.NewGuid();
        Guid CopyIn(Guid unit) => unit == Root ? rootRole : new Guid(unit.ToByteArray().Select(b => (byte)(b ^ 0x5A)).ToArray());
        var copies = new[] { Root, Secure, Customer, Child, OtherCustomer }
            .Select(unit => new CensusRole(CopyIn(unit), BillingRuleReaderRoleName, unit, rootRole))
            .Append(new CensusRole(adminRole, "System Administrator", Root, adminRole))
            .ToArray();
        var holder = Guid.NewGuid();
        var appUser = Guid.NewGuid();

        IReadOnlyList<EffectiveGrant> grants = [];
        IReadOnlyList<SecureBuFinding> ClauseOne(Guid heldCopy, PrivilegeDepth depth)
        {
            var census = SecureBuRoleDepthCensusBuilder.Build(
                "Secure Record",
                "Secure Record Owners",
                Tree,
                new Dictionary<(Guid, string), PrivilegeDepth>
                {
                    [(rootRole, "prvReadsprk_Policy")] = depth,
                    [(adminRole, "prvReadsprk_Project")] = PrivilegeDepth.Global, // a root grant, so the census is not vacuous
                },
                copies,
                [
                    new CensusUser(holder, IsHuman: true, Child, [heldCopy]),
                    new CensusUser(appUser, IsHuman: false, Root, [adminRole]),
                ],
                [],
                SecureRecordOwnerRoleSet.Embedded,
                SecureBuRoleDepthCensusBuilder.CensusPrivilegeNames(SecureRecordOwnerRoleSet.Embedded));
            grants = census.Grants.Where(g => g.PrincipalId == holder).ToArray();
            return SecureBuRoleDepthAssertion.Evaluate(census).Findings
                .Where(f => f.Verdict == SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit)
                .ToArray();
        }

        Assert.Empty(ClauseOne(CopyIn(Customer), PrivilegeDepth.Deep));
        var grant = Assert.Single(grants);
        Assert.Equal(Customer, grant.AnchorBusinessUnitId); // the copy's unit, not the holder's home (Child)
        Assert.Equal("prvReadsprk_Policy", grant.PrivilegeName);

        Assert.NotEmpty(ClauseOne(CopyIn(Root), PrivilegeDepth.Deep));       // ruled out: the root's copy
        Assert.NotEmpty(ClauseOne(CopyIn(Customer), PrivilegeDepth.Global)); // ruled out: Global
        Assert.Empty(ClauseOne(CopyIn(Child), PrivilegeDepth.Deep));         // the admin-UI copy reaches nothing it should not (nor the top unit)
    }

    /// <summary>
    /// 089e fix round 2, uac-r2's condition (binding): the EXTENDED role (every privilege the live file lists, at the file's depth) passes
    /// <see cref="SecureBuRoleDepthAssertion"/> for every unit copy holders are assigned from (each customer top unit's), and its reach
    /// covers no row owned in Secure Record. A privilege the census grades (<see cref="SecureBuRoleDepthCensusBuilder.CensusPrivilegeNames"/>)
    /// is proven through <see cref="SecureBuRoleDepthAssertion.Evaluate"/> (clause 1, no finding); one it does not grade (on 2026-11-04,
    /// <c>prvReadsprk_procedure</c>: not on the NFR-05 lists) through the census's own depth model,
    /// <see cref="SecureBuRoleDepthAssertion.Reaches(PrivilegeDepth, Guid, Guid, IReadOnlyList{BusinessUnitNode})"/>. Must-fire: the same
    /// privileges from the root's copy, or at Global, are clause-1 findings for every graded one, and reach Secure Record for every one.
    /// The tree is the D-123 shape the setup script refuses to run without: Secure Record a direct child of the root, outside every
    /// customer subtree.
    /// </summary>
    [Fact]
    public void TheExtendedBillingRuleReader_PassesTheSecureCensus_FromEveryCustomerTopUnitsCopy_AndNeverReachesSecureRecord()
    {
        var file = JsonNode.Parse(LiveJson(BillingRuleReaders))!.AsObject();
        var depth = Enum.Parse<PrivilegeDepth>(Text(file, "depth")!);
        var privileges = file["tables"]!.AsArray().Select(e => Text(e!.AsObject(), "privilegeName")!).ToArray();
        Assert.Equal(new[] { "prvReadsprk_Policy", "prvReadsprk_ruleset", "prvReadsprk_procedure" }, privileges);

        // D-123 shape: Secure Record sits directly under the root and inside no customer subtree.
        Assert.Equal(Root, Tree.Single(u => u.Id == Secure).ParentId);
        foreach (var top in new[] { Customer, OtherCustomer })
        {
            Assert.Equal(Root, Tree.Single(u => u.Id == top).ParentId);
            Assert.False(SecureBuRoleDepthAssertion.Reaches(PrivilegeDepth.Deep, top, Secure, Tree));
        }

        var graded = SecureBuRoleDepthCensusBuilder.CensusPrivilegeNames(SecureRecordOwnerRoleSet.Embedded).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var censused = privileges.Where(graded.Contains).ToArray();
        Assert.Contains("prvReadsprk_ruleset", censused);   // non-vacuity: the census grades the new rule-set Read
        Assert.Contains("prvReadsprk_Policy", censused);

        var rootRole = Guid.NewGuid();
        var adminRole = Guid.NewGuid();
        Guid CopyIn(Guid unit) => unit == Root ? rootRole : new Guid(unit.ToByteArray().Select(b => (byte)(b ^ 0x3C)).ToArray());
        var copies = new[] { Root, Secure, Customer, Child, Grandchild, OtherCustomer }
            .Select(unit => new CensusRole(CopyIn(unit), BillingRuleReaderRoleName, unit, rootRole))
            .Append(new CensusRole(adminRole, "System Administrator", Root, adminRole))
            .ToArray();

        IReadOnlyList<SecureBuFinding> ClauseOne(Guid heldCopy, Guid home, PrivilegeDepth d)
        {
            var depths = new Dictionary<(Guid, string), PrivilegeDepth> { [(adminRole, "prvReadsprk_Project")] = PrivilegeDepth.Global };
            foreach (var privilege in privileges)
            {
                depths[(rootRole, privilege)] = d;
            }

            var census = SecureBuRoleDepthCensusBuilder.Build(
                "Secure Record",
                "Secure Record Owners",
                Tree,
                depths,
                copies,
                [
                    new CensusUser(Guid.NewGuid(), IsHuman: true, home, [heldCopy]),
                    new CensusUser(Guid.NewGuid(), IsHuman: false, Root, [adminRole]),
                ],
                [],
                SecureRecordOwnerRoleSet.Embedded,
                SecureBuRoleDepthCensusBuilder.CensusPrivilegeNames(SecureRecordOwnerRoleSet.Embedded));
            return SecureBuRoleDepthAssertion.Evaluate(census).Findings
                .Where(f => f.Verdict == SecureBuVerdict.HumanPrincipalReachesSecureBusinessUnit)
                .ToArray();
        }

        // Every unit copy holders are assigned from: each customer top unit's, held by a user homed below it.
        foreach (var (top, home) in new[] { (Customer, Child), (Customer, Grandchild), (OtherCustomer, OtherCustomer) })
        {
            Assert.Empty(ClauseOne(CopyIn(top), home, depth));
            // Reaches takes no privilege (089e review K1): it is the depth model alone, the same for every privilege of the file.
            Assert.False(SecureBuRoleDepthAssertion.Reaches(depth, top, Secure, Tree), $"the file's depth from {top} reaches Secure Record");
        }

        // Must-fire: the ruled-out shapes are findings for every graded privilege, and reach Secure Record for every privilege.
        var fromRoot = ClauseOne(CopyIn(Root), Child, depth);
        var global = ClauseOne(CopyIn(Customer), Child, PrivilegeDepth.Global);
        var tableOf = file["tables"]!.AsArray().ToDictionary(e => Text(e!.AsObject(), "privilegeName")!, e => Text(e!.AsObject(), "logicalName")!);
        foreach (var privilege in censused)
        {
            // A clause-1 child-table finding names the tables it covers.
            Assert.Contains(fromRoot, f => f.Message.Contains(tableOf[privilege], StringComparison.OrdinalIgnoreCase));
            Assert.Contains(global, f => f.Message.Contains(tableOf[privilege], StringComparison.OrdinalIgnoreCase));
        }

        // The depth model takes no privilege (089e review K1), so its must-fire controls are one assertion each, not one per privilege.
        Assert.True(SecureBuRoleDepthAssertion.Reaches(depth, Root, Secure, Tree), "the file's depth from the root's copy reaches Secure Record");
        Assert.True(SecureBuRoleDepthAssertion.Reaches(PrivilegeDepth.Global, Customer, Secure, Tree), "Global reaches Secure Record");
    }
}
