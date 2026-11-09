using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1, found in passing during task 045b: the regarding pair's type lookup is bound by the
/// table's navigation property, which the Web API treats case-sensitively. It is <c>sprk_RegardingRecordType</c> on every pair
/// table but <c>sprk_invoice</c>, whose navigation property is the lower-case <c>sprk_regardingrecordtype</c>
/// (<c>SpaarkeMaster/Other/Relationships/sprk_RecordType_Ref.xml</c>). The single answer is
/// <c>OwnedChildWrite.RegardingRecordTypeNavigationPropertyOf(table)</c>.
/// </summary>
/// <remarks>
/// <para><b>Signal and limits.</b> A source scan of <c>src/server/**</c> code text (line comments stripped; <see cref="SourceScan"/>).
/// It flags the literal key <c>"sprk_regardingrecordtype@odata.bind"</c> (exactly that spelling, quote to quote) in any file other
/// than <c>OwnedChildWrite.cs</c> and the files whose name carries <c>Invoice</c> (the one table whose spelling it is). Not seen:
/// the key built at run time (<c>$"{name}@odata.bind"</c>, a concatenation), a string that escapes its quotes
/// (<c>\"sprk_regardingrecordtype@odata.bind\"</c>, i.e. a description or hint text), and client code: the browser's body to the
/// BFF's child-record route is matched case-insensitively against the table's metadata (<c>DataverseWriteItemMapper</c>), so a
/// lower-case key there is the route's documented contract, not a Web API write. Enforced by: this guard (ArchTests in CI and the
/// Stop hook). No baseline: no server file spells it today.</para>
/// <para><b>Controls.</b> <see cref="Control_ALowerCasePairTypeBind_IsFound"/> (must fire) and
/// <see cref="Control_TheNavigationPropertySpelling_AndAnEscapedHint_AreNotFound"/> (must not fire) run the same detector over synthetic
/// source.</para>
/// </remarks>
public class PairTypeBindSpellingGuardTests
{
    private static readonly Regex LowerCasePairTypeBind = new(
        @"(?<!\\)""sprk_regardingrecordtype@odata\.bind""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string Owner = "src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs";

    internal static IReadOnlyList<string> Scan(string source)
    {
        var code = SourceScan.CodeText(source.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'));
        return LowerCasePairTypeBind.Matches(code)
            .Select(m => $"line {SourceScan.LineOf(code, m.Index)}: the lower-case key {m.Value}")
            .ToList();
    }

    private static bool IsExempt(string relative) =>
        string.Equals(relative, Owner, StringComparison.Ordinal)
        || Path.GetFileName(relative).Contains("Invoice", StringComparison.OrdinalIgnoreCase);

    [Fact(DisplayName = "045b found in passing: no server code hard-codes the lower-case pair-type bind outside OwnedChildWrite and the invoice files")]
    public void NoServerFile_HardCodesTheLowerCasePairTypeBind()
    {
        var problems = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            if (IsExempt(relative))
                continue;
            problems.AddRange(Scan(File.ReadAllText(file)).Select(p => $"{relative}: {p}"));
        }

        Assert.True(problems.Count == 0,
            "The pair type's navigation property is sprk_RegardingRecordType on every pair table except sprk_invoice (case-sensitive "
            + "in the Web API). Use OwnedChildWrite.RegardingRecordTypeNavigationPropertyOf(table) instead of a hard-coded "
            + "lower-case key:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "045b found in passing, control (must fire): a lower-case pair-type bind is found")]
    public void Control_ALowerCasePairTypeBind_IsFound()
    {
        Assert.Single(Scan("""payload["sprk_regardingrecordtype@odata.bind"] = $"/sprk_recordtype_refs({t:D})";"""));
        Assert.Single(Scan("""writer.WriteString("sprk_regardingrecordtype@odata.bind", path);"""));
    }

    [Fact(DisplayName = "045b found in passing, control (must not fire): the navigation property's spelling, a run-time key, a comment and an escaped hint are not found")]
    public void Control_TheNavigationPropertySpelling_AndAnEscapedHint_AreNotFound()
    {
        Assert.Empty(Scan("""payload["sprk_RegardingRecordType@odata.bind"] = path;"""));
        Assert.Empty(Scan("""payload[$"{OwnedChildWrite.RegardingRecordTypeNavigationPropertyOf(Table)}@odata.bind"] = path;"""));
        Assert.Empty(Scan("""// payload["sprk_regardingrecordtype@odata.bind"] = path;"""));
        Assert.Empty(Scan("""var hint = "{\"sprk_regardingrecordtype@odata.bind\": \"/sprk_recordtype_refs(<id>)\"}";"""));
        Assert.True(IsExempt("src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs"));
        Assert.True(IsExempt("src/server/api/Sprk.Bff.Api/Api/Finance/InvoiceSubmissionEndpoints.cs"));
        Assert.False(IsExempt("src/server/api/Sprk.Bff.Api/Services/Signals/Actions/DecisionActionExecutors.cs"));
    }
}
