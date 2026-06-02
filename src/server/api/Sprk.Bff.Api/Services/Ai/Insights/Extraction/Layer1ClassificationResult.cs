using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Insights.Extraction;

/// <summary>
/// Typed projection of the Layer 1 (D-P5) <c>classification@v1</c> LLM response. Mirrors the
/// JSON shape published in <c>SPEC-phase-1-minimum.md §3.3</c> exactly so System.Text.Json
/// can round-trip the constrained-decoding output without a custom converter.
/// <para>
/// Lives in Zone A per <c>SPEC §3.5</c> — Layer 1 internal contract, NOT a wire shape. The
/// wire shape is <see cref="Sprk.Bff.Api.Models.Insights.ObservationArtifact"/>, produced
/// from this record by <see cref="ILayer1ClassificationEmitter"/>.
/// </para>
/// <para>
/// The 8-category enum is the Phase 1 starter taxonomy (D-P5 first-step blocker resolved
/// 2026-05-28 per user acceptance). Calibration via the D-P11 review surface will refine
/// the categories per D-63; refinement ships as <c>classification@v2</c> with D-62
/// version-driven re-extraction.
/// </para>
/// </summary>
public sealed record Layer1ClassificationResult
{
    /// <summary>
    /// The document type classification. One of the 8-category Phase 1 starter taxonomy
    /// per <c>SPEC-phase-1-minimum.md §3.3</c>. Use the strongly-typed
    /// <see cref="DocumentClassification"/> enum at call sites; the string form here matches
    /// the LLM response exactly (lower_snake_case) for constrained-decoding round-trip.
    /// </summary>
    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    /// <summary>
    /// Model confidence in [0.0, 1.0]. Per <c>SPEC-phase-1-minimum.md §3.3</c>, this drives
    /// downstream Layer 2 gating: Layer 2 (D-P6) fires only when
    /// <see cref="Classification"/> is in {<c>closing_letter</c>, <c>settlement_agreement</c>,
    /// <c>opinion_judgment</c>} AND <see cref="Confidence"/> >= 0.7.
    /// </summary>
    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    /// <summary>
    /// One-sentence rationale citing the structural / content features that drove the
    /// classification. Not used for gating; stored as an evidence annotation on the emitted
    /// Observation so the D-P11 review surface can show reviewers why the model chose this
    /// classification.
    /// </summary>
    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; init; }
}

/// <summary>
/// The Phase 1 starter document-type taxonomy per <c>SPEC-phase-1-minimum.md §3.3</c>.
/// Strongly-typed enum mirror of the <see cref="Layer1ClassificationResult.Classification"/>
/// string. Use <see cref="DocumentClassificationExtensions.ToWireString"/> /
/// <see cref="DocumentClassificationExtensions.TryParseClassification"/> to convert.
/// <para>
/// Per D-59, three values gate Layer 2 invocation: <see cref="ClosingLetter"/>,
/// <see cref="SettlementAgreement"/>, <see cref="OpinionJudgment"/>. The remaining
/// values cause Layer 2 to be skipped entirely (outcome extraction is not meaningful).
/// </para>
/// </summary>
public enum DocumentClassification
{
    /// <summary>A letter or memo summarizing the outcome of a closed matter. Outcome-bearing — gates Layer 2 on.</summary>
    ClosingLetter,

    /// <summary>A binding agreement settling a dispute with terms + amounts. Outcome-bearing — gates Layer 2 on.</summary>
    SettlementAgreement,

    /// <summary>An internal memo analyzing a legal decision / strategy. NOT outcome-bearing in the Phase 1 starter taxonomy.</summary>
    DecisionMemo,

    /// <summary>A transactional document (contract, term sheet, LOI). NOT outcome-bearing in the Phase 1 starter taxonomy.</summary>
    DealDocument,

    /// <summary>A court filing (complaint, answer, motion, brief). NOT outcome-bearing in the Phase 1 starter taxonomy.</summary>
    Pleading,

    /// <summary>A court opinion, ruling, or judgment. Outcome-bearing — gates Layer 2 on.</summary>
    OpinionJudgment,

    /// <summary>General correspondence not falling into the above categories. NOT outcome-bearing.</summary>
    Correspondence,

    /// <summary>Document type not in this list. NOT outcome-bearing.</summary>
    Other
}

/// <summary>
/// Conversion helpers between the strongly-typed <see cref="DocumentClassification"/> enum
/// and the lower_snake_case wire form used by the LLM JSON response + observed predicate
/// values on emitted Observations.
/// </summary>
public static class DocumentClassificationExtensions
{
    /// <summary>
    /// Maps an enum value to its canonical wire form (lower_snake_case) per the
    /// classification@v1 schema enum values.
    /// </summary>
    public static string ToWireString(this DocumentClassification value) => value switch
    {
        DocumentClassification.ClosingLetter => "closing_letter",
        DocumentClassification.SettlementAgreement => "settlement_agreement",
        DocumentClassification.DecisionMemo => "decision_memo",
        DocumentClassification.DealDocument => "deal_document",
        DocumentClassification.Pleading => "pleading",
        DocumentClassification.OpinionJudgment => "opinion_judgment",
        DocumentClassification.Correspondence => "correspondence",
        DocumentClassification.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown DocumentClassification")
    };

    /// <summary>
    /// Parses a wire-form classification string into the strongly-typed enum. Returns
    /// <c>false</c> when the value is not one of the 8 enum values; callers should treat
    /// unrecognized values as a schema violation upstream (the constrained-decoding schema
    /// rejects them, but this lookup is a defense-in-depth check).
    /// </summary>
    public static bool TryParseClassification(string? wire, out DocumentClassification value)
    {
        switch (wire)
        {
            case "closing_letter": value = DocumentClassification.ClosingLetter; return true;
            case "settlement_agreement": value = DocumentClassification.SettlementAgreement; return true;
            case "decision_memo": value = DocumentClassification.DecisionMemo; return true;
            case "deal_document": value = DocumentClassification.DealDocument; return true;
            case "pleading": value = DocumentClassification.Pleading; return true;
            case "opinion_judgment": value = DocumentClassification.OpinionJudgment; return true;
            case "correspondence": value = DocumentClassification.Correspondence; return true;
            case "other": value = DocumentClassification.Other; return true;
            default: value = default; return false;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the classification gates Layer 2 (D-P6 outcome extraction)
    /// per D-59 cheap-gates-expensive economics. The three outcome-bearing types are
    /// <see cref="DocumentClassification.ClosingLetter"/>,
    /// <see cref="DocumentClassification.SettlementAgreement"/>, and
    /// <see cref="DocumentClassification.OpinionJudgment"/>.
    /// <para>
    /// The D-P7 ingest playbook (task 040) consumes this predicate combined with the
    /// confidence threshold (>= 0.7 per SPEC §3.3) to decide whether to invoke Layer 2.
    /// </para>
    /// </summary>
    public static bool IsOutcomeBearing(this DocumentClassification value) => value switch
    {
        DocumentClassification.ClosingLetter => true,
        DocumentClassification.SettlementAgreement => true,
        DocumentClassification.OpinionJudgment => true,
        _ => false
    };
}
