using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Spaarke Core User can link wizard lookups at its Read depth (issue #2382). A create wizard saves its record as the user
/// and binds lookups in the same create, so Dataverse needs Append on the created table and AppendTo on every bound
/// target; a lower AppendTo fails the save with 0x80040220 for any target row the user can see but does not own.
/// </summary>
/// <remarks>
/// <para><b>The declared list</b> is <c>config/core-user-wizard-linking-role.json</c> (the one list; the dev script reads it
/// too). The rule: AppendTo on each bind target is at its declared <c>appendToDepth</c>, which is the role's Read depth on
/// that table: a floor (no lower than Read, so the save works) and a ceiling (no higher than Read). A target whose AppendTo
/// is deliberately above Read declares it with <c>appendToAboveRead</c> (its Read depth, the reason and the commit); today
/// that is <c>sprk_eventtype_ref</c> only. Append on each created table is at least the role's Create depth (none when the
/// role cannot create it).</para>
/// <para><b>The pin.</b> Every privilege Core User holds on the tables this guard owns (the bind targets and created
/// tables) is pinned in <see cref="PinnedCoreUserPrivileges"/>, so a change to any of them, or a privilege added on one of
/// those tables, fails here until the pin is updated with it. The pin covers those tables only, not the whole role: the
/// role file is shared by many projects, and a whole-role snapshot would fail on every unrelated change to it.</para>
/// <para><b>The ratchet.</b> A source scan of the create wizards collects every entity set they bind
/// (<c>entitySet: '...'</c>, a <c>`/set(</c> bind path, and the regarding <c>_ENTITY_SET_MAP</c> values). A set the file does
/// not list fails the test, so a new wizard lookup is added to the list (and the role) before it ships.</para>
/// </remarks>
public class CoreUserWizardLinkingGuardTests
{
    private static string SolutionRoot => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster");
    private static string ConfigPath => Path.Combine(SourceScan.RepoRoot, "config", "core-user-wizard-linking-role.json");
    private static string UiComponentsSrc =>
        Path.Combine(SourceScan.RepoRoot, "src", "client", "shared", "Spaarke.UI.Components", "src");

    private static readonly string[] DepthOrder = ["None", "Basic", "Local", "Deep", "Global"];

    [Fact(DisplayName = "Spaarke Core User holds AppendTo at exactly its Read depth on every wizard bind target, and Append at its Create depth on every created table (#2382)")]
    public void CoreUserCanLinkEveryWizardLookup()
    {
        var config = LoadConfig();
        var role = File.ReadAllText(RolePath(config.RoleName));

        var gaps = Gaps(role, config);

        Assert.True(gaps.Count == 0,
            "Spaarke Core User's AppendTo on a create wizard's bind target is not at its declared depth (the role's Read depth). "
            + "Below it, the save fails 0x80040220 for a row the user can see but does not own; above it is not allowed unless "
            + "config/core-user-wizard-linking-role.json declares 'appendToAboveRead'. Fix it in "
            + "src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Core User.xml and on the live role "
            + "(scripts/Set-WizardLinkingAndMembershipPrivileges.ps1):\n  " + string.Join("\n  ", gaps));
    }

    [Fact(DisplayName = "Spaarke Core User's privileges on the tables the linking guard owns match the pin (#2382)")]
    public void CoreUserPrivilegesOnOwnedTablesMatchThePin()
    {
        var config = LoadConfig();
        var gaps = PinGaps(File.ReadAllText(RolePath(config.RoleName)), config);

        Assert.True(gaps.Count == 0,
            "Spaarke Core User's privileges on a table this guard owns changed. #2382 only sets AppendTo and Append there; "
            + "any other change needs its own decision. If it was decided, update PinnedCoreUserPrivileges with it:\n  "
            + string.Join("\n  ", gaps));
    }

    [Fact(DisplayName = "Core User linking guard control: contact AppendTo raised to Global (above its Deep Read) is reported")]
    public void GuardFiresWhenContactAppendToIsRaisedAboveRead()
    {
        var config = LoadConfig();
        var role = File.ReadAllText(RolePath(config.RoleName));

        var raised = XDocument.Parse(role);
        Privilege(raised, "prvAppendToContact").SetAttributeValue("level", "Global");

        var gap = Assert.Single(Gaps(raised.ToString(), config));
        Assert.Contains("prvAppendToContact is Global, above Read (prvReadContact) Deep", gap, StringComparison.Ordinal);
        Assert.Contains(PinGaps(raised.ToString(), config), g => g.Contains("prvAppendToContact", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Core User linking guard control: the declared exception for sprk_eventtype_ref is required, bounded and current")]
    public void EventTypeAboveReadNeedsItsDeclaredException()
    {
        var config = LoadConfig();
        var role = File.ReadAllText(RolePath(config.RoleName));
        var eventType = config.BindTargets.Single(t => t.LogicalName == "sprk_eventtype_ref");
        Assert.NotNull(eventType.AboveRead);
        Assert.Equal("4e9e295a30", eventType.AboveRead!.Since);
        Assert.Single(config.BindTargets, t => t.AboveRead is not null);

        // Without the exception, Global AppendTo over Deep Read is reported.
        var undeclared = config with
        {
            BindTargets = config.BindTargets.Select(t => t == eventType ? t with { AboveRead = null } : t).ToList(),
        };
        var gap = Assert.Single(Gaps(role, undeclared));
        Assert.Contains("prvAppendTosprk_EventType_Ref is Global, above Read (prvReadsprk_EventType_Ref) Deep", gap, StringComparison.Ordinal);

        // With it, Read moving from the declared depth makes the exception stale, and is reported.
        var readRaised = XDocument.Parse(role);
        Privilege(readRaised, "prvReadsprk_EventType_Ref").SetAttributeValue("level", "Global");
        gap = Assert.Single(Gaps(readRaised.ToString(), config));
        Assert.Contains("prvReadsprk_EventType_Ref is Global; the config declares Deep", gap, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Core User linking guard control: AppendTo and Read both moved off the declared depth is reported")]
    public void GuardFiresWhenAppendToAndReadLeaveTheDeclaredDepth()
    {
        var config = LoadConfig();
        var moved = XDocument.Parse(File.ReadAllText(RolePath(config.RoleName)));
        Privilege(moved, "prvAppendTosprk_Organization").SetAttributeValue("level", "Global");
        Privilege(moved, "prvReadsprk_Organization").SetAttributeValue("level", "Global");

        var gap = Assert.Single(Gaps(moved.ToString(), config));
        Assert.Contains("prvAppendTosprk_Organization is Global; the config declares Deep", gap, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Core User linking pin control: a privilege added or changed on an owned table is reported; one on another table is not")]
    public void PinFiresOnOwnedTablesOnly()
    {
        var config = LoadConfig();
        var role = File.ReadAllText(RolePath(config.RoleName));
        Assert.Empty(PinGaps(role, config));

        var added = XDocument.Parse(role);
        RolePrivileges(added).Add(new XElement("RolePrivilege", new XAttribute("name", "prvDeletesprk_Organization"), new XAttribute("level", "Global")));
        var gap = Assert.Single(PinGaps(added.ToString(), config));
        Assert.Contains("prvDeletesprk_Organization", gap, StringComparison.Ordinal);
        Assert.Empty(Gaps(added.ToString(), config));

        var changed = XDocument.Parse(role);
        Privilege(changed, "prvWriteContact").SetAttributeValue("level", "Global");
        gap = Assert.Single(PinGaps(changed.ToString(), config));
        Assert.Contains("prvWriteContact is Global; pinned Basic", gap, StringComparison.Ordinal);

        var removed = XDocument.Parse(role);
        Privilege(removed, "prvReadsprk_ReportCard").Remove();
        gap = Assert.Single(PinGaps(removed.ToString(), config));
        Assert.Contains("prvReadsprk_ReportCard is None; pinned Deep", gap, StringComparison.Ordinal);

        var elsewhere = XDocument.Parse(role);
        RolePrivileges(elsewhere).Add(new XElement("RolePrivilege", new XAttribute("name", "prvDeletesprk_NotOwnedHere"), new XAttribute("level", "Global")));
        Assert.Empty(PinGaps(elsewhere.ToString(), config));
    }

    [Fact(DisplayName = "Core User linking guard control: removing the contact AppendTo, or lowering it to Basic, is reported")]
    public void GuardFiresWhenContactAppendToIsRemovedOrLowered()
    {
        var config = LoadConfig();
        var role = File.ReadAllText(RolePath(config.RoleName));
        Assert.Empty(Gaps(role, config));

        var removed = XDocument.Parse(role);
        Privilege(removed, "prvAppendToContact").Remove();
        var gap = Assert.Single(Gaps(removed.ToString(), config));
        Assert.Contains("prvAppendToContact", gap, StringComparison.Ordinal);

        var lowered = XDocument.Parse(role);
        Privilege(lowered, "prvAppendToContact").SetAttributeValue("level", "Basic");
        gap = Assert.Single(Gaps(lowered.ToString(), config));
        Assert.Contains("prvAppendToContact", gap, StringComparison.Ordinal);
        Assert.Contains("Basic", gap, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Core User linking guard control: an Append below the Create depth is reported; a table the role cannot create needs none")]
    public void GuardChecksAppendAgainstCreateDepth()
    {
        var config = new LinkingConfig("x", [], [new CreatedTable("sprk_project", "prvCreatesprk_Project", "prvAppendsprk_Project"),
            new CreatedTable("sprk_reportcard", "prvCreatesprk_ReportCard", "prvAppendsprk_ReportCard")]);
        const string role = """
            <Role name="x"><RolePrivileges>
              <RolePrivilege name="prvCreatesprk_Project" level="Deep" />
              <RolePrivilege name="prvAppendsprk_Project" level="Basic" />
            </RolePrivileges></Role>
            """;

        var gap = Assert.Single(Gaps(role, config));
        Assert.Contains("prvAppendsprk_Project", gap, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Core User linking ratchet: every entity set a create wizard binds is in the declared list")]
    public void EveryWizardBoundEntitySetIsDeclared()
    {
        var config = LoadConfig();
        var found = WizardSourceFiles().SelectMany(f => BoundEntitySets(File.ReadAllLines(f)).Select(s => (Set: s, File: f))).ToList();

        // Sanity floor: the wizards bind contacts, organizations, the reference tables and the regarding parents today.
        Assert.True(found.Select(x => x.Set).Distinct().Count() >= 12,
            $"The scan found only {found.Select(x => x.Set).Distinct().Count()} bound entity sets; it is probably broken.");

        var declared = config.BindTargets.Select(t => t.EntitySet).ToHashSet(StringComparer.Ordinal);
        var undeclared = found.Where(x => !declared.Contains(x.Set))
            .Select(x => $"{x.Set}  ({Path.GetRelativePath(SourceScan.RepoRoot, x.File)})").Distinct().ToList();

        Assert.True(undeclared.Count == 0,
            "A create wizard binds an entity set that config/core-user-wizard-linking-role.json does not list. Add it to "
            + "'bindTargets' and give Spaarke Core User AppendTo on it at its Read depth:\n  " + string.Join("\n  ", undeclared));
    }

    [Fact(DisplayName = "Core User linking ratchet control: new bind shapes are found; comments and non-bind strings are not")]
    public void RatchetScanFindsBindShapesOnly()
    {
        string[] source =
        [
            "    lookups.push({ col: 'sprk_x', entitySet: 'sprk_newthings', guid });",
            "    entity[`${navProp}@odata.bind`] = `/sprk_otherthings(${cleanGuid(id)})`;",
            "const _ENTITY_SET_MAP: Record<string, string> = {",
            "  sprk_widget: 'sprk_widgets',",
            "};",
            "  // entity[`x@odata.bind`] = `/sprk_commented(${id})`;",
            "   * entitySet: 'sprk_doccomment'",
            "    const label = 'sprk_notabind';",
            "    console.warn(`[X] /api/v1/child-records(${id})`);",
        ];

        Assert.Equal(["sprk_newthings", "sprk_otherthings", "sprk_widgets"], BoundEntitySets(source).OrderBy(s => s, StringComparer.Ordinal));
    }

    // ── Core ─────────────────────────────────────────────────────────────────────────────────────────────────

    internal static List<string> Gaps(string roleXml, LinkingConfig config)
    {
        var held = XDocument.Parse(roleXml).Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .GroupBy(e => (string?)e.Attribute("name") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => (string?)e.Attribute("level") ?? "None").MaxBy(Rank)!,
                StringComparer.OrdinalIgnoreCase);
        string Level(string name) => held.TryGetValue(name, out var level) ? level : "None";

        var gaps = new List<string>();
        foreach (var t in config.BindTargets)
        {
            var read = Level(t.ReadPrivilege);
            var appendTo = Level(t.AppendToPrivilege);
            var declaredRead = t.AboveRead?.ReadDepth ?? t.AppendToDepth;

            // One finding per target, the most basic first.
            if (Rank(appendTo) < Rank(read))
                gaps.Add($"{t.AppendToPrivilege} is {appendTo}; Read ({t.ReadPrivilege}) is {read}");
            else if (t.AboveRead is null && Rank(appendTo) > Rank(read))
                gaps.Add($"{t.AppendToPrivilege} is {appendTo}, above Read ({t.ReadPrivilege}) {read}; AppendTo may not exceed Read "
                         + "unless the config declares 'appendToAboveRead' for it");
            else if (!string.Equals(appendTo, t.AppendToDepth, StringComparison.OrdinalIgnoreCase))
                gaps.Add($"{t.AppendToPrivilege} is {appendTo}; the config declares {t.AppendToDepth}");
            else if (!string.Equals(read, declaredRead, StringComparison.OrdinalIgnoreCase))
                gaps.Add($"{t.ReadPrivilege} is {read}; the config declares {declaredRead}");
        }

        foreach (var c in config.CreatedTables)
        {
            var create = Level(c.CreatePrivilege);
            var append = Level(c.AppendPrivilege);
            if (Rank(append) < Rank(create))
                gaps.Add($"{c.AppendPrivilege} is {append}; Create ({c.CreatePrivilege}) is {create}");
        }

        return gaps;
    }

    /// <summary>The privilege verbs a table's privileges are named with (<c>prv{Verb}{SchemaName}</c>).</summary>
    private static readonly string[] Verbs = ["Create", "Read", "Write", "Delete", "Append", "AppendTo", "Assign", "Share"];

    /// <summary>
    /// Spaarke Core User's privileges on the tables this guard owns (schema name -> verb=level), as of #2382. Only AppendTo
    /// and Append on these tables are #2382's to set; any other change here is a separate decision.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> PinnedCoreUserPrivileges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Contact"] = "Create=Basic Read=Deep Write=Basic Delete=Basic Append=Basic AppendTo=Deep Assign=Basic Share=Basic",
        ["sprk_Organization"] = "Read=Deep AppendTo=Deep",
        ["Account"] = "Create=Basic Read=Deep Write=Basic Delete=Basic Append=Basic AppendTo=Deep Assign=Basic Share=Basic",
        ["sprk_AISearchIndex"] = "Read=Deep AppendTo=Deep",
        ["sprk_MatterType_Ref"] = "Read=Global AppendTo=Global",
        ["sprk_PracticeArea_Ref"] = "Read=Global AppendTo=Global",
        ["sprk_ProjectType_Ref"] = "Read=Global AppendTo=Global",
        ["sprk_RecordType_Ref"] = "Read=Global AppendTo=Global",
        ["sprk_EventType_Ref"] = "Read=Deep AppendTo=Global",
        ["sprk_Matter"] = "Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep",
        ["sprk_Project"] = "Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep",
        ["sprk_Invoice"] = "Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep",
        ["sprk_WorkAssignment"] = "Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep",
        ["sprk_Event"] = "Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep",
        ["sprk_ServiceRequest"] = "Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep",
        ["sprk_Budget"] = "Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep",
        ["sprk_analysis"] = "Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep",
        ["sprk_ReportCard"] = "Read=Deep",
        ["sprk_Todo"] = "Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep",
    };

    /// <summary>The differences between the role's privileges on the owned tables and <see cref="PinnedCoreUserPrivileges"/>.</summary>
    internal static List<string> PinGaps(string roleXml, LinkingConfig config)
    {
        var held = XDocument.Parse(roleXml).Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .GroupBy(e => (string?)e.Attribute("name") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (string?)g.First().Attribute("level") ?? "None", StringComparer.OrdinalIgnoreCase);

        var owned = config.BindTargets.Select(t => SchemaNameOf(t.ReadPrivilege, "prvRead"))
            .Concat(config.CreatedTables.Select(c => SchemaNameOf(c.CreatePrivilege, "prvCreate")))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var gaps = new List<string>();
        foreach (var table in owned)
        {
            if (!PinnedCoreUserPrivileges.TryGetValue(table, out var pinnedText))
            {
                gaps.Add($"{table}: the guard owns this table but PinnedCoreUserPrivileges has no entry for it");
                continue;
            }

            var pinned = pinnedText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
            foreach (var verb in Verbs)
            {
                var name = "prv" + verb + table;
                var level = held.TryGetValue(name, out var l) ? l : "None";
                var expected = pinned.TryGetValue(verb, out var e) ? e : "None";
                if (!string.Equals(level, expected, StringComparison.OrdinalIgnoreCase))
                    gaps.Add($"{name} is {level}; pinned {expected}");
            }
        }

        return gaps;
    }

    private static string SchemaNameOf(string privilege, string prefix)
    {
        Assert.StartsWith(prefix, privilege, StringComparison.Ordinal);
        return privilege[prefix.Length..];
    }

    private static readonly Regex EntitySetProperty = new(@"\bentitySet:\s*'([a-z_]+)'", RegexOptions.Compiled);
    private static readonly Regex BindPath = new(@"[`'""]/([a-z_]+)\(", RegexOptions.Compiled);
    private static readonly Regex MapEntry = new(@"^\s*[a-z_]+:\s*'([a-z_]+)',?\s*$", RegexOptions.Compiled);

    /// <summary>The entity sets one source file binds. Comment lines are skipped.</summary>
    internal static IEnumerable<string> BoundEntitySets(IEnumerable<string> lines)
    {
        var inMap = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimStart();
            if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('*') || line.StartsWith("/*", StringComparison.Ordinal))
                continue;

            if (raw.Contains("_ENTITY_SET_MAP", StringComparison.Ordinal) && raw.Contains('{'))
            {
                inMap = true;
                continue;
            }

            if (inMap)
            {
                if (line.StartsWith('}'))
                {
                    inMap = false;
                    continue;
                }

                if (MapEntry.Match(raw) is { Success: true } entry)
                    yield return entry.Groups[1].Value;
                continue;
            }

            foreach (Match m in EntitySetProperty.Matches(raw))
                yield return m.Groups[1].Value;
            foreach (Match m in BindPath.Matches(raw))
                yield return m.Groups[1].Value;
        }
    }

    /// <summary>The create wizards' non-test sources, plus the shared regarding resolver they call.</summary>
    private static List<string> WizardSourceFiles()
    {
        var components = Path.Combine(UiComponentsSrc, "components");
        var files = Directory.EnumerateDirectories(components, "Create*Wizard", SearchOption.TopDirectoryOnly)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}__tests__{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains(".test.", StringComparison.Ordinal))
            .ToList();
        files.Add(Path.Combine(UiComponentsSrc, "services", "PolymorphicResolverService.ts"));

        Assert.True(files.Count >= 20, $"Found only {files.Count} create-wizard source files under {components}.");
        return files;
    }

    private static int Rank(string level)
    {
        var i = Array.FindIndex(DepthOrder, d => string.Equals(d, level, StringComparison.OrdinalIgnoreCase));
        Assert.True(i >= 0, $"Unknown privilege level '{level}'.");
        return i;
    }

    private static XElement RolePrivileges(XDocument doc) => doc.Descendants().First(e => e.Name.LocalName == "RolePrivileges");

    private static XElement Privilege(XDocument doc, string name) =>
        doc.Descendants().Single(e => e.Name.LocalName == "RolePrivilege"
                                      && string.Equals((string?)e.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));

    private static string RolePath(string roleName) => Path.Combine(SolutionRoot, "Roles", roleName + ".xml");

    internal static LinkingConfig LoadConfig()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());

        static string S(JsonElement e, string p)
        {
            var v = e.GetProperty(p).GetString();
            Assert.False(string.IsNullOrWhiteSpace(v), $"config entry has no '{p}'");
            return v!;
        }

        static string Depth(JsonElement e, string p)
        {
            var v = S(e, p);
            Assert.Contains(v, DepthOrder);
            return v;
        }

        static AboveRead? AboveReadOf(JsonElement t) =>
            t.TryGetProperty("appendToAboveRead", out var a)
                ? new AboveRead(Depth(a, "readDepth"), S(a, "reason"), S(a, "since"))
                : null;

        var targets = root.GetProperty("bindTargets").EnumerateArray()
            .Select(t => new BindTarget(S(t, "logicalName"), S(t, "entitySet"), S(t, "readPrivilege"), S(t, "appendToPrivilege"),
                Depth(t, "appendToDepth"), AboveReadOf(t)))
            .ToList();
        var created = root.GetProperty("createdTables").EnumerateArray()
            .Select(c => new CreatedTable(S(c, "logicalName"), S(c, "createPrivilege"), S(c, "appendPrivilege")))
            .ToList();

        Assert.Equal(targets.Count, targets.Select(t => t.EntitySet).Distinct(StringComparer.Ordinal).Count());
        return new LinkingConfig(S(root, "roleName"), targets, created);
    }

    internal sealed record BindTarget(string LogicalName, string EntitySet, string ReadPrivilege, string AppendToPrivilege,
        string AppendToDepth, AboveRead? AboveRead);

    /// <summary>A declared exception: AppendTo deliberately above Read, with Read's depth, the reason and the commit.</summary>
    internal sealed record AboveRead(string ReadDepth, string Reason, string Since);

    internal sealed record CreatedTable(string LogicalName, string CreatePrivilege, string AppendPrivilege);

    internal sealed record LinkingConfig(string RoleName, IReadOnlyList<BindTarget> BindTargets, IReadOnlyList<CreatedTable> CreatedTables);
}
