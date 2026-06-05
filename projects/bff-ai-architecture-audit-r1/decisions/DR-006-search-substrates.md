# DR-006 — Search Substrates (Category 3)

> **Author**: Phase 3 Sub-Agent K (synthesis from Phase 2 outputs)
> **Date**: 2026-06-04
> **Status**: PROPOSED (pending Q-002 owner review)
> **Pinned to**: Phase 1 inventory commit `357e6936`
> **Source analysis**: [`notes/phase2/analysis-search.md`](../notes/phase2/analysis-search.md)
> **Canonical authority**: [`notes/canonical-architecture-decisions.md` §2.6](../notes/canonical-architecture-decisions.md) · §3 (W2 Cat 3 row) · §7.1+§7.2 (security adjudication surfaces) · §8.1 (W2-4, W2-5, W2-7) · §8.2 (W2-6)

## Context

Phase 1 inventory §2.3 catalogued 4 search-substrate services in `Services/Ai/`:
- `RagService` (`IRagService`) + `NullRagService` — hybrid keyword + vector + semantic ranking against Azure AI Search (RAG-specific index)
- `SemanticSearchService` — hybrid RRF/vector/keyword via pipeline of `IQueryPreprocessor`/`IResultPostprocessor`
- `RecordSearchService` — `spaarke-records-index`; record-matching surface
- `PlaybookEmbeddingService` — `playbook-embeddings` index; system-config artifacts (admin-scoped)

Inventory §7.3 surfaced the question whether `PlaybookEmbeddingService` and `SemanticSearchService` should consolidate behind a single interface, and whether the Null-peer asymmetry (1-of-4 has Null peer) was an anti-pattern.

W2 Sub-Agent F audited all 4 substrates. Findings:

**REJECT merger**: The 4 substrates differ across 5 dimensions — index, document shape, security model, API surface, lifecycle/DI pattern. Specifically:
- `RagService` is Singleton with `IEmbeddingCache` dependency; `RecordSearchService` is per-request-Scoped; `PlaybookEmbeddingService` is factory-instantiated for system-config (admin-scoped).
- `RagService` carries MANDATORY tenant + MANDATORY privilege-group filter (AIPU2-027 fail-closed); `SemanticSearchService` carries tenant only; `RecordSearchService` carries NEITHER (Dataverse-mediated security); `PlaybookEmbeddingService` carries neither (admin-scoped).
- Output shapes are structurally distinct (RRF rank fusion vs vector top-k vs document-projection record-match vs raw embeddings).

**Null-peer asymmetry RESOLVED**: 1-of-4 having a Null peer is INTENTIONAL and CORRECT — `RagEndpoints` are mapped UNCONDITIONALLY (require `NullRagService`); the other 3 substrates are conditionally mapped SYMMETRICALLY with their DI registration. **NOT a §F.1 anti-pattern**. Three explicit DO-NOT-ADD-Null-peer for SemanticSearch + RecordSearch + PlaybookEmbedding (responding to inventory §7.3 bullet 1).

**Double-Gate Null-Object Pattern (NEW from W2 Cat 3 §4.2)**: `RagService`/`NullRagService` is the **gold-standard reference impl** for two-tier registration: compound-AI-OFF branch registers Null + compound-AI-ON + resource-credentials-missing branch ALSO registers Null. Peer pattern to ADR-030 single-gate.

**2 SECURITY ADJUDICATION SURFACES** routed to Security team per Q-003:
1. `RecordSearchService` tenant-isolation model — Dataverse-layer security relied on; needs Security team verdict on layered-defense reasoning.
2. `SemanticSearchService` privilege-filter gap — only `RagService` mandates privilege-group filter; intentional access-model difference or AIPU2-027 gap?

**Inventory corrections**:
- `DocumentClassifierHandler` imports `IRagService` at HEAD (NOT `ISemanticSearchService` as inventory §2.3.2 claimed).
- `AiAnalysisNodeExecutor` is a NEW consumer of `IRecordSearchService` (not in inventory).

## Decision

1. **KEEP all 4 search substrates** as architecturally distinct services. No merger.

2. **REJECT `PlaybookEmbeddingService ↔ SemanticSearchService` consolidation** — different indices, document shapes, security models, API surfaces, lifecycles, DI patterns. The shared Azure AI Search substrate is a thin coincidence that does not justify abstraction.

3. **DESIGNATE `RagService`/`NullRagService` as canonical reference impl** for two distinct pattern docs:
   - "Spaarke Canonical Search Substrate Architecture" (Q-004 surfaced)
   - "DI Double-Gate Null-Object Pattern" (NEW; peer to ADR-030 single-gate)

4. **3 EXPLICIT DO-NOT-ADD-Null-peer** for `SemanticSearchService`, `RecordSearchService`, `PlaybookEmbeddingService` (responding to inventory §7.3 bullet 1). Their endpoint-mapping symmetry with conditional DI registration makes Null peers unnecessary — adding them would obscure the symmetry rule.

5. **ROUTE 2 security adjudication surfaces to Security team** (per Q-003 sequential):
   - **§7.1** `RecordSearchService` tenant-isolation model — verify (a) index carries only non-sensitive metadata projections (verifiable claim), (b) every consumer chain resolves through Dataverse before exposing content (architectural assumption — needs review), (c) correlation-attack entropy risk on `spaarke-records-index` is acceptable (open question).
   - **§7.2** `SemanticSearchService` privilege-filter gap — intentional access-model difference (e.g., SemanticSearch operates over org-wide non-sensitive content), or security gap predating AIPU2-027?

6. **DOCUMENT Tenant + Privilege-Filter Matrix** (per-substrate filter requirements) as architectural reference:

   | Service | Tenant filter | Privilege filter | Security model |
   |---|---|---|---|
   | RagService | MANDATORY | MANDATORY (AIPU2-027 fail-closed) | Layer-1 |
   | SemanticSearchService | YES | **NO — security adjudication surface** | Layer-1 |
   | RecordSearchService | NO | NO | Dataverse-mediated (layered-defense — adjudication surface) |
   | PlaybookEmbeddingService | NO | NO | Admin-scoped system config |

7. **CORRECT inventory mislabels** in inventory-correction PR (canonical §6 rows 10, 11):
   - `DocumentClassifierHandler` consumes `IRagService` (not `ISemanticSearchService`).
   - `AiAnalysisNodeExecutor` is a new `IRecordSearchService` consumer.

## Consequences

### Positive
- Zero code change to KEEP the 4 substrates — minimal risk.
- `RagService` gains canonical reference status for two distinct architectural patterns (Search Substrate + Double-Gate Null-Object).
- Explicit "DO-NOT-ADD-Null-peer" for 3 substrates prevents future audit cycles from re-litigating the symmetry rationale.
- Tenant + Privilege-Filter Matrix becomes the per-substrate security-model reference document.
- Inventory mislabel corrections close 2 framing gaps.

### Negative
- 2 SECURITY ADJUDICATION SURFACES require Security team adjudication — blocking until verdicts.
- If Security adjudicates retrofit on `SemanticSearchService` privilege-filter, additional implementation work emerges (cross-team Q-003 coordination).
- If Security adjudicates `RecordSearchService` layered-defense model insufficient, retrofit of tenant filter is non-trivial.

### Migration impact
- **Cross-team coordination**: AIPL (`RagService` canonical confirmation); AIPU R1 (Semantic Search privilege adjudication); Record-Matching feature team (RecordSearchService layered-defense model); SprkChat (Playbook Embedding — admin-scoped system config); Security team (2 adjudication surfaces).
- **Effort estimate**: **S (Small)** for documentation + pattern designation — no code change post-decisions. Security adjudication TBD (may add follow-on effort if retrofit needed).
- **Sequencing**: Independent of LATENT BUG remediation (DR-003) — can ship pattern docs in parallel. Security adjudication is HIGH priority but sequential.

## Canonical naming (Q-004 — surfaced not locked)

- **Candidate names**:
  - "Spaarke Canonical Search Substrate Architecture" (pattern doc; NOT merger)
  - "Double-Gate Null-Object Pattern" (NEW; peer to ADR-030 single-gate)
- **Reference impl**: `RagService` / `NullRagService` (`src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` + `NullRagService.cs`)
- **Pattern elements (Search Substrate)** (5):
  1. Hybrid keyword + vector + semantic ranking against Azure AI Search
  2. Mandatory tenant filter + mandatory privilege-group filter (AIPU2-027 fail-closed for RagService canonical; other substrates per matrix)
  3. P3 Fail-Fast Null peer for AI-Search-keys-missing OR compound-OFF (RagService canonical; others use endpoint-mapping symmetry)
  4. `IEmbeddingCache` integration for embedding generation (RagService consumes `EmbeddingCache` per DR-002)
  5. Graceful degradation with OTEL spans
- **Pattern elements (Double-Gate Null-Object — NEW)** (3):
  1. Tier 1: compound-AI-OFF branch registers Null peer (peer to ADR-030 single-gate)
  2. Tier 2: compound-AI-ON + resource-credentials-missing branch ALSO registers Null peer (extra safety net)
  3. Same `ServiceDescriptor.ServiceType` across all registration sites (ADR-032 P3 Fail-Fast)
- **KEEP siblings (3 substrates + DO-NOT-ADD-Null-peer)**:
  - `SemanticSearchService` (tenant filter only — adjudication surface)
  - `RecordSearchService` (Dataverse-mediated security — adjudication surface)
  - `PlaybookEmbeddingService` (admin-scoped system config)

## ADR candidates from this decision (Q-005 — bullets only)

- **W2-4** Search-Substrate Canonical Architecture — HIGH priority (4-substrate stack pattern doc)
- **W2-5** DI Double-Gate Null-Object Pattern — HIGH priority (peer to ADR-030 single-gate)
- **W2-7** Endpoint Mapping ↔ DI Registration Symmetry Rule — HIGH priority (generalizes W1-4; superseded by W4-1 formal — see DR-008)
- **W2-6** Search-Substrate Security Model Matrix — MEDIUM priority (per-substrate filter requirements; depends on §7.1+§7.2 Security adjudication)

## Open questions for owner review (Q-002)

1. **REJECT merger confirmation** (canonical §11.2 Q-6): Owner accepts `PlaybookEmbeddingService ↔ SemanticSearchService` consolidation REJECTED?
2. **3 DO-NOT-ADD-Null-peer confirmation** (canonical §11.2 Q-7): Owner accepts SemanticSearch + RecordSearch + PlaybookEmbedding explicit DO-NOT-ADD verdict?
3. **`RagService`/`NullRagService` canonical lock** (canonical §11.3 Q-13): Owner locks as canonical Search Substrate + Double-Gate Null-Object reference?
4. **Security adjudication §7.1** (canonical §11.5 Q-20): `RecordSearchService` tenant-isolation model — Security team + Record-Matching feature team verdict?
5. **Security adjudication §7.2** (canonical §11.5 Q-21): `SemanticSearchService` privilege-filter gap — Security team verdict (intentional or retrofit)?
6. **Tenant + Privilege-Filter Matrix authoring**: Standalone pattern doc, or embed in the broader Search Substrate Architecture pattern doc?

## References

- Source analysis: [`notes/phase2/analysis-search.md`](../notes/phase2/analysis-search.md)
- Wave summary: [`notes/phase2/wave-2-summary.md`](../notes/phase2/wave-2-summary.md) §1.2 + §2.6 (security adjudication surfaces)
- Canonical authority: [`notes/canonical-architecture-decisions.md`](../notes/canonical-architecture-decisions.md) §2.6 + §3 + §5.3 (cross-sub-agent validation) + §6 (inventory corrections rows 10-11) + §7.1+§7.2 + §8.1+§8.2 + §11.2 Q-6/Q-7 + §11.3 Q-13 + §11.5 Q-20/Q-21
- Related ADR candidates: W2-4 (HIGH), W2-5 (HIGH), W2-7 (HIGH — superseded by W4-1), W2-6 (MEDIUM)
- Related DRs: **DR-002** (RagService consumes `EmbeddingCache` from canonical Cache Stack), **DR-007** (RagService consumes prompt builders indirectly), **DR-008** (Endpoint↔DI Symmetry Rule — RagService/NullRagService is gold-standard reference; W2-7 superseded by W4-1 formal)
- ADR cross-references: ADR-030 (Null-Object single-gate; Double-Gate is peer pattern), ADR-032 §F.1 (P3 Fail-Fast)
- Inventory corrections from this category: §6 rows 10 (`DocumentClassifierHandler` consumer mislabel), 11 (`AiAnalysisNodeExecutor` new `IRecordSearchService` consumer)
