using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Ai.Memory;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// <b>MemoryItem v1</b> — the versioned, tolerant-reader structured-memory OBJECT (spec FR-A0-03,
/// design D-M1; governance envelope per FR-B-02). It <b>extends the existing
/// <see cref="MemoryFact"/></b> (reusing its <c>Type / Key / Value / Source / ConfirmedByUser /
/// Confidence / RecordedAt</c> shape verbatim) and wraps it in a governance envelope plus generic
/// subject keying, so the Memory Service generalization (task 050), the envelope (task 051),
/// governance (task 052), the <see cref="ContextEnvelope"/> Memory slice (task 015), and
/// <c>memory.write</c> (task 057) all bind to THIS stable shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two scopes only — Record and User (NOT Conversation).</b> A MemoryItem is durable, semi-stable
/// knowledge keyed by SUBJECT:
/// <list type="bullet">
///   <item><b>Record scope</b> — keyed generically by <c>(entityType, entityId)</c> via
///   <see cref="SubjectType"/> / <see cref="SubjectId"/>. It is NOT matter-only: a project, invoice,
///   work-assignment, or any host record keys identically. TERMINOLOGY: this is the canonical
///   <b>Record scope <c>(entityType, entityId)</c></b>; Compose's "workspace-scope memory" is the
///   SAME concept under this canonical name.</item>
///   <item><b>User scope</b> — keyed by <see cref="UserId"/> (general per-user memory, not per-matter).</item>
/// </list>
/// Conversation memory is deliberately excluded: it stays the ADR-040 session-ledger facade, NOT a
/// MemoryItem store. <see cref="MemoryItemContract.EnsureValidMemoryScope"/> rejects a write to
/// <see cref="MemoryScope.Conversation"/>.
/// </para>
/// <para>
/// <b>Structured object, NOT an embedding.</b> A MemoryItem carries typed structured facts
/// (<see cref="Fact"/>) — never an embedding/vector. The contract has no embedding field, and
/// <see cref="MemoryItemContract.IsStructuredObjectNotEmbedding"/> asserts the serialized shape
/// carries none. Semantic-retrieval vectors are a SEPARATE concern
/// (<see cref="RetrievalReference"/> / design D-M3); retrieval results are never implicitly promoted
/// to memory.
/// </para>
/// <para>
/// <b>Provenance / governance envelope is METADATA, not a gate.</b> <c>memory.write</c> is
/// AI-initiated + silent (FR-B-08) — the envelope is written at capture time to DESCRIBE the item,
/// it does NOT gate the write. The user review/delete surface (FR-B-03) is the control. Provenance:
/// <see cref="Source"/> (<c>user | ai-derived | insights-engine</c>), <see cref="BindingId"/> /
/// <see cref="LedgerRef"/> (which action/ledger entry produced it), <see cref="SessionId"/> /
/// <see cref="TurnId"/>, and <see cref="TrustLevel"/>. <see cref="TrustLevel"/> MAY be present but its
/// ENFORCEMENT is DEFERRED to the governance project — it is carried, not acted on, in v1.
/// </para>
/// <para>
/// <b>Tier-3 erasure (ADR-015).</b> MemoryItems are user-owned, GDPR-erasable. The envelope carries
/// <see cref="Sensitivity"/>, <see cref="Expiration"/>, <see cref="DeletionPolicy"/>, and
/// <see cref="RetentionClass"/> so an erasure/retention sweep has the fields it needs.
/// </para>
/// <para>
/// <b>Partition by SUBJECT.</b> <see cref="PartitionKey"/> is the subject id (<see cref="SubjectId"/>
/// for Record scope, <see cref="UserId"/> for User scope) — NOT <c>/tenantId</c>. This mirrors the
/// D-M1 decision to partition the NEW memory container by subject in dedicated-per-customer envs.
/// </para>
/// <para>
/// <b>Tolerant-reader + additive-only versioning.</b> <see cref="Version"/> stamps every item with
/// <see cref="MemoryItemContract.SchemaVersion"/>. Consumers deserialize with
/// <see cref="MemoryItemContract.SerializerOptions"/>, which IGNORES unknown properties — a v1 reader
/// survives a future additive v1.x. Legacy matter-keyed <see cref="MemoryFact"/>s migrate via
/// <see cref="MemoryItemMigration.FromLegacyMatterFact"/> with sensible defaults
/// (<c>subjectType = "matter"</c>), no data loss.
/// </para>
/// <para>
/// <b>Not a MemoryItem: <c>AnchoredAnnotation</c>.</b> Compose's anchored annotation is
/// document-positional UI state (not governed memory); it stays in Compose's session payload and needs
/// no negotiated MemoryItem sub-type (Compose spec §ADR-Tensions Path-A).
/// </para>
/// </remarks>
public sealed record MemoryItem
{
    /// <summary>Contract version stamp — always <see cref="MemoryItemContract.SchemaVersion"/> for v1.</summary>
    public required string Version { get; init; }

    /// <summary>Stable memory-item id. Auto-generated on creation; referenced by <see cref="MemoryItemReference.ItemId"/>.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>Memory scope discriminator: <see cref="MemoryScope.Record"/> or <see cref="MemoryScope.User"/>.</summary>
    public required string Scope { get; init; }

    // ── Record-scope keying — generic (entityType, entityId) ──

    /// <summary>Record scope: the entity TYPE (e.g. <c>matter</c>, <c>project</c>, <c>invoice</c>). = entityType. Null for User scope.</summary>
    public string? SubjectType { get; init; }

    /// <summary>Record scope: the entity ID within its type. = entityId. Null for User scope.</summary>
    public string? SubjectId { get; init; }

    // ── User-scope keying ──

    /// <summary>User scope: the owning user id. Null for Record scope.</summary>
    public string? UserId { get; init; }

    // ── Reused structured fact (the extended MemoryFact) ──

    /// <summary>
    /// The structured fact — the EXISTING <see cref="MemoryFact"/> reused verbatim
    /// (<c>Type / Key / Value / Source / ConfirmedByUser / Confidence / RecordedAt</c>). Confidence and
    /// the low-level population source live here; the envelope wraps this, it does not duplicate it.
    /// </summary>
    public required MemoryFact Fact { get; init; }

    // ── Provenance envelope (METADATA, not a gate) ──

    /// <summary>Provenance origin class: <c>user | ai-derived | insights-engine</c> (<see cref="MemoryOrigin"/>). Metadata — does NOT gate the write.</summary>
    public string Source { get; init; } = MemoryOrigin.User;

    /// <summary>Provenance: the Binding whose invocation produced this item, when AI-derived. Identifier only.</summary>
    public string? BindingId { get; init; }

    /// <summary>Provenance: ledger entry ref (<c>{bindingId}@t{n}</c>) the capture originated from, when applicable.</summary>
    public string? LedgerRef { get; init; }

    /// <summary>Provenance: originating session id, when captured during a session.</summary>
    public string? SessionId { get; init; }

    /// <summary>Provenance: originating 1-based turn within the session, when applicable.</summary>
    public int? TurnId { get; init; }

    /// <summary>Provenance trust level (metadata). MAY be present; ENFORCEMENT is DEFERRED to the governance project (FR-B-08).</summary>
    public string? TrustLevel { get; init; }

    // ── Governance / Tier-3 erasure envelope (ADR-015) ──

    /// <summary>Sensitivity classification (e.g. <c>normal</c>, <c>confidential</c>) for downstream governance.</summary>
    public string? Sensitivity { get; init; }

    /// <summary>Optional expiration instant after which the item is eligible for retention cleanup.</summary>
    public DateTimeOffset? Expiration { get; init; }

    /// <summary>Deletion policy driving erasure (e.g. <c>user-erasable</c> — Tier-3 default).</summary>
    public string? DeletionPolicy { get; init; }

    /// <summary>Retention class (e.g. <c>tier-3-user-owned</c>) for retention/erasure sweeps (ADR-015).</summary>
    public string? RetentionClass { get; init; }

    // ── Audit ──

    /// <summary>UTC instant the item was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC instant of the last update to the item, when it has been updated.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Identity that created the item (user id / system principal), when known.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>
    /// Subject partition key — <see cref="SubjectId"/> for Record scope, <see cref="UserId"/> for User
    /// scope (NOT tenantId). Empty string when the keying field is unset. Not serialized (derived).
    /// </summary>
    [JsonIgnore]
    public string PartitionKey => string.Equals(Scope, MemoryScope.User, StringComparison.Ordinal)
        ? UserId ?? string.Empty
        : SubjectId ?? string.Empty;
}

/// <summary>Stable <see cref="MemoryItem.Scope"/> discriminator values. Additive-only.</summary>
public static class MemoryScope
{
    /// <summary>Record scope — keyed generically by <c>(entityType, entityId)</c> (SubjectType / SubjectId).</summary>
    public const string Record = "record";

    /// <summary>User scope — keyed by <c>userId</c>.</summary>
    public const string User = "user";

    /// <summary>
    /// Conversation scope — <b>NOT a MemoryItem scope</b>. Conversation memory stays the ADR-040
    /// ledger facade; a write to this scope is rejected by <see cref="MemoryItemContract.EnsureValidMemoryScope"/>.
    /// Declared here only so the rejection is a named, testable constant.
    /// </summary>
    public const string Conversation = "conversation";
}

/// <summary>Stable <see cref="MemoryItem.Source"/> provenance-origin values (metadata, not a gate). Additive-only.</summary>
public static class MemoryOrigin
{
    /// <summary>Originated from explicit user content/entry.</summary>
    public const string User = "user";

    /// <summary>Derived by the AI during analysis/session.</summary>
    public const string AiDerived = "ai-derived";

    /// <summary>Produced by the Insights Engine.</summary>
    public const string InsightsEngine = "insights-engine";
}

/// <summary>
/// Version stamp, canonical tolerant-reader serializer options, scope validation, and the
/// structured-object-not-embedding invariant for MemoryItem v1.
/// </summary>
public static class MemoryItemContract
{
    /// <summary>The v1 schema version stamped on every <see cref="MemoryItem.Version"/>.</summary>
    public const string SchemaVersion = "memory-item/v1";

    /// <summary>
    /// Canonical serializer options: camelCase names, unknown properties IGNORED (tolerant reader —
    /// a v1 consumer survives a future additive v1.x), enum-as-string, nulls omitted. This is the same
    /// serialization posture the existing memory service uses for its structured facts.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    /// <summary>The scopes a MemoryItem may be written to. Conversation is intentionally excluded (ADR-040).</summary>
    public static readonly IReadOnlySet<string> ValidScopes =
        new HashSet<string>(StringComparer.Ordinal) { MemoryScope.Record, MemoryScope.User };

    /// <summary>
    /// Guards the two-scope invariant: a MemoryItem may be written ONLY to Record or User scope.
    /// A Conversation-scope write throws (conversation memory is the ADR-040 ledger facade, not a
    /// MemoryItem store); any other unknown scope is an argument error.
    /// </summary>
    public static void EnsureValidMemoryScope(string scope)
    {
        if (string.Equals(scope, MemoryScope.Conversation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "MemoryItem MUST NOT be written to the 'conversation' scope — conversation memory stays the " +
                "ADR-040 session-ledger facade, not a MemoryItem store. Use Record or User scope.");
        }

        if (!ValidScopes.Contains(scope))
        {
            throw new ArgumentException(
                $"Unknown memory scope '{scope}'. MemoryItem supports only 'record' ((entityType,entityId)) and 'user' (userId).",
                nameof(scope));
        }
    }

    /// <summary>Field names that would make the object an embedding/vector rather than a structured memory object.</summary>
    public static readonly IReadOnlySet<string> ForbiddenEmbeddingFieldNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "embedding", "embeddings", "vector", "vectors" };

    /// <summary>
    /// Structured-object invariant (D-M1): true iff the serialized item carries NO embedding/vector field
    /// — i.e. memory is a structured object, not a vector. Asserted against the serialized JSON so a field
    /// injected downstream is still caught.
    /// </summary>
    public static bool IsStructuredObjectNotEmbedding(JsonElement serializedItem)
    {
        if (serializedItem.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in serializedItem.EnumerateObject())
        {
            if (ForbiddenEmbeddingFieldNames.Contains(property.Name))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Reference PRODUCER (walking skeleton): validates scope, stamps the version, and persists a
/// <see cref="MemoryItem"/> via a caller-supplied sink. Pure over the sink — no DI, no I/O of its own —
/// so task 050 can drop in the real (generalized) memory service without changing the contract.
/// </summary>
public static class MemoryItemWriter
{
    /// <summary>
    /// Writes <paramref name="item"/> through <paramref name="persist"/> after enforcing the two-scope
    /// invariant and stamping <see cref="MemoryItemContract.SchemaVersion"/>. Returns the stamped item.
    /// </summary>
    public static MemoryItem Write(MemoryItem item, Action<MemoryItem> persist)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persist);

        MemoryItemContract.EnsureValidMemoryScope(item.Scope);

        var stamped = string.Equals(item.Version, MemoryItemContract.SchemaVersion, StringComparison.Ordinal)
            ? item
            : item with { Version = MemoryItemContract.SchemaVersion };

        persist(stamped);
        return stamped;
    }
}

/// <summary>
/// Reference CONSUMER (walking skeleton): reads MemoryItems back from a store HONORING scope. Pure
/// filters over an enumerable store; the production reader (task 050) replaces the enumerable with a
/// subject-partitioned Cosmos query but keeps the same scope-honoring shape.
/// </summary>
public static class MemoryItemReader
{
    /// <summary>Record-scope read: items for exactly <c>(subjectType, subjectId)</c>. Never crosses scope or subject.</summary>
    public static IReadOnlyList<MemoryItem> ForRecord(IEnumerable<MemoryItem> store, string subjectType, string subjectId)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store
            .Where(i => string.Equals(i.Scope, MemoryScope.Record, StringComparison.Ordinal)
                        && string.Equals(i.SubjectType, subjectType, StringComparison.Ordinal)
                        && string.Equals(i.SubjectId, subjectId, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>User-scope read: items for exactly <c>userId</c>. Never crosses scope or user.</summary>
    public static IReadOnlyList<MemoryItem> ForUser(IEnumerable<MemoryItem> store, string userId)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store
            .Where(i => string.Equals(i.Scope, MemoryScope.User, StringComparison.Ordinal)
                        && string.Equals(i.UserId, userId, StringComparison.Ordinal))
            .ToList();
    }
}

/// <summary>
/// Tolerant-reader MIGRATION of legacy matter-keyed <see cref="MemoryFact"/>s into MemoryItem v1.
/// The existing memory subsystem is matter-only; a legacy fact reads as a Record-scope MemoryItem with
/// sensible defaults (<c>subjectType = "matter"</c>) and NO data loss — the fact is reused verbatim.
/// </summary>
public static class MemoryItemMigration
{
    /// <summary>Default Record subjectType applied to legacy matter-keyed facts.</summary>
    public const string LegacySubjectType = "matter";

    /// <summary>
    /// Reads a legacy matter-keyed <see cref="MemoryFact"/> as a MemoryItem v1: Record scope,
    /// <c>subjectType = "matter"</c>, <c>subjectId = matterId</c>, provenance origin mapped from the
    /// fact's low-level source, Tier-3 retention defaults, and <see cref="MemoryFact.RecordedAt"/>
    /// preserved as <see cref="MemoryItem.CreatedAt"/>. No field of the fact is dropped.
    /// </summary>
    public static MemoryItem FromLegacyMatterFact(MemoryFact fact, string matterId, string? createdBy = null)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentException.ThrowIfNullOrEmpty(matterId);

        return new MemoryItem
        {
            Version = MemoryItemContract.SchemaVersion,
            Scope = MemoryScope.Record,
            SubjectType = LegacySubjectType,
            SubjectId = matterId,
            Fact = fact,
            Source = MapLegacySource(fact.Source),
            CreatedAt = fact.RecordedAt,
            CreatedBy = createdBy,
            DeletionPolicy = "user-erasable",
            RetentionClass = "tier-3-user-owned",
        };
    }

    /// <summary>Maps a legacy <see cref="MemoryFact.Source"/> to a v1 provenance origin. Unknown/import/admin → user (trusted default).</summary>
    private static string MapLegacySource(string factSource) => factSource switch
    {
        "ai-extraction" => MemoryOrigin.AiDerived,
        _ => MemoryOrigin.User,
    };
}
