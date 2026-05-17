using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Cosmos DB document representing a durable AI chat session (ADR-015 Tier 3: Work History).
///
/// Partition key: <c>/tenantId</c> — enforces tenant isolation and supports GDPR erasure
/// by partition delete.
///
/// Container: <c>sessions</c> (Cosmos DB database configured via CosmosPersistence:DatabaseName).
/// Retention: 90 days default (defined at container provisioning time — ADR-015).
/// </summary>
public class StoredSession
{
    /// <summary>Cosmos DB document id — matches sessionId.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Unique session identifier.</summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Tenant identifier. Used as the partition key (/tenantId) for all Cosmos DB operations.
    /// Required — every document must be scoped to a tenant (ADR-015, NFR-09).
    /// </summary>
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Playbook that governs this session's agent behaviour. Nullable for knowledge-only sessions.</summary>
    [JsonPropertyName("playbookId")]
    public Guid? PlaybookId { get; set; }

    /// <summary>Ordered message history for the session.</summary>
    [JsonPropertyName("messages")]
    public List<SessionMessage> Messages { get; set; } = [];

    /// <summary>
    /// Widget state dictionary keyed by widget instance ID.
    /// Stores serialised widget payloads so the three-pane UI can restore state on resume.
    /// </summary>
    [JsonPropertyName("widgetStates")]
    public Dictionary<string, string> WidgetStates { get; set; } = [];

    /// <summary>UTC timestamp when the session was first created.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the most recent message or state update.</summary>
    [JsonPropertyName("lastActivity")]
    public DateTimeOffset LastActivity { get; set; }
}
