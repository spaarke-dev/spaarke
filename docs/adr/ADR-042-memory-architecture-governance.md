# ADR-042: Memory Architecture & Governance

- **Status**: **Proposed** (2026-07-10) — moves to **Accepted at gate G-R2-B** of `spaarke-ai-architecture-redesign-r2` (promotion condition: memory wave shipped + operator UAT at task 069). Implementation evidence already on master: PR #620 (tasks 050/051/052/057/053) + PR #622 (054/056), deployed to `spaarke-bff-dev` 2026-07-10.
- **Deciders**: Operator + Fable architecture review of the memory wave (rulings 2026-07-08/09/10, recorded in `projects/spaarke-ai-architecture-redesign-r2/CLAUDE.md` Decisions log)
- **Concise version**: [`.claude/adr/ADR-042-memory-architecture-governance.md`](../../.claude/adr/ADR-042-memory-architecture-governance.md) (the operational MUST/MUST-NOT surface — binding)

## Context

Before this wave, Spaarke AI's durable memory was a matter-only bolt-on: `MatterMemoryService` keyed facts to `sprk_matter` exclusively (any other host record — project, invoice, work assignment — got NO memory: the FR-B-01 regression), stored them as one aggregate document per subject in a `/tenantId`-partitioned container shared with pinned-context and workspace-tab documents, had no governance envelope (no provenance, retention, or erasure semantics beyond a blanket container TTL), and had no write path at all for the AI to capture knowledge during a session. Conversation context, meanwhile, had just been formalized as the ADR-040 session ledger — creating the risk that "memory" would be rebuilt as a second, parallel session store.

The product bar (operator north star): a customer comparing Spaarke's memory to Harvey/Legora or any enterprise AI system should find governed, record-scoped, derived-knowledge memory that matches or exceeds — retention, provenance, erasure, walls, and audit are the procurement demo.

## Decision

Spaarke AI memory is **governed structured memory** with exactly **two active scopes**, subject-partitioned storage, a provenance/governance envelope on every item, and an **AI-initiated, silent, provenance-tagged** write posture.

### 1. Two active scopes (and only two)

| Scope | Key | Holds |
|---|---|---|
| **Record** | generic `(entityType, entityId)` — NOT matter-only | **Derived knowledge** about the record (strategy, posture, constraints) — never copies of live Dataverse fields |
| **User** | `userId` = Dataverse **`systemuserid`** (canonical identity, ADR-028 one-hop) | General per-user memory (preferences, drafting style, standing facts) — one store spanning everything, NOT per-user-per-matter |

**Conversation is NOT a memory scope** — it stays the ADR-040 session-ledger facade; a MemoryItem write to `conversation` scope is rejected by contract (`MemoryItemContract.EnsureValidMemoryScope`). **Organizational** (Work IQ candidate) and **Semantic** (Azure AI Search/SPE retrieval) are provider **interfaces only** in r2 — inbound, empty until wired.

Memory items are **structured objects, never embeddings** (design D-M1): the contract has no vector field and `IsStructuredObjectNotEmbedding` asserts the serialized shape carries none. Semantic retrieval is a separate concern with its own provenance (`RetrievalReference`); retrieval results are never implicitly promoted to memory.

### 2. Subject-partitioned storage (never `/tenantId`)

Container `memory-items` (Cosmos, `spaarke-ai` db), partition key **`/subjectId`** — the entityId for Record scope, the userId for User scope. Rationale: Spaarke deployments are **customer-dedicated**, so `/tenantId` is one hot logical partition marching toward the 20 GB partition cap (the legacy container has exactly this shape); subject-partitioning spreads naturally and makes per-subject reads/erasure single-partition. Because Cosmos partition keys are immutable, this required a **new container** — the legacy `memory` container is left untouched (it is SHARED with pinned-context and workspace-tab documents and MUST NOT be retired or re-keyed).

Storage semantics: **one document per fact** aligned to MemoryItem v1; **deterministic document id** over `(scope, factType, normalized key)` so a repeated capture for the same key **supersedes** (replaces) rather than accumulates — memory hygiene under silent writes; ETag optimistic concurrency (412 on concurrent write); **subject-key normalization at the single store chokepoint** (casing/braces variance from LLM-supplied types and Dataverse-style GUIDs must land on one canonical partition, or capture→recall silently misses).

### 3. Governance envelope — metadata, not a gate

Every item carries: scope + subject keying; provenance (`source`: `user | ai-derived | insights-engine`, `bindingId`, `ledgerRef`, `sessionId`/`turnId`, `trustLevel` — **carried, not acted on**); `sensitivity` and `deletionPolicy` (**inert fields** in r2 — no enforcement machinery, per operator minimal-governance ruling 2026-07-09); `expiration`; `retentionClass` → mapped to a **per-item Cosmos TTL at write time** (Cosmos does the expiry — no reaper, no read-filter); and audit stamps (`createdAt/By`, `updatedAt` preserved across supersession). Tolerant reader: items missing envelope fields deserialize with documented defaults; unknown future fields are ignored.

### 4. Write posture: AI-initiated + silent + provenance-tagged (NO write-gate)

`memory.write` is a typed catalog capability that declares `Write` with low-tier/reversible risk data — so the **real** ConfirmationPolicyEngine resolves it to silent execution **from catalog data**. Silence is the engine's decision, not a gate bypass; declaring it Read/Pure to "skip the gate" is forbidden. There is **no confirmation dialog and no explicit-consent floor** — the explicit-only write floor was considered and **removed as over-engineered** (operator ruling 2026-07-08): automatic memory is the value proposition, and a per-fact dialog kills it.

The controls are: the **user review/delete surface** (list, point-delete, GDPR erase-all under `/api/memory`), **provenance on every item**, **scope isolation** (structural: User-scope partition IS the caller's own systemuserid; Record reads derive from the caller's own Dataverse record access), the **Dataverse-field-mirror guard** (a record fact that merely mirrors a live field is rejected at write and filtered at envelope assembly — record memory holds derived knowledge only; the Binder reads live fields directly from Business context), and platform content safety.

### 5. Erasure & audit (ADR-015 Tier 3)

Memory items are **Tier 3: user-owned, GDPR-erasable** — idempotent point-delete and partition-wide hard erase. The Tier-2 append-only **audit** log records every write/supersede/delete/erase with **identifiers and counts only** (never fact content, NFR-07) and is never touched by erasure — independent governance tiers.

### 6. Not a parallel session cache — by construction

Conversation context travels through the ContextEnvelope as **ledger references** (`LedgerEntryReference` exposes no content member — copying cannot be expressed); Record/User memory are cross-session governed objects read from the store. There is no second session-state store (ADR-040).

### 7. Insights Engine as future consumer

Record memory is shaped to become the Insights Engine's durable store: the `insights-engine` provenance origin is reserved and contract-tested (no special-casing on the write path). Wiring the Insights Engine to write into it is a named follow-on, not r2 scope.

## Deferred hard-governance boundary (explicitly NOT implemented)

The following are **deliberately deferred to a separate governance/security project** — this ADR does NOT claim them:

- **Untrusted-origin ban** (untrusted content can never originate a memory write — stated as a principle in the project charter; mechanical enforcement is deferred)
- **`trustLevel`/`sourceTrustLevel` enforcement** (the field is carried, inert; negative-tested to participate in no deny path)
- **Litigation-hold** semantics
- **Memory-poisoning eval families**
- **Row-level record-read authorization** for memory (r2 ships entity-type granularity via the caller's own Dataverse Read privilege; per-row OBO enforcement is scoped into the security project — GitHub #616 with the task-063 retrieval-ACL findings)

Until that project lands, the interim controls in §4 are the control surface. Reviewers MUST reject any claim that these deferred rules are enforced.

## Alternatives considered

- **`/tenantId` partitioning (reuse the legacy container)**: rejected — dedicated-per-customer environments make tenant a single hot partition against the 20 GB cap; partition keys are immutable so the fix later would be a full data migration.
- **Aggregate document per subject** (the legacy shape): rejected — per-item governance (retention TTL, point-delete, provenance, review) is impossible or contorted on an aggregate; the MemoryItem v1 contract is per-item.
- **Explicit-confirmation write floor** (gate every memory write): rejected as over-engineered — destroys the automatic-memory value proposition; review/delete + provenance + scope isolation are the proportionate controls at this stage.
- **Embeddings as memory**: rejected (D-M1) — memory is typed, reviewable, erasable structured facts; vectors are retrieval infrastructure with separate provenance.
- **Building memory into the session store**: rejected — conversation memory is the ledger (ADR-040); durable memory is cross-session and governed differently (Tier-3 per-item vs session lifecycle).

## Consequences

- Positive: any host record's chat is memory-capable (FR-B-01 closed); capture→recall works end-to-end with supersession hygiene; the procurement-grade governance story (provenance, retention-by-TTL, GDPR erasure, ids-only audit, user review) is real and demonstrable; the Insights Engine has a durable home reserved.
- Negative / accepted: silent writes mean users may be surprised by what was remembered — the review surface is the mitigation (client UI for it is a follow-on; r2 ships the API); entity-type read granularity until the security project lands; a second Cosmos container to operate (created via bicep + the 069 checklist).
- Enforcement: code review flags any new memory store outside `MemoryItemStore`, any tenant-keyed memory partition, any confirmation gate added to `memory.write` (or any Read/Pure re-declaration of it), any Dataverse-field mirror written as record memory, and any copying of ledger payloads into memory/envelope structures.

## References

- Implementing tasks: 050 (store + generalization; decision record `projects/spaarke-ai-architecture-redesign-r2/notes/050-memory-migration-decision.md`), 051 (envelope contracts; `notes/051-envelope-field-mapping.md`), 052 (minimal governance), 057 (memory.write), 053 (Binder memory slices; `notes/053-implementation-design.md`), 054 (budgets), 056 (fresh-retrieval bias)
- Spec: `projects/spaarke-ai-architecture-redesign-r2/spec.md` FR-B-01..B-16; design D-M1..D-M4
- Related ADRs: ADR-040 (ledger facade), ADR-015 (governance tiers), ADR-039 (catalog data drives the gate), ADR-028 (canonical identity), ADR-013 (PublicContracts boundary)
- Deferred governance: GitHub #616 (security project scope: retrieval ACL + row-level memory read)
