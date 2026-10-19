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

    internal static IReadOnlyList<string> Violations(string code)
    {
        var found = new List<string>();
        if (!code.Contains("FinanceBaseValues.", StringComparison.Ordinal)) found.Add("does not call FinanceBaseValues");
        if (CountedStatusLiteral.IsMatch(code)) found.Add("spells an invoice statuscode (659490000-659490004) instead of FinanceBaseValues.CountedInvoiceStatuses");
        if (CountedStatusConstant.IsMatch(code)) found.Add("names an InvoiceStatusCodes value instead of using FinanceBaseValues.CountedInvoiceStatuses");
        if (PercentFormula.IsMatch(code)) found.Add("multiplies by 100 itself instead of FinanceBaseValues.UtilizationPercent");
        if (HeldPredicateSpelling.IsMatch(code)) found.Add("spells the held-submission predicate instead of FinanceBaseValues.HeldSubmissionConditions");
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

    [Fact(DisplayName = "Task 025b control: the detector stays quiet on the shared call")]
    public void TheDetector_StaysQuietOnTheSharedCall() =>
        Assert.Empty(Violations("var u = FinanceBaseValues.UtilizationPercent(spend, budget);"));

    [Fact(DisplayName = "ebilling-r1 015 control: the detector stays quiet on the shared status set, and on a name that only resembles the constants")]
    public void TheDetector_StaysQuietOnTheSharedStatusSet() =>
        Assert.Empty(Violations("var s = FinanceBaseValues.CountedInvoiceStatuses.Cast<object>().ToArray(); var t = MyInvoiceStatusCodes.X;"));
}
