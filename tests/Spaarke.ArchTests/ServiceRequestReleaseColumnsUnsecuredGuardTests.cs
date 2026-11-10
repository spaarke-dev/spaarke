using System.Xml.Linq;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Task 176 (F-176-1, uac-r2 option A): every column the service request sole-root release reads
/// (<see cref="RecordContainerResolver.ServiceRequestReleaseColumns"/>, derived from the request's
/// <c>ChildAncestorLinks</c> entry) is declared NOT field-secured (<c>&lt;IsSecured&gt;0&lt;/IsSecured&gt;</c>) in the
/// <c>sprk_servicerequest</c> Entity.xml under <c>src/dataverse/solutions</c>.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The release reads a MISSING key for another link or the pair as null: Dataverse omits a null attribute
/// from a successful Retrieve. Field-level security also removes a key — a masked value reads exactly like an empty one, and
/// the release would then drop the hold on a request that names a secure record. Securing any of these columns breaks the
/// missing = null reading; update this guard (and the release) first.</para>
/// <para>An explicit <c>0</c> is required: a missing <c>IsSecured</c> element, a column with no declaration, or any
/// declaration (in any solution) marked <c>1</c> fails.</para>
/// <para>Enforced by: <c>ServiceRequestReleaseColumnsUnsecuredGuardTests.SecuringAnyReleaseColumnBreaksMissingAsNull_UpdateTheGuardFirst</c>.</para>
/// </remarks>
public class ServiceRequestReleaseColumnsUnsecuredGuardTests
{
    private const string Table = "sprk_servicerequest";

    private static string SolutionsRoot => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions");

    private static IReadOnlyList<string> ReleaseColumns()
    {
        var links = RecordContainerResolver.ChildAncestorLinks.For(Table);
        Assert.NotNull(links);
        return RecordContainerResolver.ServiceRequestReleaseColumns(links!);
    }

    [Fact(DisplayName = "Task 176: the service request release columns are not field-secured — securing any of them breaks the missing = null reading; update the guard first")]
    public void SecuringAnyReleaseColumnBreaksMissingAsNull_UpdateTheGuardFirst()
    {
        var columns = ReleaseColumns();
        // Sanity floor: matter + 5 other links + the pair's id and type at F-176-1. Fewer means the entry or the derivation broke.
        Assert.True(columns.Count >= 8, $"Only {columns.Count} release columns; expected at least 8.");
        Assert.Contains("sprk_regardingmatter", columns);

        var violations = Violations(columns, ReadDeclarations(SolutionsRoot));

        Assert.True(violations.Count == 0,
            "A column the service request sole-root release reads is not declared unsecured. The release reads a missing key "
            + "as null, and field-level security also removes the key, so a secured column could hide a secure record and "
            + "release the hold (F-176-1). Keep <IsSecured>0</IsSecured>, or change the release before securing it:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Release-columns guard must-not-fire: the real sprk_servicerequest Entity.xml declares each release column, each explicitly unsecured")]
    public void RealEntityXmlDeclaresEachReleaseColumnUnsecured()
    {
        var declarations = ReadDeclarations(SolutionsRoot);
        foreach (var column in ReleaseColumns())
        {
            Assert.True(declarations.TryGetValue(column, out var found) && found.Count > 0, $"{column}: no declaration found.");
            Assert.All(found!, d => Assert.False(d.IsSecured, $"{column}: {d.File} declares it secured."));
        }
    }

    [Fact(DisplayName = "Release-columns guard must-fire: flipping any one real release column to IsSecured=1 is reported, exactly that column")]
    public void GuardFiresOnEachRealColumnMadeSecured()
    {
        var columns = ReleaseColumns();
        var real = ReadDeclarations(SolutionsRoot);
        Assert.Empty(Violations(columns, real));

        foreach (var column in columns)
        {
            var perturbed = real.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            perturbed[column] = real[column].Select(d => d with { IsSecured = true }).ToList();

            var violation = Assert.Single(Violations(columns, perturbed));
            Assert.StartsWith(column + ":", violation, StringComparison.Ordinal);
            Assert.Contains("secured", violation, StringComparison.Ordinal);
        }
    }

    [Fact(DisplayName = "Release-columns guard must-fire: a fixture attribute marked secured, one with no IsSecured element, and an undeclared column")]
    public void GuardFiresOnFixtureColumns()
    {
        string[] columns = ["sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingtodo", "sprk_regardingrecordid"];
        var xml = EntityXml(
            Attribute("sprk_regardingmatter", "<IsSecured>0</IsSecured>"),
            Attribute("sprk_regardingproject", "<IsSecured>1</IsSecured>"),
            Attribute("sprk_regardingtodo", ""));
        var declarations = Parse(xml, "fixture.xml")
            .GroupBy(d => d.Column, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Declaration>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        var violations = Violations(columns, declarations);

        Assert.Equal(3, violations.Count);
        Assert.Contains(violations, v => v.StartsWith("sprk_regardingproject:", StringComparison.Ordinal) && v.Contains("secured"));
        Assert.Contains(violations, v => v.StartsWith("sprk_regardingtodo:", StringComparison.Ordinal) && v.Contains("secured"));
        Assert.Contains(violations, v => v.StartsWith("sprk_regardingrecordid:", StringComparison.Ordinal) && v.Contains("no declaration"));
        Assert.DoesNotContain(violations, v => v.StartsWith("sprk_regardingmatter:", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Release-columns guard parse: only the sprk_servicerequest table, and a secured look-alike column does not count")]
    public void ParseMatchesTheTableAndExactColumn()
    {
        var request = EntityXml(
            Attribute("sprk_regardingmatter", "<IsSecured>0</IsSecured>"),
            Attribute("sprk_regardingmattername", "<IsSecured>1</IsSecured>"));
        var declaration = Assert.Single(Parse(request, "r.xml"), d => d.Column == "sprk_regardingmatter");
        Assert.False(declaration.IsSecured);

        var otherTable = EntityXml(Attribute("sprk_regardingmatter", "<IsSecured>1</IsSecured>"))
            .Replace("sprk_ServiceRequest", "sprk_Event", StringComparison.Ordinal);
        Assert.Empty(Parse(otherTable, "e.xml"));
    }

    // ------------------------------------------------------------------------------------------------------------------

    internal sealed record Declaration(string Column, string File, bool IsSecured);

    /// <summary>Every attribute declaration on <c>sprk_servicerequest</c> under <paramref name="solutionsRoot"/>, keyed by column.</summary>
    private static Dictionary<string, IReadOnlyList<Declaration>> ReadDeclarations(string solutionsRoot) =>
        Directory.EnumerateFiles(solutionsRoot, "Entity.xml", SearchOption.AllDirectories)
            .Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f))), "Entities", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => Parse(File.ReadAllText(f), Path.GetRelativePath(SourceScan.RepoRoot, f)))
            .GroupBy(d => d.Column, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Declaration>)g.ToList(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The attribute declarations in one Entity.xml when it is <c>sprk_servicerequest</c>'s. Unsecured only on an explicit 0.</summary>
    private static IReadOnlyList<Declaration> Parse(string entityXml, string file)
    {
        var doc = XDocument.Parse(entityXml);
        var table = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value.Trim();
        if (!string.Equals(table, Table, StringComparison.OrdinalIgnoreCase))
            return [];

        return doc.Descendants()
            .Where(e => e.Name.LocalName == "attribute")
            .Select(e => (
                Column: e.Elements().FirstOrDefault(c => c.Name.LocalName == "LogicalName")?.Value.Trim(),
                Secured: e.Elements().FirstOrDefault(c => c.Name.LocalName == "IsSecured")?.Value.Trim()))
            .Where(a => !string.IsNullOrEmpty(a.Column))
            .Select(a => new Declaration(a.Column!.ToLowerInvariant(), file, a.Secured != "0"))
            .ToList();
    }

    /// <summary>The guard's verdict: one line per violation, prefixed with the column.</summary>
    private static List<string> Violations(
        IEnumerable<string> columns, IReadOnlyDictionary<string, IReadOnlyList<Declaration>> declarations)
    {
        var violations = new List<string>();
        foreach (var column in columns.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!declarations.TryGetValue(column, out var found) || found.Count == 0)
            {
                violations.Add($"{column}: no declaration on {Table} under src/dataverse/solutions, so it cannot be shown unsecured.");
                continue;
            }

            foreach (var d in found.Where(d => d.IsSecured))
                violations.Add($"{column}: {d.File} declares it field-secured (IsSecured is not an explicit 0).");
        }

        return violations;
    }

    private static string Attribute(string logicalName, string isSecuredElement) => $"""
        <attribute PhysicalName="{logicalName}">
          <Type>lookup</Type>
          <LogicalName>{logicalName}</LogicalName>
          {isSecuredElement}
        </attribute>
        """;

    private static string EntityXml(params string[] attributes) => $"""
        <Entity>
          <Name LocalizedName="X" OriginalName="X">sprk_ServiceRequest</Name>
          <EntityInfo>
            <entity Name="sprk_ServiceRequest">
              <attributes>
                {string.Join("\n", attributes)}
              </attributes>
            </entity>
          </EntityInfo>
        </Entity>
        """;
}
