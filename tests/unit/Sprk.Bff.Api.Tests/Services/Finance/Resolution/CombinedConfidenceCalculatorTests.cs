using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Resolution;

/// <summary>
/// <see cref="CombinedConfidenceCalculator"/> (ebilling-r1 task 046, FR-23, D-47, D-69): the one score that gates rejection (FR-26, I-5).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): a wrong weight, cap or renormalization silently moves invoices across the 0.90 reliability threshold, which
/// is the line between "a person reviews it" and "the AI may reject it". Every expected value below is worked by hand from the seeded
/// settings (weights 0.50 / 0.25 / 0.15 / 0.10, caps 0.50 / 0.80); the grid test pins the invariants that must hold for any input.
/// </remarks>
public sealed class CombinedConfidenceCalculatorTests
{
    private static readonly EbillingSettings Seeded = new();

    private static FieldConfidence Calc(
        decimal? analyzer,
        decimal? self = null,
        ArithmeticOutcome arithmetic = ArithmeticOutcome.NotApplicable,
        ReferenceOutcome reference = ReferenceOutcome.NotApplicable,
        decimal strength = 1m,
        EbillingSettings? settings = null) =>
        CombinedConfidenceCalculator.Calculate(new FieldSignals(analyzer, self, arithmetic, reference, strength), settings ?? Seeded);

    [Fact]
    public void AllFourSignalsPerfect_IsOne()
    {
        var result = Calc(1m, 1m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched);

        result.Combined.Should().Be(1m);
        result.CapsInForce.Should().Be(ConfidenceCaps.None);
    }

    [Fact]
    public void MixedSignals_AreTheWeightedAverage()
    {
        // 0.50 x 0.90 + 0.25 x 1 + 0.15 x 1 + 0.10 x 0.80 = 0.45 + 0.25 + 0.15 + 0.08
        var result = Calc(0.9m, 0.8m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched);

        result.Combined.Should().Be(0.93m);
        result.AnalyzerSignal.Should().Be(0.9m);
        result.SelfReportSignal.Should().Be(0.8m);
    }

    [Fact]
    public void FailedArithmetic_CapsTheScoreAtTheArithmeticCap_EvenWhenEverythingElseIsPerfect()
    {
        // raw = (0.50 + 0 + 0.15 + 0.10) = 0.75, capped to 0.50
        var result = Calc(1m, 1m, ArithmeticOutcome.Failed, ReferenceOutcome.Matched);

        result.Combined.Should().Be(0.50m);
        result.CapsInForce.Should().Be(ConfidenceCaps.ArithmeticFail);
        result.Combined.Should().BeLessThan(Seeded.ReliabilityThreshold);
    }

    [Fact]
    public void MissingReference_CapsTheScoreAtTheReferenceCap()
    {
        // raw = (0.50 + 0.25 + 0 + 0.10) = 0.85, capped to 0.80
        var result = Calc(1m, 1m, ArithmeticOutcome.Passed, ReferenceOutcome.Missing);

        result.Combined.Should().Be(0.80m);
        result.CapsInForce.Should().Be(ConfidenceCaps.ReferenceMissing);
        result.Combined.Should().BeLessThan(Seeded.ReliabilityThreshold);
    }

    [Fact]
    public void BothCaps_TheLowerOneWins_AndBothAreReported()
    {
        var result = Calc(1m, 1m, ArithmeticOutcome.Failed, ReferenceOutcome.Missing);

        result.Combined.Should().Be(0.50m);
        result.CapsInForce.Should().Be(ConfidenceCaps.ArithmeticFail | ConfidenceCaps.ReferenceMissing);
    }

    [Fact]
    public void ACap_IsACeiling_NotAReplacement_ASmallerRawScoreStaysSmaller()
    {
        // analyzer 0.2: raw = (0.10 + 0 + 0.15) / 0.90, well below the 0.50 cap
        var result = Calc(0.2m, null, ArithmeticOutcome.Failed, ReferenceOutcome.Matched);

        result.Combined.Should().Be(0.25m / 0.90m);
        result.Combined.Should().BeLessThan(Seeded.ArithmeticFailCap);
    }

    [Fact]
    public void SelfReport_CanNeverRaiseTheScoreAboveWhatTheAnalyzerSupports()
    {
        var overconfident = Calc(0.6m, 1.0m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched);
        var exactlyAsSure = Calc(0.6m, 0.6m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched);

        overconfident.SelfReportSignal.Should().Be(0.6m);
        overconfident.Combined.Should().Be(exactlyAsSure.Combined);
    }

    [Fact]
    public void SelfReport_BelowTheAnalyzer_LowersTheScore()
    {
        var sure = Calc(0.9m, 0.9m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched);
        var doubtful = Calc(0.9m, 0.1m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched);

        doubtful.Combined.Should().BeLessThan(sure.Combined);
        doubtful.SelfReportSignal.Should().Be(0.1m);
    }

    [Fact]
    public void NoAnalyzerConfidence_CountsAsZero_NotAsMissing()
    {
        var none = Calc(null, null, ArithmeticOutcome.Passed, ReferenceOutcome.Matched);
        var zero = Calc(0m, null, ArithmeticOutcome.Passed, ReferenceOutcome.Matched);

        none.AnalyzerSignal.Should().Be(0m);
        none.Combined.Should().Be(zero.Combined);
        none.Combined.Should().Be((0.25m + 0.15m) / 0.90m);
    }

    [Fact]
    public void NoAnalyzerConfidence_MeansSelfReportCannotHelp()
    {
        var result = Calc(null, 1m, ArithmeticOutcome.NotApplicable, ReferenceOutcome.NotApplicable);

        result.SelfReportSignal.Should().Be(0m);
        result.Combined.Should().Be(0m);
    }

    [Fact]
    public void SignalsThatDoNotApplyToAField_AreLeftOut_NotCountedAsPassOrFail()
    {
        // A free-text field has no arithmetic or roster check: the score is the analyzer's alone.
        Calc(0.8m).Combined.Should().Be(0.8m);

        // With a self report: (0.50 x 0.80 + 0.10 x 0.70) / (0.50 + 0.10)
        Calc(0.8m, 0.7m).Combined.Should().Be(0.47m / 0.60m);
    }

    [Fact]
    public void ReferenceStrength_ScalesTheReferenceSignal_WhenMatched()
    {
        var exact = Calc(1m, null, ArithmeticOutcome.Passed, ReferenceOutcome.Matched, strength: 1m);
        var aiAssisted = Calc(1m, null, ArithmeticOutcome.Passed, ReferenceOutcome.Matched, strength: 0.92m);

        // (0.50 + 0.25 + 0.15 x 0.92) / 0.90
        aiAssisted.Combined.Should().Be(0.888m / 0.90m);
        aiAssisted.Combined.Should().BeLessThan(exact.Combined);
        aiAssisted.ReferenceStrength.Should().Be(0.92m);
    }

    [Fact]
    public void ReferenceStrength_IsOnlyReported_WhenTheReferenceMatched()
    {
        Calc(1m, null, ArithmeticOutcome.Passed, ReferenceOutcome.Missing, strength: 0.7m).ReferenceStrength.Should().BeNull();
        Calc(1m, null, ArithmeticOutcome.Passed, ReferenceOutcome.NotApplicable, strength: 0.7m).ReferenceStrength.Should().BeNull();
    }

    [Fact]
    public void Weights_NeedNotSumToOne()
    {
        var settings = new EbillingSettings { WeightAnalyzer = 2m, WeightArithmetic = 1m, WeightReferenceMatch = 1m, WeightSelfReport = 1m };

        Calc(1m, 1m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched, settings: settings).Combined.Should().Be(1m);

        // analyzer 0 (so the self report is clamped to 0 as well): (0 + 1 + 1 + 0) / 5
        Calc(0m, 1m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched, settings: settings).Combined.Should().Be(0.4m);
    }

    [Fact]
    public void ChangingASetting_ChangesTheScore()
    {
        // Seeded: (0.50 x 0.5 + 0.25 + 0.15) / 0.90
        var seeded = Calc(0.5m, null, ArithmeticOutcome.Passed, ReferenceOutcome.Matched);
        seeded.Combined.Should().Be(0.65m / 0.90m);

        // Analyzer weight 0.10: (0.10 x 0.5 + 0.25 + 0.15) / 0.50
        var lessAnalyzer = Calc(
            0.5m, null, ArithmeticOutcome.Passed, ReferenceOutcome.Matched, settings: new EbillingSettings { WeightAnalyzer = 0.10m });
        lessAnalyzer.Combined.Should().Be(0.45m / 0.50m);

        // A lower arithmetic cap lowers a failed field further.
        var lowerCap = Calc(
            1m, 1m, ArithmeticOutcome.Failed, ReferenceOutcome.Matched, settings: new EbillingSettings { ArithmeticFailCap = 0.30m });
        lowerCap.Combined.Should().Be(0.30m);

        // A higher reference cap lets a missing reference score higher (raw 0.85 is now below the 0.95 cap).
        var higherCap = Calc(
            1m, 1m, ArithmeticOutcome.Passed, ReferenceOutcome.Missing, settings: new EbillingSettings { ReferenceMissingCap = 0.95m });
        higherCap.Combined.Should().Be(0.85m);
    }

    [Fact]
    public void AllWeightsZero_FailsClosed_ToZero()
    {
        var settings = new EbillingSettings { WeightAnalyzer = 0m, WeightArithmetic = 0m, WeightReferenceMatch = 0m, WeightSelfReport = 0m };

        Calc(1m, 1m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched, settings: settings).Combined.Should().Be(0m);
    }

    [Fact]
    public void ANegativeWeight_IsTreatedAsZero_NotAsAPenalty()
    {
        var negative = new EbillingSettings { WeightArithmetic = -1m };
        var zero = new EbillingSettings { WeightArithmetic = 0m };

        Calc(0.7m, null, ArithmeticOutcome.Failed, ReferenceOutcome.Matched, settings: negative).Combined
            .Should().Be(Calc(0.7m, null, ArithmeticOutcome.Failed, ReferenceOutcome.Matched, settings: zero).Combined);
    }

    [Fact]
    public void OutOfRangeInputs_AreClamped()
    {
        var high = Calc(1.5m, 2m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched, strength: 3m);
        high.AnalyzerSignal.Should().Be(1m);
        high.SelfReportSignal.Should().Be(1m);
        high.Combined.Should().Be(1m);

        var low = Calc(-0.2m, -1m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched, strength: -1m);
        low.AnalyzerSignal.Should().Be(0m);
        low.SelfReportSignal.Should().Be(0m);
        low.Combined.Should().Be(0.25m / 1.00m);
    }

    [Fact]
    public void TheScoreIsNeverRounded_SoJustUnderTheThresholdStaysUnder()
    {
        var result = Calc(0.89996m);

        result.Combined.Should().Be(0.89996m);
        result.Combined.Should().BeLessThan(Seeded.ReliabilityThreshold);
    }

    [Fact]
    public void ASettingThatIsOutOfRange_StillGivesAScoreInRange()
    {
        var settings = new EbillingSettings { ArithmeticFailCap = -0.5m, ReferenceMissingCap = 7m };

        Calc(1m, 1m, ArithmeticOutcome.Failed, ReferenceOutcome.Matched, settings: settings).Combined.Should().Be(0m);
        Calc(1m, 1m, ArithmeticOutcome.Passed, ReferenceOutcome.Missing, settings: settings).Combined.Should().Be(0.85m);
    }

    [Fact]
    public void IsDeterministic()
    {
        var a = Calc(0.77m, 0.66m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched, 0.93m);
        var b = Calc(0.77m, 0.66m, ArithmeticOutcome.Passed, ReferenceOutcome.Matched, 0.93m);

        a.Should().Be(b);
    }

    [Fact]
    public void Grid_EveryCombination_KeepsTheInvariants()
    {
        decimal?[] analyzers = [null, 0m, 0.25m, 0.5m, 0.75m, 0.9m, 1m];
        decimal?[] selfReports = [null, 0m, 0.5m, 1m];
        decimal[] strengths = [0m, 0.5m, 0.92m, 1m];
        var violations = new List<string>();

        foreach (var analyzer in analyzers)
        foreach (var self in selfReports)
        foreach (var arithmetic in Enum.GetValues<ArithmeticOutcome>())
        foreach (var reference in Enum.GetValues<ReferenceOutcome>())
        foreach (var strength in strengths)
        {
            var label = $"analyzer={analyzer} self={self} arithmetic={arithmetic} reference={reference} strength={strength}";
            var result = Calc(analyzer, self, arithmetic, reference, strength);

            if (result.Combined is < 0m or > 1m)
            {
                violations.Add($"{label}: combined {result.Combined} outside 0..1");
            }

            if (arithmetic == ArithmeticOutcome.Failed && result.Combined > Seeded.ArithmeticFailCap)
            {
                violations.Add($"{label}: failed arithmetic but combined {result.Combined} > {Seeded.ArithmeticFailCap}");
            }

            if (reference == ReferenceOutcome.Missing && result.Combined > Seeded.ReferenceMissingCap)
            {
                violations.Add($"{label}: missing reference but combined {result.Combined} > {Seeded.ReferenceMissingCap}");
            }

            if (result.SelfReportSignal is { } s && s > result.AnalyzerSignal)
            {
                violations.Add($"{label}: self-report signal {s} above analyzer signal {result.AnalyzerSignal}");
            }

            // Determinism.
            if (Calc(analyzer, self, arithmetic, reference, strength) != result)
            {
                violations.Add($"{label}: not deterministic");
            }

            // Monotone in the analyzer: a higher analyzer confidence never lowers the score.
            if (analyzer is { } a && a < 1m)
            {
                var higher = Calc(Math.Min(1m, a + 0.1m), self, arithmetic, reference, strength);
                if (higher.Combined < result.Combined)
                {
                    violations.Add($"{label}: raising the analyzer lowered the score {result.Combined} -> {higher.Combined}");
                }
            }

            // Monotone in the self report, in the reference strength, and a passed check never scores below a failed one.
            if (self is { } r && r < 1m && Calc(analyzer, r + 0.1m > 1m ? 1m : r + 0.1m, arithmetic, reference, strength).Combined < result.Combined)
            {
                violations.Add($"{label}: raising the self report lowered the score");
            }

            if (reference == ReferenceOutcome.Matched && strength < 1m
                && Calc(analyzer, self, arithmetic, reference, Math.Min(1m, strength + 0.1m)).Combined < result.Combined)
            {
                violations.Add($"{label}: raising the reference strength lowered the score");
            }

            if (arithmetic == ArithmeticOutcome.Failed
                && Calc(analyzer, self, ArithmeticOutcome.Passed, reference, strength).Combined < result.Combined)
            {
                violations.Add($"{label}: a passed check scored below a failed one");
            }

            if (reference == ReferenceOutcome.Missing
                && Calc(analyzer, self, arithmetic, ReferenceOutcome.Matched, 1m).Combined < result.Combined)
            {
                violations.Add($"{label}: a matched reference scored below a missing one");
            }
        }

        violations.Should().BeEmpty();
    }
}
