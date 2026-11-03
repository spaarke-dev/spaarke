using Sprk.Bff.Api.Services.Finance.Models;

namespace Sprk.Bff.Api.Services.Finance.Resolution;

/// <summary>The signals of one field (D-47) before they are combined.</summary>
/// <param name="AnalyzerConfidence">The analyzer's confidence for the source span; <c>null</c> = none returned, counted as 0 (fail closed).</param>
/// <param name="SelfReportConfidence">The model's own confidence; <c>null</c> = none given (the signal is left out, not counted as 0).</param>
/// <param name="Arithmetic">The arithmetic result for the field.</param>
/// <param name="Reference">The reference-data result for the field.</param>
/// <param name="ReferenceStrength">How strong a <see cref="ReferenceOutcome.Matched"/> reference is, 0 to 1 (1 for an exact or normalized name).</param>
public readonly record struct FieldSignals(
    decimal? AnalyzerConfidence,
    decimal? SelfReportConfidence,
    ArithmeticOutcome Arithmetic,
    ReferenceOutcome Reference,
    decimal ReferenceStrength = 1m);

/// <summary>
/// The combined confidence of one field (D-47, FR-23): a pure function of the four signals and the eBilling settings. Only this score
/// gates rejection (FR-26, I-5).
/// </summary>
/// <remarks>
/// <para><b>Formula.</b> <c>combined = sum(weight_i x signal_i) / sum(weight_i)</c> over the signals that apply to the field, then capped.
/// The weights are the settings' weights (<see cref="EbillingSettings.WeightAnalyzer"/> and its three siblings); nothing is hard-coded
/// here. Dividing by the weights of the applicable signals does two jobs: the weights need not sum to 1 (the platform does not enforce
/// it, task 025), and a signal that does not apply to a field (a free-text field has no arithmetic or roster check) is left out rather
/// than counted as a pass or a fail.</para>
/// <para><b>Signals.</b> Analyzer: its confidence, 0 when it gave none. Arithmetic: 1 passed, 0 failed, left out when no check involved the
/// field. Reference match: the match strength (1 for an exact or normalized name, the similarity for an AI-assisted one) when matched, 0
/// when missing, left out when the field has no reference data. Self-report: the model's confidence, left out when it gave none.</para>
/// <para><b>Self-report can never raise a score above the analyzer.</b> The self-report signal is <c>min(self-report, analyzer)</c>: a
/// model that doubts itself lowers the score, a model more sure than the analyzer changes nothing. (Arithmetic and reference signals
/// are independent evidence and may lift a score above the analyzer's: a name the roster recognizes is better evidence than an OCR
/// span alone.)</para>
/// <para><b>Caps are ceilings.</b> A failed arithmetic check limits the score to <see cref="EbillingSettings.ArithmeticFailCap"/>; a
/// missing reference match to <see cref="EbillingSettings.ReferenceMissingCap"/>; both → the lower. The seeded caps (0.50, 0.80) are
/// below the 0.90 threshold on purpose: a field that fails arithmetic or has no reference match cannot support a rejection.</para>
/// <para><b>No rounding</b>, so a score of 0.89996 is never rounded up over the threshold. The result is always within 0..1.</para>
/// </remarks>
public static class CombinedConfidenceCalculator
{
    public static FieldConfidence Calculate(FieldSignals signals, EbillingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var analyzer = Clamp(signals.AnalyzerConfidence ?? 0m);
        decimal? selfReport = signals.SelfReportConfidence is { } reported ? Math.Min(Clamp(reported), analyzer) : null;

        decimal weightedSum = 0m;
        decimal weightTotal = 0m;

        void Add(decimal weight, decimal signal)
        {
            var w = Math.Max(0m, weight);
            weightedSum += w * signal;
            weightTotal += w;
        }

        Add(settings.WeightAnalyzer, analyzer);

        if (signals.Arithmetic != ArithmeticOutcome.NotApplicable)
        {
            Add(settings.WeightArithmetic, signals.Arithmetic == ArithmeticOutcome.Passed ? 1m : 0m);
        }

        decimal? referenceStrength = null;
        if (signals.Reference != ReferenceOutcome.NotApplicable)
        {
            referenceStrength = signals.Reference == ReferenceOutcome.Matched ? Clamp(signals.ReferenceStrength) : 0m;
            Add(settings.WeightReferenceMatch, referenceStrength.Value);
        }

        if (selfReport is { } selfReportSignal)
        {
            Add(settings.WeightSelfReport, selfReportSignal);
        }

        // No weight on any applicable signal (all zero in the settings): nothing supports the value, so fail closed.
        var combined = weightTotal > 0m ? weightedSum / weightTotal : 0m;

        var caps = ConfidenceCaps.None;
        if (signals.Arithmetic == ArithmeticOutcome.Failed)
        {
            caps |= ConfidenceCaps.ArithmeticFail;
            combined = Math.Min(combined, Clamp(settings.ArithmeticFailCap));
        }

        if (signals.Reference == ReferenceOutcome.Missing)
        {
            caps |= ConfidenceCaps.ReferenceMissing;
            combined = Math.Min(combined, Clamp(settings.ReferenceMissingCap));
        }

        return new FieldConfidence
        {
            Combined = Clamp(combined),
            AnalyzerSignal = analyzer,
            SelfReportSignal = selfReport,
            Arithmetic = signals.Arithmetic,
            Reference = signals.Reference,
            ReferenceStrength = signals.Reference == ReferenceOutcome.Matched ? referenceStrength : null,
            CapsInForce = caps,
        };
    }

    private static decimal Clamp(decimal value) => Math.Min(1m, Math.Max(0m, value));
}
