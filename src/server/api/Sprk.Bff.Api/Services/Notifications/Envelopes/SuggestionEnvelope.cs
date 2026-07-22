using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Notifications.Envelopes;

/// <summary>
/// The proactive-suggestion envelope (design.md §5B.4, spec FR-03), mirroring
/// <see cref="CommunicationEnvelope"/>. Carries IDENTIFIERS + minimal DISPLAY
/// metadata only — never the full grounded payload and NEVER a pre-authorized
/// action token (NFR-02/NFR-03). The client re-fetches/re-grounds the actionable
/// detail through the BFF at action time, which re-checks grounding AND access.
/// </summary>
/// <remarks>
/// <para>
/// Field list is verbatim from design.md §5B.4 — exactly eight properties, no
/// more, no fewer (guarded by a reflection-based test in
/// <c>tests/unit/Sprk.Bff.Api.Tests/Services/Notifications/EnvelopeSerializationTests.cs</c>).
/// </para>
/// <para>
/// <see cref="Kind"/> MUST be <see cref="NotificationKind.Suggestion"/> — call
/// <see cref="Validate"/> at construction time to enforce this (mirrors the
/// <c>GateDecisionV2.Validate</c> pattern in <c>Services/Ai/PublicContracts/</c>).
/// </para>
/// </remarks>
public sealed record SuggestionEnvelope
{
    /// <summary>Discriminator. MUST be <see cref="NotificationKind.Suggestion"/> (enforced by <see cref="Validate"/>).</summary>
    [JsonPropertyName("kind")]
    public required NotificationKind Kind { get; init; }

    /// <summary>The suggestion's identity GUID.</summary>
    [JsonPropertyName("suggestionId")]
    public required Guid SuggestionId { get; init; }

    /// <summary>Producer origin: <c>"daily-briefing"</c> | <c>"event"</c> | <c>"insight"</c>.</summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>The matter/project/etc. the suggestion is about (ADR-024 regarding family). Dataverse record id.</summary>
    [JsonPropertyName("regardingRecordId")]
    public required string RegardingRecordId { get; init; }

    /// <summary>Short display title.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// OPTIONAL short excerpt — access-checked by the producer before inclusion
    /// (NFR-02/NFR-03). Never the full grounded payload.
    /// </summary>
    [JsonPropertyName("snippet")]
    public string? Snippet { get; init; }

    /// <summary>
    /// Drives the renderer + the dispatch it will re-enter (e.g. <c>"create-matter"</c>,
    /// <c>"review"</c>). NEVER a pre-authorized action token — the client re-grounds via
    /// the BFF at action time (design.md §5B.4).
    /// </summary>
    [JsonPropertyName("actionHint")]
    public required string ActionHint { get; init; }

    /// <summary>When the suggestion expires.</summary>
    [JsonPropertyName("expiresAt")]
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Fail-fast guard: rejects a <see cref="SuggestionEnvelope"/> constructed with a
    /// non-<see cref="NotificationKind.Suggestion"/> <see cref="Kind"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not <see cref="NotificationKind.Suggestion"/>.</exception>
    public SuggestionEnvelope Validate()
    {
        if (Kind != NotificationKind.Suggestion)
        {
            throw new InvalidOperationException(
                $"SuggestionEnvelope.Kind must be '{NotificationKind.Suggestion}' (design.md §5B.4); got '{Kind}'.");
        }

        return this;
    }
}
