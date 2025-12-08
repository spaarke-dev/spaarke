using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Event message for Document entity operations from Dataverse plugin.
/// Contains all context needed for background processing.
/// </summary>
public class DocumentEvent
{
    // Event Identification
    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Operation Context
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty; // Create, Update, Delete

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = string.Empty;

    // Entity Data
    [JsonPropertyName("entityData")]
    public Dictionary<string, object> EntityData { get; set; } = new();

    [JsonPropertyName("preEntityData")]
    public Dictionary<string, object>? PreEntityData { get; set; } // For Update operations

    // Processing Instructions
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 1; // 1=Normal, 2=High, 3=Critical

    [JsonPropertyName("processingDelay")]
    public TimeSpan ProcessingDelay { get; set; } = TimeSpan.Zero;

    [JsonPropertyName("maxRetryAttempts")]
    public int MaxRetryAttempts { get; set; } = 3;

    // Metadata
    [JsonPropertyName("source")]
    public string Source { get; set; } = "DocumentEventPlugin";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";
}
