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
    private static readonly Regex CountedStatusLiteral = new(@"\b10000000[123]\b", RegexOptions.Compiled);
    private static readonly Regex PercentFormula = new(@"\*\s*100m?\b", RegexOptions.Compiled);

    internal static IReadOnlyList<string> Violations(string code)
    {
        var found = new List<string>();
        if (!code.Contains("FinanceBaseValues.", StringComparison.Ordinal)) found.Add("does not call FinanceBaseValues");
        if (CountedStatusLiteral.IsMatch(code)) found.Add("spells a counted invoice status (100000001-3) instead of FinanceBaseValues.CountedInvoiceStatuses");
        if (PercentFormula.IsMatch(code)) found.Add("multiplies by 100 itself instead of FinanceBaseValues.UtilizationPercent");
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
        var copy = "var u = spend / budget * 100m; var s = new[] { 100000001, 100000002, 100000003 };";
        Assert.Equal(3, Violations(copy).Count);
    }

    [Fact(DisplayName = "Task 025b control: the detector stays quiet on the shared call")]
    public void TheDetector_StaysQuietOnTheSharedCall() =>
        Assert.Empty(Violations("var u = FinanceBaseValues.UtilizationPercent(spend, budget);"));
}
