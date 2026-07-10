using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Subject-partitioned store for <see cref="MemoryItem"/> v1 documents — the task-050
/// generalization of the retired matter-only <c>MatterMemoryService</c> (FR-B-01).
///
/// TWO active scopes (spec FR-B-01, operator refinement 2026-07-08):
/// <list type="bullet">
///   <item><b>Record</b> — keyed generically by <c>(subjectType, subjectId)</c> = (entityType, entityId).
///   Matters, projects, invoices, work assignments, events, documents — NOT matter-only.
///   Holds DERIVED/synthesized knowledge ONLY; the Context Binder reads live Dataverse fields
///   directly (FR-B-04), so record memory never duplicates them (guarded — see
///   <see cref="DataverseFieldMirrorGuard"/>).</item>
///   <item><b>User</b> — ONE general per-user store keyed by <c>userId</c>. The canonical
///   <c>userId</c> is the Dataverse <c>systemuserid</c> (operator ruling 2026-07-09), resolved
///   via the existing ADR-028 one-hop AAD-oid→systemuserid machinery.</item>
/// </list>
///
/// Storage: Cosmos container <c>memory-items</c>, <b>partitioned by SUBJECT</b> (<c>/subjectId</c> =
/// entityId for Record scope, userId for User scope) — NEVER <c>/tenantId</c> (dedicated-per-customer
/// environments make tenant a single hot logical partition against the 20 GB cap; isolation is the
/// deployment boundary). One document PER FACT (operator ruling 2026-07-09, aligned to MemoryItem v1)
/// with a DETERMINISTIC id derived from <c>(scope, fact.Type, fact.Key)</c> so a repeated capture for
/// the same key UPDATES the fact rather than accumulating duplicates/contradictions
/// (upsert-by-key supersession — memory hygiene under silent AI-initiated writes, FR-B-08).
///
/// ADR-040: this is NOT a parallel session cache. Record/User memory are CROSS-SESSION governed
/// objects; conversation memory stays the session-ledger facade and is rejected at the contract
/// layer (<see cref="MemoryItemContract.EnsureValidMemoryScope"/>).
///
/// ADR-015 Tier 3: user-owned, GDPR-erasable. <see cref="EraseSubjectAsync"/> hard-deletes every
/// document in a subject partition (Art. 17); the Tier 2 <c>audit</c> container is never touched.
///
/// Lifetime: Scoped (one instance per HTTP request); <c>CosmosClient</c> is singleton.
/// </summary>
public interface IMemoryItemStore
{
    /// <summary>
    /// Upserts one memory item. The document id is DETERMINISTIC over
    /// <c>(scope, fact.Type, fact.Key)</c>, so a second write for the same key within the same
    /// subject REPLACES the prior fact (supersession) — preserving the original
    /// <see cref="MemoryItem.CreatedAt"/>/<see cref="MemoryItem.CreatedBy"/> and stamping
    /// <see cref="MemoryItem.UpdatedAt"/>. Replacement uses ETag-based optimistic concurrency:
    /// a concurrent writer between the read and the write surfaces as a
    /// <see cref="Microsoft.Azure.Cosmos.CosmosException"/> with status 412 (retry with re-read).
    /// </summary>
    /// <param name="item">The item to persist. Scope + subject keying are validated; Record-scope
    /// facts that merely mirror a Dataverse field are rejected (FR-B-01).</param>
    /// <param name="tenantId">Tenant identifier stored as plain METADATA on the document
    /// (operator ruling 2026-07-09) — NOT the partition key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stamped item as persisted (deterministic <see cref="MemoryItem.Id"/>, version stamp).</returns>
    Task<MemoryItem> UpsertAsync(MemoryItem item, string tenantId, CancellationToken ct = default);

    /// <summary>Record-scope read: all items for exactly <c>(subjectType, subjectId)</c>. Single-partition point query — never crosses subject or scope.</summary>
    Task<IReadOnlyList<MemoryItem>> GetForRecordAsync(string subjectType, string subjectId, CancellationToken ct = default);

    /// <summary>User-scope read: all items for exactly <c>userId</c> (one general per-user store). Single-partition query.</summary>
    Task<IReadOnlyList<MemoryItem>> GetForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a single item by id within its subject partition (the FR-B-03 review/delete unit).
    /// Idempotent: silently succeeds when the item does not exist.
    /// </summary>
    /// <param name="subjectKey">The partition value — entityId (Record) or userId (User).</param>
    /// <param name="itemId">The document id of the item to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteItemAsync(string subjectKey, string itemId, CancellationToken ct = default);

    /// <summary>
    /// GDPR Art. 17 erasure: hard-deletes EVERY memory document in the subject partition
    /// (all facts for one record, or one user's entire user memory). Idempotent. Touches ONLY
    /// the Tier 3 <c>memory-items</c> container — the Tier 2 audit log is never affected
    /// (independent governance tiers per ADR-015).
    /// </summary>
    /// <param name="subjectKey">The partition value — entityId (Record) or userId (User).</param>
    /// <param name="ct">Cancellation token.</param>
    Task EraseSubjectAsync(string subjectKey, CancellationToken ct = default);

    /// <summary>
    /// Serializes a record subject's memory into a concise system-prompt fragment
    /// (same 500-token budget, confidence filtering, and truncation policy as the retired
    /// matter-only service — logic reused verbatim). For <c>subjectType == "matter"</c> the
    /// heading stays byte-identical to the legacy fragment
    /// (<c>### Matter Context (from prior sessions)</c>); other entity types render a
    /// generalized heading. Returns <see cref="string.Empty"/> when no memory exists.
    /// </summary>
    Task<string> ToRecordPromptFragmentAsync(string subjectType, string subjectId, CancellationToken ct = default);
}
