using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// A reference to a Dataverse entity that was in scope when the session was saved.
///
/// Stored inside <see cref="StoredSession.EntityRefs"/> and checked during restore
/// via ETag comparison to detect whether the entity has changed since the session
/// was persisted (ADR-015 D-08: data-refreshed restore, not stale snapshot).
///
/// ETag: the OData ETag header value returned by Dataverse at the time the session
/// was last written. Compared against a fresh HEAD/GET during <see cref="ISessionRestoreService.RestoreSessionAsync"/>
/// to surface staleness to the caller without silently ignoring it.
/// </summary>
public class SessionEntityRef
{
    /// <summary>Dataverse entity logical name (e.g., "opportunity", "sprk_matter").</summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Dataverse record GUID as a string (primary key).</summary>
    [JsonPropertyName("entityId")]
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// OData ETag value captured at session-save time (e.g., <c>W/"1234567"</c>).
    /// Null if ETag was not available when the session was persisted.
    /// </summary>
    [JsonPropertyName("savedETag")]
    public string? SavedETag { get; set; }

    /// <summary>Human-readable display name for the entity (for staleness warning messages).</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}
