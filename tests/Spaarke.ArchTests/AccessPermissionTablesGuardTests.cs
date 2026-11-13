using System.Xml.Linq;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 022b — the Restricted-source rule reads a mapping source's
/// <c>sprk_accesspermission</c> only on a table the code says carries it (<see cref="InheritedAccessPermission.SourceCarriesColumn"/>:
/// the four inheriting tables, the secure roots from the table registry, and the source-only tables). That set must equal
/// the solution's tables that define the column, exactly. A table the code leaves out is never read for it, so values copied
/// from it are never checked; a table the code names that lacks the column would fail every read of it.
/// </summary>
/// <remarks>
/// <para><b>The source.</b> Every <c>Entity.xml</c> of <c>src/dataverse/solutions/SpaarkeMaster</c> whose attributes include
/// one with the logical name <c>sprk_accesspermission</c> (exactly; <c>sprk_accesspermissiongrant</c> is another column). The
/// table is the entity folder's name, lower-cased.</para>
/// <para><b>No baseline.</b> Any disagreement fails (UAC-r2, 2026-11-12).</para>
/// <para><b>Controls.</b> <see cref="Disagreements_MustFireAndMustNotFire"/> runs the same comparison over seeded sets: a
/// table only the solution has, and one only the code has, are reported (must fire); equal sets are not (must not fire).</para>
/// </remarks>
public class AccessPermissionTablesGuardTests
{
    private const string AccessPermission = "sprk_accesspermission";

    private static string SolutionRoot => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster");

    /// <summary>Every table whose <c>Entity.xml</c> defines <c>sprk_accesspermission</c>, lower-cased.</summary>
    private static IReadOnlySet<string> SolutionTables()
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SolutionRoot, "Entities"), "Entity.xml", SearchOption.AllDirectories))
        {
            var table = new DirectoryInfo(Path.GetDirectoryName(file)!).Name.ToLowerInvariant();
            var defines = XDocument.Load(file).Descendants("attribute").Any(attribute => string.Equals(
                ((string?)attribute.Element("LogicalName") ?? (string?)attribute.Attribute("PhysicalName"))?.Trim(),
                AccessPermission, StringComparison.OrdinalIgnoreCase));
            if (defines)
                tables.Add(table);
        }

        return tables;
    }

    /// <summary>The tables only one side names, as "table: where", in order. Pure.</summary>
    private static IReadOnlyList<string> Disagreements(IEnumerable<string> code, IEnumerable<string> solution)
    {
        var codeSet = code.Select(t => t.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var solutionSet = solution.Select(t => t.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        return solutionSet.Except(codeSet).Select(t => $"{t}: the solution defines it, the code does not name it")
            .Concat(codeSet.Except(solutionSet).Select(t => $"{t}: the code names it, the solution does not define it"))
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();
    }

    [Fact(DisplayName = "Task 022b: the tables the code reads sprk_accesspermission on are exactly the solution's tables that define it")]
    public void TheCodesTablesAreTheSolutionsTables()
    {
        var solution = SolutionTables();
        var code = InheritedAccessPermission.SourceTablesCarryingColumn().ToList();

        // Non-vacuity: both sides see the tables the rule is about.
        Assert.Contains("sprk_matter", solution);
        Assert.Contains("sprk_document", solution);
        Assert.Contains("sprk_matter", code);
        Assert.Contains("sprk_todo", code);
        Assert.All(code, table => Assert.True(InheritedAccessPermission.SourceCarriesColumn(table), table));

        var disagreements = Disagreements(code, solution);
        Assert.True(disagreements.Count == 0,
            "The Restricted-source rule reads sprk_accesspermission only on the tables " +
            "InheritedAccessPermission.SourceCarriesColumn names (the inheriting tables, the secure roots and the source-only " +
            "tables). They must be exactly the solution's tables that define the column:\n" + string.Join("\n", disagreements));
    }

    [Fact(DisplayName = "Task 022b guard control: a seeded table only one side names is reported; equal sets are not")]
    public void Disagreements_MustFireAndMustNotFire()
    {
        string[] code = ["sprk_matter", "sprk_todo"];

        // Must fire: a table only the solution defines, and one only the code names.
        Assert.Equal(new[] { "sprk_seeded: the solution defines it, the code does not name it" },
            Disagreements(code, ["sprk_matter", "sprk_todo", "sprk_seeded"]));
        Assert.Equal(new[] { "sprk_todo: the code names it, the solution does not define it" },
            Disagreements(code, ["sprk_matter"]));

        // Must not fire: equal sets (case aside).
        Assert.Empty(Disagreements(code, ["SPRK_TODO", "sprk_matter"]));
    }
}
