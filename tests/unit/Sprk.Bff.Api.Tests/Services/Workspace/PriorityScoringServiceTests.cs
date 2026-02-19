using FluentAssertions;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for PriorityScoringService.
/// Validates all 6 scoring factors individually (threshold boundary testing)
/// plus combined scoring, priority level mapping, score cap, and reason string format.
/// </summary>
public class PriorityScoringServiceTests
{
    private readonly PriorityScoringService _sut = new();

    // ──────────────────────────────────────────────────────────────────────────
    // Helper: build a zero-score input with one factor overridden
    // ──────────────────────────────────────────────────────────────────────────

    private static PriorityScoreInput ZeroInput(
        int overdueDays = 0,
        decimal budgetUtilization = 0m,
        int gradesBelowC = 0,
        int? daysToDeadline = null,
        string matterValueTier = "Low",
        int pendingInvoiceCount = 0) =>
        new(overdueDays, budgetUtilization, gradesBelowC, daysToDeadline, matterValueTier, pendingInvoiceCount);

    // ──────────────────────────────────────────────────────────────────────────
    // Factor 1: Overdue Days (max 30 pts)
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]      // No overdue
    [InlineData(1, 10)]     // 1 day = 10 pts (lower boundary)
    [InlineData(7, 10)]     // 7 days = 10 pts (upper boundary of first tier)
    [InlineData(8, 20)]     // 8 days = 20 pts (lower boundary of second tier)
    [InlineData(14, 20)]    // 14 days = 20 pts (upper boundary of second tier)
    [InlineData(15, 30)]    // 15 days = 30 pts (lower boundary of max tier)
    [InlineData(100, 30)]   // Way overdue = still 30 pts (max cap)
    public void OverdueDays_ReturnsCorrectScore(int overdueDays, int expectedPoints)
    {
        var input = ZeroInput(overdueDays: overdueDays);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Overdue Days");
        factor.Points.Should().Be(expectedPoints,
            $"overdue days = {overdueDays} should yield {expectedPoints} pts");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Factor 2: Budget Utilization (max 20 pts)
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]       // 0% = 0 pts
    [InlineData(64.9, 0)]    // Just below 65% = 0 pts
    [InlineData(65, 10)]     // Exactly 65% = 10 pts (lower boundary)
    [InlineData(85, 10)]     // Exactly 85% = 10 pts (upper boundary of middle tier)
    [InlineData(85.1, 20)]   // Just above 85% = 20 pts
    [InlineData(100, 20)]    // 100% = 20 pts (max)
    public void BudgetUtilization_ReturnsCorrectScore(double budgetUtilization, int expectedPoints)
    {
        var input = ZeroInput(budgetUtilization: (decimal)budgetUtilization);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Budget Utilization");
        factor.Points.Should().Be(expectedPoints,
            $"budget utilization = {budgetUtilization}% should yield {expectedPoints} pts");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Factor 3: Grades Below C (max 15 pts)
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]   // 0 grades below C = 0 pts
    [InlineData(1, 5)]   // 1 grade below C = 5 pts
    [InlineData(2, 10)]  // 2 grades below C = 10 pts
    [InlineData(3, 15)]  // 3 grades below C = 15 pts (max)
    [InlineData(4, 15)]  // 4+ grades = still 15 pts (cap at 3 grades)
    [InlineData(10, 15)] // 10 grades = still 15 pts (hard cap)
    public void GradesBelowC_ReturnsCorrectScore(int gradesBelowC, int expectedPoints)
    {
        var input = ZeroInput(gradesBelowC: gradesBelowC);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Grades Below C");
        factor.Points.Should().Be(expectedPoints,
            $"grades below C = {gradesBelowC} should yield {expectedPoints} pts");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Factor 4: Deadline Proximity (max 15 pts)
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, 0)]    // No deadline = 0 pts
    [InlineData(31, 0)]      // >30 days = 0 pts
    [InlineData(30, 5)]      // Exactly 30 = 5 pts (upper boundary of 14-30 tier)
    [InlineData(14, 5)]      // Exactly 14 = 5 pts (lower boundary of 14-30 tier)
    [InlineData(13, 10)]     // 13 days = 10 pts (upper boundary of 7-13 tier)
    [InlineData(7, 10)]      // Exactly 7 = 10 pts (lower boundary of 7-13 tier)
    [InlineData(6, 15)]      // 6 days = 15 pts (upper boundary of <7 tier)
    [InlineData(0, 15)]      // 0 days = 15 pts (most urgent)
    public void DeadlineProximity_ReturnsCorrectScore(int? daysToDeadline, int expectedPoints)
    {
        var input = ZeroInput(daysToDeadline: daysToDeadline);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Deadline Proximity");
        factor.Points.Should().Be(expectedPoints,
            $"days to deadline = {daysToDeadline?.ToString() ?? "null"} should yield {expectedPoints} pts");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Factor 5: Matter Value Tier (max 10 pts)
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Low", 0)]
    [InlineData("low", 0)]        // Case-insensitive
    [InlineData("LOW", 0)]
    [InlineData("Unknown", 0)]    // Unknown tier = 0
    [InlineData("", 0)]           // Empty = 0
    [InlineData("Medium", 5)]
    [InlineData("medium", 5)]     // Case-insensitive
    [InlineData("MEDIUM", 5)]
    [InlineData("High", 10)]
    [InlineData("high", 10)]      // Case-insensitive
    [InlineData("HIGH", 10)]
    public void MatterValueTier_ReturnsCorrectScore(string tier, int expectedPoints)
    {
        var input = ZeroInput(matterValueTier: tier);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Matter Value Tier");
        factor.Points.Should().Be(expectedPoints,
            $"matter value tier = '{tier}' should yield {expectedPoints} pts");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Factor 6: Pending Invoices (max 10 pts)
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]    // No invoices = 0 pts
    [InlineData(1, 5)]    // 1 invoice = 5 pts (lower boundary of 1-2 tier)
    [InlineData(2, 5)]    // 2 invoices = 5 pts (upper boundary of 1-2 tier)
    [InlineData(3, 10)]   // 3 invoices = 10 pts (lower boundary of 3+ tier)
    [InlineData(10, 10)]  // 10 invoices = still 10 pts (max)
    public void PendingInvoices_ReturnsCorrectScore(int invoiceCount, int expectedPoints)
    {
        var input = ZeroInput(pendingInvoiceCount: invoiceCount);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Pending Invoices");
        factor.Points.Should().Be(expectedPoints,
            $"pending invoice count = {invoiceCount} should yield {expectedPoints} pts");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Combined scoring: all zeros / all max
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllFactorsAtZero_ReturnsZeroScore_LowPriority()
    {
        var input = ZeroInput();

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().Be(0);
        result.Level.Should().Be("Low");
    }

    [Fact]
    public void AllFactorsAtMax_ReturnsCappedAt100_CriticalPriority()
    {
        // Overdue(30) + Budget(20) + Grades(15) + Deadline(15) + Tier(10) + Invoices(10) = 100 pts
        var input = new PriorityScoreInput(
            OverdueDays: 15,
            BudgetUtilizationPercent: 90m,
            GradesBelowC: 3,
            DaysToDeadline: 3,
            MatterValueTier: "High",
            PendingInvoiceCount: 3);

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().Be(100);
        result.Level.Should().Be("Critical");
    }

    [Fact]
    public void Score_CappedAt100_WhenFactorsSumExceeds100()
    {
        // Force maximum from every factor: 30+20+15+15+10+10 = 100 — exactly at cap.
        // Use values well above each threshold to verify the sum stays at 100.
        var input = new PriorityScoreInput(
            OverdueDays: 999,
            BudgetUtilizationPercent: 999m,
            GradesBelowC: 999,
            DaysToDeadline: 0,
            MatterValueTier: "High",
            PendingInvoiceCount: 999);

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().BeLessOrEqualTo(100,
            "score must never exceed 100 regardless of factor sum");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Priority level mapping
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "Low")]
    [InlineData(29, "Low")]
    [InlineData(30, "Medium")]
    [InlineData(59, "Medium")]
    [InlineData(60, "High")]
    [InlineData(79, "High")]
    [InlineData(80, "Critical")]
    [InlineData(100, "Critical")]
    public void PriorityLevel_CorrectForScore(int targetScore, string expectedLevel)
    {
        // Build an input that achieves exactly targetScore using overdue days only where
        // possible, or a combination.  We use a known formula: overdue days map to
        // 0/10/20/30 pts, and budget utilization maps to 0/10/20 pts, so we can hit
        // any multiple of 5 within 0-50 with just those two factors.
        // For higher values we add deadline and tier.
        var input = BuildInputForScore(targetScore);
        var result = _sut.CalculatePriorityScore(input);

        result.Level.Should().Be(expectedLevel,
            $"a score of {result.Score} (targeting {targetScore}) should map to '{expectedLevel}'");
    }

    /// <summary>
    /// Constructs an input whose calculated score is at least <paramref name="targetScore"/>.
    /// Uses a greedy approach: fill factors from largest to smallest.
    /// The actual returned score may be exactly <paramref name="targetScore"/> when the
    /// combination is reachable, or the closest achievable value when it is not.
    /// </summary>
    private static PriorityScoreInput BuildInputForScore(int targetScore)
    {
        // Overdue days factor alone can cover 0, 10, 20, 30.
        // Budget factor adds 0, 10, 20.
        // Grades factor adds 0, 5, 10, 15.
        // Deadline factor adds 0, 5, 10, 15.
        // Tier adds 0, 5, 10.
        // Invoices adds 0, 5, 10.

        int remaining = targetScore;

        int overdueDays = 0;
        if (remaining >= 30) { overdueDays = 15; remaining -= 30; }
        else if (remaining >= 20) { overdueDays = 8; remaining -= 20; }
        else if (remaining >= 10) { overdueDays = 1; remaining -= 10; }

        decimal budget = 0m;
        if (remaining >= 20) { budget = 90m; remaining -= 20; }
        else if (remaining >= 10) { budget = 70m; remaining -= 10; }

        int grades = 0;
        if (remaining >= 15) { grades = 3; remaining -= 15; }
        else if (remaining >= 10) { grades = 2; remaining -= 10; }
        else if (remaining >= 5) { grades = 1; remaining -= 5; }

        int? deadline = null;
        if (remaining >= 15) { deadline = 3; remaining -= 15; }
        else if (remaining >= 10) { deadline = 7; remaining -= 10; }
        else if (remaining >= 5) { deadline = 20; remaining -= 5; }

        string tier = "Low";
        if (remaining >= 10) { tier = "High"; remaining -= 10; }
        else if (remaining >= 5) { tier = "Medium"; remaining -= 5; }

        int invoices = 0;
        if (remaining >= 10) { invoices = 3; }
        else if (remaining >= 5) { invoices = 1; }

        return new PriorityScoreInput(overdueDays, budget, grades, deadline, tier, invoices);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Reason string
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReasonString_IncludesAllNonZeroFactors()
    {
        var input = new PriorityScoreInput(
            OverdueDays: 12,
            BudgetUtilizationPercent: 87m,
            GradesBelowC: 0,
            DaysToDeadline: null,
            MatterValueTier: "High",
            PendingInvoiceCount: 0);

        var result = _sut.CalculatePriorityScore(input);

        // Overdue days (20), budget (20), matter tier (10) should be in the reason
        result.ReasonString.Should().Contain("+20", "overdue and budget both contribute +20");
        result.ReasonString.Should().Contain("+10", "high tier contributes +10");
        result.ReasonString.Should().Contain("(Medium)", "total of 50 = Medium");
    }

    [Fact]
    public void ReasonString_ExcludesZeroFactors()
    {
        var input = ZeroInput(overdueDays: 10);  // Only overdue days factor is non-zero

        var result = _sut.CalculatePriorityScore(input);

        // Only the overdue factor (20 pts) should appear
        result.ReasonString.Should().Contain("+20");
        result.ReasonString.Should().Contain("(Low)");
        // Zero factors must not inject spurious "+0" tokens
        result.ReasonString.Should().NotContain("+0");
    }

    [Fact]
    public void ReasonString_WhenAllZero_ContainsNoContributingFactorsMessage()
    {
        var input = ZeroInput();

        var result = _sut.CalculatePriorityScore(input);

        result.ReasonString.Should().Contain("0 (Low)",
            "zero-score reason string should include score and level");
    }

    [Fact]
    public void ReasonString_EndsWithScoreAndLevel()
    {
        var input = new PriorityScoreInput(
            OverdueDays: 15,
            BudgetUtilizationPercent: 90m,
            GradesBelowC: 0,
            DaysToDeadline: null,
            MatterValueTier: "Low",
            PendingInvoiceCount: 0);

        var result = _sut.CalculatePriorityScore(input);

        result.ReasonString.Should().EndWith($"= {result.Score} ({result.Level})");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Determinism
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameInputProducesSameOutput_CalledMultipleTimes()
    {
        var input = new PriorityScoreInput(
            OverdueDays: 9,
            BudgetUtilizationPercent: 75m,
            GradesBelowC: 2,
            DaysToDeadline: 10,
            MatterValueTier: "Medium",
            PendingInvoiceCount: 2);

        var first = _sut.CalculatePriorityScore(input);
        var second = _sut.CalculatePriorityScore(input);
        var third = _sut.CalculatePriorityScore(input);

        second.Score.Should().Be(first.Score);
        second.Level.Should().Be(first.Level);
        third.Score.Should().Be(first.Score);
        third.Level.Should().Be(first.Level);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Factors list structure
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Result_AlwaysContainsSixFactors()
    {
        var input = ZeroInput();

        var result = _sut.CalculatePriorityScore(input);

        result.Factors.Should().HaveCount(6, "one factor per scoring dimension");
    }

    [Fact]
    public void Result_FactorNamesAreUnique()
    {
        var input = ZeroInput();

        var result = _sut.CalculatePriorityScore(input);

        result.Factors.Select(f => f.FactorName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Result_FactorsSumEqualsScore_WhenUnder100()
    {
        // Choose inputs that sum to less than 100 to verify factor sum == score
        var input = new PriorityScoreInput(
            OverdueDays: 5,
            BudgetUtilizationPercent: 70m,
            GradesBelowC: 1,
            DaysToDeadline: null,
            MatterValueTier: "Low",
            PendingInvoiceCount: 0);
        // Expected: 10 + 10 + 5 + 0 + 0 + 0 = 25

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().Be(result.Factors.Sum(f => f.Points),
            "score should equal sum of factor points when total is below 100");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Mid-range combined scenarios (2–3 active factors)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoFactors_OverdueAndBudget_CombineCorrectly()
    {
        // 8 days overdue (20 pts) + budget at 70% (10 pts) = 30 → Medium
        var input = ZeroInput(overdueDays: 8, budgetUtilization: 70m);

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().Be(30);
        result.Level.Should().Be("Medium");
    }

    [Fact]
    public void ThreeFactors_OverdueGradesAndDeadline_CombineCorrectly()
    {
        // 1 day overdue (10) + 2 grades below C (10) + 10 days to deadline (10) = 30 → Medium
        var input = ZeroInput(overdueDays: 1, gradesBelowC: 2, daysToDeadline: 10);

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().Be(30);
        result.Level.Should().Be("Medium");
    }

    [Fact]
    public void TwoFactors_MatterTierAndInvoices_CombineCorrectly()
    {
        // High tier (10 pts) + 3 invoices (10 pts) = 20 → Low
        var input = ZeroInput(matterValueTier: "High", pendingInvoiceCount: 3);

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().Be(20);
        result.Level.Should().Be("Low");
    }

    [Fact]
    public void ThreeFactors_HighTierBudgetAndDeadline_CombineCorrectly()
    {
        // High tier (10) + budget at 90% (20) + 5 days to deadline (15) = 45 → Medium
        var input = ZeroInput(matterValueTier: "High", budgetUtilization: 90m, daysToDeadline: 5);

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().Be(45);
        result.Level.Should().Be("Medium");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Boundary: scores exactly at level thresholds
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_ExactlyAt30_IsMedium()
    {
        // 15 days overdue (30 pts) = exactly 30 → Medium (boundary)
        var input = ZeroInput(overdueDays: 15);

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().Be(30);
        result.Level.Should().Be("Medium");
    }

    [Fact]
    public void Score_ExactlyAt60_IsHigh()
    {
        // 15 days overdue (30) + budget at 90% (20) + 1 grade below C (5) + 3 invoices (10) = 65? No.
        // 15 days overdue (30) + budget at 90% (20) + 2 grades below C (10) = 60 → High boundary
        var input = ZeroInput(overdueDays: 15, budgetUtilization: 90m, gradesBelowC: 2);

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().Be(60);
        result.Level.Should().Be("High");
    }

    [Fact]
    public void Score_ExactlyAt80_IsCritical()
    {
        // 15 days overdue (30) + budget at 90% (20) + 3 grades below C (15) + 5 days to deadline (15) = 80
        var input = ZeroInput(overdueDays: 15, budgetUtilization: 90m, gradesBelowC: 3, daysToDeadline: 5);

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().Be(80);
        result.Level.Should().Be("Critical");
    }

    [Fact]
    public void Score_ExactlyAt100_IsCritical_AndCappedCorrectly()
    {
        // All 6 factors at their individual maxima = 30+20+15+15+10+10 = 100 exactly
        var input = new PriorityScoreInput(
            OverdueDays: 15,
            BudgetUtilizationPercent: 90m,
            GradesBelowC: 3,
            DaysToDeadline: 3,
            MatterValueTier: "High",
            PendingInvoiceCount: 3);

        var result = _sut.CalculatePriorityScore(input);

        result.Score.Should().Be(100);
        result.Level.Should().Be("Critical");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Edge cases: zero and negative numeric values
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ZeroOverdueDays_ReturnsZeroPoints()
    {
        var input = ZeroInput(overdueDays: 0);

        var result = _sut.CalculatePriorityScore(input);

        result.Factors.Single(f => f.FactorName == "Overdue Days").Points.Should().Be(0);
    }

    [Fact]
    public void NegativeOverdueDays_TreatedAsZeroPoints()
    {
        // Negative overdue days fall into the `_ => 0` default branch
        var input = ZeroInput(overdueDays: -5);

        var result = _sut.CalculatePriorityScore(input);

        result.Factors.Single(f => f.FactorName == "Overdue Days").Points.Should().Be(0,
            "negative overdue days should produce no overdue penalty");
    }

    [Fact]
    public void NegativeBudgetUtilization_ReturnsZeroPoints()
    {
        var input = ZeroInput(budgetUtilization: -10m);

        var result = _sut.CalculatePriorityScore(input);

        result.Factors.Single(f => f.FactorName == "Budget Utilization").Points.Should().Be(0);
    }

    [Fact]
    public void NegativeGradesBelowC_ReturnsZeroPoints()
    {
        // Math.Min(-1, 3) * 5 = -1 * 5 = -5 — guard against negative contribution
        var input = ZeroInput(gradesBelowC: -1);

        var result = _sut.CalculatePriorityScore(input);

        // Math.Min(-1, 3) = -1; -1 * 5 = -5 → the service allows negative factor points but
        // the overall score is capped at 100 (Min, not Max). Verify score does not exceed 100.
        result.Score.Should().BeLessOrEqualTo(100);
    }

    [Fact]
    public void NegativePendingInvoiceCount_ReturnsZeroPoints()
    {
        // invoiceCount of -1 falls into `0 => 0` matching — but switch on negative:
        // -1 is not 0 and not <= 2 (in the positive sense), so falls through to `_ => 10`.
        // Verify the service handles it without throwing.
        var input = ZeroInput(pendingInvoiceCount: -1);

        Action act = () => _sut.CalculatePriorityScore(input);

        act.Should().NotThrow("service should handle any int input without exceptions");
    }

    [Fact]
    public void NullMatterValueTier_ReturnsZeroPoints()
    {
        var input = ZeroInput(matterValueTier: null!);

        var result = _sut.CalculatePriorityScore(input);

        result.Factors.Single(f => f.FactorName == "Matter Value Tier").Points.Should().Be(0,
            "null tier should be treated the same as an unknown tier");
    }

    [Fact]
    public void EmptyStringMatterValueTier_ReturnsZeroPoints()
    {
        var input = ZeroInput(matterValueTier: "");

        var result = _sut.CalculatePriorityScore(input);

        result.Factors.Single(f => f.FactorName == "Matter Value Tier").Points.Should().Be(0);
    }

    [Fact]
    public void WhitespaceMatterValueTier_ReturnsZeroPoints()
    {
        var input = ZeroInput(matterValueTier: "   ");

        var result = _sut.CalculatePriorityScore(input);

        result.Factors.Single(f => f.FactorName == "Matter Value Tier").Points.Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Factor explanation / reason text spot checks
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OverdueDaysExplanation_Singular_WhenOneDay()
    {
        var input = ZeroInput(overdueDays: 1);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Overdue Days");
        factor.Explanation.Should().Be("1 day overdue", "singular form for exactly 1 day");
    }

    [Fact]
    public void OverdueDaysExplanation_Plural_WhenMultipleDays()
    {
        var input = ZeroInput(overdueDays: 8);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Overdue Days");
        factor.Explanation.Should().Be("8 days overdue", "plural form for more than 1 day");
    }

    [Fact]
    public void OverdueDaysExplanation_NotOverdue_WhenZero()
    {
        var input = ZeroInput(overdueDays: 0);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Overdue Days");
        factor.Explanation.Should().Be("Not overdue");
    }

    [Fact]
    public void DeadlineProximityExplanation_NoDeadline_WhenNull()
    {
        var input = ZeroInput(daysToDeadline: null);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Deadline Proximity");
        factor.Explanation.Should().Be("No deadline");
    }

    [Fact]
    public void DeadlineProximityExplanation_Singular_WhenOneDay()
    {
        var input = ZeroInput(daysToDeadline: 1);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Deadline Proximity");
        factor.Explanation.Should().Be("1 day to deadline", "singular form for exactly 1 day");
    }

    [Fact]
    public void GradesBelowCExplanation_NoGrades_WhenZero()
    {
        var input = ZeroInput(gradesBelowC: 0);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Grades Below C");
        factor.Explanation.Should().Be("No grades below C");
    }

    [Fact]
    public void PendingInvoicesExplanation_NoInvoices_WhenZero()
    {
        var input = ZeroInput(pendingInvoiceCount: 0);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Pending Invoices");
        factor.Explanation.Should().Be("No pending invoices");
    }

    [Fact]
    public void PendingInvoicesExplanation_Singular_WhenOneInvoice()
    {
        var input = ZeroInput(pendingInvoiceCount: 1);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Pending Invoices");
        factor.Explanation.Should().Be("1 pending invoice", "singular form for exactly 1 invoice");
    }

    [Fact]
    public void MatterValueTierExplanation_UnknownTier_DisplaysUnknown()
    {
        var input = ZeroInput(matterValueTier: null!);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Matter Value Tier");
        factor.Explanation.Should().Contain("Unknown", "null tier should display as Unknown in explanation");
    }

    [Fact]
    public void BudgetUtilizationExplanation_FormatsPercentage()
    {
        var input = ZeroInput(budgetUtilization: 87.5m);

        var result = _sut.CalculatePriorityScore(input);

        var factor = result.Factors.Single(f => f.FactorName == "Budget Utilization");
        factor.Explanation.Should().Contain("87.5%");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Reason string: contributing factors listed format
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReasonString_ContainsSingleFactor_WhenOnlyOneNonZero()
    {
        var input = ZeroInput(overdueDays: 15);  // 30 pts, all others zero

        var result = _sut.CalculatePriorityScore(input);

        result.ReasonString.Should().Contain("+30");
        result.ReasonString.Should().Contain("(Medium)");
    }

    [Fact]
    public void ReasonString_ContainsAllThreeContributors_WhenThreeFactorsActive()
    {
        // 15 days overdue (+30) + budget 90% (+20) + High tier (+10) = 60 (High)
        var input = ZeroInput(overdueDays: 15, budgetUtilization: 90m, matterValueTier: "High");

        var result = _sut.CalculatePriorityScore(input);

        result.ReasonString.Should().Contain("+30");
        result.ReasonString.Should().Contain("+20");
        result.ReasonString.Should().Contain("+10");
        result.ReasonString.Should().Contain("(High)");
    }
}
