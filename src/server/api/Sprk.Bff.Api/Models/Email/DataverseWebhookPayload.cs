using System.Text.Json.Serialization;
using Sprk.Bff.Api.Infrastructure.Json;

namespace Sprk.Bff.Api.Models.Email;

/// <summary>
/// Payload received from Dataverse Service Endpoint webhook.
/// Contains the entity context for the triggered event.
/// </summary>
public class DataverseWebhookPayload
{
    /// <summary>
    /// Message name (e.g., "Create", "Update", "Delete").
    /// </summary>
    [JsonPropertyName("MessageName")]
    public string? MessageName { get; set; }

    /// <summary>
    /// Primary entity logical name (e.g., "email").
    /// </summary>
    [JsonPropertyName("PrimaryEntityName")]
    public string? PrimaryEntityName { get; set; }

    /// <summary>
    /// Primary entity ID (the email activity ID).
    /// </summary>
    [JsonPropertyName("PrimaryEntityId")]
    public Guid PrimaryEntityId { get; set; }

    /// <summary>
    /// User ID that triggered the webhook.
    /// </summary>
    [JsonPropertyName("UserId")]
    public Guid? UserId { get; set; }

    /// <summary>
    /// Organization ID.
    /// </summary>
    [JsonPropertyName("OrganizationId")]
    public Guid? OrganizationId { get; set; }

    /// <summary>
    /// Business unit ID.
    /// </summary>
    [JsonPropertyName("BusinessUnitId")]
    public Guid? BusinessUnitId { get; set; }

    /// <summary>
    /// Depth of the plugin execution context.
    /// </summary>
    [JsonPropertyName("Depth")]
    public int Depth { get; set; }

    /// <summary>
    /// Stage of the plugin execution (10=PreValidation, 20=PreOperation, 40=PostOperation).
    /// </summary>
    [JsonPropertyName("Stage")]
    public int Stage { get; set; }

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    [JsonPropertyName("CorrelationId")]
    public Guid? CorrelationId { get; set; }

    /// <summary>
    /// Operation ID from Dataverse.
    /// </summary>
    [JsonPropertyName("OperationId")]
    public Guid? OperationId { get; set; }

    /// <summary>
    /// When the operation was created.
    /// Dataverse sends this in WCF date format: /Date(1234567890000)/
    /// </summary>
    [JsonPropertyName("OperationCreatedOn")]
    [JsonConverter(typeof(NullableWcfDateTimeConverter))]
    public DateTime? OperationCreatedOn { get; set; }

    /// <summary>
    /// Input parameters (entity images).
    /// </summary>
    [JsonPropertyName("InputParameters")]
    public List<WebhookParameter>? InputParameters { get; set; }

    /// <summary>
    /// Pre-entity images.
    /// </summary>
    [JsonPropertyName("PreEntityImages")]
    public List<WebhookEntityImage>? PreEntityImages { get; set; }

    /// <summary>
    /// Post-entity images.
    /// </summary>
    [JsonPropertyName("PostEntityImages")]
    public List<WebhookEntityImage>? PostEntityImages { get; set; }
}

/// <summary>
/// Input/output parameter in webhook payload.
/// </summary>
public class WebhookParameter
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

/// <summary>
/// Entity image in webhook payload.
/// </summary>
public class WebhookEntityImage
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("value")]
    public WebhookEntityData? Value { get; set; }
}

/// <summary>
/// Entity data within an entity image.
/// </summary>
public class WebhookEntityData
{
    [JsonPropertyName("Attributes")]
    public List<WebhookAttribute>? Attributes { get; set; }

    [JsonPropertyName("EntityState")]
    public object? EntityState { get; set; }

    [JsonPropertyName("FormattedValues")]
    public List<WebhookAttribute>? FormattedValues { get; set; }

    [JsonPropertyName("Id")]
    public Guid Id { get; set; }

    [JsonPropertyName("LogicalName")]
    public string? LogicalName { get; set; }
}

/// <summary>
/// Attribute key-value pair.
/// </summary>
public class WebhookAttribute
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

/// <summary>
/// Response for webhook trigger.
/// </summary>
public class WebhookTriggerResponse
{
    /// <summary>
    /// Whether the job was successfully queued.
    /// </summary>
    public bool Accepted { get; set; }

    /// <summary>
    /// Job ID for tracking.
    /// </summary>
    public Guid? JobId { get; set; }

    /// <summary>
    /// Correlation ID for tracing.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Message describing the result.
    /// </summary>
    public string? Message { get; set; }
}
