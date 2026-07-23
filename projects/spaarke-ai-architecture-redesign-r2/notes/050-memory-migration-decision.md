# Task 050 — Memory store migration decision record

> **Date**: 2026-07-10 · **Task**: AIR2-050 (FR-B-01) · **Status**: RULED by operator 2026-07-09 (pre-flight architecture review), executed here.
> The task's escalation trigger (irreversible Cosmos partition decision) was **pre-answered** by the operator — nothing destructive was executed.

## Decision summary

| Question | Ruling | Rationale |
|---|---|---|
| **Partition key** | NEW container `memory-items`, partition **`/subjectId`** (entityId for Record scope, userId for User scope) — NEVER `/tenantId` | Deployments are customer-dedicated → `/tenantId` is a single hot logical partition against the 20 GB cap (the legacy `memory` container has exactly this shape). Subject-partitioning spreads naturally; isolation is the deployment boundary. Cosmos partition keys cannot change in place → new container, reused code. |
| **Document granularity** | **PER-FACT documents** aligned to MemoryItem v1 (`MemoryItemDocument` = the contract + `tenantId` metadata + `ttl` + `_etag`; no third shape) | The frozen task-016 contract is per-item with a per-item governance envelope. Per-fact docs make 052's minimal governance real: retention = per-doc Cosmos TTL, review/delete = point-delete, per-item provenance. The legacy one-aggregate-doc-per-subject shape was NOT carried forward (operator: "unnecessary redundancy or alt-path"). |
| **Live-doc migration** | **Fresh container; NO doc migration; legacy docs left untouched** | Dev-only data, single consumer. The consumer (PlaybookChatContextProvider) now reads the new store, which simply starts empty on dev — memory re-accumulates. Fully non-destructive; purge of the legacy matter-memory docs is a post-069-UAT hygiene follow-up. |
| **Legacy `memory` container** | **NOT retired, NOT re-keyed** | It is SHARED: PinnedContextRepository (`pinned-context_*`) and WorkspaceStateService (`workspace-tab_*`) docs live in it. Only the MatterMemoryService *code* was retired; the container and its other tenants are untouched. Bicep comment updated to warn future maintainers. |
| **Canonical `userId`** | Dataverse **`systemuserid`** (ADR-028 one-hop oid→systemuserid; same identity 055's CallerContactResolver rides) | Partition keys are forever — one canonical identity, documented on `IMemoryItemStore` + `MemoryItemDocument`. |
| **Upsert semantics** | **Deterministic doc id over (scope, fact.Type, normalized fact.Key)** → repeated capture REPLACES (supersession), preserving `createdAt`/`createdBy`, stamping `updatedAt`, ETag-threaded (412 on concurrent write) | Memory hygiene under silent AI-initiated writes (FR-B-08/057): without upsert-by-key, automatic capture accumulates duplicates and stale contradictions. |
| **TTL** | Container `defaultTtl: -1` (enables per-item `ttl`, no container-wide expiry). Per-item `ttl` is set by task 052 from `retentionClass` — no reaper, no custom expiry machinery (minimal-governance ruling 2026-07-09) | The legacy container's blanket 90-day TTL is NOT the model — retention becomes a per-item governance property. |

## What was retired vs. reused

- **Retired (deleted)**: `MatterMemoryService`, `IMatterMemoryService`, `MatterMemory` (matter-only keying was the FR-B-01 regression to remove). The only production consumer (`PlaybookChatContextProvider.AppendMatterMemoryAsync`) was generalized to `AppendRecordMemoryAsync` — ANY host record now reads its memory; matter hosts render a byte-identical fragment heading.
- **Reused verbatim**: `MemoryFact` model (untouched); prompt-fragment budget serialization (500-token cap, 0.7 confidence filter, lowest-confidence-first truncation — ported with the same constants and pinned by the same test assertions); ETag optimistic-concurrency contract (412 propagates); GDPR hard-delete posture (Tier 3 only; Tier 2 `audit` never touched).
- **Deferred deliberately**: legacy-doc purge (post-069 hygiene); `MemoryItemMigration.FromLegacyMatterFact` remains available in PublicContracts if any legacy doc ever needs reading forward (none migrated by ruling).

## Deployment note (dev)

The `memory-items` container is declared in `infrastructure/bicep/modules/cosmos-db.bicep`. Dev (`spaarke-ai` database) needs the container created before the store's first write — either re-run the bicep deployment or create it directly (name `memory-items`, partition `/subjectId`, default TTL `-1`). Tracked on the G-R2-B deploy checklist (task 069 prep).
