using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// <c>GET /api/v1/external/notifications</c> (spaarke-SPA-external-access-platform-r3 task 037, FR-08): the caller's
/// derived in-portal feed, newest first, with the unread count against the caller's last-seen marker.
/// </summary>
public sealed class ExternalNotificationFeedResponse
{
    /// <summary>The feed items, newest first: at most 100, none older than 30 days.</summary>
    [JsonPropertyName("value")]
    public IReadOnlyList<ExternalNotificationDto> Value { get; init; } = [];

    /// <summary>How many items in <see cref="Value"/> are newer than <see cref="LastSeenAt"/> (all of them when it is null).</summary>
    [JsonPropertyName("unreadCount")]
    public int UnreadCount { get; init; }

    /// <summary>The caller's last-seen marker (set by <c>POST /notifications/seen</c>); null when none is stored.</summary>
    [JsonPropertyName("lastSeenAt")]
    public DateTimeOffset? LastSeenAt { get; init; }

    /// <summary>
    /// <c>true</c> when the feed is known to be incomplete: a bound cut it (more than 100 items, or more records or rows
    /// than one read scans) or a source read failed. Omitted when <c>false</c>, as on the other external collections.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Truncated { get; init; }
}

/// <summary>
/// One feed item. An allow-list: the item kind and id, the record it is about (route segment, id, title), when it
/// happened, the sender's display name for a message, and whether it is unread. No message body, subject, address,
/// privilege or internal-only value, and no contact or systemuser id, crosses this surface.
/// </summary>
public sealed class ExternalNotificationDto
{
    /// <summary>Item kind for a new grant to the caller.</summary>
    public const string KindGrant = "grant";

    /// <summary>Item kind for a new message by someone else on a record the caller can read.</summary>
    public const string KindMessage = "message";

    /// <summary><see cref="KindGrant"/> or <see cref="KindMessage"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    /// <summary>The source row id: the <c>sprk_externalrecordaccessid</c> of a grant, the <c>sprk_communicationid</c> of a message.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>
    /// The record's external route segment: <c>projects</c>, <c>matters</c>, <c>workassignments</c> or
    /// <c>servicerequests</c> — the same segment as <c>/api/v1/external/{recordType}/{recordId}/messages</c>.
    /// </summary>
    [JsonPropertyName("recordType")]
    public string RecordType { get; init; } = "";

    /// <summary>The record id.</summary>
    [JsonPropertyName("recordId")]
    public string RecordId { get; init; } = "";

    /// <summary>The record's display name; null when it could not be read.</summary>
    [JsonPropertyName("recordTitle")]
    public string? RecordTitle { get; init; }

    /// <summary>When it happened (ISO-8601, UTC): the grant row's creation or in-place renewal, or the message's <c>createdon</c>.</summary>
    [JsonPropertyName("occurredOn")]
    public DateTimeOffset OccurredOn { get; init; }

    /// <summary>For a message: the sender's display name (as the thread read gives it); null for a grant or an unrecorded sender.</summary>
    [JsonPropertyName("senderName")]
    public string? SenderName { get; init; }

    /// <summary>Whether the item is newer than the caller's last-seen marker (always true when no marker is stored).</summary>
    [JsonPropertyName("isUnread")]
    public bool IsUnread { get; init; }
}

/// <summary><c>POST /api/v1/external/notifications/seen</c>: the marker as stored.</summary>
public sealed class ExternalNotificationSeenResponse
{
    /// <summary>The new last-seen marker (server time, UTC).</summary>
    [JsonPropertyName("lastSeenAt")]
    public DateTimeOffset LastSeenAt { get; init; }
}
