using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 082 (D-172 direct association only; notes/rule-set-scope-design.md 3.1; uac-r2 review item 8, round 2 item 082.1):
/// billing's <c>CoreRecordChain</c> reads the invoice's core record and its DIRECT parents only. It reuses uac-r2's
/// <c>TypedFilingColumns</c> and the pair-only <c>NamePairParentAsync</c>, and nothing that climbs past the direct parent or follows an
/// intermediate (#1630).
/// </summary>
/// <remarks>
/// <para><b>Enforced by:</b> this source scan of <c>CoreRecordChain.cs</c> (CI and the Stop hook), as an ALLOW-LIST (082a review K1):
/// (1) every <c>SecureRootInheritance.&lt;member&gt;</c> reference must be one of <see cref="AllowedMembers"/>, so a new walk member
/// uac-r2 adds later is refused until someone decides it here; (2) no reference to <c>CoreAncestorResolver</c> (the intermediate-root
/// topology); (3) no intermediate lookup column or intermediate table literal, taken live from
/// <c>SecureRootInheritance.IntermediateFilingColumns</c> (a hand-written read of an invoice or event is a second hop by another
/// route); (4) the by-name list <see cref="Forbidden"/> stays as a second net for an instance call or a member reached under another
/// qualifier; (5) the chain still resolves the pair through <c>NamePairParentAsync</c> and reuses <c>TypedFilingColumns</c>. Line
/// comments are ignored, so the file can name what it must not call. Controls: one planted line per class fires; the sanctioned shape
/// and the real file do not.</para>
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

    /// <summary>The <c>SecureRootInheritance</c> members <c>CoreRecordChain.cs</c> may reference: the typed-filing map, the BFF-managed-pair
    /// test and the pair's two columns, the pair-only rule, and the three core table names. Anything else fails.</summary>
    private static readonly HashSet<string> AllowedMembers = new(StringComparer.Ordinal)
    {
        "TypedFilingColumns", "PairNamesAParentOnItsOwn", "PairIdColumn", "PairTypeColumn", "NamePairParentAsync",
        "Matter", "Project", "WorkAssignment",
    };

    private static readonly string[] CoreTables =
        [SecureRootInheritance.Matter, SecureRootInheritance.Project, SecureRootInheritance.WorkAssignment];

    /// <summary>The intermediate lookup columns and intermediate tables of uac-r2's filing map, and the columns on an intermediate that
    /// name its matter or project, read live so the guard cannot drift from the definition (core table names are excluded: the chain
    /// names them through the constants).</summary>
    internal static IReadOnlyList<string> IntermediateLiterals() =>
        SecureRootInheritance.IntermediateFilingColumns.Values
            .SelectMany(columns => columns)
            .SelectMany(c => new[] { c.Column, c.Intermediate }
                .Concat(SecureRootInheritance.IntermediateParentColumns(c.Intermediate).Select(p => p.Column)))
            .Where(name => !CoreTables.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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

        foreach (Match match in Regex.Matches(code, @"\bSecureRootInheritance\s*\.\s*(?<member>\w+)"))
        {
            var member = match.Groups["member"].Value;
            if (!AllowedMembers.Contains(member))
            {
                found.Add($"line {SourceScan.LineOf(code, match.Index)}: SecureRootInheritance.{member} is not in the allow-list");
            }
        }

        foreach (Match match in Regex.Matches(code, @"\bCoreAncestorResolver\b"))
        {
            found.Add($"line {SourceScan.LineOf(code, match.Index)}: CoreAncestorResolver (the intermediate-root topology)");
        }

        foreach (var literal in IntermediateLiterals())
        {
            foreach (Match match in Regex.Matches(code, "\"" + Regex.Escape(literal) + "\""))
            {
                found.Add($"line {SourceScan.LineOf(code, match.Index)}: intermediate literal \"{literal}\"");
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

    [Fact(DisplayName = "Task 082a: the guard fires on a SecureRootInheritance member outside the allow-list that the by-name list does not know (must-fire control, K1)")]
    public void TheGuard_FiresOnAMemberOutsideTheAllowList()
    {
        const string member = "WalkEverythingAsync";
        var lines = File.ReadAllLines(ChainFile).Append($"var future = await SecureRootInheritance.{member}(entities, logger, table, id, ct);");

        Assert.DoesNotContain(member, Forbidden);
        Assert.Contains(Violations(lines), v => v.Contains(member, StringComparison.Ordinal) && v.Contains("allow-list", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 082a: the guard fires on a CoreAncestorResolver reference (must-fire control, K1)")]
    public void TheGuard_FiresOnACoreAncestorResolverReference()
    {
        var lines = File.ReadAllLines(ChainFile).Append("var roots = CoreAncestorResolver.IntermediateRootColumns[table];");

        Assert.Contains(Violations(lines), v => v.Contains("CoreAncestorResolver", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 082a: the guard fires on a hand-written intermediate lookup column (must-fire control, K1)")]
    public void TheGuard_FiresOnAnIntermediateColumnLiteral()
    {
        Assert.Contains("sprk_regardinginvoice", IntermediateLiterals());
        var lines = File.ReadAllLines(ChainFile).Append("query.Criteria.AddCondition(\"sprk_regardinginvoice\", ConditionOperator.NotNull);");

        Assert.Contains(Violations(lines), v => v.Contains("sprk_regardinginvoice", StringComparison.Ordinal));
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
            "// never CoreAncestorResolver or \"sprk_regardinginvoice\": they follow an intermediate",
            "var columns = SecureRootInheritance.TypedFilingColumns[table];",
            "var tables = new[] { SecureRootInheritance.Matter, SecureRootInheritance.Project, SecureRootInheritance.WorkAssignment };",
            "var answer = await SecureRootInheritance.NamePairParentAsync(entities, logger, table, id, type, types, ct);",
        };

        Assert.Empty(Violations(lines));
    }
}
