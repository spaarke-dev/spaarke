using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Audit;

/// <summary>
/// Compliance audit record for a single AI interaction (ADR-015 Tier 2: Compliance Audit).
///
/// Storage: Cosmos DB container <c>audit</c>, partition key <c>/tenantId</c>.
/// Retention: 7 years (configured at container provisioning time — no TTL on container).
/// Immutability: enforced by both infrastructure policy and code (CreateItemAsync only).
///
/// MUST NOT contain verbatim prompts or AI responses — hashes only (ADR-015 Tier 2).
/// </summary>
public sealed class AuditEntry
{
    /// <summary>
    /// Cosmos DB document id. Format: <c>{sessionId}_{newGuid}</c>.
    /// The compound format guarantees uniqueness within a session while remaining human-readable.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = $"{Guid.NewGuid()}_{Guid.NewGuid()}";

    /// <summary>
    /// Tenant identifier used as the Cosmos DB partition key (/tenantId).
    /// Every audit record is scoped to a single tenant — cross-tenant queries are blocked (ADR-015).
    /// </summary>
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    /// <summary>Azure AD object ID of the user who initiated the AI interaction.</summary>
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    /// <summary>Correlation identifier for the chat or analysis session.</summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    /// <summary>
    /// UTC timestamp of the AI interaction. Stored as ISO-8601 string for Cosmos DB indexing compatibility.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Action category for this audit entry.
    /// Allowed values: "chat_response", "tool_call", "document_access", "citation_generated".
    /// </summary>
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    /// <summary>
    /// Names of AI tools invoked during this interaction (tool names only, no arguments or outputs).
    /// Stored for compliance tracking — identifies which capabilities were exercised.
    /// </summary>
    [JsonPropertyName("toolsCalled")]
    public IReadOnlyList<string> ToolsCalled { get; init; } = [];

    /// <summary>
    /// Document IDs accessed during this interaction (GUIDs only, no content).
    /// Supports privilege audit trails by matter (ADR-015 Tier 2 identifiers allowed).
    /// </summary>
    [JsonPropertyName("documentsAccessed")]
    public IReadOnlyList<string> DocumentsAccessed { get; init; } = [];

    /// <summary>
    /// SHA-256 hex digest of the full AI response text.
    /// The raw response is never stored — the hash enables tamper detection without retaining content (ADR-015).
    /// Compute via <see cref="AuditHashHelper.HashResponse"/>.
    /// </summary>
    [JsonPropertyName("responseHash")]
    public required string ResponseHash { get; init; }

    /// <summary>
    /// Aggregated results from safety filter checks applied to this interaction.
    /// Includes prompt shield pass/fail, groundedness score, and citation verification count.
    /// </summary>
    [JsonPropertyName("safetyResults")]
    public SafetyCheckResult SafetyResults { get; init; } = new();

    /// <summary>
    /// Matter ID for privilege audit trail. Null when the interaction is not matter-scoped.
    /// When set, compliance officers can query audit records by matter for privilege review.
    /// </summary>
    [JsonPropertyName("matterContext")]
    public string? MatterContext { get; init; }
}

/// <summary>
/// Aggregated safety filter results for an AI interaction.
/// Stored in the Tier 2 audit log — contains scores and flags only, no content.
/// </summary>
public sealed class SafetyCheckResult
{
    /// <summary>True when the Azure Content Safety prompt shield passed for this interaction.</summary>
    [JsonPropertyName("promptShieldPassed")]
    public bool PromptShieldPassed { get; init; }

    /// <summary>
    /// Groundedness score (0.0–1.0) from the grounding evaluation step.
    /// Higher is more grounded. 1.0 if groundedness was not evaluated.
    /// </summary>
    [JsonPropertyName("groundednessScore")]
    public double GroundednessScore { get; init; } = 1.0;

    /// <summary>Number of citations that passed source verification.</summary>
    [JsonPropertyName("citationsVerified")]
    public int CitationsVerified { get; init; }
}
