using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// field-mapping-server-write-path-r1 task 044 (owner ruling 2026-11-06; ontology D-210): the core record pair of a Signal and a Decision
/// Record (<c>sprk_corerecordtype</c> + <c>sprk_corerecordid</c>) is NOT field-secured. Write integrity rests on the roles (an exact
/// allow-list, <c>Spaarke.ArchTests.SignalWriterRoleGuardTests</c>), the writers' refusal (D-204), column auditing and task 052's report;
/// Signal access fails closed when the pair cannot be confirmed (<c>SignalCoreRecordAccess</c>). This test pins the unsecured state in
/// the solution source: every <c>Entity.xml</c> of the two tables keeps <c>IsSecured</c> 0 on both columns, and no field-security
/// profile in any solution names either column. A later half-apply (a column secured, or a profile row added without the other parts)
/// fails here. Read as text; every check runs on seeded drifts (must fire) and on the committed files (must not fire).
/// </summary>
[Trait("Category", "Security")]
public class CorePairFieldSecurityAgreementTests
{
    private static readonly string[] Tables = ["sprk_signal", "sprk_decisionrecord"];
    private static readonly string[] Columns = ["sprk_corerecordtype", "sprk_corerecordid"];

    [Fact]
    public void TheCommittedSolutions_LeaveThePairUnsecured_AndNoProfileNamesIt()
    {
        var entities = EntityFiles(SolutionsRoot()).ToList();
        entities.Select(e => e.Table).Distinct().Should().BeEquivalentTo(Tables, "both tables' Entity.xml must be found, or the check is vacuous");

        Gaps(entities.Select(e => (e.Table, e.Path, XDocument.Load(e.Path))), ProfileFiles(SolutionsRoot()).Select(XDocument.Load))
            .Should().BeEmpty();
    }

    public static IEnumerable<object[]> Drifts() =>
        from table in Tables
        from column in Columns
        from kind in new[] { "secured", "profile" }
        select new object[] { kind, table, column };

    [Theory]
    [MemberData(nameof(Drifts))]
    public void TheCheck_FiresOnEachSeededDrift(string kind, string table, string column)
    {
        var entities = EntityFiles(SolutionsRoot()).Select(e => (e.Table, e.Path, Doc: XDocument.Load(e.Path))).ToList();
        var profiles = ProfileFiles(SolutionsRoot()).Select(XDocument.Load).ToList();
        Gaps(entities, profiles).Should().BeEmpty("the committed files agree (must not fire)");

        if (kind == "secured")
        {
            var target = entities.First(e => e.Table == table).Doc;
            Attribute(target, column)!.Element("IsSecured")!.Value = "1";
        }
        else
        {
            profiles[0].Root!.Elements("FieldSecurityProfile").First().Element("FieldPermissions")!.Add(
                new XElement("FieldPermission",
                    new XElement("EntityName", table), new XElement("AttributeName", column),
                    new XElement("CanRead", 4), new XElement("CanUpdate", 0), new XElement("CanCreate", 0)));
        }

        Gaps(entities, profiles).Should().ContainSingle($"the seeded drift '{kind}' on {table}.{column} must fail (must fire)")
            .Which.Should().Contain(table).And.Contain(column);
    }

    [Fact]
    public void TheCheck_FiresWhenAColumnIsMissingFromAnEntityFile()
    {
        var entities = EntityFiles(SolutionsRoot()).Select(e => (e.Table, e.Path, Doc: XDocument.Load(e.Path))).ToList();
        Attribute(entities[0].Doc, "sprk_corerecordid")!.Remove();

        Gaps(entities, []).Should().Contain(g => g.Contains("not found", StringComparison.Ordinal), "a missing column is never a pass");
    }

    // ── The check ───────────────────────────────────────────────────────────────────────────────────────────

    internal static List<string> Gaps(IEnumerable<(string Table, string Path, XDocument Doc)> entities, IEnumerable<XDocument> profiles)
    {
        var gaps = new List<string>();
        foreach (var (table, path, doc) in entities)
        {
            foreach (var column in Columns)
            {
                var attribute = Attribute(doc, column);
                if (attribute is null)
                    gaps.Add($"{table}.{column}: column not found in {path}");
                else if (attribute.Element("IsSecured")?.Value.Trim() is not "0")
                    gaps.Add($"{table}.{column}: IsSecured is '{attribute.Element("IsSecured")?.Value}' in {path}; the pair is not field-secured (D-210)");
            }
        }

        foreach (var profile in profiles.SelectMany(p => p.Root!.Elements("FieldSecurityProfile")))
        {
            foreach (var permission in profile.Descendants("FieldPermission"))
            {
                var table = permission.Element("EntityName")?.Value.Trim().ToLowerInvariant();
                var column = permission.Element("AttributeName")?.Value.Trim().ToLowerInvariant();
                if (Tables.Contains(table) && Columns.Contains(column))
                    gaps.Add($"{table}.{column}: named by field-security profile '{profile.Attribute("name")?.Value}'; no profile names the pair (D-210)");
            }
        }

        return gaps;
    }

    private static XElement? Attribute(XDocument entity, string column) =>
        entity.Descendants("attribute").FirstOrDefault(a =>
            string.Equals(a.Element("LogicalName")?.Value.Trim(), column, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every <c>Entity.xml</c> of the two tables in any solution under <c>src/dataverse/solutions</c>.</summary>
    private static IEnumerable<(string Table, string Path)> EntityFiles(string root) =>
        from path in Directory.EnumerateFiles(root, "Entity.xml", SearchOption.AllDirectories)
        let table = Path.GetFileName(Path.GetDirectoryName(path))!.ToLowerInvariant()
        where Tables.Contains(table)
        select (table, path);

    /// <summary>Every <c>FieldSecurityProfiles.xml</c> in any solution under <c>src/dataverse/solutions</c>.</summary>
    private static IEnumerable<string> ProfileFiles(string root) =>
        Directory.EnumerateFiles(root, "FieldSecurityProfiles.xml", SearchOption.AllDirectories);

    private static string SolutionsRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "dataverse", "solutions");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
                return candidate;
        }

        throw new InvalidOperationException("Repository root (src/dataverse/solutions) not found.");
    }
}
