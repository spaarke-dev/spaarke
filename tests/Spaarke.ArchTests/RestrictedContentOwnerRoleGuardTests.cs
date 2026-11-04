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
/// <para>ebilling-r1 task 089d adds a third declared role that is NOT an owner role: <c>config/billing-rule-readers-role.json</c>,
/// the <c>Billing Rule Readers</c> team, which HAS members (child-unit billing admins) and whose role holds Read at Deep on
/// <c>sprk_policy</c> only. Held by a team in the customer's top unit, Deep reaches that unit and its children and never Secure
/// Record (a sibling); Global, or Deep held in the root, would reach Secure Record (D-123). Its file must say <c>Deep</c>,
/// <c>customer</c>, <c>membership: members</c> and the billing-admin marker as <c>memberPrivilege</c>, and list no other table.</para>
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

    /// <summary>089d: the Billing Rule Readers team and role names, pinned as the owner teams' are.</summary>
    internal const string BillingRuleReadersTeamName = "Billing Rule Readers";

    internal const string BillingRuleReaderRoleName = "Billing Rule Reader";

    /// <summary>
    /// A declared role: its file, the team and role names it must carry, its exact tables, why a table may never be in it, its one
    /// depth, and, for a member team, the privilege every member must hold (<c>null</c>: a memberless owner team).
    /// </summary>
    internal sealed record DeclaredRole(
        string FileName, string TeamName, string TeamNameSource, IReadOnlyCollection<string> Tables, string TablesSource,
        Func<string, string?> NeverListed, string RoleName, string Depth = "Basic", string? MemberPrivilege = null)
    {
        /// <summary>A memberless owner team (it owns rows); otherwise a member team (it only reads).</summary>
        public bool IsOwnerTeam => MemberPrivilege is null;
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
    /// 089d: Read at Deep on <c>sprk_policy</c> only (the Exclude route's caller check; <c>sprk_policyversion</c> is read there as
    /// the application, so it is not listed). Members must hold the billing-admin marker the BFF checks.
    /// </summary>
    internal static readonly DeclaredRole BillingRuleReaders = new(
        "billing-rule-readers-role.json",
        BillingRuleReadersTeamName,
        "the declared",
        ["sprk_policy"],
        "the rule table the Exclude route checks as the caller (sprk_policy)",
        _ => null,
        BillingRuleReaderRoleName,
        Depth: "Deep",
        MemberPrivilege: BillingAdminAuthorization.WriteRuleSetPrivilege);

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
        // 089d: a memberless owner team names no membership (or 'none') and no member privilege; a member team names both.
        if (role.IsOwnerTeam)
        {
            if (file["membership"] is not null && Text(file, "membership") != "none")
                findings.Add($"membership is '{Text(file, "membership")}', not none (an owner team has no members)");
            if (file["memberPrivilege"] is not null)
                findings.Add("memberPrivilege is set on a memberless owner team");
        }
        else
        {
            if (Text(file, "membership") != "members")
                findings.Add($"membership is '{Text(file, "membership")}', not members");
            if (Text(file, "memberPrivilege") != role.MemberPrivilege)
                findings.Add($"memberPrivilege is '{Text(file, "memberPrivilege")}', not the billing-admin marker '{role.MemberPrivilege}'");
        }
        if (Text(file, "businessUnit") != "customer")
            findings.Add($"businessUnit is '{Text(file, "businessUnit")}', not customer");
        if (Text(file, "teamName") != role.TeamName)
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
    public void TheLiveBillingRuleReadersFile_HoldsReadAtDeep_OnSprkPolicyOnly_ForBillingAdminMembers()
    {
        Assert.Empty(Findings(LiveJson(BillingRuleReaders), BillingRuleReaders));
    }

    /// <summary>
    /// The declared roles name different teams and roles; the OWNER roles share no table (whose would the row be?). A member team
    /// only reads, so its table may also be an owner team's (089d: sprk_policy is the restricted-content team's too); the script
    /// applies the same rule.
    /// </summary>
    [Fact]
    public void TheRoles_DeclareDifferentTeamsAndRoles_AndTheOwnerRolesNoTableInCommon()
    {
        var files = Declared.Select(role => JsonNode.Parse(LiveJson(role))!.AsObject()).ToArray();
        Assert.Equal(files.Length, files.Select(f => Text(f, "teamName")).Distinct(StringComparer.OrdinalIgnoreCase).Count());
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

    // ── 089d: the Billing Rule Readers member team ─────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> BillingRuleReadersMustFire() => new()
    {
        { "the team in the root", Mutated(BillingRuleReaders, f => f["businessUnit"] = "root") },
        { "the team in Secure Record", Mutated(BillingRuleReaders, f => f["businessUnit"] = "secure") },
        { "Global depth (the file's)", Mutated(BillingRuleReaders, f => f["depth"] = "Global") },
        { "Business Unit depth (misses the child units)", Mutated(BillingRuleReaders, f => f["depth"] = "Local") },
        { "Basic depth", Mutated(BillingRuleReaders, f => f["depth"] = "Basic") },
        { "a depth on one entry", Mutated(BillingRuleReaders, f => EntryFor(f, "sprk_policy")["depth"] = "Global") },
        { "an extra privilege (Read on sprk_policyversion)", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Add(Entry("sprk_policyversion", "prvReadsprk_PolicyVersion"))) },
        { "an extra privilege (Write on sprk_policy)", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Add(Entry("sprk_policy", "prvWritesprk_Policy"))) },
        { "an extra table (sprk_ruleset)", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Add(Entry("sprk_ruleset", "prvReadsprk_ruleset"))) },
        { "another access type", Mutated(BillingRuleReaders, f => f["access"] = "Write") },
        { "sprk_policy's entry naming another table's Read", Mutated(BillingRuleReaders, f => EntryFor(f, "sprk_policy")["privilegeName"] = "prvReadsprk_PolicyVersion") },
        { "sprk_policy missing", Mutated(BillingRuleReaders, f => f["tables"]!.AsArray().Clear()) },
        { "membership none (an owner team at Deep)", Mutated(BillingRuleReaders, f => f["membership"] = "none") },
        { "membership missing", Mutated(BillingRuleReaders, f => f.Remove("membership")) },
        { "memberPrivilege missing (any user could be a member)", Mutated(BillingRuleReaders, f => f.Remove("memberPrivilege")) },
        { "memberPrivilege other than the billing-admin marker", Mutated(BillingRuleReaders, f => f["memberPrivilege"] = "prvReadsprk_Policy") },
        { "a team name other than the declared one", Mutated(BillingRuleReaders, f => f["teamName"] = RestrictedContentOwnerTeam.DefaultOwnerTeamName) },
        { "a role name other than the declared one", Mutated(BillingRuleReaders, f => f["roleName"] = "Spaarke Billing Administrator") },
    };

    [Theory]
    [MemberData(nameof(BillingRuleReadersMustFire))]
    public void ASeededBadBillingRuleReadersDocument_GivesAFinding(string what, string json)
    {
        Assert.NotEqual(LiveJson(BillingRuleReaders), json);
        Assert.True(Findings(json, BillingRuleReaders).Count > 0, $"must fire on {what}");
    }

    [Fact]
    public void TheBillingRuleReadersMustFireControls_EachNameTheirOwnBreak()
    {
        IReadOnlyList<string> Of(Action<JsonObject> change) => Findings(Mutated(BillingRuleReaders, change), BillingRuleReaders);

        Assert.Contains(Of(f => f["businessUnit"] = "root"), f => f == "businessUnit is 'root', not customer");
        Assert.Contains(Of(f => f["depth"] = "Global"), f => f == "depth is 'Global', not Deep");
        Assert.Contains(Of(f => f["tables"]!.AsArray().Add(Entry("sprk_policyversion", "prvReadsprk_PolicyVersion"))),
            f => f.StartsWith("sprk_policyversion: not the rule table", StringComparison.Ordinal));
        Assert.Contains(Of(f => f["tables"]!.AsArray().Add(Entry("sprk_policy", "prvWritesprk_Policy"))),
            f => f == "sprk_policy: prvWritesprk_Policy is not this table's Read privilege");
        Assert.Contains(Of(f => f["membership"] = "none"), f => f == "membership is 'none', not members");
        Assert.Contains(Of(f => f.Remove("memberPrivilege")), f => f.StartsWith("memberPrivilege is ''", StringComparison.Ordinal));
        // The owner teams' rule in the other direction: a memberless owner file that grows members is refused.
        Assert.Contains(Findings(Mutated(f => f["membership"] = "members")),
            f => f.StartsWith("membership is 'members', not none", StringComparison.Ordinal));
        Assert.Contains(Findings(Mutated(f => f["memberPrivilege"] = BillingAdminAuthorization.WriteRuleSetPrivilege)),
            f => f == "memberPrivilege is set on a memberless owner team");
    }

    /// <summary>The member privilege is the one the BFF's billing-admin marker checks (Exclude's second filter), not a copy.</summary>
    [Fact]
    public void TheBillingRuleReadersMemberPrivilege_IsTheBffsBillingAdminMarker()
    {
        Assert.Equal("prvWritesprk_ruleset", BillingRuleReaders.MemberPrivilege);
        Assert.Equal(BillingAdminAuthorization.WriteRuleSetPrivilege, Text(JsonNode.Parse(LiveJson(BillingRuleReaders))!.AsObject(), "memberPrivilege"));
    }

    /// <summary>
    /// 089d negative case, through the Secure census's own depth model (<see cref="SecureBuRoleDepthAssertion.Reaches(PrivilegeDepth, Guid, Guid, IReadOnlyList{BusinessUnitNode})"/>,
    /// a team-held grant anchored at the TEAM's unit): the team's Deep Read, held in the customer's top unit, reaches a rule owned in
    /// a child unit of it and never one owned in Secure Record, a sibling. The two shapes uac-r2 ruled out (D-123), Global and Deep
    /// held in the root, both reach Secure Record, so the census would report either.
    /// </summary>
    [Fact]
    public void TheBillingRuleReadersTeam_DeepFromTheCustomerTopUnit_ReachesItsChildUnits_AndNeverSecureRecord()
    {
        var root = Guid.NewGuid();
        var secure = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var child = Guid.NewGuid();
        var grandchild = Guid.NewGuid();
        var otherCustomer = Guid.NewGuid();
        BusinessUnitNode[] tree =
        [
            new(root, "Spaarke", null),
            new(secure, "Secure Record", root),
            new(customer, "Customer One", root),
            new(child, "Customer One / Litigation", customer),
            new(grandchild, "Customer One / Litigation / West", child),
            new(otherCustomer, "Customer Two", root),
        ];
        var depth = Enum.Parse<PrivilegeDepth>(Text(JsonNode.Parse(LiveJson(BillingRuleReaders))!.AsObject(), "depth")!);
        bool Reaches(PrivilegeDepth d, Guid anchor, Guid owner) => SecureBuRoleDepthAssertion.Reaches(d, anchor, owner, tree);

        Assert.Equal(PrivilegeDepth.Deep, depth);
        Assert.True(Reaches(depth, customer, customer));    // a rule owned in the customer's top unit
        Assert.True(Reaches(depth, customer, child));       // ... in a child unit (the child-unit admin's 403 goes away)
        Assert.True(Reaches(depth, customer, grandchild));  // ... and below
        Assert.False(Reaches(depth, customer, secure));     // never a rule owned by the Secure Record Owners team
        Assert.False(Reaches(depth, customer, root));       // never the root's product rules
        Assert.False(Reaches(depth, customer, otherCustomer)); // never another customer's unit
        Assert.False(Reaches(PrivilegeDepth.Deep, child, customer)); // why a child-unit admin's own Deep Read is not enough
        Assert.True(Reaches(PrivilegeDepth.Deep, root, secure));     // ruled out: Deep held in the root
        Assert.True(Reaches(PrivilegeDepth.Global, customer, secure)); // ruled out: Global
    }
}
