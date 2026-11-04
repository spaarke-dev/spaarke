using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 082 (D-172 direct association only; notes/rule-set-scope-design.md 3.1; uac-r2 review item 8, round 2 item 082.1):
/// billing's <c>CoreRecordChain</c> reads the invoice's core record and its DIRECT parents only. It reuses uac-r2's
/// <c>TypedFilingColumns</c> and the pair-only <c>NamePairParentAsync</c>, and never references the members that climb past the direct
/// parent or follow an intermediate (#1630): <c>NameParentsAsync</c>, <c>FindFilingParentsAsync</c>, <c>IntermediateFilingColumns</c>,
/// <c>IntermediateParentColumns</c>, <c>TypedLookupsOf</c> and <c>ReadSecureParentsAsync</c>.
/// </summary>
/// <remarks>
/// <para><b>Enforced by:</b> this source scan of <c>CoreRecordChain.cs</c> (CI and the Stop hook). Line comments are ignored, so the
/// file can name what it must not call. <c>NameParentsAsync</c> is private today, so a reference would not compile; the guard still
/// names it, because making it internal is the obvious shortcut. Controls: a planted reference fires, the sanctioned shape does not.</para>
/// <para><b>Not covered:</b> a call through a helper in another file. The chain's only Dataverse reads are its own query and
/// <c>NamePairParentAsync</c>; a new helper would show in review.</para>
/// </remarks>
public sealed class CoreRecordChainNoMultiHopGuardTests
{
    private static readonly string ChainFile = Path.Combine(
        SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Services", "Signals", "Billing", "CoreRecordChain.cs");

    private static readonly string[] Forbidden =
    [
        "NameParentsAsync", "FindFilingParentsAsync", "IntermediateFilingColumns", "IntermediateParentColumns", "TypedLookupsOf",
        "ReadSecureParentsAsync",
    ];

    internal static IReadOnlyList<string> Violations(IEnumerable<string> lines)
    {
        var code = SourceScan.CodeText(lines);
        var found = new List<string>();
        foreach (var member in Forbidden)
        {
            foreach (Match match in Regex.Matches(code, $@"\b{member}\b"))
            {
                found.Add($"line {SourceScan.LineOf(code, match.Index)}: {member}");
            }
        }

        if (!Regex.IsMatch(code, @"\bNamePairParentAsync\b"))
        {
            found.Add("does not resolve the pair through SecureRootInheritance.NamePairParentAsync");
        }

        if (!Regex.IsMatch(code, @"\bTypedFilingColumns\b"))
        {
            found.Add("does not reuse SecureRootInheritance.TypedFilingColumns");
        }

        return found;
    }

    [Fact(DisplayName = "Task 082: CoreRecordChain reads direct parents only (no multi-hop or intermediate member)")]
    public void TheChain_ReferencesNoMultiHopMember()
    {
        Assert.True(File.Exists(ChainFile), $"{ChainFile} was not found; the guard would be vacuous.");
        var violations = Violations(File.ReadAllLines(ChainFile));
        Assert.True(violations.Count == 0, "CoreRecordChain.cs: " + string.Join("; ", violations));
    }

    [Theory(DisplayName = "Task 082: the guard fires on a planted multi-hop or intermediate reference (must-fire control)")]
    [InlineData("var named = await SecureRootInheritance.NameParentsAsync(entities, logger, facts, types, ct);")]
    [InlineData("foreach (var c in SecureRootInheritance.IntermediateFilingColumns[table]) { }")]
    [InlineData("var walk = await inheritance.FindFilingParentsAsync(table, id, ct);")]
    public void TheGuard_FiresOnAPlantedReference(string planted)
    {
        var lines = File.ReadAllLines(ChainFile).Append(planted);

        Assert.NotEmpty(Violations(lines));
    }

    [Fact(DisplayName = "Task 082: the guard fires when the pair is parsed here instead of through NamePairParentAsync (must-fire control)")]
    public void TheGuard_FiresWithoutTheSharedPairRule()
    {
        var lines = new[]
        {
            "var columns = SecureRootInheritance.TypedFilingColumns[table];",
            "if (Guid.TryParse(row.GetAttributeValue<string>(\"sprk_regardingrecordid\"), out var id)) { }",
        };

        Assert.Contains(Violations(lines), v => v.Contains("NamePairParentAsync", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 082: the guard stays quiet on the sanctioned shape and on a comment naming a forbidden member (must-not-fire control)")]
    public void TheGuard_StaysQuietOnTheSanctionedShape()
    {
        var lines = new[]
        {
            "// never NameParentsAsync or IntermediateFilingColumns: they climb past the direct parent",
            "var columns = SecureRootInheritance.TypedFilingColumns[table];",
            "var answer = await SecureRootInheritance.NamePairParentAsync(entities, logger, table, id, type, types, ct);",
        };

        Assert.Empty(Violations(lines));
    }
}
