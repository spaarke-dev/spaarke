using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.Communication;

/// <summary>
/// Structured LLM output for the Association Engine's extract+classify rung (rung 5, FR-15) and the
/// W5 "Communication Triage" substrate (task 053). Produced by a guaranteed-valid-JSON structured-output
/// call (<c>IOpenAiClient.GetStructuredCompletionAsync&lt;T&gt;</c>) over the normalized message envelope
/// (subject + plain-text body) via the <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.ICommunicationClassificationAi"/>
/// facade.
/// </summary>
/// <remarks>
/// <para>
/// <b>Types, not IDs.</b> An LLM cannot produce Dataverse record GUIDs, so
/// <see cref="CandidateRecordTypes"/> is a <c>$choices</c>-constrained set of entity <i>logical names</i>
/// (which kinds of records the message likely relates to) — never a concrete target. Rung 5 therefore emits
/// metadata-only signals; it never writes a regarding lookup and never auto-files (FR-11 / ADR-045).
/// </para>
/// <para>
/// <b>Privilege is a flag, never a decision (ADR-015).</b> <see cref="PrivilegeFlagged"/> is a signal for the
/// human reviewer only — the engine never files, withholds, or redacts on the strength of it.
/// </para>
/// </remarks>
public sealed record CommunicationClassificationResult
{
    /// <summary>
    /// Candidate Dataverse record <i>types</i> (logical names, e.g. <c>sprk_matter</c>) the message likely
    /// relates to — constrained to the allowed set by the JSON schema. Types only: the LLM cannot resolve a
    /// specific record, so this feeds triage/provenance, not a regarding write. Empty when none apply.
    /// </summary>
    [JsonPropertyName("candidateRecordTypes")]
    public IReadOnlyList<string> CandidateRecordTypes { get; init; } = Array.Empty<string>();

    /// <summary>Content category (e.g. <c>court-notice</c>, <c>invoice</c>, <c>general-correspondence</c>). Null when undetermined.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>Urgency signal (e.g. <c>routine</c>, <c>elevated</c>, <c>urgent</c>). Null when undetermined.</summary>
    [JsonPropertyName("urgency")]
    public string? Urgency { get; init; }

    /// <summary>Obligations the message implies (e.g. <c>deadline-response</c>, <c>executed-document</c>). Empty when none.</summary>
    [JsonPropertyName("obligations")]
    public IReadOnlyList<string> Obligations { get; init; } = Array.Empty<string>();

    /// <summary>Suggested next actions for the reviewer (e.g. <c>calendar-deadline</c>, <c>route-to-billing</c>). Empty when none.</summary>
    [JsonPropertyName("suggestedActions")]
    public IReadOnlyList<string> SuggestedActions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// <b>Signal only (ADR-015).</b> <c>true</c> when the content shows privilege markers. Surfaced to the
    /// reviewer as a flag; the engine never makes a privilege decision.
    /// </summary>
    [JsonPropertyName("privilegeFlagged")]
    public bool PrivilegeFlagged { get; init; }

    /// <summary>Short human-readable rationale, written into <c>sprk_associationprovenance</c> for the W4 review surface.</summary>
    [JsonPropertyName("rationale")]
    public string? Rationale { get; init; }
}
