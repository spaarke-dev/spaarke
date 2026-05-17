using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Persistent, structured per-matter AI memory document stored in the Cosmos DB <c>memory</c> container.
///
/// Storage: Cosmos DB container <c>memory</c>, partition key <c>/tenantId</c> (ADR-015 Tier 3).
/// Retention: 90 days default (configurable at container provisioning time).
/// GDPR: <see cref="IMatterMemoryService.ClearMemoryAsync"/> deletes this document on user request (Art. 17).
///
/// Document id format: <c>{tenantId}_{matterId}</c> — one document per matter per tenant.
/// The composite id guarantees a single document per matter while remaining human-readable.
///
/// Optimistic concurrency: <see cref="ETag"/> is read from the Cosmos DB response header and stored
/// here so that concurrent writers (multiple active sessions for the same matter) can detect conflicts
/// via <c>ItemRequestOptions.IfMatchEtag</c>. <see cref="Version"/> is incremented on every write as a
/// higher-level monotonic counter visible to application code without needing to inspect HTTP headers.
/// </summary>
public sealed class MatterMemory
{
    /// <summary>
    /// Cosmos DB document id. Format: <c>{tenantId}_{matterId}</c>.
    /// Unique per (tenant, matter) pair; human-readable for support/debugging.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Tenant identifier — also the Cosmos DB partition key (/tenantId).
    /// All queries and writes are scoped to a single tenant (ADR-015).
    /// </summary>
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    /// <summary>Matter (case/file) identifier within the tenant.</summary>
    [JsonPropertyName("matterId")]
    public required string MatterId { get; init; }

    /// <summary>
    /// All structured facts known about this matter, ordered by <see cref="MemoryFact.RecordedAt"/> ascending.
    /// The list may grow over multiple sessions; facts are appended via
    /// <see cref="IMatterMemoryService.SaveFactAsync"/> rather than replaced.
    /// </summary>
    [JsonPropertyName("facts")]
    public List<MemoryFact> Facts { get; init; } = [];

    /// <summary>
    /// UTC timestamp of the last write to this memory document.
    /// Updated on every <see cref="IMatterMemoryService.SaveFactAsync"/> call.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Monotonic version counter. Incremented on every successful write.
    /// Provides an application-level concurrency indicator independent of <see cref="ETag"/>.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>
    /// Cosmos DB ETag for optimistic concurrency.
    /// Read from <c>ItemResponse&lt;T&gt;.ETag</c> after each read and stored here
    /// so the next write can pass it via <c>ItemRequestOptions.IfMatchEtag</c>.
    ///
    /// The <c>[JsonPropertyName("_etag")]</c> attribute maps to the Cosmos system property.
    /// Setting <c>CosmosSerializerOptions.PropertyNamingPolicy = CamelCase</c> does NOT
    /// automatically serialise underscore-prefixed system properties, so the explicit
    /// attribute is required.
    /// </summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
