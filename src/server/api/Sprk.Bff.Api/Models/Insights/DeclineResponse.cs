using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Insights;

/// <summary>
/// Structured decline response returned when an Insights-mode playbook determines it cannot
/// produce a defensible Inference (e.g., insufficient comparable matters for <c>predict-matter-cost</c>).
/// Per D-49 (LAVERN Pattern #7), this replaces "Agent reasons about whether to decline" prose with a
/// deterministic tool the playbook invokes when <c>EvidenceSufficiencyNode</c> returns insufficient.
/// <para>
/// The five-field shape forces structured uncertainty (Reason / Explanation / MinimumEvidenceNeeded /
/// SuggestedActions / ConfidenceInDecline) instead of generic AI hedging — per the negative-space rule
/// "Do not write decline prose from the Agent; invoke <c>IDeclineToFindTool</c> for structured <c>DeclineResponse</c>" (D-49).
/// </para>
/// <para>
/// Zone B POCO per SPEC §3.5 — pure record, no AI internals imports.
/// </para>
/// </summary>
public sealed record DeclineResponse
{
    /// <summary>
    /// Short machine-readable reason code (e.g., <c>insufficient-evidence</c>, <c>scope-mismatch</c>,
    /// <c>quality-gate-failed</c>). Surfaces may use this to drive iconography or follow-up prompts.
    /// </summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>
    /// Human-readable explanation of why the question cannot be answered (e.g.,
    /// <c>Only 4 comparable matters were found; the predict-matter-cost playbook requires at least 12.</c>).
    /// </summary>
    [JsonPropertyName("explanation")]
    public required string Explanation { get; init; }

    /// <summary>
    /// Structured description of what evidence would be needed to produce an answer (e.g.,
    /// <c>{ "comparableMatters": { "have": 4, "need": 12 } }</c>). Free-form JSON-compatible
    /// object keyed by evidence-category name; consumers render gap analysis from this shape.
    /// </summary>
    [JsonPropertyName("minimumEvidenceNeeded")]
    public required IReadOnlyDictionary<string, object> MinimumEvidenceNeeded { get; init; }

    /// <summary>
    /// Suggested next actions the user could take to enable an answer (e.g.,
    /// <c>["Broaden the matter-type filter from 'IP licensing' to 'IP'", "Author a Precedent for this opposing counsel"]</c>).
    /// </summary>
    [JsonPropertyName("suggestedActions")]
    public required IReadOnlyList<string> SuggestedActions { get; init; }

    /// <summary>
    /// The producer's confidence that this decline is correct (i.e., that the answer truly cannot
    /// be produced, not that the producer just gave up too early). In [0.0, 1.0]. Per D-49.
    /// </summary>
    [JsonPropertyName("confidenceInDecline")]
    public required double ConfidenceInDecline { get; init; }
}
