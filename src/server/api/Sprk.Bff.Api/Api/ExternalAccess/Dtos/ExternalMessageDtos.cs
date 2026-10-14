using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// One message on a record's thread, as the external plane returns it (spaarke-SPA-external-access-platform-r3
/// task 033, FR-04). An explicit allow-list: id, subject, body (with its format, so the client renders HTML as HTML),
/// sender display name, created on, direction and a caller-relative isMine flag. Nothing else crosses this surface — not the sender's systemuser id,
/// not the From/To addresses, not the privilege classification, not the internal-only flag, not attachments. JSON names
/// follow the other external DTOs (Dataverse logical names).
/// </summary>
public sealed class ExternalMessageDto
{
    /// <summary><c>sprk_communicationid</c>.</summary>
    [JsonPropertyName("sprk_communicationid")]
    public string SprkCommunicationid { get; init; } = "";

    /// <summary><c>sprk_subject</c>; null when unset.</summary>
    [JsonPropertyName("sprk_subject")]
    public string? SprkSubject { get; init; }

    /// <summary><c>sprk_body</c>; null when unset.</summary>
    [JsonPropertyName("sprk_body")]
    public string? SprkBody { get; init; }

    /// <summary><c>sprk_bodyformat</c>: 100000000 PlainText, 100000001 HTML; null when unset.</summary>
    [JsonPropertyName("sprk_bodyformat")]
    public int? SprkBodyformat { get; init; }

    /// <summary><c>sprk_direction</c>: 100000000 Incoming, 100000001 Outgoing; null when unset.</summary>
    [JsonPropertyName("sprk_direction")]
    public int? SprkDirection { get; init; }

    /// <summary>The sender's display name: the <c>sprk_sentby</c> systemuser's name, else (task 034) the
    /// <c>sprk_createdbycontact</c> contact's name; null when no sender is recorded (e.g. inbound mail).</summary>
    [JsonPropertyName("sendername")]
    public string? SenderName { get; init; }

    /// <summary><c>createdon</c> (ISO-8601, UTC); the thread is ordered by it, oldest first.</summary>
    [JsonPropertyName("createdon")]
    public DateTimeOffset? Createdon { get; init; }

    /// <summary>
    /// Whether the CALLER authored this message (task 034, for own-vs-other rendering): a contact caller's own
    /// <c>sprk_createdbycontact</c> post, or a systemuser caller's own <c>sprk_sentby</c> message. A boolean only —
    /// no contact or systemuser id crosses this surface.
    /// </summary>
    [JsonPropertyName("isMine")]
    public bool IsMine { get; init; }
}

/// <summary>
/// The body of <c>POST /api/v1/external/{root}/{id}/messages</c> (spaarke-SPA-external-access-platform-r3 task 034, FR-11):
/// the message text only. A closed DTO — a sender, from, creator or format named in the request does not bind, so the
/// sender is always the resolved caller and the body is always stored as plain text.
/// </summary>
public sealed class PostExternalMessageRequest
{
    /// <summary>The message text (required, at most <c>CommunicationService.MaxThreadPostBodyLength</c> characters).
    /// Stored verbatim as plain text.</summary>
    [JsonPropertyName("sprk_body")]
    public string? SprkBody { get; init; }

    /// <summary>
    /// Every other property of the request. Captured only so the route can REFUSE an attachment (FR-11: posts carry no
    /// attachments) instead of silently dropping it; every other extra property is ignored.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Other { get; init; }
}
