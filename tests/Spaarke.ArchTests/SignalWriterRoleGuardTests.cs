using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 044 (owner ruling 2026-11-06; ontology D-204, D-210): an exact allow-list of roles that may
/// create or write Signals and Decision Records. The core record pair carries no field security, so write integrity rests on the roles,
/// the writers' refusal (D-204), column auditing and task 052's report. Create and Write are separate sets, each checked for EQUALITY
/// with its allow-list (an extra role fails; an allow-listed role defined in the repo that no longer holds the privilege fails), at any
/// depth, over every role definition under <c>src/</c> (any <c>*.xml</c> that has a <c>RolePrivilege</c> element or a <c>Role</c> root,
/// whatever its folder is called), whoever holds the role (users or teams). A privilege name in any <c>config/**/*.json</c>,
/// <c>scripts/**/*.json</c> or <c>scripts/**/*.ps1</c> is also a finding, with the named exceptions below.
/// <para>Only roles the repo defines can be checked here: the platform roles on the allow-lists are not role files, and who may hold
/// the service roles is live state that is not in the repo, so both are checked read-only against the live environment.</para>
/// <para>Complements <see cref="FeatureRolePrivilegeGuardTests.MustNotHold"/> (046b), which forbids Create, Write and Delete on these
/// tables to every person role except the cleanup role: this guard also covers the app users' role (so its missing Write on Decision
/// Records, on which append-only depends, is pinned) and does not except the cleanup role.</para>
/// </summary>
public class SignalWriterRoleGuardTests
{
    /// <summary>The allow-lists (role names appear only here). Agreed by ontology-r1 and unified-access-control-r2, 2026-11-06.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> AllowList = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["prvCreatesprk_Signal"] = ["System Administrator", "System Customizer", "Service Writer", "Spaarke Ontology Service"],
        ["prvWritesprk_Signal"] = ["System Administrator", "System Customizer", "Service Writer", "Spaarke Ontology Service"],
        ["prvCreatesprk_DecisionRecord"] = ["System Administrator", "System Customizer", "Service Writer", "Spaarke Ontology Service"],
        ["prvWritesprk_DecisionRecord"] = ["System Administrator", "System Customizer", "Service Writer"],
    };

    /// <summary>The platform roles on the allow-lists. They are not role files: a repo role file with one of these names would put a role
    /// the repo controls on the allow-list under a platform role's name, so it is a finding (fix round 2).</summary>
    internal static readonly string[] PlatformRoles = ["System Administrator", "System Customizer", "Service Writer"];

    private const string OntologyService = "Spaarke Ontology Service";

    /// <summary>Role definitions are read from everywhere under <c>src/</c>, not from one folder name (fix round 3).</summary>
    private static string SourceDirectory => Path.Combine(SourceScan.RepoRoot, "src");

    /// <summary>The committed SpaarkeMaster role folder (the seeded-tree tests copy it).</summary>
    private static string MasterRolesDirectory => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Roles");

    /// <summary>Files that name a guarded privilege only to REMOVE it (verified by <see cref="RemovalOnlyScriptsHoldNoGrantCall"/>). Matched
    /// by file name anywhere under <c>scripts/</c>. Add a file here only after reading it and confirming it never adds or replaces.</summary>
    internal static readonly string[] RemovalOnlyScripts = ["Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1"];

    /// <summary>The role-privilege expectation files (absent = must not be held; present = held at this depth). They name the guarded
    /// privileges legitimately, so the name scan skips this folder and <see cref="ExpectationFindings"/> checks them against
    /// <see cref="AllowList"/> instead.</summary>
    private static readonly string ExpectationsFolder = "scripts/provisioning/role-privilege-expectations/";

    [Fact(DisplayName = "044: the roles holding Create or Write on Signals and Decision Records equal their allow-lists (D-204, D-210)")]
    public void TheRoleFilesHoldExactlyTheAllowLists()
    {
        var roles = LoadRoleFiles(SourceDirectory);
        Assert.Contains(OntologyService, roles.Keys);
        var findings = Findings(roles);
        Assert.True(findings.Count == 0,
            "The roles that may create or write Signals and Decision Records are an exact allow-list (ontology D-204, D-210). Raise any "
            + "change with ontology-r1 and unified-access-control-r2:\n  " + string.Join("\n  ", findings));
    }

    [Fact(DisplayName = "044: the allow-list guard fires on each seeded drift and not on the committed role files")]
    public void TheGuardFiresOnEachSeededDrift()
    {
        var committed = LoadRoleFiles(SourceDirectory);
        Assert.Empty(Findings(committed)); // must not fire: today's state

        Fires(committed, r => Grant(r, "Spaarke Core User", "prvCreatesprk_Signal", "Basic"), "Spaarke Core User", "prvCreatesprk_Signal");
        Fires(committed, r => Grant(r, "Spaarke Console User", "prvWritesprk_Signal", "Deep"), "Spaarke Console User", "prvWritesprk_Signal");
        Fires(committed, r => Grant(r, "Spaarke Basic User", "prvCreatesprk_DecisionRecord", "Global"), "Spaarke Basic User", "prvCreatesprk_DecisionRecord");
        Fires(committed, r => Grant(r, "Spaarke Ontology Administrator", "prvWritesprk_Signal", "Global"), "Spaarke Ontology Administrator", "prvWritesprk_Signal");
        // Specifically: the writer gaining Write on Decision Records (append-only depends on its absence).
        Fires(committed, r => Grant(r, OntologyService, "prvWritesprk_DecisionRecord", "Global"), OntologyService, "prvWritesprk_DecisionRecord");
        // A role defined in another solution's Roles folder (held by a team or a user alike: the file does not say, so both are covered).
        Fires(committed, r => r["Spaarke Added Elsewhere"] = new(StringComparer.OrdinalIgnoreCase) { ["prvWritesprk_DecisionRecord"] = "Basic" },
            "Spaarke Added Elsewhere", "prvWritesprk_DecisionRecord");
        // An allow-listed role the repo defines that no longer holds its privilege.
        Fires(committed, r => r[OntologyService].Remove("prvCreatesprk_Signal"), OntologyService, "prvCreatesprk_Signal");
        // Case and depth do not matter: any depth is a grant.
        Fires(committed, r => Grant(r, "Spaarke Core User", "PRVWRITESPRK_SIGNAL", "Local"), "Spaarke Core User", "prvWritesprk_Signal");
        // Fix round 2 (a): an element that names the privilege is a grant whatever its level says, an empty level included.
        Fires(committed, r => Grant(r, "Spaarke Core User", "prvCreatesprk_DecisionRecord", ""), "Spaarke Core User", "prvCreatesprk_DecisionRecord");
        // Fix round 2 (b): a repo role file under a platform role name on the allow-lists, holding something or nothing, in any case.
        Fires(committed, r => r["System Customizer"] = new(StringComparer.OrdinalIgnoreCase), "System Customizer", "platform role name");
        Fires(committed, r => r["service writer"] = new(StringComparer.OrdinalIgnoreCase) { ["prvWritesprk_DecisionRecord"] = "Global" },
            "service writer", "platform role name");
    }

    [Theory(DisplayName = "044: a RolePrivilege element that names a guarded privilege is a grant, whatever its level attribute says")]
    [InlineData("level=\"\"")]
    [InlineData("")]
    [InlineData("level=\"   \"")]
    public void AnElementNamingThePrivilege_IsAGrant_WhateverItsLevel(string levelAttribute)
    {
        var findings = FindingsOfOneRoleFile(
            $"<Role name=\"Seeded Role\"><RolePrivileges><RolePrivilege name=\"prvWritesprk_Signal\" {levelAttribute} /></RolePrivileges></Role>");
        Assert.Contains(findings, f => f.StartsWith("Seeded Role:", StringComparison.Ordinal) && f.Contains("prvWritesprk_Signal", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "044: a privilege named twice in one role file is a grant, whichever element carries the level")]
    [InlineData("level=\"\"", "level=\"Global\"")]
    [InlineData("", "level=\"Basic\"")]
    [InlineData("level=\"Deep\"", "")]
    [InlineData("", "")]
    public void APrivilegeNamedTwice_IsAGrant(string first, string second)
    {
        var findings = FindingsOfOneRoleFile(
            "<Role name=\"Seeded Role\"><RolePrivileges>"
            + $"<RolePrivilege name=\"prvCreatesprk_Signal\" {first} /><RolePrivilege name=\"prvCreatesprk_Signal\" {second} />"
            + "</RolePrivileges></Role>");
        Assert.Contains(findings, f => f.StartsWith("Seeded Role:", StringComparison.Ordinal) && f.Contains("prvCreatesprk_Signal", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "044: a role file that holds none of the guarded privileges is not a finding")]
    public void ARoleFileWithoutTheGuardedPrivileges_IsNotAFinding()
    {
        // Must not fire: a Read privilege, and a name that only starts with a guarded one.
        Assert.Empty(FindingsOfOneRoleFile(
            "<Role name=\"Seeded Role\"><RolePrivileges><RolePrivilege name=\"prvReadsprk_Signal\" level=\"Global\" />"
            + "<RolePrivilege name=\"prvWritesprk_SignalFeedback\" level=\"Global\" /></RolePrivileges></Role>"));
    }

    [Fact(DisplayName = "044: no config/**/*.json, scripts/**/*.json or scripts/**/*.ps1 names a guarded privilege (the removal-only script excepted)")]
    public void NoConfigOrScriptNamesAGuardedPrivilege()
    {
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "config"), "*.json"));
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "scripts"), "*.ps1", SearchOption.AllDirectories));
        var findings = ConfigPrivilegeFindings(SourceScan.RepoRoot);
        Assert.True(findings.Count == 0,
            "A script or config file names a privilege on the Signal / Decision Record allow-lists (ontology D-204, D-210). A file that only "
            + "REMOVES such privileges goes on RemovalOnlyScripts, after it is read. Raise any other with ontology-r1 and unified-access-control-r2:\n  "
            + string.Join("\n  ", findings));
    }

    [Fact(DisplayName = "044: the name scan fires on a seeded privilege name in a nested config json or a script, in any case, and not on a longer name")]
    public void TheNameScanFiresOnASeededPrivilegeName()
    {
        var root = Path.Combine(Path.GetTempPath(), "sprk-044-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "config", "nested"));
            Directory.CreateDirectory(Path.Combine(root, "scripts", "provisioning", "deeper"));
            File.WriteAllText(Path.Combine(root, "config", "some-role.json"),
                "{ \"privileges\": [ \"prvReadsprk_Signal\", \"prvWritesprk_SignalFeedback\", \"prvCreatesprk_DecisionRecordLine\" ] }");
            // Must not fire: a longer privilege name in a script.
            File.WriteAllText(Path.Combine(root, "scripts", "provisioning", "Grant-Feedback.ps1"), "$p = 'prvWritesprk_SignalFeedback'");
            Assert.Empty(ConfigPrivilegeFindings(root));

            foreach (var privilege in AllowList.Keys)
            {
                var seeds = new[]
                {
                    Path.Combine(root, "config", "seeded-role.json"),
                    Path.Combine(root, "config", "nested", "seeded-role.json"),
                    Path.Combine(root, "scripts", "provisioning", "deeper", "Seeded-Grant.ps1"),
                    Path.Combine(root, "scripts", "Seeded-Grant.ps1"),
                    Path.Combine(root, "scripts", "provisioning", "deeper", "seeded.json"),
                };
                foreach (var seed in seeds)
                {
                    File.WriteAllText(seed, seed.EndsWith(".ps1", StringComparison.Ordinal)
                        ? $"Add-Privilege -Name '{privilege.ToUpperInvariant()}'"
                        : $"{{ \"privileges\": [ \"{privilege.ToUpperInvariant()}\" ] }}");
                    Assert.Contains(ConfigPrivilegeFindings(root), f => f.Contains(Path.GetFileName(seed), StringComparison.Ordinal)
                        && f.Contains(privilege, StringComparison.OrdinalIgnoreCase)); // must fire
                    File.Delete(seed);
                }
            }

            // A removal-only script is excepted by name wherever it sits; the same text under another name fires.
            var text = "Remove-Privilege -Name prvWritesprk_Signal";
            var renamed = Path.Combine(root, "scripts", "deeper-" + RemovalOnlyScripts[0]);
            File.WriteAllText(renamed, text);
            Assert.Contains(ConfigPrivilegeFindings(root), f => f.Contains("deeper-", StringComparison.Ordinal)); // other name: fires
            File.Delete(renamed);
            File.WriteAllText(Path.Combine(root, "scripts", "provisioning", "deeper", RemovalOnlyScripts[0]), text);
            Assert.Empty(ConfigPrivilegeFindings(root)); // the exception by name
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "044: each excepted removal-only script exists, calls RemovePrivilegeRole, and never adds or replaces a privilege")]
    public void RemovalOnlyScriptsHoldNoGrantCall()
    {
        foreach (var name in RemovalOnlyScripts)
        {
            var matches = Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "scripts"), name, SearchOption.AllDirectories).ToList();
            Assert.True(matches.Count == 1, $"{name} is on RemovalOnlyScripts and must exist exactly once under scripts/ (found {matches.Count}).");
            var findings = GrantCallFindings(File.ReadAllText(matches[0]));
            Assert.True(findings.Count == 0, $"{name} is excepted as removal-only but: {string.Join("; ", findings)}");
        }

        // Must not fire / must fire.
        Assert.Empty(GrantCallFindings("Invoke-DvPost \"roles($id)/Microsoft.Dynamics.CRM.RemovePrivilegeRole\" @{}"));
        Assert.NotEmpty(GrantCallFindings("Invoke-DvPost \"roles($id)/Microsoft.Dynamics.CRM.RemovePrivilegeRole\" @{}\nInvoke-DvPost \"roles($id)/Microsoft.Dynamics.CRM.AddPrivilegesRole\" @{}"));
        Assert.NotEmpty(GrantCallFindings("RemovePrivilegeRole; ReplacePrivilegesRole"));
        Assert.NotEmpty(GrantCallFindings("RemovePrivilegeRole; roleprivileges_association"));
        Assert.NotEmpty(GrantCallFindings("Invoke-DvGet \"roles\"")); // no RemovePrivilegeRole call at all: not a removal script
    }

    [Fact(DisplayName = "044: the role-privilege expectation files agree with the allow-lists")]
    public void TheExpectationFilesAgreeWithTheAllowLists()
    {
        var directory = Path.Combine(SourceScan.RepoRoot, ExpectationsFolder.TrimEnd('/'));
        var files = Directory.EnumerateFiles(directory, "*.json").ToList();
        Assert.NotEmpty(files);
        var findings = files.Order(StringComparer.Ordinal).SelectMany(f => ExpectationFindings(Path.GetFileName(f), File.ReadAllText(f))).ToList();
        Assert.True(findings.Count == 0,
            "A role-privilege expectation file disagrees with the Signal / Decision Record allow-lists (ontology D-204, D-210):\n  " + string.Join("\n  ", findings));
    }

    [Fact(DisplayName = "044: the expectation check fires on a disagreeing present or absent entry and not on an agreeing one")]
    public void TheExpectationCheckFiresOnDisagreement()
    {
        static string Json(string presentRole, string present, string absentRole, string absent) =>
            "{ \"roles\": { \"" + presentRole + "\": { \"present\": { \"" + present + "\": \"Global\" } }, \"" + absentRole + "\": { \"absent\": [ \"" + absent + "\" ] } } }";

        // Must not fire: the allow-listed service role holds a privilege it may; the console role must not hold one.
        Assert.Empty(ExpectationFindings("t.json", Json("Spaarke Ontology Service", "prvWritesprk_Signal", "Spaarke Console User", "prvWritesprk_Signal")));
        Assert.Empty(ExpectationFindings("t.json", Json("Spaarke Ontology Service", "prvReadsprk_Signal", "Spaarke Ontology Service", "prvWritesprk_DecisionRecord")));
        // Must fire: a role off the allow-list expected to hold a guarded privilege (any case)...
        Assert.NotEmpty(ExpectationFindings("t.json", Json("Spaarke Console User", "PRVWRITESPRK_SIGNAL", "Spaarke Console User", "prvReadsprk_Signal")));
        // ...the service role expected to hold Write on Decision Records (not on that list)...
        Assert.NotEmpty(ExpectationFindings("t.json", Json("Spaarke Ontology Service", "prvWritesprk_DecisionRecord", "Spaarke Console User", "prvReadsprk_Signal")));
        // ...and an allow-listed role expected NOT to hold a privilege it is allowed.
        Assert.NotEmpty(ExpectationFindings("t.json", Json("Spaarke Console User", "prvReadsprk_Signal", "Spaarke Ontology Service", "prvCreatesprk_Signal")));
    }

    [Fact(DisplayName = "044: the guard reads every role definition under src/, whatever its folder is called")]
    public void TheGuardReadsEveryRoleDefinition()
    {
        var root = Path.Combine(Path.GetTempPath(), "sprk-044-roles-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "SpaarkeMaster", "Roles"));
            Directory.CreateDirectory(Path.Combine(root, "SomeOtherSolution", "Roles"));
            Directory.CreateDirectory(Path.Combine(root, "SomeOtherSolution", "SecurityRoles"));
            Directory.CreateDirectory(Path.Combine(root, "SomeOtherSolution", "Other"));
            foreach (var file in Directory.EnumerateFiles(MasterRolesDirectory, "*.xml"))
                File.Copy(file, Path.Combine(root, "SpaarkeMaster", "Roles", Path.GetFileName(file)));
            Assert.Empty(Findings(LoadRoleFiles(root))); // must not fire

            // Must not fire: an XML that is not a role definition (no RolePrivilege element, root is not a role).
            File.WriteAllText(Path.Combine(root, "SomeOtherSolution", "Other", "Form.xml"), "<forms><Role Id=\"x\" /></forms>");
            Assert.Empty(Findings(LoadRoleFiles(root)));

            // Must fire (fix round 3): a role definition outside a folder named Roles.
            var elsewhere = Path.Combine(root, "SomeOtherSolution", "SecurityRoles", "Elsewhere Role.xml");
            File.WriteAllText(elsewhere,
                "<Role name=\"Elsewhere Role\"><RolePrivileges><RolePrivilege name=\"prvWritesprk_Signal\" level=\"Global\" /></RolePrivileges></Role>");
            Assert.Contains(Findings(LoadRoleFiles(root)), f => f.StartsWith("Elsewhere Role:", StringComparison.Ordinal));
            File.Delete(elsewhere);

            // Must fire: the same, nested in a customizations-style document with no Role root.
            var nested = Path.Combine(root, "SomeOtherSolution", "Other", "customizations.xml");
            File.WriteAllText(nested,
                "<ImportExportXml><Roles><Role name=\"Nested Role\"><RolePrivileges><RolePrivilege name=\"prvCreatesprk_Signal\" level=\"Basic\" /></RolePrivileges></Role></Roles></ImportExportXml>");
            Assert.NotEmpty(Findings(LoadRoleFiles(root)));
            File.Delete(nested);

            File.WriteAllText(Path.Combine(root, "SomeOtherSolution", "Roles", "Team Role.xml"), // must fire
                "<Role name=\"Team Role\"><RolePrivileges><RolePrivilege name=\"prvCreatesprk_Signal\" level=\"Basic\" /></RolePrivileges></Role>");
            Assert.Contains(Findings(LoadRoleFiles(root)), f => f.Contains("Team Role", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── The check ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>For each guarded privilege: a role file holding it that is not on its allow-list (extra), and an allow-listed role the
    /// repo defines that does not hold it (missing). A role file holds a privilege when any <c>RolePrivilege</c> element names it, whatever
    /// its level says (empty, missing or repeated). A repo role file under a platform role name on the allow-lists is a finding.</summary>
    internal static List<string> Findings(IReadOnlyDictionary<string, Dictionary<string, string>> roles)
    {
        var findings = new List<string>();
        foreach (var platform in roles.Keys.Where(k => PlatformRoles.Contains(k.Trim(), StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
            findings.Add($"{platform}: a repo role file uses a platform role name on the allow-lists (the platform roles are not role files)");

        foreach (var (privilege, allowed) in AllowList)
        {
            var holders = roles.Where(r => r.Value.ContainsKey(privilege))
                .Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var extra in holders.Where(h => !allowed.Contains(h, StringComparer.Ordinal)).Order(StringComparer.Ordinal))
                findings.Add($"{extra}: holds {privilege} at {LevelText(roles[extra][privilege])}, and is not on its allow-list");
            foreach (var missing in allowed.Where(a => roles.ContainsKey(a) && !holders.Contains(a)))
                findings.Add($"{missing}: is on the {privilege} allow-list and defined in the repo, but does not hold it");
        }

        return findings;
    }

    /// <summary>Every role definition in any <c>*.xml</c> under <paramref name="root"/> (fix round 3: not only a <c>Roles</c> folder): a file
    /// with a <c>RolePrivilege</c> element, or whose root element is <c>Role</c>. Role name → privilege (case-insensitive) → level. A file that
    /// is unparsable, has a privilege but no role element, has no role name, or defines a role a second time fails (never read as "holds nothing").</summary>
    internal static Dictionary<string, Dictionary<string, string>> LoadRoleFiles(string root)
    {
        var roles = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var path in RoleDefinitionFiles(root).Order(StringComparer.Ordinal))
        {
            var doc = XDocument.Parse(File.ReadAllText(path));
            var elements = doc.Root?.Name.LocalName == "Role"
                ? new List<XElement> { doc.Root }
                : doc.Descendants().Where(e => e.Name.LocalName == "Role" && e.Descendants().Any(d => d.Name.LocalName == "RolePrivilege")).ToList();
            Assert.True(elements.Count > 0, $"{path} names a RolePrivilege but holds no Role element.");
            foreach (var element in elements)
            {
                var name = (string?)element.Attribute("name");
                Assert.False(string.IsNullOrWhiteSpace(name), $"Role definition in {path} has no role name.");
                Assert.False(roles.ContainsKey(name!), $"Role '{name}' is defined twice (second: {path}).");
                // Every element counts (fix round 2): the name alone is the grant, and a privilege named twice keeps every level it was given.
                roles[name!] = element.Descendants().Where(e => e.Name.LocalName == "RolePrivilege")
                    .GroupBy(e => ((string?)e.Attribute("name") ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key,
                        g => string.Join(",", g.Select(e => ((string?)e.Attribute("level") ?? string.Empty).Trim()).Where(l => l.Length > 0).Distinct()),
                        StringComparer.OrdinalIgnoreCase);
            }
        }

        return roles;
    }

    private static readonly Regex RoleRoot = new(@"\A\s*(?:<\?xml[^>]*\?>\s*|<!--.*?-->\s*|<!DOCTYPE[^>]*>\s*)*<Role[\s>/]", RegexOptions.Singleline | RegexOptions.Compiled);

    private static bool IsBuildOrDependencyPath(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(p => p is "node_modules" or "bin" or "obj" or ".git");

    /// <summary>Every <c>*.xml</c> under <paramref name="root"/> that defines a role: its text has a <c>RolePrivilege</c> element, or its root is <c>Role</c>.</summary>
    internal static List<string> RoleDefinitionFiles(string root) =>
        Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories)
            .Where(f => !IsBuildOrDependencyPath(Path.GetRelativePath(root, f)))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("<RolePrivilege", StringComparison.Ordinal) || RoleRoot.IsMatch(text);
            })
            .ToList();

    /// <summary>Each <c>config/**/*.json</c>, <c>scripts/**/*.json</c>, <c>scripts/**/*.ps1</c> and <c>scripts/**/*.psm1</c> under
    /// <paramref name="repoRoot"/> that names a guarded privilege (whole name, any case), except the removal-only scripts (by file name) and the
    /// expectation files (checked by <see cref="ExpectationFindings"/>).</summary>
    internal static List<string> ConfigPrivilegeFindings(string repoRoot)
    {
        var findings = new List<string>();
        var scanned = new List<string>();
        var config = Path.Combine(repoRoot, "config");
        if (Directory.Exists(config))
            scanned.AddRange(Directory.EnumerateFiles(config, "*.json", SearchOption.AllDirectories));
        var scripts = Path.Combine(repoRoot, "scripts");
        if (Directory.Exists(scripts))
        {
            foreach (var pattern in new[] { "*.json", "*.ps1", "*.psm1" })
                scanned.AddRange(Directory.EnumerateFiles(scripts, pattern, SearchOption.AllDirectories));
        }

        foreach (var path in scanned.Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            if (IsBuildOrDependencyPath(relative)
                || relative.StartsWith(ExpectationsFolder, StringComparison.OrdinalIgnoreCase)
                || RemovalOnlyScripts.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                continue;
            var text = File.ReadAllText(path);
            foreach (var privilege in AllowList.Keys)
            {
                if (Regex.IsMatch(text, $"(?<![A-Za-z0-9_]){privilege}(?![A-Za-z0-9_])", RegexOptions.IgnoreCase))
                    findings.Add($"{relative}: names {privilege}");
            }
        }

        return findings;
    }

    /// <summary>Why <paramref name="scriptText"/> is not a removal-only script: it adds or replaces role privileges, or never removes one.</summary>
    internal static List<string> GrantCallFindings(string scriptText)
    {
        var findings = new List<string>();
        if (!scriptText.Contains("RemovePrivilegeRole", StringComparison.OrdinalIgnoreCase))
            findings.Add("it never calls RemovePrivilegeRole");
        foreach (Match m in Regex.Matches(scriptText, @"(Add|Replace)Privileges?Role|roleprivileges_association", RegexOptions.IgnoreCase))
            findings.Add($"it contains '{m.Value}'");
        return findings;
    }

    /// <summary>Disagreements between one role-privilege expectation file and <see cref="AllowList"/>: a role off a list expected to hold
    /// (<c>present</c>) a guarded privilege, or an allow-listed role expected not to hold (<c>absent</c>) a privilege on its list.</summary>
    internal static List<string> ExpectationFindings(string fileName, string json)
    {
        var findings = new List<string>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("roles", out var roles))
            return [$"{fileName}: has no 'roles' object"];
        foreach (var role in roles.EnumerateObject())
        {
            if (role.Value.TryGetProperty("present", out var present))
            {
                foreach (var entry in present.EnumerateObject())
                {
                    var privilege = AllowList.Keys.FirstOrDefault(k => string.Equals(k, entry.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (privilege is not null && !AllowList[privilege].Contains(role.Name.Trim(), StringComparer.OrdinalIgnoreCase))
                        findings.Add($"{fileName}: {role.Name} is expected to hold {privilege} and is not on its allow-list");
                }
            }

            if (role.Value.TryGetProperty("absent", out var absent))
            {
                foreach (var entry in absent.EnumerateArray())
                {
                    var privilege = AllowList.Keys.FirstOrDefault(k => string.Equals(k, (entry.GetString() ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                    if (privilege is not null && AllowList[privilege].Contains(role.Name.Trim(), StringComparer.OrdinalIgnoreCase))
                        findings.Add($"{fileName}: {role.Name} is expected NOT to hold {privilege}, which is on its allow-list");
                }
            }
        }

        return findings;
    }

    private static string LevelText(string level) => level.Length == 0 ? "no stated level" : level;

    /// <summary>The findings when <paramref name="roleXml"/> is added, as its own role file, to a copy of the committed Roles folder.</summary>
    private static List<string> FindingsOfOneRoleFile(string roleXml)
    {
        var root = Path.Combine(Path.GetTempPath(), "sprk-044-role-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "SpaarkeMaster", "Roles"));
            Directory.CreateDirectory(Path.Combine(root, "SeededSolution", "Roles"));
            foreach (var file in Directory.EnumerateFiles(MasterRolesDirectory, "*.xml"))
                File.Copy(file, Path.Combine(root, "SpaarkeMaster", "Roles", Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(root, "SeededSolution", "Roles", "Seeded Role.xml"), roleXml);
            return Findings(LoadRoleFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Fires(
        IReadOnlyDictionary<string, Dictionary<string, string>> committed, Action<Dictionary<string, Dictionary<string, string>>> seed,
        string role, string privilege)
    {
        var copy = committed.ToDictionary(r => r.Key, r => new Dictionary<string, string>(r.Value, StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);
        seed(copy);
        var findings = Findings(copy);
        Assert.True(findings.Any(f => f.StartsWith(role + ":", StringComparison.Ordinal) && f.Contains(privilege, StringComparison.OrdinalIgnoreCase)),
            $"Seeding {role} / {privilege} must fire. Findings: {string.Join("; ", findings)}");
    }

    private static void Grant(Dictionary<string, Dictionary<string, string>> roles, string role, string privilege, string level)
    {
        Assert.True(roles.ContainsKey(role), $"The seeded role '{role}' is not a role file.");
        roles[role][privilege] = level;
    }
}
