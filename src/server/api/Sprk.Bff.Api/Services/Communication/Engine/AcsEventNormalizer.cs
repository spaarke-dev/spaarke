using System.Text.Json;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// Maps an ACS chat Event Grid event (carried on <see cref="IncomingMessagingJobPayload"/>) into the
/// channel-neutral <see cref="NormalizedMessage"/> envelope. This is the ACS analog of
/// <see cref="GraphMessageNormalizer"/> and the SINGLE ACS→envelope boundary (ADR-045 / FR-02): downstream
/// the ingestor, Association Engine, and enrichment see only <see cref="NormalizedMessage"/> — never an
/// <c>Azure.Communication.*</c> type. The mapping reads the raw Event Grid JSON directly (no ACS SDK types
/// are deserialized), so the "no ACS type leaks past the boundary" invariant holds trivially.
/// </summary>
/// <remarks>
/// <para>Pure mapping — no I/O. Task 030's ingress already extracted the message/thread ids into the payload;
/// this mapper reads the remaining message fields (sender identity, body, compose time) from
/// <see cref="IncomingMessagingJobPayload.RawEvent"/>.</para>
/// <para>Event Grid nests message fields under <c>data</c>; the offline-simulator fixtures
/// (<c>notes/spikes/acs-harness/EventSchemas.cs</c>) flatten them to the top level. This mapper reads
/// <c>data</c> first and falls back to the top level, mirroring the ingress's <c>BuildJob</c>.</para>
/// </remarks>
public sealed class AcsEventNormalizer
{
    /// <summary>
    /// Maps the ACS chat event on <paramref name="payload"/> to an inbound <see cref="NormalizedMessage"/>.
    /// The ACS <c>ChatThreadId</c> becomes <see cref="NormalizedMessage.ConversationId"/>; the ACS message id
    /// is carried separately by the caller as the dedupe key (<c>ProviderMessageId</c>), not on the envelope.
    /// </summary>
    public NormalizedMessage Normalize(IncomingMessagingJobPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var root = payload.RawEvent;
        var hasData = TryGetObject(root, "data", out var data);

        // Body: ACS chat messages are plain text ("messageType": "Text"). No HTML variant in R1.
        var body = GetString(data, hasData, root, "messageBody");

        // Compose time is the ACS-authoritative send timestamp; fall back to the Event Grid eventTime.
        var sentAt = TryGetDateTimeOffset(data, hasData, root, "composeTime") ?? payload.EventTime;

        return new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            From = ResolveSender(data, hasData, root),
            // ACS chat is thread-scoped; the event carries no explicit To/Cc list. Recipients are the
            // thread participants (a Dataverse-authoritative projection resolved elsewhere), not the event.
            To = Array.Empty<string>(),
            Cc = Array.Empty<string>(),
            Subject = null,
            BodyText = body,
            BodyHtml = null,
            InternetMessageId = null,
            InReplyTo = null,
            References = Array.Empty<string>(),
            ConversationId = payload.AcsThreadId,
            SentAt = sentAt,
            // ACS chat events do not carry file attachments in R1 (attachments arrive via SPE, not chat).
            Attachments = Array.Empty<NormalizedAttachment>(),
        };
    }

    /// <summary>
    /// Resolves the sender identity from <c>senderCommunicationIdentifier</c>. Prefers a human-readable
    /// display name when present, else the raw ACS identifier (<c>rawId</c> or <c>communicationUser.id</c>).
    /// </summary>
    private static string? ResolveSender(JsonElement data, bool hasData, JsonElement root)
    {
        var displayName = GetString(data, hasData, root, "senderDisplayName");
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName;

        var source = hasData ? data : root;
        if (source.ValueKind == JsonValueKind.Object
            && source.TryGetProperty("senderCommunicationIdentifier", out var sender)
            && sender.ValueKind == JsonValueKind.Object)
        {
            var rawId = GetStringProp(sender, "rawId");
            if (!string.IsNullOrWhiteSpace(rawId))
                return rawId;

            if (sender.TryGetProperty("communicationUser", out var user)
                && user.ValueKind == JsonValueKind.Object)
            {
                var userId = GetStringProp(user, "id");
                if (!string.IsNullOrWhiteSpace(userId))
                    return userId;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement data, bool hasData, JsonElement root, string property)
        => (hasData ? GetStringProp(data, property) : null) ?? GetStringProp(root, property);

    private static string? GetStringProp(JsonElement e, string property)
    {
        if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
        return null;
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement data, bool hasData, JsonElement root, string property)
    {
        var source = hasData && data.TryGetProperty(property, out _) ? data : root;
        if (source.ValueKind == JsonValueKind.Object
            && source.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.TryGetDateTimeOffset(out var dto))
        {
            return dto;
        }
        return null;
    }

    private static bool TryGetObject(JsonElement e, string property, out JsonElement value)
    {
        if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty(property, out value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }
        value = default;
        return false;
    }
}
