using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

/// <summary>
/// unified-access-control-r2 task 173 (owner rounds 81/84) — the two pure parts of the inherited Access Permission rule:
/// how levels rank, and which lookups make a record "filed under" another, on the server AND on the form.
/// </summary>
/// <remarks>The walk, the stamp path and the reconcile are exercised through the real classes in
/// <c>tests/integration/data-mutation/ChildAccessPermission/</c>.</remarks>
public class ParentLineageTests
{
    private const int Standard = InheritedAccessPermission.Standard;
    private const int Limited = InheritedAccessPermission.Limited;
    private const int Restricted = InheritedAccessPermission.Restricted;

    [Theory]
    [InlineData(new[] { Standard, Limited }, Limited)]
    [InlineData(new[] { Limited, Restricted }, Restricted)]
    [InlineData(new[] { Restricted, Standard, Limited }, Restricted)]
    [InlineData(new[] { Standard }, Standard)]
    public void MostRestrictive_RanksRestrictedOverLimitedOverStandard(int[] values, int expected)
    {
        InheritedAccessPermission.MostRestrictive(values.Select(v => (int?)v)).Should().Be(expected);
    }

    [Fact]
    public void MostRestrictive_ANullValueCountsAsStandard()
    {
        InheritedAccessPermission.MostRestrictive([null, null]).Should().Be(Standard);
        InheritedAccessPermission.MostRestrictive([null, Limited]).Should().Be(Limited);
    }

    [Fact]
    public void MostRestrictive_AnUnknownOptionValue_IsNeverRankedByGuess()
    {
        InheritedAccessPermission.MostRestrictive([Standard, 100000003]).Should().BeNull();
    }

    [Fact]
    public void ChildFiling_IsTheLineageMap_WithoutADocumentsOwnCurrentVersion()
    {
        foreach (var (table, lineage) in SecureChildLineage.Children)
        {
            var expected = lineage.Lookups
                .Where(l => !(table == "sprk_document" && l.Key == "sprk_currentversionid"))
                .ToDictionary(l => l.Key, l => l.Value);
            ParentLineage.ChildFiling[table].Should().BeEquivalentTo(expected, $"{table}'s filing is its lineage (one source)");
        }
    }

    /// <summary>
    /// The form library decides "has a parent" (and so locks the field) from its own literal copy of the server's filing
    /// lookups. A lookup the server walks but the form ignores leaves a field editable whose value the reconcile then
    /// overwrites; one the form reads but the server does not locks a parentless record's own value.
    /// </summary>
    [Fact]
    public void FormLibraryParentLookups_MatchTheServerMap()
    {
        var path = FindRepoFile("src/client/webresources/js/sprk_accesspermission_inherited.js");
        path.Should().NotBeNull("the form library is checked in");
        var source = File.ReadAllText(path!);
        var match = Regex.Match(source, @"PARENT_LOOKUPS:BEGIN[^\n]*\n\s*ns\.PARENT_LOOKUPS\s*=\s*(?<json>\{.*?\});\s*/\*\s*PARENT_LOOKUPS:END",
            RegexOptions.Singleline);
        match.Success.Should().BeTrue("the map sits between the PARENT_LOOKUPS markers");

        var form = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(match.Groups["json"].Value)!;

        form.Keys.Should().BeEquivalentTo(InheritedAccessPermission.Tables);
        foreach (var table in InheritedAccessPermission.Tables)
        {
            form[table].Should().BeEquivalentTo(ParentLineage.ChildFiling[table], $"{table}: the form and the server must agree on what a parent is");
        }
    }

    private static string? FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
