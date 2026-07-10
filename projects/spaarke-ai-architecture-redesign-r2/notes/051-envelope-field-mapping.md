# Task 051 — Governance envelope field mapping + tolerant-reader defaults

> **Date**: 2026-07-10 · **Task**: AIR2-051 (FR-B-02) · **Status**: executed (SHRANK scope — see note)
>
> **SHRANK note**: the POML was authored before task 050 landed. 050's `MemoryItemDocument` already
> carries the FULL envelope (every MemoryItem v1 field + 3 storage members), and the POML's
> `MatterMemoryService.cs` input was deleted by 050. What remained for 051: this documented mapping,
> the tolerant-reader default tests, the inert `trustLevel` negative tests, and the
> `source: insights-engine` construction test. No production-code change was needed — the criterion
> "MemoryFact carries the envelope" is satisfied by composition: **MemoryItem v1 wraps the existing
> `MemoryFact` verbatim** (`MemoryItem.Fact`), which IS the mandated reuse (no field duplicated).

## Where each FR-B-02 envelope concept lives

| Envelope concept (POML/spec) | Lives on | Field | Reused or new? |
|---|---|---|---|
| scope (record \| user) | MemoryItem / Document | `Scope` (+ `MemoryScope` constants) | NEW (016) |
| subjectType / subjectId | MemoryItem / Document | `SubjectType` / `SubjectId` | NEW (016) — Record keying |
| userId | MemoryItem / Document | `UserId` (Dataverse `systemuserid`) | NEW (016) — User keying |
| source (origin class) | MemoryItem / Document | `Source` (`user \| ai-derived \| insights-engine`) | NEW (016). NOTE: `MemoryFact.Source` is the LOW-LEVEL population source ("user"/"ai-extraction"/"import") and is reused untouched — the two are different granularities, documented on `MemoryItem.Fact`. |
| sessionId / turnId provenance | MemoryItem / Document | `SessionId` / `TurnId` (+ `BindingId`, `LedgerRef`) | NEW (016) |
| confidence | **MemoryFact (reused)** | `Fact.Confidence` | REUSED — not duplicated on the envelope |
| sensitivity | MemoryItem / Document | `Sensitivity` | NEW (016) — **INERT** (operator ruling 2026-07-09) |
| expiration | MemoryItem / Document | `Expiration` | NEW (016) — 052 maps retention to Cosmos `ttl` |
| deletionPolicy | MemoryItem / Document | `DeletionPolicy` | NEW (016) — **INERT** |
| retentionClass | MemoryItem / Document | `RetentionClass` | NEW (016) — 052 maps → per-item `ttl` |
| created / createdBy | MemoryItem / Document | `CreatedAt` / `CreatedBy` (legacy facts: `Fact.RecordedAt` preserved as `CreatedAt` via migration) | NEW (016), reusing `RecordedAt` on read-forward |
| updated | MemoryItem / Document | `UpdatedAt` (stamped by upsert-by-key supersession, 050) | NEW (016) |
| sourceTrustLevel | MemoryItem / Document | `TrustLevel` | NEW (016) — **INERT**: carried verbatim, participates in NO deny path (FR-B-08; enforcement deferred to the governance project). Pinned by negative tests. |
| Type / Key / Value / ConfirmedByUser | **MemoryFact (reused verbatim)** | `Fact.*` | REUSED — grouping, upsert-by-key identity (050), and confidence filtering all ride these |

Storage-only members (NOT contract fields, per ADR-013 — consumers see only MemoryItem v1 via
`ToItem()`): `tenantId` (plain metadata, never the partition), `ttl` (per-item Cosmos TTL; null =
no expiry), `_etag` (optimistic concurrency), `documentType` (discriminator `memory-item`).

## Tolerant-reader defaults (pinned by `MemoryItemEnvelopeTests`)

A persisted document missing the optional envelope fields deserializes with:

| Field | Default when absent |
|---|---|
| `documentType` | `"memory-item"` |
| `version` | `MemoryItemContract.SchemaVersion` (`memory-item/v1`) |
| `source` | `"user"` (trusted origin class) |
| `tenantId`, `userId`, `bindingId`, `ledgerRef`, `sessionId`, `turnId`, `trustLevel`, `sensitivity`, `expiration`, `deletionPolicy`, `retentionClass`, `updatedAt`, `createdBy`, `ttl`, `_etag` | `null` |
| `createdAt` | `DateTimeOffset.UtcNow` at read time (only reachable for docs written without a stamp; the store always stamps it) |

Required members (present in every persisted document by construction): `id`, `subjectId`, `scope`,
`fact` (whose own required members are `type`, `key`, `value`). Unknown/future-additive JSON
properties are ignored (`MemoryItemContract.SerializerOptions` — `UnmappedMemberHandling.Skip`).

Legacy pre-envelope `MemoryFact`s read forward via `MemoryItemMigration.FromLegacyMatterFact`
(Record scope, `subjectType = "matter"`, Tier-3 defaults `user-erasable` / `tier-3-user-owned`,
`RecordedAt` → `CreatedAt`, fact reused by reference — zero loss). Seam retained by ruling; no live
docs were migrated (fresh `memory-items` container, 050 decision record).

## Placement Justification (§10/§11)

No new production component. Test-only additions: `MemoryItemEnvelopeTests.cs` (new, pins the
contracts above) + one store-level inert-trust negative in `MemoryItemStoreTests.cs`. Cost of doing
nothing: the tolerant-reader and inertness contracts that 052/053/057 build on would be unpinned —
a future edit could silently make `trustLevel` a deny path or break pre-envelope deserialization
without any test failing.
