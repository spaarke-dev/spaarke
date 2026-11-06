using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 040 (FR-34, scope: sync mode) — the sync-mode migration script
/// (<c>scripts/Set-FieldMappingRuleSyncMode.ps1</c>) decides a rule's class from two tables it carries: the Assigned-To columns and
/// the filing columns. A script cannot read the BFF's registries, so the tables are copied into it and THESE tests hold the copies to
/// the sources: a column added to the registry or to a family edge that is not added to the script fails here, naming the line to add.
/// </summary>
public class SyncModeMigrationClassDataTests
{
    private static string ScriptText()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "Set-FieldMappingRuleSyncMode.ps1");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException("scripts/Set-FieldMappingRuleSyncMode.ps1 was not found above the test output folder.");
    }

    /// <summary>The <c>'table' = @('a','b')</c> lines between the named BEGIN/END markers, table → columns.</summary>
    private static SortedDictionary<string, string[]> Block(string script, string name)
    {
        var match = Regex.Match(script, $@"#\s*BEGIN-CLASS-DATA {name}\b(?<body>.*?)#\s*END-CLASS-DATA {name}\b", RegexOptions.Singleline);
        match.Success.Should().BeTrue($"the script carries a '{name}' class-data block between BEGIN-CLASS-DATA and END-CLASS-DATA markers");

        var result = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (Match line in Regex.Matches(match.Groups["body"].Value, @"^\s*'(?<t>[a-z0-9_]+)'\s*=\s*@\((?<c>[^)]*)\)\s*$", RegexOptions.Multiline))
        {
            var columns = Regex.Matches(line.Groups["c"].Value, @"'(?<x>[a-z0-9_]+)'").Select(m => m.Groups["x"].Value).OrderBy(c => c, StringComparer.Ordinal).ToArray();
            result.Add(line.Groups["t"].Value, columns);
        }

        return result;
    }

    private static string Render(IDictionary<string, string[]> table) =>
        string.Join(Environment.NewLine, table.Select(kv => $"    '{kv.Key}' = @({string.Join(", ", kv.Value.Select(c => $"'{c}'"))})"));

    private static SortedDictionary<string, string[]> ExpectedAssignedTo()
    {
        var expected = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (table, columns) in MembershipOptionsDefaults.CanonicalAccessConferringRegistry)
            expected[table.ToLowerInvariant()] = columns.Select(c => c.Field.ToLowerInvariant()).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
        return expected;
    }

    private static SortedDictionary<string, string[]> ExpectedFiling()
    {
        var expected = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var group in RecordFamilyEdges.All.Where(e => e.FamilyKind == RecordFamilyEdges.FamilyKind.Filing).GroupBy(e => e.ChildTable))
        {
            var columns = group.Select(e => e.LookupColumn.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            // The regarding pair is two columns: the id the edge names and the type that says which table the id belongs to.
            if (group.Any(e => e.EdgeKind == RecordFamilyEdges.EdgeKind.Pair))
                columns.Add(RecordFamilyEdges.PairTypeColumn.ToLowerInvariant());
            expected[group.Key.ToLowerInvariant()] = columns.OrderBy(c => c, StringComparer.Ordinal).ToArray();
        }

        return expected;
    }

    [Fact]
    public void TheAssignedToTable_IsTheCanonicalRegistry()
    {
        var actual = Block(ScriptText(), "assigned-to");

        actual.Should().BeEquivalentTo(ExpectedAssignedTo(), o => o.WithStrictOrdering(),
            "the script's Assigned-To table must equal MembershipOptionsDefaults.CanonicalAccessConferringRegistry. The block should read:\n"
            + Render(ExpectedAssignedTo()));
    }

    [Fact]
    public void TheFilingTable_IsTheFamilyEdges()
    {
        var actual = Block(ScriptText(), "filing");

        actual.Should().BeEquivalentTo(ExpectedFiling(), o => o.WithStrictOrdering(),
            "the script's filing table must equal RecordFamilyEdges.All (every edge's lookup column, plus the regarding pair's type column). The block should read:\n"
            + Render(ExpectedFiling()));
    }

    [Fact]
    public void NoColumnIsBothAssignedToAndFiling_SoTheClassIsNeverAmbiguous()
    {
        var assigned = ExpectedAssignedTo();
        var filing = ExpectedFiling();

        var both = assigned.Where(kv => filing.TryGetValue(kv.Key, out var f) && f.Intersect(kv.Value).Any())
            .Select(kv => kv.Key).ToArray();

        both.Should().BeEmpty();
    }
}
