using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Cosmos DB document shape for one persisted <see cref="MemoryItem"/> — the MemoryItem v1
/// contract flattened onto a Cosmos document plus the three storage-only members the contract
/// deliberately omits: <see cref="TenantId"/> (plain metadata, operator ruling 2026-07-09 —
/// NOT the partition key), <see cref="Ttl"/> (per-item Cosmos TTL; task 052 maps it from
/// <see cref="RetentionClass"/>), and <see cref="ETag"/> (optimistic concurrency). No third
/// shape is introduced (CLAUDE.md §11): every contract field maps 1:1 via
/// <see cref="FromItem"/> / <see cref="ToItem"/>.
///
/// Storage: container <c>memory-items</c>, partition key <c>/subjectId</c>.
/// <see cref="SubjectId"/> is the SUBJECT partition value — the entityId for Record scope, the
/// userId for User scope (mirroring <see cref="MemoryItem.PartitionKey"/>). One document per
/// fact; <see cref="Id"/> is DETERMINISTIC over <c>(scope, fact.Type, fact.Key)</c> so
/// upsert-by-key supersession falls out of the id scheme (see
/// <see cref="MemoryItemStore.BuildItemId"/>).
/// </summary>
public sealed class MemoryItemDocument
{
    /// <summary>Deterministic document id — <c>mem_{scope}_{factType}_{keyHash16}</c>. Unique within the subject partition; a repeated capture for the same key maps to the SAME id (supersession).</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// SUBJECT partition value (<c>/subjectId</c> partition key path): the entityId for Record
    /// scope, the userId for User scope. NEVER the tenant id (FR-B-01 MUST rule).
    /// </summary>
    [JsonPropertyName("subjectId")]
    public required string SubjectId { get; init; }

    /// <summary>Document discriminator — always <c>memory-item</c> (defensive; the container is dedicated today, but the discriminator keeps queries future-proof, matching the shared-container precedent).</summary>
    [JsonPropertyName("documentType")]
    public string DocumentType { get; init; } = MemoryItemStore.DocumentTypeValue;

    /// <summary>Tenant identifier — plain METADATA for diagnostics/portability (operator ruling 2026-07-09). NOT the partition key; deployments are customer-dedicated.</summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    /// <summary>Contract version stamp (<see cref="MemoryItemContract.SchemaVersion"/>).</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = MemoryItemContract.SchemaVersion;

    /// <summary>Memory scope discriminator: <see cref="MemoryScope.Record"/> or <see cref="MemoryScope.User"/>.</summary>
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary>Record scope: the entity TYPE (e.g. <c>matter</c>, <c>project</c>, <c>invoice</c>). Null for User scope.</summary>
    [JsonPropertyName("subjectType")]
    public string? SubjectType { get; init; }

    /// <summary>User scope: the owning user id (Dataverse <c>systemuserid</c> — canonical per operator ruling 2026-07-09). Null for Record scope.</summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    /// <summary>The structured fact — the existing <see cref="MemoryFact"/> reused verbatim.</summary>
    [JsonPropertyName("fact")]
    public required MemoryFact Fact { get; init; }

    /// <summary>Provenance origin class: <c>user | ai-derived | insights-engine</c> (<see cref="MemoryOrigin"/>). Metadata — never a gate (FR-B-08).</summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = MemoryOrigin.User;

    /// <summary>Provenance: the Binding whose invocation produced this item, when AI-derived.</summary>
    [JsonPropertyName("bindingId")]
    public string? BindingId { get; init; }

    /// <summary>Provenance: originating ledger entry ref (<c>{bindingId}@t{n}</c>), when applicable.</summary>
    [JsonPropertyName("ledgerRef")]
    public string? LedgerRef { get; init; }

    /// <summary>Provenance: originating session id, when captured during a session.</summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    /// <summary>Provenance: originating 1-based turn within the session, when applicable.</summary>
    [JsonPropertyName("turnId")]
    public int? TurnId { get; init; }

    /// <summary>Provenance trust level — carried, NOT acted on (enforcement deferred to the governance project, FR-B-08).</summary>
    [JsonPropertyName("trustLevel")]
    public string? TrustLevel { get; init; }

    /// <summary>Sensitivity classification — INERT field at this stage (operator ruling 2026-07-09: no enforcement machinery).</summary>
    [JsonPropertyName("sensitivity")]
    public string? Sensitivity { get; init; }

    /// <summary>Optional expiration instant carried for governance (task 052 maps retention to <see cref="Ttl"/>).</summary>
    [JsonPropertyName("expiration")]
    public DateTimeOffset? Expiration { get; init; }

    /// <summary>Deletion policy (e.g. <c>user-erasable</c>) — INERT field at this stage.</summary>
    [JsonPropertyName("deletionPolicy")]
    public string? DeletionPolicy { get; init; }

    /// <summary>Retention class (e.g. <c>tier-3-user-owned</c>) — task 052 maps this to the per-item <see cref="Ttl"/>.</summary>
    [JsonPropertyName("retentionClass")]
    public string? RetentionClass { get; init; }

    /// <summary>UTC instant the item was first created (preserved across upsert-by-key supersession).</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC instant of the last supersession update, when the item has been updated.</summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Identity that created the item (preserved across supersession).</summary>
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }

    /// <summary>
    /// Per-item Cosmos TTL in seconds. Null = no expiry (container default TTL is <c>-1</c>,
    /// which enables per-item TTL without a container-wide default). Task 052 sets this from
    /// <see cref="RetentionClass"/> — no reaper or custom expiry machinery exists by design
    /// (operator ruling 2026-07-09: minimal governance).
    /// </summary>
    [JsonPropertyName("ttl")]
    public int? Ttl { get; init; }

    /// <summary>Cosmos ETag for optimistic concurrency (same explicit-attribute pattern as the retired legacy document — camelCase policy does not map underscore-prefixed system properties).</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }

    /// <summary>Maps a validated contract item onto the document shape (storage-only members supplied by the store).</summary>
    public static MemoryItemDocument FromItem(MemoryItem item, string id, string subjectKey, string? tenantId) => new()
    {
        Id = id,
        SubjectId = subjectKey,
        TenantId = tenantId,
        Version = MemoryItemContract.SchemaVersion,
        Scope = item.Scope,
        SubjectType = item.SubjectType,
        UserId = item.UserId,
        Fact = item.Fact,
        Source = item.Source,
        BindingId = item.BindingId,
        LedgerRef = item.LedgerRef,
        SessionId = item.SessionId,
        TurnId = item.TurnId,
        TrustLevel = item.TrustLevel,
        Sensitivity = item.Sensitivity,
        Expiration = item.Expiration,
        DeletionPolicy = item.DeletionPolicy,
        RetentionClass = item.RetentionClass,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt,
        CreatedBy = item.CreatedBy,
    };

    /// <summary>Maps the document back to the MemoryItem v1 contract shape (the ONLY shape consumers see, per ADR-013).</summary>
    public MemoryItem ToItem() => new()
    {
        Version = Version,
        Id = Id,
        Scope = Scope,
        SubjectType = SubjectType,
        SubjectId = string.Equals(Scope, MemoryScope.Record, StringComparison.Ordinal) ? SubjectId : null,
        UserId = UserId,
        Fact = Fact,
        Source = Source,
        BindingId = BindingId,
        LedgerRef = LedgerRef,
        SessionId = SessionId,
        TurnId = TurnId,
        TrustLevel = TrustLevel,
        Sensitivity = Sensitivity,
        Expiration = Expiration,
        DeletionPolicy = DeletionPolicy,
        RetentionClass = RetentionClass,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        CreatedBy = CreatedBy,
    };
}
