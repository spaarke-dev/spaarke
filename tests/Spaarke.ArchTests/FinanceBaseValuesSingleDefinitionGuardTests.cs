using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ontology-platform-r1 task 025b (owner decision D-122): a matter's or project's spend, budget and utilization have ONE definition,
/// <c>FinanceBaseValues</c>, used by the rollup that stores them (<c>FinanceRollupService</c>) and the Signal evaluator that judges a
/// Threshold rule from the base rows (<c>SignalEvaluator</c>). If either grew its own status set or its own percent formula, a Signal
/// would state a number the matter form does not show.
/// </summary>
/// <remarks>
/// Enforced by: this source scan (CI and the Stop hook). The rollup's compute needs a live ServiceClient, so no unit test can observe
/// which formula it calls; a scan of its source is the strongest mechanism that works. Controls: <see cref="TheDetector_FiresOnAPrivateCopy"/>
/// and <see cref="TheDetector_StaysQuietOnTheSharedCall"/> prove the rule can fail and can pass.
/// </remarks>
public class FinanceBaseValuesSingleDefinitionGuardTests
{
    // ebilling-r1 task 015 (D-41, D-103): the counted set is statuscode Approved, Sent to AP, Paid (In Review is not counted). The
    // publisher-range values 659490000..659490004 are what a private copy would spell (a bare 1 is undetectable and is never spelled
    // alone). A private list built from the named constants (InvoiceStatusCodes.X) is the same copy by another spelling, so it is
    // flagged too: both consumers take the set from FinanceBaseValues.CountedInvoiceStatuses and name no status themselves.
    private static readonly Regex CountedStatusLiteral = new(@"\b65949000[0-4]\b", RegexOptions.Compiled);
    private static readonly Regex CountedStatusConstant = new(@"\bInvoiceStatusCodes\.", RegexOptions.Compiled);
    private static readonly Regex PercentFormula = new(@"\*\s*100m?\b", RegexOptions.Compiled);
    // ebilling-r1 049 (D-102, D-104): the held predicate (a blocked submission's status and reason code) is defined once, in
    // FinanceBaseValues.HeldSubmissionConditions. A consumer that spells the submission columns or the reason code has its own copy.
    private static readonly Regex HeldPredicateSpelling = new(@"sprk_processingstatus|sprk_outcomereasoncode|ebilling\.submission\.blocked", RegexOptions.Compiled);

    // ebilling-r1 D-130: the spend AMOUNT rule (approved total, else billed total) is defined once, in FinanceBaseValues.SpendAmount /
    // SumSpend / SpendAmountColumns. A consumer that names either amount column has its own copy (and would drift, e.g. back to the
    // billed total alone).
    private static readonly Regex AmountRuleSpelling = new(
        @"sprk_approvedtotal|sprk_totalamount|InvoiceApprovedTotal|InvoiceTotalAmount|Invoice_TotalAmount", RegexOptions.Compiled);

    internal static IReadOnlyList<string> Violations(string code)
    {
        var found = new List<string>();
        if (!code.Contains("FinanceBaseValues.", StringComparison.Ordinal)) found.Add("does not call FinanceBaseValues");
        if (CountedStatusLiteral.IsMatch(code)) found.Add("spells an invoice statuscode (659490000-659490004) instead of FinanceBaseValues.CountedInvoiceStatuses");
        if (CountedStatusConstant.IsMatch(code)) found.Add("names an InvoiceStatusCodes value instead of using FinanceBaseValues.CountedInvoiceStatuses");
        if (PercentFormula.IsMatch(code)) found.Add("multiplies by 100 itself instead of FinanceBaseValues.UtilizationPercent");
        if (HeldPredicateSpelling.IsMatch(code)) found.Add("spells the held-submission predicate instead of FinanceBaseValues.HeldSubmissionConditions");
        if (AmountRuleSpelling.IsMatch(code)) found.Add("names an invoice amount column instead of FinanceBaseValues.SpendAmount / SumSpend / SpendAmountColumns");
        return found;
    }

    private static string CodeOf(string relativePath)
    {
        var path = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", relativePath.Replace('/', Path.DirectorySeparatorChar));
        return string.Join('\n', File.ReadAllLines(path)
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith("*", StringComparison.Ordinal)));
    }

    [Theory(DisplayName = "Task 025b: the rollup and the Signal evaluator share ONE definition of spend, budget and utilization")]
    [InlineData("Services/Finance/FinanceRollupService.cs")]
    [InlineData("Services/Signals/SignalEvaluator.cs")]
    public void RollupAndEvaluator_UseFinanceBaseValues_AndKeepNoPrivateCopy(string relativePath) =>
        Assert.True(Violations(CodeOf(relativePath)).Count == 0,
            relativePath + ": " + string.Join("; ", Violations(CodeOf(relativePath))));

    // The AI tool handler totals spend from the rollup's own query, so it follows the same amount rule (D-130). It keeps its own
    // utilization arithmetic (not in scope here), so only the amount rule is guarded for it.
    [Fact(DisplayName = "ebilling-r1 D-130: the financial calculation tool handler takes the spend amount from FinanceBaseValues, with no copy of the rule")]
    public void TheToolHandler_UsesTheSharedAmountRule()
    {
        var code = CodeOf("Services/Finance/Tools/FinancialCalculationToolHandler.cs");
        Assert.DoesNotMatch(AmountRuleSpelling, code);
        Assert.Contains("FinanceBaseValues.SpendAmount(", code, StringComparison.Ordinal);
    }

    // ebilling-r1 D-130 (ontology-r1 review): widened from the three named consumers to ALL of src/server/**. A new reader that names an
    // invoice amount column can be a second copy of the spend rule (and drift back to the billed total). The files below already name a
    // column and are NOT spend sums (reasons from projects/ebilling-r1/notes/049-spend-approved-total.md "Paths left alone"); FinanceBaseValues
    // itself is the definition. This is a RATCHET: a file not listed that names a column fails; a listed file that no longer does fails, so
    // the baseline only shrinks. Comment lines are ignored (as in CodeOf).
    private static readonly IReadOnlyDictionary<string, string> AmountColumnBaseline = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Infrastructure/DI/ExternalAccessModule.cs"] = "column allow-list for the external SPA's invoice detail; no sum",
        ["Services/Finance/FinanceSummaryService.cs"] = "recent-invoices listing: each invoice's own billed total, not a spend sum (DTO contract)",
        ["Services/Finance/InvoiceReviewService.cs"] = "holds the sprk_invoice column-name constants the submission persister writes (050a: it no longer writes an invoice); no sum",
        ["Services/Finance/InvoiceTotalsService.cs"] = "writes sprk_totalamount / sprk_approvedtotal (the source of the amount)",
        ["Services/Insights/LiveFacts/InvoiceLiveFactResolver.cs"] = "per-invoice fact documented as the billed total; not an aggregate",
    };

    private static string RelativeToBff(string file) =>
        Path.GetRelativePath(Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api"), file).Replace('\\', '/');

    internal static IReadOnlyList<string> FilesNamingAnAmountColumn(IEnumerable<(string Path, string Code)> sources) =>
        sources.Where(s => AmountRuleSpelling.IsMatch(StripComments(s.Code))).Select(s => s.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();

    private static string StripComments(string code) => string.Join('\n', code.Split('\n')
        .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith("*", StringComparison.Ordinal)));

    [Fact(DisplayName = "ebilling-r1 D-130: no source under src/server/** outside FinanceBaseValues and the baselined non-spend files names an invoice amount column")]
    public void NoServerSource_OutsideTheBaseline_NamesAnInvoiceAmountColumn()
    {
        var named = FilesNamingAnAmountColumn(SourceScan.ServerSourceFiles().Select(f => (RelativeToBff(f), File.ReadAllText(f))))
            .Where(p => p != "Services/Finance/FinanceBaseValues.cs").ToList();

        Assert.True(named.Except(AmountColumnBaseline.Keys).Count() == 0,
            "names an invoice amount column (a possible second copy of the D-130 spend rule); use FinanceBaseValues.SpendAmount / SumSpend / SpendAmountColumns, "
            + "or, if it is not a spend sum, add it to AmountColumnBaseline with the reason: " + string.Join(", ", named.Except(AmountColumnBaseline.Keys)));
        Assert.True(AmountColumnBaseline.Keys.Except(named).Count() == 0,
            "baseline entries that no longer name an amount column (remove them, the ratchet only shrinks): " + string.Join(", ", AmountColumnBaseline.Keys.Except(named)));
    }

    [Fact(DisplayName = "ebilling-r1 D-130 control: the widened scan fires on a new file that names an amount column, and stays quiet on a comment and on the shared call")]
    public void TheWidenedScan_FiresOnANewFile_AndStaysQuietOnACommentAndTheSharedCall()
    {
        var found = FilesNamingAnAmountColumn(new[]
        {
            ("Services/Reports/SpendReport.cs", "var a = row.GetAttributeValue<Money>(\"sprk_approvedtotal\");"),
            ("Services/Reports/Commented.cs", "// sprk_approvedtotal is read by FinanceBaseValues\n/// <c>sprk_totalamount</c>\nvar t = FinanceBaseValues.SumSpend(rows);"),
        });

        Assert.Equal(new[] { "Services/Reports/SpendReport.cs" }, found);
    }

    [Fact(DisplayName = "Task 025b control: the detector fires on a private copy of the status set and the formula")]
    public void TheDetector_FiresOnAPrivateCopy()
    {
        var copy = "var u = spend / budget * 100m; var s = new[] { 1, 659490000, 659490001, 659490002 };";
        Assert.Equal(3, Violations(copy).Count);
    }

    [Fact(DisplayName = "ebilling-r1 015 control: the detector fires on a private list of InvoiceStatusCodes constants")]
    public void TheDetector_FiresOnAPrivateListOfNamedStatusConstants()
    {
        var copy = "var s = new[] { InvoiceStatusCodes.Approved, InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.Paid }; var c = FinanceBaseValues.X;";
        Assert.Equal(new[] { "names an InvoiceStatusCodes value instead of using FinanceBaseValues.CountedInvoiceStatuses" }, Violations(copy));
    }

    [Fact(DisplayName = "ebilling-r1 049 control: the detector fires on a private copy of the held predicate")]
    public void TheDetector_FiresOnAPrivateHeldPredicate()
    {
        var copy = "var c = FinanceBaseValues.X; q.AddCondition(\"sprk_outcomereasoncode\", ConditionOperator.Equal, \"ebilling.submission.blocked\");";
        Assert.Equal(new[] { "spells the held-submission predicate instead of FinanceBaseValues.HeldSubmissionConditions" }, Violations(copy));
    }

    [Fact(DisplayName = "ebilling-r1 049 control: the detector stays quiet on the shared held predicate")]
    public void TheDetector_StaysQuietOnTheSharedHeldPredicate() =>
        Assert.Empty(Violations("foreach (var (column, value) in FinanceBaseValues.HeldSubmissionConditions) { link.AddCondition(column, ConditionOperator.Equal, value); }"));

    [Fact(DisplayName = "ebilling-r1 D-130 control: the detector fires on a private copy of the spend amount rule")]
    public void TheDetector_FiresOnAPrivateAmountRule()
    {
        var copy = "var c = FinanceBaseValues.X; var a = e.GetAttributeValue<Money>(\"sprk_approvedtotal\")?.Value ?? e.GetAttributeValue<Money>(Invoice_TotalAmount)?.Value ?? 0m;";
        Assert.Equal(new[] { "names an invoice amount column instead of FinanceBaseValues.SpendAmount / SumSpend / SpendAmountColumns" }, Violations(copy));
    }

    [Fact(DisplayName = "ebilling-r1 D-130 control: the detector stays quiet on the shared amount rule")]
    public void TheDetector_StaysQuietOnTheSharedAmountRule() =>
        Assert.Empty(Violations("var total = FinanceBaseValues.SumSpend(invoices); var one = FinanceBaseValues.SpendAmount(invoice); var cols = FinanceBaseValues.SpendAmountColumns;"));

    [Fact(DisplayName = "Task 025b control: the detector stays quiet on the shared call")]
    public void TheDetector_StaysQuietOnTheSharedCall() =>
        Assert.Empty(Violations("var u = FinanceBaseValues.UtilizationPercent(spend, budget);"));

    [Fact(DisplayName = "ebilling-r1 015 control: the detector stays quiet on the shared status set, and on a name that only resembles the constants")]
    public void TheDetector_StaysQuietOnTheSharedStatusSet() =>
        Assert.Empty(Violations("var s = FinanceBaseValues.CountedInvoiceStatuses.Cast<object>().ToArray(); var t = MyInvoiceStatusCodes.X;"));
}
