# Contract: MemoryItem v1 (task 016 · FR-A0-03 / FR-B-02 / D-M1)

> **Status**: contract green on branch (10/10 tests). Unblocks Compose r2 **FR-30** (persist AI-derived insights).
> **Home**: `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/MemoryItem.cs` (ADR-013 facade).
> **Test**: `tests/integration/contract/Api/Ai/MemoryItemContractTests.cs` (self-contained; ADR-038 KEEP-path).

## What it is

The versioned, tolerant-reader **structured-memory OBJECT** (NOT an embedding) plus its governance
envelope. It **extends the existing `MemoryFact`** (reuses `Type/Key/Value/Source/ConfirmedByUser/
Confidence/RecordedAt` verbatim by composition — `MemoryItem.Fact`) and generalizes the matter-only
memory subsystem off `sprk_matter` to two subject-keyed scopes.

## Two scopes (Record + User) — NOT Conversation

| Scope | Keyed by | Field(s) | Partition key |
|---|---|---|---|
| **Record** | generic `(entityType, entityId)` | `SubjectType` (= entityType), `SubjectId` (= entityId) | `SubjectId` |
| **User** | `userId` | `UserId` | `UserId` |

- **Terminology**: the canonical name is **Record scope `(entityType, entityId)`**. Compose's
  "workspace-scope memory" is the SAME concept under this canonical name.
- **NOT matter-only**: a project / invoice / work-assignment keys identically. The test proves a
  non-matter entity keys correctly and that the SAME id under a different `entityType` is a distinct subject.
- **Conversation is excluded**: conversation memory stays the ADR-040 ledger facade. A write to
  `MemoryScope.Conversation` is rejected by `MemoryItemContract.EnsureValidMemoryScope`
  (`InvalidOperationException`); any other unknown scope is an `ArgumentException`.
- **Partition by SUBJECT** (`entityId`/`userId`), NOT `/tenantId` — mirrors the D-M1 NEW-container decision.

## MemoryFact → MemoryItem field map

| Concern | Source | Notes |
|---|---|---|
| Structured fact (`Type/Key/Value/Source/ConfirmedByUser/Confidence/RecordedAt`) | reused `MemoryFact` via `MemoryItem.Fact` | verbatim; confidence + low-level population source live here |
| Scope discriminator | `Scope` (`record`\|`user`) | aligns with `ContextEnvelope.MemoryItemReference.Scope` |
| Record keying | `SubjectType` / `SubjectId` | = (entityType, entityId) |
| User keying | `UserId` | |
| Version | `Version` = `memory-item/v1` | tolerant-reader gate |
| Identity | `Id` (GUID) | referenced by `MemoryItemReference.ItemId` |

## Provenance / governance envelope — METADATA, NOT a gate

`memory.write` is AI-initiated + silent (FR-B-08). The envelope DESCRIBES the item; it does NOT gate the
write. The user review/delete surface (FR-B-03) is the control. A minimal write (source only, no
`trustLevel`/`bindingId`) still succeeds — proven by the test.

- **Provenance**: `Source` (`user` \| `ai-derived` \| `insights-engine`), `BindingId`, `LedgerRef`
  (`{bindingId}@t{n}`), `SessionId`, `TurnId`, `TrustLevel`.
- **`TrustLevel` enforcement is DEFERRED** to the governance project — carried, not acted on, in v1.
- **Tier-3 erasure (ADR-015)**: `Sensitivity`, `Expiration`, `DeletionPolicy`, `RetentionClass` support
  GDPR-erasable, user-owned memory.
- **Audit**: `CreatedAt`, `UpdatedAt`, `CreatedBy`.

## Structured object, NOT an embedding (D-M1 invariant)

No embedding/vector field exists on the contract. `MemoryItemContract.IsStructuredObjectNotEmbedding`
asserts the serialized shape carries none (`embedding`/`embeddings`/`vector`/`vectors`). Semantic-retrieval
vectors are a SEPARATE concern (`RetrievalReference` / D-M3) — retrieval results are never implicitly
promoted to memory.

## Tolerant-reader migration of legacy matter-keyed facts

`MemoryItemMigration.FromLegacyMatterFact(fact, matterId)`:
- Record scope, `subjectType = "matter"`, `subjectId = matterId`.
- `Source` mapped from the fact's low-level source (`ai-extraction` → `ai-derived`; else → `user`).
- `RetentionClass = tier-3-user-owned`, `DeletionPolicy = user-erasable`.
- `CreatedAt` = `fact.RecordedAt` (preserved). The `MemoryFact` is reused verbatim — **no data loss**.

Versioning is **additive-only**: new optional fields + additive scope/source constants, never a rename
or type change. Consumers use `MemoryItemContract.SerializerOptions` (camelCase, unknown members skipped).

## NOT a MemoryItem: `AnchoredAnnotation`

Compose's `AnchoredAnnotation` is document-positional UI state (not governed memory). It stays in
Compose's session payload and needs **no** negotiated MemoryItem sub-type (Compose spec §ADR-Tensions
Path-A).

## Example payload (v1 wire)

```json
{
  "version": "memory-item/v1",
  "id": "8f3c…",
  "scope": "record",
  "subjectType": "project",
  "subjectId": "proj-42",
  "fact": { "type": "KeyFact", "key": "Contract Value", "value": "$2.4M", "source": "ai-extraction", "confidence": 0.9, "recordedAt": "2026-07-08T12:00:00+00:00" },
  "source": "ai-derived",
  "bindingId": "bind-1",
  "ledgerRef": "bind-1@t3",
  "sessionId": "sess-1",
  "turnId": 3,
  "trustLevel": "unverified",
  "sensitivity": "normal",
  "deletionPolicy": "user-erasable",
  "retentionClass": "tier-3-user-owned",
  "createdBy": "agent",
  "createdAt": "2026-07-08T12:00:00+00:00"
}
```

## Reference producer / consumer (walking skeleton)

- `MemoryItemWriter.Write(item, persist)` — validates scope, stamps version, persists via a caller sink
  (task 050 drops in the real generalized service unchanged).
- `MemoryItemReader.ForRecord/ForUser(store, …)` — pure scope-honoring filters (task 050 swaps the
  enumerable for a subject-partitioned Cosmos query, same shape).

## Placement Justification (CLAUDE.md §10/§11)

- **Existing**: overlaps the matter-only `MemoryFact`/`MatterMemoryService`. **Extends** it (composition +
  generic keying + envelope) rather than greenfielding a new memory model.
- **Location**: `Services/Ai/PublicContracts/` — the ADR-013 facade every consumer reaches memory through.
- **Cost-of-doing-nothing**: without a frozen object+envelope shape, tasks 050/051/052/015/057 and Compose
  FR-30 would each bind to divergent memory shapes → schema drift at integration.
- **Publish-size**: additive — one DTO source file + static helpers, **zero new NuGet/dependencies**;
  delta effectively 0 MB (well under the 60 MB ceiling / +5 MB per-task threshold). No new CVE surface.
