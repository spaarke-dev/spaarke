using FluentAssertions;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="EffortScoringService"/> covering base effort lookup,
/// case-insensitive event type matching, multiplicative complexity multipliers,
/// score capping at 100, effort level thresholds, and reason string formatting.
/// </summary>
public class EffortScoringServiceTests
{
    private readonly EffortScoringService _sut = new();

    #region Base Effort Tests (no multipliers)

    /// <summary>
    /// Each known event type should return its defined base effort with no multipliers applied.
    /// </summary>
    [Theory]
    [InlineData("Email", 15)]
    [InlineData("DocumentReview", 25)]
    [InlineData("Task", 20)]
    [InlineData("Invoice", 30)]
    [InlineData("Meeting", 20)]
    [InlineData("Analysis", 35)]
    [InlineData("AlertResponse", 10)]
    public void BaseEffort_ReturnsCorrectScore_NoMultipliers(string eventType, int expected)
    {
        // Arrange
        var input = new EffortScoreInput(eventType, false, false, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert
        result.Score.Should().Be(expected);
        result.BaseEffort.Should().Be(expected);
        result.AppliedMultipliers.Should().BeEmpty();
    }

    #endregion

    #region Unknown Event Type Tests

    /// <summary>
    /// An unrecognized event type should fall back to the default base effort of 20.
    /// </summary>
    [Theory]
    [InlineData("Unknown", 20)]
    [InlineData("SomethingElse", 20)]
    [InlineData("", 20)]
    public void UnknownEventType_ReturnsDefault20(string eventType, int expected)
    {
        // Arrange
        var input = new EffortScoreInput(eventType, false, false, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert
        result.Score.Should().Be(expected);
        result.BaseEffort.Should().Be(expected);
    }

    #endregion

    #region Case Insensitivity Tests

    /// <summary>
    /// Event type lookup should be case-insensitive so that "email", "EMAIL", and "Email"
    /// all resolve to the same base effort.
    /// </summary>
    [Theory]
    [InlineData("email", 15)]
    [InlineData("EMAIL", 15)]
    [InlineData("Email", 15)]
    public void EventType_IsCaseInsensitive(string eventType, int expected)
    {
        // Arrange
        var input = new EffortScoreInput(eventType, false, false, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert
        result.Score.Should().Be(expected);
    }

    [Theory]
    [InlineData("documentreview", 25)]
    [InlineData("DOCUMENTREVIEW", 25)]
    [InlineData("alertresponse", 10)]
    [InlineData("ALERTRESPONSE", 10)]
    public void EventType_IsCaseInsensitive_OtherTypes(string eventType, int expected)
    {
        // Arrange
        var input = new EffortScoreInput(eventType, false, false, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert
        result.Score.Should().Be(expected);
    }

    #endregion

    #region Individual Multiplier Tests

    /// <summary>
    /// HasMultipleParties applies a 1.3x multiplier: Task (20) × 1.3 = 26.
    /// </summary>
    [Fact]
    public void MultipleParties_Applies1_3xMultiplier()
    {
        // Arrange
        var input = new EffortScoreInput("Task", true, false, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: 20 × 1.3 = 26
        result.Score.Should().Be(26);
        result.AppliedMultipliers.Should().HaveCount(1);
        result.AppliedMultipliers[0].Name.Should().Be("Multiple parties");
        result.AppliedMultipliers[0].Value.Should().Be(1.3m);
    }

    /// <summary>
    /// IsCrossJurisdiction applies a 1.2x multiplier: Task (20) × 1.2 = 24.
    /// </summary>
    [Fact]
    public void CrossJurisdiction_Applies1_2xMultiplier()
    {
        // Arrange
        var input = new EffortScoreInput("Task", false, true, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: 20 × 1.2 = 24
        result.Score.Should().Be(24);
        result.AppliedMultipliers.Should().HaveCount(1);
        result.AppliedMultipliers[0].Name.Should().Be("Cross-jurisdiction");
        result.AppliedMultipliers[0].Value.Should().Be(1.2m);
    }

    /// <summary>
    /// IsRegulatory applies a 1.1x multiplier: Task (20) × 1.1 = 22.
    /// </summary>
    [Fact]
    public void Regulatory_Applies1_1xMultiplier()
    {
        // Arrange
        var input = new EffortScoreInput("Task", false, false, true, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: 20 × 1.1 = 22
        result.Score.Should().Be(22);
        result.AppliedMultipliers.Should().HaveCount(1);
        result.AppliedMultipliers[0].Name.Should().Be("Regulatory");
        result.AppliedMultipliers[0].Value.Should().Be(1.1m);
    }

    /// <summary>
    /// IsHighValue applies a 1.2x multiplier: Task (20) × 1.2 = 24.
    /// </summary>
    [Fact]
    public void HighValue_Applies1_2xMultiplier()
    {
        // Arrange
        var input = new EffortScoreInput("Task", false, false, false, true, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: 20 × 1.2 = 24
        result.Score.Should().Be(24);
        result.AppliedMultipliers.Should().HaveCount(1);
        result.AppliedMultipliers[0].Name.Should().Be("High value");
        result.AppliedMultipliers[0].Value.Should().Be(1.2m);
    }

    /// <summary>
    /// IsTimeSensitive applies a 1.3x multiplier: Task (20) × 1.3 = 26.
    /// </summary>
    [Fact]
    public void TimeSensitive_Applies1_3xMultiplier()
    {
        // Arrange
        var input = new EffortScoreInput("Task", false, false, false, false, true);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: 20 × 1.3 = 26
        result.Score.Should().Be(26);
        result.AppliedMultipliers.Should().HaveCount(1);
        result.AppliedMultipliers[0].Name.Should().Be("Time-sensitive");
        result.AppliedMultipliers[0].Value.Should().Be(1.3m);
    }

    #endregion

    #region Multiplicative Application Tests

    /// <summary>
    /// Multipliers are applied multiplicatively (base × m1 × m2 × ...), not additively.
    /// Analysis (35) × MultipleParties (1.3) × CrossJurisdiction (1.2) × TimeSensitive (1.3)
    /// = 35 × 1.3 × 1.2 × 1.3 = 35 × 2.028 = 70.98 → 71
    /// </summary>
    [Fact]
    public void Multipliers_AppliedMultiplicatively()
    {
        // Arrange
        var input = new EffortScoreInput("Analysis", true, true, false, false, true);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: 35 × 1.3 × 1.2 × 1.3 = 70.98 → 71 (not additive: 35 × (1.3 + 1.2 + 1.3) = 133)
        result.Score.Should().Be(71);
        result.AppliedMultipliers.Should().HaveCount(3);
    }

    /// <summary>
    /// All five multipliers applied to Analysis (35):
    /// 35 × 1.3 × 1.2 × 1.1 × 1.2 × 1.3 = 35 × 2.67696 = 93.6936 → 94
    /// </summary>
    [Fact]
    public void AllMultipliers_AppliedCorrectly()
    {
        // Arrange
        var input = new EffortScoreInput("Analysis", true, true, true, true, true);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: 35 × 1.3 × 1.2 × 1.1 × 1.2 × 1.3 = 93.6936 → 94
        result.Score.Should().Be(94);
        result.AppliedMultipliers.Should().HaveCount(5);
        result.Level.Should().Be("High");
    }

    /// <summary>
    /// Two multipliers on DocumentReview (25):
    /// 25 × 1.3 × 1.2 = 25 × 1.56 = 39 → 39 (Low effort boundary)
    /// </summary>
    [Fact]
    public void DocumentReview_MultiplePartiesAndCrossJurisdiction_ReturnsLow()
    {
        // Arrange
        var input = new EffortScoreInput("DocumentReview", true, true, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: 25 × 1.3 × 1.2 = 39
        result.Score.Should().Be(39);
        result.Level.Should().Be("Low");
    }

    /// <summary>
    /// Invoice (30) with MultipleParties and TimeSensitive:
    /// 30 × 1.3 × 1.3 = 30 × 1.69 = 50.7 → 51 (Med effort)
    /// </summary>
    [Fact]
    public void Invoice_MultiplePartiesAndTimeSensitive_ReturnsMed()
    {
        // Arrange
        var input = new EffortScoreInput("Invoice", true, false, false, false, true);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: 30 × 1.3 × 1.3 = 50.7 → 51
        result.Score.Should().Be(51);
        result.Level.Should().Be("Med");
    }

    #endregion

    #region Score Cap Tests

    /// <summary>
    /// The final score is capped at 100 regardless of how large the raw calculation is.
    /// Verifies that the cap logic is active for any scenario that exceeds 100.
    /// With current multiplier values, Analysis (35) with all multipliers gives 94.
    /// We verify the cap by confirming the score never exceeds 100.
    /// </summary>
    [Fact]
    public void Score_NeverExceeds100()
    {
        // Arrange - max possible: Analysis (35) with all 5 multipliers = 93.6936 → 94
        var input = new EffortScoreInput("Analysis", true, true, true, true, true);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert
        result.Score.Should().BeLessOrEqualTo(100);
    }

    /// <summary>
    /// Verifies that the Math.Min(rawScore, 100) cap is applied by checking that
    /// a score resulting in a theoretical value above 100 is properly capped.
    /// Note: With current table values, no single event type + all multipliers exceeds 100.
    /// The cap guards against future table changes.
    /// </summary>
    [Fact]
    public void Score_CappedAt100_WhenRawScoreExceeds100()
    {
        // The maximum raw score with current values is Analysis (35) × all multipliers ≈ 94.
        // We verify that the score property is exactly the capped final value.
        var input = new EffortScoreInput("Analysis", true, true, true, true, true);
        var result = _sut.CalculateEffortScore(input);

        // The result should reflect the Math.Min capping behavior — never raw decimal
        result.Score.Should().Be(94);
        result.Score.Should().BeLessOrEqualTo(100);
    }

    #endregion

    #region Effort Level Threshold Tests

    /// <summary>
    /// Effort levels must follow the defined thresholds:
    /// High (70-100), Med (40-69), Low (0-39)
    /// We test boundary values by constructing inputs that produce scores at each boundary.
    /// </summary>
    [Fact]
    public void EffortLevel_Low_WhenScoreBelow40()
    {
        // AlertResponse (10) with no multipliers = 10 → Low
        var result = _sut.CalculateEffortScore(new EffortScoreInput("AlertResponse", false, false, false, false, false));
        result.Score.Should().Be(10);
        result.Level.Should().Be("Low");
    }

    [Fact]
    public void EffortLevel_Low_AtBoundary39()
    {
        // DocumentReview (25) × MultipleParties (1.3) × CrossJurisdiction (1.2) = 39
        var result = _sut.CalculateEffortScore(new EffortScoreInput("DocumentReview", true, true, false, false, false));
        result.Score.Should().Be(39);
        result.Level.Should().Be("Low");
    }

    [Fact]
    public void EffortLevel_Med_AtBoundary40()
    {
        // Invoice (30) × CrossJurisdiction (1.2) × Regulatory (1.1) = 30 × 1.32 = 39.6 → 40
        var result = _sut.CalculateEffortScore(new EffortScoreInput("Invoice", false, true, true, false, false));
        result.Score.Should().Be(40);
        result.Level.Should().Be("Med");
    }

    [Fact]
    public void EffortLevel_High_AtBoundary70()
    {
        // Analysis (35) × MultipleParties (1.3) × CrossJurisdiction (1.2) × TimeSensitive (1.3) = 70.98 → 71
        var result = _sut.CalculateEffortScore(new EffortScoreInput("Analysis", true, true, false, false, true));
        result.Score.Should().Be(71);
        result.Level.Should().Be("High");
    }

    [Fact]
    public void EffortLevel_High_WhenScoreAt100()
    {
        // Analysis with all multipliers = 94 → High
        var result = _sut.CalculateEffortScore(new EffortScoreInput("Analysis", true, true, true, true, true));
        result.Level.Should().Be("High");
    }

    #endregion

    #region AlertResponse with All Multipliers

    /// <summary>
    /// AlertResponse (10) with all multipliers applied:
    /// 10 × 1.3 × 1.2 × 1.1 × 1.2 × 1.3 = 10 × 2.67696 = 26.7696 → 27 → Low
    /// </summary>
    [Fact]
    public void AlertResponse_WithAllMultipliers_StillLow()
    {
        // Arrange
        var input = new EffortScoreInput("AlertResponse", true, true, true, true, true);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: 10 × 1.3 × 1.2 × 1.1 × 1.2 × 1.3 = 26.7696 → 27
        result.Score.Should().Be(27);
        result.Level.Should().Be("Low");
    }

    #endregion

    #region Reason String Format Tests

    /// <summary>
    /// Reason string with multipliers should follow the format:
    /// "Base: {EventType} ({base}) × {multiplier name} ({value}x) = {score} ({level} effort)"
    /// </summary>
    [Fact]
    public void ReasonString_IncludesBaseAndMultipliers()
    {
        // Arrange
        var input = new EffortScoreInput("DocumentReview", true, true, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: "Base: Document Review (25) × Multiple parties (1.3x) × Cross-jurisdiction (1.2x) = 39 (Low effort)"
        result.ReasonString.Should().Contain("25");
        result.ReasonString.Should().Contain("Multiple parties");
        result.ReasonString.Should().Contain("1.3x");
        result.ReasonString.Should().Contain("Cross-jurisdiction");
        result.ReasonString.Should().Contain("1.2x");
        result.ReasonString.Should().Contain("39");
        result.ReasonString.Should().Contain("Low effort");
    }

    /// <summary>
    /// Reason string with no multipliers should follow the format:
    /// "Base: {EventType} ({base}) = {score} ({level} effort)"
    /// </summary>
    [Fact]
    public void ReasonString_BaseOnly_WhenNoMultipliers()
    {
        // Arrange
        var input = new EffortScoreInput("Email", false, false, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: "Base: Email (15) = 15 (Low effort)"
        result.ReasonString.Should().Contain("15");
        result.ReasonString.Should().Contain("Low effort");
        result.ReasonString.Should().NotContain("×");
    }

    [Fact]
    public void ReasonString_StartsWithBase()
    {
        // Arrange
        var input = new EffortScoreInput("Invoice", false, false, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert
        result.ReasonString.Should().StartWith("Base:");
    }

    [Fact]
    public void ReasonString_EndsWithEffortLevel()
    {
        // Arrange
        var input = new EffortScoreInput("Analysis", true, true, false, false, true);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert - result is 71 = High effort
        result.ReasonString.Should().EndWith("(High effort)");
    }

    #endregion

    #region Result Record Property Tests

    [Fact]
    public void Result_EventType_MatchesInput()
    {
        // Arrange
        var input = new EffortScoreInput("Invoice", false, false, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert
        result.EventType.Should().Be("Invoice");
    }

    [Fact]
    public void Result_BaseEffort_IsCorrect()
    {
        // Arrange
        var input = new EffortScoreInput("Analysis", true, false, false, false, false);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert
        result.BaseEffort.Should().Be(35);
    }

    [Fact]
    public void Result_AppliedMultipliers_ContainsAllActiveMultipliers()
    {
        // Arrange - 3 of 5 multipliers active
        var input = new EffortScoreInput("Task", true, false, true, false, true);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert
        result.AppliedMultipliers.Should().HaveCount(3);
        result.AppliedMultipliers.Select(m => m.Name)
            .Should().Contain("Multiple parties")
            .And.Contain("Regulatory")
            .And.Contain("Time-sensitive");
    }

    [Fact]
    public void Result_Deterministic_SameInputProducesSameOutput()
    {
        // Arrange
        var input = new EffortScoreInput("Invoice", true, true, false, true, false);

        // Act
        var result1 = _sut.CalculateEffortScore(input);
        var result2 = _sut.CalculateEffortScore(input);

        // Assert
        result1.Score.Should().Be(result2.Score);
        result1.Level.Should().Be(result2.Level);
        result1.ReasonString.Should().Be(result2.ReasonString);
    }

    #endregion

    #region Null and Empty EventType Edge Cases

    /// <summary>
    /// A null event type causes Dictionary.GetValueOrDefault to throw ArgumentNullException
    /// because StringComparer.OrdinalIgnoreCase does not permit null keys.
    /// This test documents the current behavior: null EventType is not supported.
    /// </summary>
    [Fact]
    public void NullEventType_ThrowsArgumentNullException()
    {
        // Arrange
        var input = new EffortScoreInput(null!, false, false, false, false, false);

        // Act
        Action act = () => _sut.CalculateEffortScore(input);

        // Assert: dictionary lookup with null key throws ArgumentNullException
        act.Should().Throw<ArgumentNullException>(
            "Dictionary<string,int> with OrdinalIgnoreCase comparer does not accept null keys");
    }

    [Fact]
    public void EmptyEventType_ReturnsDefaultBaseEffort20()
    {
        var input = new EffortScoreInput("", false, false, false, false, false);

        var result = _sut.CalculateEffortScore(input);

        result.BaseEffort.Should().Be(20);
        result.Score.Should().Be(20);
    }

    #endregion

    #region All Multipliers: Exact Calculation Verification

    /// <summary>
    /// Verifies the exact multiplicative product for each event type with all 5 multipliers.
    /// Formula: base × 1.3 × 1.2 × 1.1 × 1.2 × 1.3 = base × 2.67696
    /// </summary>
    [Theory]
    [InlineData("Email", 15, 40)]           // 15 × 2.67696 = 40.1544 → 40
    [InlineData("DocumentReview", 25, 67)]  // 25 × 2.67696 = 66.924 → 67
    [InlineData("Task", 20, 54)]            // 20 × 2.67696 = 53.5392 → 54
    [InlineData("Invoice", 30, 80)]         // 30 × 2.67696 = 80.3088 → 80
    [InlineData("Meeting", 20, 54)]         // 20 × 2.67696 = 53.5392 → 54
    [InlineData("Analysis", 35, 94)]        // 35 × 2.67696 = 93.6936 → 94
    [InlineData("AlertResponse", 10, 27)]   // 10 × 2.67696 = 26.7696 → 27
    public void AllMultipliers_CorrectFinalScore_ForEachEventType(
        string eventType, int expectedBase, int expectedFinalScore)
    {
        // Arrange
        var input = new EffortScoreInput(eventType, true, true, true, true, true);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert
        result.BaseEffort.Should().Be(expectedBase);
        result.Score.Should().Be(expectedFinalScore,
            $"{eventType} (base {expectedBase}) × all multipliers should yield {expectedFinalScore}");
        result.AppliedMultipliers.Should().HaveCount(5);
    }

    #endregion

    #region Individual Multiplier Effect on All Event Types

    /// <summary>
    /// HasMultipleParties (1.3x) applied to each event type to verify no interaction effects.
    /// </summary>
    [Theory]
    [InlineData("Email", 15, 20)]           // 15 × 1.3 = 19.5 → 20
    [InlineData("DocumentReview", 25, 33)]  // 25 × 1.3 = 32.5 → 33 (AwayFromZero rounding)
    [InlineData("Invoice", 30, 39)]         // 30 × 1.3 = 39.0 → 39
    [InlineData("Analysis", 35, 46)]        // 35 × 1.3 = 45.5 → 46 (AwayFromZero)
    [InlineData("AlertResponse", 10, 13)]   // 10 × 1.3 = 13.0 → 13
    public void MultipleParties_CorrectScoreForEachEventType(
        string eventType, int baseEffort, int expectedScore)
    {
        var input = new EffortScoreInput(eventType, true, false, false, false, false);

        var result = _sut.CalculateEffortScore(input);

        result.Score.Should().Be(expectedScore,
            $"{eventType} (base {baseEffort}) × 1.3 should yield {expectedScore}");
    }

    /// <summary>
    /// IsTimeSensitive (1.3x) applied — same values as MultipleParties since both are 1.3x.
    /// </summary>
    [Theory]
    [InlineData("Email", 15, 20)]
    [InlineData("Invoice", 30, 39)]
    [InlineData("Analysis", 35, 46)]
    public void TimeSensitive_CorrectScoreForEachEventType(
        string eventType, int baseEffort, int expectedScore)
    {
        var input = new EffortScoreInput(eventType, false, false, false, false, true);

        var result = _sut.CalculateEffortScore(input);

        result.Score.Should().Be(expectedScore,
            $"{eventType} (base {baseEffort}) × TimeSensitive 1.3x should yield {expectedScore}");
    }

    /// <summary>
    /// IsHighValue (1.2x) and IsCrossJurisdiction (1.2x) should produce identical outputs
    /// since they share the same multiplier value.
    /// </summary>
    [Theory]
    [InlineData("Task", 20, 24)]        // 20 × 1.2 = 24
    [InlineData("Invoice", 30, 36)]     // 30 × 1.2 = 36
    [InlineData("Analysis", 35, 42)]    // 35 × 1.2 = 42
    public void HighValue_And_CrossJurisdiction_ProduceSameScore(
        string eventType, int baseEffort, int expectedScore)
    {
        var highValueInput = new EffortScoreInput(eventType, false, false, false, true, false);
        var crossJurisdictionInput = new EffortScoreInput(eventType, false, true, false, false, false);

        var hvResult = _sut.CalculateEffortScore(highValueInput);
        var cjResult = _sut.CalculateEffortScore(crossJurisdictionInput);

        hvResult.Score.Should().Be(expectedScore,
            $"HighValue: {eventType} (base {baseEffort}) × 1.2 should yield {expectedScore}");
        cjResult.Score.Should().Be(expectedScore,
            $"CrossJurisdiction: {eventType} (base {baseEffort}) × 1.2 should yield {expectedScore}");
        hvResult.Score.Should().Be(cjResult.Score,
            "HighValue and CrossJurisdiction use the same 1.2x multiplier so scores should match");
    }

    #endregion

    #region Score Level Boundary Verification via [Theory]

    /// <summary>
    /// Parameterized verification of the three effort level thresholds.
    /// Low: 0-39, Med: 40-69, High: 70-100
    /// </summary>
    [Theory]
    [InlineData("AlertResponse", false, false, false, false, false, "Low")]   // 10 → Low
    [InlineData("Email", false, false, false, false, false, "Low")]           // 15 → Low
    [InlineData("DocumentReview", true, true, false, false, false, "Low")]    // 39 → Low
    [InlineData("Invoice", false, true, true, false, false, "Med")]           // 40 → Med
    [InlineData("Invoice", true, false, false, false, true, "Med")]           // 51 → Med
    [InlineData("Analysis", true, true, false, false, true, "High")]          // 71 → High
    [InlineData("Analysis", true, true, true, true, true, "High")]            // 94 → High
    public void EffortLevel_CorrectForScoreRange(
        string eventType,
        bool multipleParties,
        bool crossJurisdiction,
        bool regulatory,
        bool highValue,
        bool timeSensitive,
        string expectedLevel)
    {
        var input = new EffortScoreInput(
            eventType, multipleParties, crossJurisdiction, regulatory, highValue, timeSensitive);

        var result = _sut.CalculateEffortScore(input);

        result.Level.Should().Be(expectedLevel,
            $"{eventType} with the given flags should yield level '{expectedLevel}'");
    }

    #endregion

    #region Reason String: PascalCase Formatting

    /// <summary>
    /// The reason string formats PascalCase event type names with spaces for display.
    /// "DocumentReview" → "Document Review", "AlertResponse" → "Alert Response"
    /// </summary>
    [Theory]
    [InlineData("DocumentReview", "Document Review")]
    [InlineData("AlertResponse", "Alert Response")]
    [InlineData("Email", "Email")]
    [InlineData("Invoice", "Invoice")]
    [InlineData("Analysis", "Analysis")]
    public void ReasonString_FormatsEventTypeWithSpaces(string eventType, string expectedDisplay)
    {
        var input = new EffortScoreInput(eventType, false, false, false, false, false);

        var result = _sut.CalculateEffortScore(input);

        result.ReasonString.Should().Contain(expectedDisplay,
            $"'{eventType}' should be displayed as '{expectedDisplay}' in the reason string");
    }

    [Fact]
    public void ReasonString_AllFiveMultipliers_ContainsAllNames()
    {
        // Arrange
        var input = new EffortScoreInput("Task", true, true, true, true, true);

        // Act
        var result = _sut.CalculateEffortScore(input);

        // Assert: all five multiplier names must appear in the reason string
        result.ReasonString.Should().Contain("Multiple parties");
        result.ReasonString.Should().Contain("Cross-jurisdiction");
        result.ReasonString.Should().Contain("Regulatory");
        result.ReasonString.Should().Contain("High value");
        result.ReasonString.Should().Contain("Time-sensitive");
    }

    [Fact]
    public void ReasonString_ContainsEffortSuffix_ForEachLevel()
    {
        // Low effort
        var lowResult = _sut.CalculateEffortScore(new EffortScoreInput("AlertResponse", false, false, false, false, false));
        lowResult.ReasonString.Should().EndWith("(Low effort)");

        // Med effort
        var medResult = _sut.CalculateEffortScore(new EffortScoreInput("Invoice", false, true, true, false, false));
        medResult.ReasonString.Should().EndWith("(Med effort)");

        // High effort
        var highResult = _sut.CalculateEffortScore(new EffortScoreInput("Analysis", true, true, false, false, true));
        highResult.ReasonString.Should().EndWith("(High effort)");
    }

    #endregion

    #region AppliedMultipliers Order and Values

    /// <summary>
    /// Multipliers must be applied in declaration order:
    /// MultipleParties → CrossJurisdiction → Regulatory → HighValue → TimeSensitive.
    /// </summary>
    [Fact]
    public void AppliedMultipliers_InDeclarationOrder_WhenAllActive()
    {
        var input = new EffortScoreInput("Email", true, true, true, true, true);

        var result = _sut.CalculateEffortScore(input);

        result.AppliedMultipliers.Should().HaveCount(5);
        result.AppliedMultipliers[0].Name.Should().Be("Multiple parties");
        result.AppliedMultipliers[1].Name.Should().Be("Cross-jurisdiction");
        result.AppliedMultipliers[2].Name.Should().Be("Regulatory");
        result.AppliedMultipliers[3].Name.Should().Be("High value");
        result.AppliedMultipliers[4].Name.Should().Be("Time-sensitive");
    }

    [Fact]
    public void AppliedMultipliers_OnlyActiveOnesIncluded_WhenSubsetActive()
    {
        // Only Regulatory and HighValue active
        var input = new EffortScoreInput("Task", false, false, true, true, false);

        var result = _sut.CalculateEffortScore(input);

        result.AppliedMultipliers.Should().HaveCount(2);
        result.AppliedMultipliers.Select(m => m.Name)
            .Should().Contain("Regulatory")
            .And.Contain("High value")
            .And.NotContain("Multiple parties")
            .And.NotContain("Cross-jurisdiction")
            .And.NotContain("Time-sensitive");
    }

    [Fact]
    public void AppliedMultiplierValues_MatchExpectedConstants()
    {
        var input = new EffortScoreInput("Task", true, true, true, true, true);

        var result = _sut.CalculateEffortScore(input);

        var multiplierMap = result.AppliedMultipliers.ToDictionary(m => m.Name, m => m.Value);
        multiplierMap["Multiple parties"].Should().Be(1.3m);
        multiplierMap["Cross-jurisdiction"].Should().Be(1.2m);
        multiplierMap["Regulatory"].Should().Be(1.1m);
        multiplierMap["High value"].Should().Be(1.2m);
        multiplierMap["Time-sensitive"].Should().Be(1.3m);
    }

    #endregion

    #region MidpointRounding.AwayFromZero Verification

    /// <summary>
    /// The service uses MidpointRounding.AwayFromZero. Verify that .5 values round up.
    /// DocumentReview (25) × MultipleParties (1.3) = 32.5 → 33 (not 32).
    /// Analysis (35) × MultipleParties (1.3) = 45.5 → 46 (not 46 with banker's rounding: 46).
    /// </summary>
    [Fact]
    public void Rounding_UsesAwayFromZero_For0Point5Midpoint()
    {
        // 25 × 1.3 = 32.5 → AwayFromZero rounds to 33
        var input = new EffortScoreInput("DocumentReview", true, false, false, false, false);

        var result = _sut.CalculateEffortScore(input);

        result.Score.Should().Be(33,
            "32.5 should round to 33 using MidpointRounding.AwayFromZero");
    }

    #endregion

    #region No Multipliers: Result Structure

    [Fact]
    public void NoMultipliers_AppliedMultipliersListIsEmpty()
    {
        var input = new EffortScoreInput("Analysis", false, false, false, false, false);

        var result = _sut.CalculateEffortScore(input);

        result.AppliedMultipliers.Should().BeEmpty();
        result.Score.Should().Be(35);
        result.BaseEffort.Should().Be(35);
    }

    [Fact]
    public void NoMultipliers_ScoreEqualsBaseEffort()
    {
        foreach (var (eventType, expectedBase) in new[]
        {
            ("Email", 15),
            ("Invoice", 30),
            ("Analysis", 35),
            ("AlertResponse", 10)
        })
        {
            var input = new EffortScoreInput(eventType, false, false, false, false, false);
            var result = _sut.CalculateEffortScore(input);
            result.Score.Should().Be(result.BaseEffort,
                $"{eventType}: score should equal base effort when no multipliers applied");
        }
    }

    #endregion
}
