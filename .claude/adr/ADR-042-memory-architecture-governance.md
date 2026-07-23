# ADR-042: Memory Architecture & Governance (Concise)

> **Status**: **Proposed** (2026-07-10) — Accepted at gate **G-R2-B** of
> `spaarke-ai-architecture-redesign-r2` (memory wave shipped: PRs #620/#622,
> deployed 2026-07-10; promotion on operator UAT at task 069).
> **Domain**: AI platform — durable memory, governance, erasure
> **Source**: operator rulings 2026-07-08/09/10 (project Decisions log);
> implementing tasks 050/051/052/057 (+053 Binder slices).
> **Why this ADR exists**: R1 memory was matter-only, ungoverned, and had no
> write path; this codifies the two-scope governed model so future work does
> not re-litigate it (or quietly re-gate it).

---

## Decision

Memory = **governed structured objects** in exactly TWO active scopes —
**Record** (generic `(entityType, entityId)`) + **User** (`userId` =
Dataverse `systemuserid`) — stored per-fact in the SUBJECT-partitioned
`memory-items` container, wrapped in a provenance/governance envelope, written
**AI-initiated + silent + provenance-tagged** via `memory.write` (no gate;
review/delete is the control). Conversation memory stays the ADR-040 ledger.

## Constraints

### ✅ MUST
- **MUST** key Record memory generically by `(entityType, entityId)` — never
  matter-only — and User memory by canonical `systemuserid` (ADR-028 one-hop).
- **MUST** partition by SUBJECT (`/subjectId` = entityId | userId), one
  document per fact, deterministic id over `(scope, factType, normalized key)`
  → repeated capture SUPERSEDES (never accumulates); subject keys normalized
  at the store chokepoint (`MemoryItemStore`).
- **MUST** store memory as structured objects (typed fact + envelope) — never
  embeddings; retrieval is a separate concern with its own provenance.
- **MUST** carry the governance envelope on every item: provenance
  (`source` user|ai-derived|insights-engine, bindingId, ledgerRef,
  sessionId/turnId, trustLevel), sensitivity/deletionPolicy (INERT in r2),
  expiration, retentionClass → per-item Cosmos TTL at write (no reaper),
  audit stamps preserved across supersession. Tolerant reader.
- **MUST** keep `memory.write` a catalog capability declaring `Write` with
  low-tier/reversible risk data — silence is the ConfirmationPolicyEngine's
  decision FROM CATALOG DATA, executed through the real gate path.
- **MUST** reject record facts that mirror live Dataverse fields (guard at
  write + envelope-assembly filter) — record memory holds DERIVED knowledge;
  the Binder reads live fields from Business context directly.
- **MUST** treat items as ADR-015 Tier 3: user-owned, GDPR-erasable
  (idempotent point-delete + partition erase); audit every write/delete/erase
  to the Tier-2 log with identifiers/counts ONLY (never content, NFR-07).
- **MUST** authorize structurally: User scope = the caller's own partition;
  Record reads derive from the caller's own Dataverse record access.
- **MUST** keep Conversation as the ledger facade — MemoryItem writes to
  `conversation` scope are rejected; ledger context travels as references.

### ❌ MUST NOT
- **MUST NOT** partition memory by `/tenantId` (dedicated-per-customer envs →
  hot partition vs the 20 GB cap; keys are immutable).
- **MUST NOT** retire or re-key the legacy `memory` container (SHARED with
  pinned-context + workspace-tab documents).
- **MUST NOT** add a confirmation gate / explicit-consent floor to
  `memory.write`, or re-declare it Read/Pure to bypass the engine (the
  explicit-only floor was REMOVED by operator ruling 2026-07-08).
- **MUST NOT** enforce (or claim) the DEFERRED hard-governance rules —
  untrusted-origin ban, trustLevel enforcement, litigation-hold,
  memory-poisoning evals, row-level record-read ACL — all scoped to the
  separate governance/security project (GitHub #616). `trustLevel` is carried
  inert (negative-tested: no deny path).
- **MUST NOT** create a second session-state store or copy ledger payloads
  into memory/envelope structures (ADR-040 by construction).
- **MUST NOT** touch the Tier-2 audit container during erasure.

## Integration
ADR-040 (Conversation = ledger; references-only) · ADR-015 (Tier 3 erasure /
Tier 2 audit) · ADR-039 (catalog data drives the silent gate decision) ·
ADR-028 (canonical `systemuserid`) · ADR-013 (consumed via PublicContracts —
MemoryItem v1 / ContextEnvelope only). Insights Engine = reserved future
producer (`insights-engine` origin; wiring is a named follow-on).

**Full ADR**: [docs/adr/ADR-042-memory-architecture-governance.md](../../docs/adr/ADR-042-memory-architecture-governance.md)
