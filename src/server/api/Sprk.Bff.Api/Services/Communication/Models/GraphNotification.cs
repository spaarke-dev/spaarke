using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Collection of change notifications sent by Microsoft Graph webhook subscriptions.
/// Graph delivers notifications as a JSON array in the "value" property.
/// </summary>
public sealed class GraphChangeNotificationCollection
{
    [JsonPropertyName("value")]
    public GraphChangeNotification[] Value { get; init; } = Array.Empty<GraphChangeNotification>();
}

/// <summary>
/// A single change notification from a Microsoft Graph webhook subscription.
/// Contains metadata about the change (subscription, resource, type) and
/// the clientState used for authenticity validation.
/// </summary>
public sealed class GraphChangeNotification
{
    [JsonPropertyName("subscriptionId")]
    public string? SubscriptionId { get; init; }

    [JsonPropertyName("clientState")]
    public string? ClientState { get; init; }

    [JsonPropertyName("changeType")]
    public string? ChangeType { get; init; }

    [JsonPropertyName("resource")]
    public string? Resource { get; init; }

    [JsonPropertyName("subscriptionExpirationDateTime")]
    public DateTimeOffset? SubscriptionExpirationDateTime { get; init; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    [JsonPropertyName("resourceData")]
    public GraphResourceData? ResourceData { get; init; }
}

/// <summary>
/// Resource data included in a Graph change notification.
/// Contains the OData type and ID of the changed resource.
/// </summary>
public sealed class GraphResourceData
{
    [JsonPropertyName("@odata.type")]
    public string? ODataType { get; init; }

    [JsonPropertyName("@odata.id")]
    public string? ODataId { get; init; }

    [JsonPropertyName("@odata.etag")]
    public string? ODataEtag { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>
/// Response returned when the webhook accepts valid notifications.
/// </summary>
public sealed class IncomingWebhookResponse
{
    /// <summary>Whether the notifications were accepted for processing.</summary>
    public bool Accepted { get; init; }

    /// <summary>Number of notifications received in the batch.</summary>
    public int NotificationsReceived { get; init; }

    /// <summary>Number of notifications enqueued for processing (after deduplication).</summary>
    public int NotificationsEnqueued { get; init; }

    /// <summary>Correlation ID for tracing.</summary>
    public string? CorrelationId { get; init; }
}
