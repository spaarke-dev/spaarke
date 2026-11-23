using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 193 (owner decision 2026-11-23, DEF-027): <c>sprk_ratecard</c> no longer has the core-record lookups
/// <c>sprk_matter</c>, <c>sprk_project</c> and <c>sprk_workassignment</c>, nor the three relationships behind them. Since task 085 a
/// rate card belongs to a Procedure Version and nothing reads the lookups. They are deleted from dev by the guarded
/// <c>scripts/ebilling/Remove-EbillingRateCardCoreLookups.ps1</c> (after the owner's go), and the solution source and schema scripts no longer name them.
/// </summary>
/// <remarks>
/// <para>Why a guard: the lookups come back without anyone writing them. A solution export taken from dev BEFORE the delete script runs
/// (the 029-style re-sync of SpaarkeMaster) brings the three attributes, the form controls, the view cells and the three relationship
/// files back; an old branch merged later does the same; a copied schema script re-adds them with <c>Add-Lookup</c>. A reintroduced
/// lookup makes the core-record delete planner (unified-access-control-r2) reach rate cards through it again and makes the form and view
/// reference columns that a clean environment does not have.</para>
/// <para>Enforced by: this source scan (CI and the Stop hook). It fails on
/// (1) the three relationship names anywhere under <c>src/dataverse</c> and <c>scripts/ebilling</c> (solution XML, schema scripts, exports),
/// and (2) any whole-word mention of the three column names (also in the <c>_name_value</c> filter spelling) in the XML of the
/// <c>sprk_ratecard</c> table under <c>src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ratecard</c>: its <c>Entity.xml</c>, forms
/// and views. The columns of the same name on other tables (<c>sprk_ruleset</c>, <c>sprk_invoice</c>, ...) are different columns and are
/// not scanned; the detector never looks at them.</para>
/// <para>Allowed, with the reason (see <see cref="AllowedFiles"/>): the delete script names the relationships it deletes. Its Pester
/// tests (<c>tests/scripts</c>) are not under a scanned root. Nothing else is allowed. <c>projects/**</c> is task history and is not scanned.</para>
/// <para>Controls: <see cref="TheRelationshipDetector_FiresOnEachRetiredName"/> and <see cref="TheColumnDetector_FiresOnEachSpelling"/>
/// (must fire), <see cref="TheDetectors_StayQuietOnWhatStays"/> (must not fire), <see cref="TheWalk_ReachesTheScannedFiles"/> (the scan
/// is not vacuous and the allowed file is excluded and still exists).</para>
/// <para>Maintenance. If this test fails: do not add an exemption to make it pass. A solution export that brought the lookups back is
/// re-exported or cleaned (remove the attributes, controls, cells and relationship blocks the same way task 193 did; the columns
/// <c>sprk_firm</c>, <c>sprk_procedureversion</c> and <c>sprk_sourcedocument</c> stay). If the rate card ever needs a core-record
/// lookup again, that is a design change: take it through the owner and update this guard, the delete script and
/// <c>docs/data-model/sprk_ratecard.md</c> together. When every environment has taken the SpaarkeMaster upgrade import that removes the columns
/// and the delete script is retired, keep this guard: it costs one file walk.</para>
/// </remarks>
public class RateCardCoreLookupRetirementGuardTests
{
    private static readonly string[] RelationshipRoots = { "src/dataverse", "scripts/ebilling" };

    private const string RateCardEntityDirectory = "src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ratecard";

    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "dist", "coverage", ".git" };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".ps1", ".psm1", ".psd1", ".json", ".md", ".js", ".ts", ".txt", ".yml", ".yaml", ".sql", ".csv", ".resx",
    };

    /// <summary>Files that must spell the retired names, each with its reason (repo-relative, forward slashes).</summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["scripts/ebilling/Remove-EbillingRateCardCoreLookups.ps1"] =
            "the guarded dev delete (task 193): its delete list names the three relationships it removes by schema name "
            + "(no ADR governs this exemption: it follows the owner decision of 2026-11-23, DEF-027; the file's purpose is the removal itself)",
    };

    private static readonly Regex RelationshipNames = new(
        @"sprk_matter_ratecard_matter|sprk_project_ratecard_project|sprk_workassignment_ratecard_workassignment",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A whole-word mention of one of the three column names: bare (datafieldname="sprk_matter", <LogicalName>sprk_matter</LogicalName>,
    // PhysicalName="sprk_Matter") or in the filter spelling (_sprk_matter_value). A longer name (sprk_matterid, sprk_matter_ruleset_matter)
    // does not match.
    private static readonly Regex ColumnNames = new(
        @"(?<![A-Za-z0-9_])_?sprk_(?:matter|project|workassignment)(?:_value)?(?![A-Za-z0-9_])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static bool NamesARetiredRelationship(string text) => RelationshipNames.IsMatch(text);

    internal static bool NamesARetiredColumn(string text) => ColumnNames.IsMatch(text);

    /// <summary>Text files under <c>{repo}/{root}</c>, pruning <see cref="ExcludedDirectoryNames"/> before descending.</summary>
    private static IEnumerable<string> Walk(string root, bool applyAllowedFiles)
    {
        var start = Path.Combine(SourceScan.RepoRoot, root);
        if (!Directory.Exists(start))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!TextExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                if (applyAllowedFiles && AllowedFiles.ContainsKey(SourceScan.Relative(file).Replace('\\', '/')))
                {
                    continue;
                }

                yield return file;
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!ExcludedDirectoryNames.Contains(Path.GetFileName(sub)))
                {
                    pending.Push(sub);
                }
            }
        }
    }

    [Fact(DisplayName = "ebilling-r1 193: no solution source, export or schema script names the three retired sprk_ratecard relationships")]
    public void NoSolutionSourceOrSchemaScript_NamesTheRetiredRelationships()
    {
        var found = new List<string>();
        foreach (var file in RelationshipRoots.SelectMany(r => Walk(r, applyAllowedFiles: true)))
        {
            var relative = SourceScan.Relative(file);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (NamesARetiredRelationship(lines[i]))
                {
                    found.Add($"{relative}:{i + 1}");
                }
            }
        }

        Assert.True(found.Count == 0,
            "sprk_ratecard's core-record lookups (sprk_matter, sprk_project, sprk_workassignment) and their relationships are retired (ebilling-r1 task 193). "
            + "A solution export or schema script brought a relationship back; clean it (see the maintenance note in this file). Found:\n"
            + string.Join("\n", found));
    }

    [Fact(DisplayName = "ebilling-r1 193: the sprk_ratecard table source (Entity.xml, forms, views) names none of the three retired columns")]
    public void TheRateCardTableSource_NamesNoneOfTheRetiredColumns()
    {
        var found = new List<string>();
        foreach (var file in Walk(RateCardEntityDirectory, applyAllowedFiles: false).Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var relative = SourceScan.Relative(file);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (NamesARetiredColumn(lines[i]))
                {
                    found.Add($"{relative}:{i + 1}");
                }
            }
        }

        Assert.True(found.Count == 0,
            "sprk_ratecard no longer has sprk_matter, sprk_project or sprk_workassignment (ebilling-r1 task 193). "
            + "The table's solution source names one: a solution export taken before the delete script ran brought it back. Found:\n"
            + string.Join("\n", found));
    }

    [Theory(DisplayName = "ebilling-r1 193 control: the relationship detector fires on each retired relationship")]
    [InlineData("<SchemaName>sprk_matter_ratecard_matter</SchemaName>")]
    [InlineData("<EntityRelationship Name=\"sprk_project_ratecard_project\">")]
    [InlineData("Add-Lookup -Column 'sprk_workassignment' -RelSchema 'SPRK_WORKASSIGNMENT_RATECARD_WORKASSIGNMENT' -Target 'sprk_workassignment'")]
    public void TheRelationshipDetector_FiresOnEachRetiredName(string text) => Assert.True(NamesARetiredRelationship(text));

    [Theory(DisplayName = "ebilling-r1 193 control: the column detector fires on each spelling of a retired column")]
    [InlineData("<attribute PhysicalName=\"sprk_Matter\">")]
    [InlineData("<LogicalName>sprk_project</LogicalName>")]
    [InlineData("<control id=\"sprk_workassignment\" datafieldname=\"sprk_workassignment\" />")]
    [InlineData("<cell name=\"sprk_matter\" width=\"150\" />")]
    [InlineData("<attribute name=\"sprk_project\" />")]
    [InlineData("<condition attribute=\"x\" value=\"_sprk_workassignment_value\" />")]
    public void TheColumnDetector_FiresOnEachSpelling(string text) => Assert.True(NamesARetiredColumn(text));

    [Theory(DisplayName = "ebilling-r1 193 control: the detectors stay quiet on what stays (sprk_firm, sprk_procedureversion, sprk_sourcedocument, other relationships)")]
    [InlineData("<LogicalName>sprk_firm</LogicalName>")]
    [InlineData("<control id=\"sprk_procedureversion\" datafieldname=\"sprk_procedureversion\" />")]
    [InlineData("Add-Lookup -Column 'sprk_sourcedocument' -RelSchema 'sprk_document_ratecard_sourcedocument' -Target 'sprk_document'")]
    [InlineData("<SchemaName>sprk_matter_ruleset_matter</SchemaName>")]
    [InlineData("<LogicalName>sprk_matterid</LogicalName><LogicalName>sprk_projectstatus</LogicalName>")]
    [InlineData("var rel = \"sprk_ratecard_ratecardrate_ratecard\";")]
    public void TheDetectors_StayQuietOnWhatStays(string text)
    {
        Assert.False(NamesARetiredRelationship(text));
        Assert.False(NamesARetiredColumn(text));
    }

    [Fact(DisplayName = "ebilling-r1 193 control: the walk reaches the rate card source, the relationship files and the schema scripts, and skips the allowed file")]
    public void TheWalk_ReachesTheScannedFiles()
    {
        var relationshipFiles = RelationshipRoots.SelectMany(r => Walk(r, applyAllowedFiles: true))
            .Select(f => SourceScan.Relative(f).Replace('\\', '/')).ToList();
        Assert.Contains("src/dataverse/solutions/SpaarkeMaster/Other/Relationships.xml", relationshipFiles);
        Assert.Contains("src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_Matter.xml", relationshipFiles);
        Assert.Contains("src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ratecard/Entity.xml", relationshipFiles);
        Assert.Contains("scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1", relationshipFiles);

        var rateCardFiles = Walk(RateCardEntityDirectory, applyAllowedFiles: false)
            .Select(f => SourceScan.Relative(f).Replace('\\', '/')).ToList();
        Assert.Contains("src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ratecard/Entity.xml", rateCardFiles);
        Assert.Contains(rateCardFiles, f => f.Contains("/FormXml/main/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rateCardFiles, f => f.Contains("/SavedQueries/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(rateCardFiles, f => f.Contains("/sprk_ratecardrate/", StringComparison.OrdinalIgnoreCase));

        foreach (var allowed in AllowedFiles.Keys)
        {
            Assert.True(File.Exists(Path.Combine(SourceScan.RepoRoot, allowed)), $"allowed file is gone; remove it from AllowedFiles: {allowed}");
            Assert.DoesNotContain(allowed, relationshipFiles);
        }
    }
}
