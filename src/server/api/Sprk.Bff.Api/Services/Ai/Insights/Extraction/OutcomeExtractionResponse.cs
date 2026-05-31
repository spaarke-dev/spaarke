using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Insights.Extraction;

/// <summary>
/// Strongly-typed projection of the raw JSON returned by the <c>outcome-extraction@v1</c>
/// prompt (per <c>SPEC-phase-1-minimum.md §3.4</c>). Mirrors the prompt's three-sibling-map
/// shape (<see cref="Evidence"/>, <see cref="Confidence"/>, plus the flat field values) before
/// the Layer 2 node executor transposes it into the single <see cref="ExtractionResult.Fields"/>
/// dictionary that downstream gates consume.
/// <para>
/// Lives in Zone A per <c>SPEC §3.5</c>. Not a wire shape — the only consumers are the Layer 2
/// node executor (D-P6) and <see cref="OutcomeExtractionResponseValidator"/>.
/// </para>
/// </summary>
public sealed record OutcomeExtractionResponse
{
    /// <summary>
    /// Outcome category enum. Null means "not extractable from the document". Allowed values
    /// per <c>SPEC-phase-1-minimum.md §3.4</c>:
    /// <c>favorable_to_client | unfavorable_to_client | neutral | mixed | unclear</c>.
    /// </summary>
    [JsonPropertyName("outcomeCategory")]
    public string? OutcomeCategory { get; init; }

    /// <summary>Numeric settlement amount in <see cref="SettlementCurrency"/>. Null when absent.</summary>
    [JsonPropertyName("settlementAmount")]
    public decimal? SettlementAmount { get; init; }

    /// <summary>ISO 4217 currency code. Defaults to <c>USD</c> per prompt.</summary>
    [JsonPropertyName("settlementCurrency")]
    public string? SettlementCurrency { get; init; }

    /// <summary>ISO 8601 date (yyyy-MM-dd) of outcome. Null when absent.</summary>
    [JsonPropertyName("outcomeDate")]
    public string? OutcomeDate { get; init; }

    /// <summary>Matter duration in days. Null when absent.</summary>
    [JsonPropertyName("matterDurationDays")]
    public int? MatterDurationDays { get; init; }

    /// <summary>
    /// Key terms explicitly stated in the document (e.g., cure-period clauses, indemnity caps).
    /// May be empty. Per <c>SPEC-phase-1-minimum.md §3.4</c>.
    /// </summary>
    [JsonPropertyName("keyTerms")]
    public IReadOnlyList<OutcomeExtractionKeyTerm> KeyTerms { get; init; } = Array.Empty<OutcomeExtractionKeyTerm>();

    /// <summary>
    /// Per-field verbatim evidence quotes from the source document. Null entries indicate
    /// the corresponding field was not extracted (and should be null in the field section too).
    /// </summary>
    [JsonPropertyName("evidence")]
    public OutcomeExtractionEvidence Evidence { get; init; } = new();

    /// <summary>
    /// Per-field producer confidences in [0.0, 1.0]. Fields the model didn't extract
    /// return 0.0 (and the corresponding field + evidence are null).
    /// </summary>
    [JsonPropertyName("confidence")]
    public OutcomeExtractionConfidence Confidence { get; init; } = new();

    /// <summary>
    /// Optional per-field one-sentence explanations populated when a field is null.
    /// Surfaces to the D-P11 review surface (Phase 1.5+) so reviewers can see why the
    /// model abstained rather than guessing.
    /// </summary>
    [JsonPropertyName("explanations")]
    public IReadOnlyDictionary<string, string>? Explanations { get; init; }
}

/// <summary>One labeled key term from the document.</summary>
public sealed record OutcomeExtractionKeyTerm
{
    [JsonPropertyName("term")]
    public required string Term { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

/// <summary>Per-field verbatim quotes paired with the extracted field values.</summary>
public sealed record OutcomeExtractionEvidence
{
    [JsonPropertyName("outcomeCategory")]
    public string? OutcomeCategory { get; init; }

    [JsonPropertyName("settlementAmount")]
    public string? SettlementAmount { get; init; }

    [JsonPropertyName("outcomeDate")]
    public string? OutcomeDate { get; init; }

    [JsonPropertyName("matterDurationDays")]
    public string? MatterDurationDays { get; init; }
}

/// <summary>Per-field model confidences in [0.0, 1.0].</summary>
public sealed record OutcomeExtractionConfidence
{
    [JsonPropertyName("outcomeCategory")]
    public double OutcomeCategory { get; init; }

    [JsonPropertyName("settlementAmount")]
    public double SettlementAmount { get; init; }

    [JsonPropertyName("outcomeDate")]
    public double OutcomeDate { get; init; }

    [JsonPropertyName("matterDurationDays")]
    public double MatterDurationDays { get; init; }
}
