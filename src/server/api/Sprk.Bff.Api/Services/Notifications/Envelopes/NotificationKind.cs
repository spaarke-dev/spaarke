using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Notifications.Envelopes;

// =============================================================================
// The notification-spine `kind` taxonomy (design.md §10 decision #5, spec FR-10).
// A CLOSED set of discriminator values shared by every envelope the spine's
// outbox (task 011/012) carries and every consumer (SignalR client, poll
// endpoint, Assistant renderer) reads. Locked here so a typo or a novel kind
// fails to COMPILE (unknown enum member) or fails DESERIALIZATION explicitly
// (via <see cref="NotificationKindJsonConverter"/>) — it never silently reaches
// a consumer that doesn't understand it.
//
// ACTIVE kinds have a defined envelope shape + a real producer/consumer
// (this project, later phases). RESERVED kinds are valid discriminator VALUES
// with NO envelope shape and NO consumer wired anywhere — they exist only to
// stop a later producer from colliding with an ad-hoc string of the same intent.
// =============================================================================

/// <summary>
/// The closed notification `kind` discriminator (design.md §10 #5). See file header.
/// </summary>
[JsonConverter(typeof(NotificationKindJsonConverter))]
public enum NotificationKind
{
    // ── ACTIVE — envelope shape defined (this task); producer/consumer wired in later tasks ──

    /// <summary>A proactive suggestion (design.md §5B.4). Envelope: <see cref="SuggestionEnvelope"/>.</summary>
    Suggestion,

    /// <summary>A communication was analyzed and may warrant a domain action (design.md §5A.2/§5A.3). Envelope: <see cref="CommunicationEnvelope"/>.</summary>
    CommunicationAssessed,

    /// <summary>A new communication persisted; surfaces should refresh (design.md §5A.2/§5A.3). Envelope: <see cref="CommunicationEnvelope"/>.</summary>
    CommunicationArrived,

    // ── RESERVED — valid discriminator VALUES only; no envelope shape, no consumer wired ──

    /// <summary>RESERVED (design.md §10 #5). No envelope shape; no consumer.</summary>
    JobComplete,

    /// <summary>RESERVED (design.md §10 #5). No envelope shape; no consumer.</summary>
    Share,

    /// <summary>RESERVED (design.md §10 #5). No envelope shape; no consumer.</summary>
    SystemAlert,
}

/// <summary>
/// Fail-closed wire converter for <see cref="NotificationKind"/>. Maps each enum
/// member to its exact kebab-case wire value (design.md §10 #5: <c>suggestion</c>,
/// <c>communication-assessed</c>, <c>communication-arrived</c>, <c>job-complete</c>,
/// <c>share</c>, <c>system-alert</c>). An unrecognized wire string throws
/// <see cref="JsonException"/> — it never silently deserializes to a default/null
/// kind (the exact failure mode this taxonomy lock exists to prevent).
/// </summary>
public sealed class NotificationKindJsonConverter : JsonConverter<NotificationKind>
{
    private static readonly IReadOnlyDictionary<string, NotificationKind> WireToKind =
        new Dictionary<string, NotificationKind>(StringComparer.Ordinal)
        {
            ["suggestion"] = NotificationKind.Suggestion,
            ["communication-assessed"] = NotificationKind.CommunicationAssessed,
            ["communication-arrived"] = NotificationKind.CommunicationArrived,
            ["job-complete"] = NotificationKind.JobComplete,
            ["share"] = NotificationKind.Share,
            ["system-alert"] = NotificationKind.SystemAlert,
        };

    private static readonly IReadOnlyDictionary<NotificationKind, string> KindToWire =
        WireToKind.ToDictionary(pair => pair.Value, pair => pair.Key);

    public override NotificationKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;

        if (raw is not null && WireToKind.TryGetValue(raw, out var kind))
        {
            return kind;
        }

        throw new JsonException(
            $"Unrecognized notification 'kind' value '{raw ?? "<non-string>"}'. The kind taxonomy is a " +
            $"closed set (see {nameof(NotificationKind)}) — a novel/typo'd kind is rejected rather than " +
            "silently reaching a consumer that doesn't understand it.");
    }

    public override void Write(Utf8JsonWriter writer, NotificationKind value, JsonSerializerOptions options)
    {
        if (!KindToWire.TryGetValue(value, out var raw))
        {
            // Defensive: every enum member is mapped above; this only fires if a future
            // member is added without a corresponding wire-value entry.
            throw new JsonException($"No wire mapping registered for {nameof(NotificationKind)}.{value}.");
        }

        writer.WriteStringValue(raw);
    }
}
