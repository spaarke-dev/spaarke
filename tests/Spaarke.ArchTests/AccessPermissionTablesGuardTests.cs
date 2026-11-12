using System.Xml.Linq;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 022b — the Restricted-source rule reads a mapping source's
/// <c>sprk_accesspermission</c> only on a table the code says carries it (<see cref="InheritedAccessPermission.CarriesColumn"/>:
/// the four inheriting tables and the secure roots, from the table registry). That set must equal the solution's tables that
/// define the column. A table the code leaves out is never read for it, so values copied from it are never checked; a table
/// the code names that lacks the column would fail every read of it.
/// </summary>
/// <remarks>
/// <para><b>The source.</b> Every <c>Entity.xml</c> of <c>src/dataverse/solutions/SpaarkeMaster</c> whose attributes include
/// one with the logical name <c>sprk_accesspermission</c> (exactly; <c>sprk_accesspermissiongrant</c> is another column). The
/// table is the entity folder's name, lower-cased.</para>
/// <para><b>Ratchet.</b> <see cref="KnownDisagreements"/> lists each table on which the two sides disagree today, with the
/// reason. A new disagreement fails; an entry that no longer disagrees fails too (remove it).</para>
/// <para><b>Controls.</b> <see cref="Disagreements_MustFireAndMustNotFire"/> runs the same comparison over seeded sets: a
/// table only the solution has, and one only the code has, are reported (must fire); equal sets are not (must not fire).</para>
/// </remarks>
public class AccessPermissionTablesGuardTests
{
    private const string AccessPermission = "sprk_accesspermission";

    /// <summary>Tables where the code's set and the solution disagree today, each with the reason (logical names).</summary>
    private static readonly IReadOnlyDictionary<string, string> KnownDisagreements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sprk_agreement"] =
            "The solution defines the column on the agreement; the code's set (the inheriting tables and the secure roots) does " +
            "not name it, so the Restricted-source rule does not read it there. Reported for a decision on where the agreement " +
            "is declared (task 022b notes, known limits).",
    };

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

    /// <summary>The disagreements <see cref="KnownDisagreements"/> does not hold, and the entries that are stale. Pure.</summary>
    private static IReadOnlyList<string> Problems(IEnumerable<string> code, IEnumerable<string> solution,
        IReadOnlyDictionary<string, string> known)
    {
        var found = Disagreements(code, solution);
        var tables = found.Select(d => d[..d.IndexOf(':')]).ToHashSet(StringComparer.Ordinal);
        return found.Where(d => !known.ContainsKey(d[..d.IndexOf(':')])).Select(d => $"NEW disagreement {d}")
            .Concat(known.Keys.Where(k => !tables.Contains(k)).Select(k => $"STALE entry {k}: the two sides agree now; remove it"))
            .ToList();
    }

    [Fact(DisplayName = "Task 022b: the tables the code reads sprk_accesspermission on are the solution's tables that define it")]
    public void TheCodesTablesAreTheSolutionsTables()
    {
        var solution = SolutionTables();
        var code = InheritedAccessPermission.TablesCarryingColumn().ToList();

        // Non-vacuity: both sides see the tables the rule is about.
        Assert.Contains("sprk_matter", solution);
        Assert.Contains("sprk_document", solution);
        Assert.Contains("sprk_matter", code);
        Assert.Contains("sprk_todo", code);
        Assert.All(code, table => Assert.True(InheritedAccessPermission.CarriesColumn(table), table));

        var problems = Problems(code, solution, KnownDisagreements);
        Assert.True(problems.Count == 0,
            "The Restricted-source rule reads sprk_accesspermission only on the tables InheritedAccessPermission.CarriesColumn " +
            "names (the inheriting tables and the secure roots). They must be the solution's tables that define the column:\n" +
            string.Join("\n", problems));
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
        Assert.Equal(new[] { "NEW disagreement sprk_seeded: the solution defines it, the code does not name it" },
            Problems(code, ["sprk_matter", "sprk_todo", "sprk_seeded"], new Dictionary<string, string>()));
        Assert.Equal(new[] { "STALE entry sprk_gone: the two sides agree now; remove it" },
            Problems(code, ["sprk_matter", "sprk_todo"], new Dictionary<string, string> { ["sprk_gone"] = "x" }));

        // Must not fire: equal sets (case aside), and a disagreement the baseline holds.
        Assert.Empty(Disagreements(code, ["SPRK_TODO", "sprk_matter"]));
        Assert.Empty(Problems(code, ["sprk_matter", "sprk_todo", "sprk_seeded"],
            new Dictionary<string, string> { ["sprk_seeded"] = "x" }));
    }
}
