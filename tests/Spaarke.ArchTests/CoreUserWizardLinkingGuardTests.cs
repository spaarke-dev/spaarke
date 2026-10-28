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
/// too). The rule: AppendTo on each bind target is at least the role's Read depth on that table, and Append on each created
/// table is at least the role's Create depth (none when the role cannot create it).</para>
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

    [Fact(DisplayName = "Spaarke Core User holds AppendTo at its Read depth on every wizard bind target, and Append at its Create depth on every created table (#2382)")]
    public void CoreUserCanLinkEveryWizardLookup()
    {
        var config = LoadConfig();
        var role = File.ReadAllText(RolePath(config.RoleName));

        var gaps = Gaps(role, config);

        Assert.True(gaps.Count == 0,
            "Spaarke Core User cannot link a lookup a create wizard binds; the save fails 0x80040220 for a row the user can see "
            + "but does not own. Raise it in src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Core User.xml and on the live role "
            + "(scripts/Set-WizardLinkingAndMembershipPrivileges.ps1):\n  " + string.Join("\n  ", gaps));
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
            if (Rank(appendTo) < Rank(read))
                gaps.Add($"{t.AppendToPrivilege} is {appendTo}; Read ({t.ReadPrivilege}) is {read}");
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

        var targets = root.GetProperty("bindTargets").EnumerateArray()
            .Select(t => new BindTarget(S(t, "logicalName"), S(t, "entitySet"), S(t, "readPrivilege"), S(t, "appendToPrivilege")))
            .ToList();
        var created = root.GetProperty("createdTables").EnumerateArray()
            .Select(c => new CreatedTable(S(c, "logicalName"), S(c, "createPrivilege"), S(c, "appendPrivilege")))
            .ToList();

        Assert.Equal(targets.Count, targets.Select(t => t.EntitySet).Distinct(StringComparer.Ordinal).Count());
        return new LinkingConfig(S(root, "roleName"), targets, created);
    }

    internal sealed record BindTarget(string LogicalName, string EntitySet, string ReadPrivilege, string AppendToPrivilege);

    internal sealed record CreatedTable(string LogicalName, string CreatePrivilege, string AppendPrivilege);

    internal sealed record LinkingConfig(string RoleName, IReadOnlyList<BindTarget> BindTargets, IReadOnlyList<CreatedTable> CreatedTables);
}
