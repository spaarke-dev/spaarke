using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Notifications.Envelopes;

/// <summary>
/// The communication-event envelope (design.md §5A.3, spec FR-03), shared by both
/// communication <see cref="NotificationKind"/> values. Carries IDENTIFIERS + minimal
/// DISPLAY metadata only — never a message body, never privileged content beyond the
/// access-gated optional <see cref="Snippet"/> (NFR-02/NFR-03). Clients re-fetch
/// details through the BFF, which re-checks access; the spine is an accelerator,
/// never an authorization bypass.
/// </summary>
/// <remarks>
/// <para>
/// Field list is verbatim from design.md §5A.3 — exactly nine properties, no more,
/// no fewer (guarded by a reflection-based test in
/// <c>tests/unit/Sprk.Bff.Api.Tests/Services/Notifications/EnvelopeSerializationTests.cs</c>).
/// </para>
/// <para>
/// <see cref="Kind"/> MUST be <see cref="NotificationKind.CommunicationArrived"/> or
/// <see cref="NotificationKind.CommunicationAssessed"/> — call <see cref="Validate"/>
/// at construction time to enforce this (mirrors the <c>GateDecisionV2.Validate</c>
/// pattern in <c>Services/Ai/PublicContracts/</c>).
/// </para>
/// </remarks>
public sealed record CommunicationEnvelope
{
    /// <summary>Discriminator. MUST be <see cref="NotificationKind.CommunicationArrived"/> or <see cref="NotificationKind.CommunicationAssessed"/> (enforced by <see cref="Validate"/>).</summary>
    [JsonPropertyName("kind")]
    public required NotificationKind Kind { get; init; }

    /// <summary>The <c>sprk_communication</c> GUID.</summary>
    [JsonPropertyName("communicationId")]
    public required Guid CommunicationId { get; init; }

    /// <summary>The <c>sprk_communicationthread</c> GUID — the grouping consumers use to fan out/timeline-group (messaging-r1 §6 thread model).</summary>
    [JsonPropertyName("threadId")]
    public required Guid ThreadId { get; init; }

    /// <summary>Channel: <c>"email"</c> | <c>"message"</c> | <c>"sms"</c>.</summary>
    [JsonPropertyName("channel")]
    public required string Channel { get; init; }

    /// <summary>Direction: <c>"inbound"</c> | <c>"outbound"</c>.</summary>
    [JsonPropertyName("direction")]
    public required string Direction { get; init; }

    /// <summary>The matter/project/etc. this thread is about (ADR-024 regarding family). Dataverse record id.</summary>
    [JsonPropertyName("regardingRecordId")]
    public required string RegardingRecordId { get; init; }

    /// <summary>Sender DISPLAY NAME ONLY — never an address, never content.</summary>
    [JsonPropertyName("senderDisplay")]
    public required string SenderDisplay { get; init; }

    /// <summary>
    /// OPTIONAL short excerpt — ONLY populated by the producer when the record is
    /// non-private/non-privileged (NFR-02/NFR-03). Never a full message body.
    /// </summary>
    [JsonPropertyName("snippet")]
    public string? Snippet { get; init; }

    /// <summary>Unread-badge delta, e.g. <c>+1</c>.</summary>
    [JsonPropertyName("badgeDelta")]
    public required int BadgeDelta { get; init; }

    /// <summary>
    /// Fail-fast guard: rejects a <see cref="CommunicationEnvelope"/> constructed with
    /// a non-communication <see cref="Kind"/> (design.md §5A.2 — the two communication
    /// kinds are semantically distinct from <c>suggestion</c> and the reserved kinds,
    /// and this envelope shape is theirs alone).
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not a communication kind.</exception>
    public CommunicationEnvelope Validate()
    {
        if (Kind is not (NotificationKind.CommunicationArrived or NotificationKind.CommunicationAssessed))
        {
            throw new InvalidOperationException(
                $"CommunicationEnvelope.Kind must be '{NotificationKind.CommunicationArrived}' or " +
                $"'{NotificationKind.CommunicationAssessed}' (design.md §5A.2); got '{Kind}'.");
        }

        return this;
    }
}
