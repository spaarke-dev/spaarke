using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-039: every column a reference seed file names (<c>scripts/seed-data/reference/*.json</c>, <c>columns</c> plus the primary key)
/// must be an attribute of that entity in the SpaarkeMaster solution source.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> H12b POSTs these columns to the customer environment. A column that SpaarkeMaster does not define (a dev-only column
/// the export picked up, or one a later solution change renamed or removed) makes Dataverse refuse the create with "property does not
/// exist", and H12b fails on every stamp. The check is a static comparison of the seed file with <c>Entity.xml</c>, so a bad export fails
/// in CI instead of in a customer run.</para>
/// <para><b>Enforced by:</b> this test (CI and the Stop hook). <b>Controls:</b> an unknown column is reported (must fire); a defined
/// column, in any letter case, is not (must not fire).</para>
/// </remarks>
public sealed class ReferenceSeedColumnsExistTests
{
    private const string SeedDirectory = "scripts/seed-data/reference";
    private const string EntitiesDirectory = "src/dataverse/solutions/SpaarkeMaster/Entities";

    private static string Abs(string repoRelative) => Path.Combine(SourceScan.RepoRoot, repoRelative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The logical names (lower-cased) of every attribute an <c>Entity.xml</c> defines.</summary>
    internal static HashSet<string> AttributeNames(string entityXml)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in XDocument.Parse(entityXml).Descendants("attribute"))
        {
            foreach (var name in new[] { attribute.Attribute("PhysicalName")?.Value, attribute.Element("LogicalName")?.Value })
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name.Trim());
                }
            }
        }

        return names;
    }

    /// <summary>The wanted columns that the entity does not define (case-insensitive).</summary>
    internal static IReadOnlyList<string> MissingColumns(string entityXml, IEnumerable<string> wanted)
    {
        var defined = AttributeNames(entityXml);
        return wanted.Where(c => !defined.Contains(c)).ToList();
    }

    private static string EntityXmlPath(string table)
    {
        var directory = Directory.EnumerateDirectories(Abs(EntitiesDirectory))
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), table, StringComparison.OrdinalIgnoreCase));
        Assert.True(directory is not null, $"No entity folder for '{table}' under {EntitiesDirectory}: the seed file names a table SpaarkeMaster does not ship.");
        return Path.Combine(directory, "Entity.xml");
    }

    [Fact(DisplayName = "ISS-039: every column in each reference seed file is an attribute of its entity in SpaarkeMaster")]
    public void EverySeedColumn_IsDefinedInTheSolution()
    {
        var files = Directory.EnumerateFiles(Abs(SeedDirectory), "*.json").ToList();
        Assert.True(files.Count >= 3, $"Only {files.Count} seed files found in {SeedDirectory}; the check would be vacuous.");

        var problems = new List<string>();
        foreach (var file in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var table = root.GetProperty("table").GetString()!;
            var wanted = root.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!)
                .Append(root.GetProperty("primaryKey").GetString()!)
                .ToList();
            Assert.True(wanted.Count >= 4, $"{table}: only {wanted.Count} columns read from the seed file; the check would be vacuous.");

            var missing = MissingColumns(File.ReadAllText(EntityXmlPath(table)), wanted);
            if (missing.Count > 0)
            {
                problems.Add($"{Path.GetFileName(file)}: column(s) {string.Join(", ", missing)} are not attributes of {table} in {EntitiesDirectory}. " +
                             "Dataverse would refuse the H12b create. Remove them from the export (Export-ReferenceData.ps1) or add them to the solution.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact(DisplayName = "ISS-039 control (must fire): a column the entity does not define is reported, against the real SpaarkeMaster XML")]
    public void Control_UnknownColumn_Fires_OnTheRealEntityXml()
    {
        var xml = File.ReadAllText(EntityXmlPath("sprk_triagecategory"));

        var missing = MissingColumns(xml, ["sprk_name", "sprk_priorityweight", "sprk_devonlycolumn"]);

        Assert.Equal(["sprk_devonlycolumn"], missing);
    }

    [Fact(DisplayName = "ISS-039 control (must not fire): defined columns match in any letter case, by logical or physical name")]
    public void Control_DefinedColumns_DoNotFire()
    {
        const string xml = """
            <Entity><EntityInfo><entity><attributes>
              <attribute PhysicalName="sprk_ClassifierGuidance"><Name>sprk_classifierguidance</Name><LogicalName>sprk_classifierguidance</LogicalName></attribute>
              <attribute PhysicalName="sprk_Enabled"><Name>sprk_enabled</Name></attribute>
            </attributes></entity></EntityInfo></Entity>
            """;

        Assert.Empty(MissingColumns(xml, ["sprk_classifierguidance", "SPRK_ENABLED"]));
    }
}
