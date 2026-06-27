# DR-002 — Cache Patterns (Category 4)

> **Author**: Phase 3 Sub-Agent K (synthesis from Phase 2 outputs)
> **Date**: 2026-06-04
> **Status**: PROPOSED (pending Q-002 owner review)
> **Pinned to**: Phase 1 inventory commit `357e6936`
> **Source analysis**: [`notes/phase2/analysis-cache.md`](../notes/phase2/analysis-cache.md)
> **Canonical authority**: [`notes/canonical-architecture-decisions.md` §2.1](../notes/canonical-architecture-decisions.md) · §3 (W1 Cat 4 row) · §8.1 (W1-1, W1-7) · §8.3 (W1-8, W1-11, W2-8)

## Context

Phase 1 inventory §2.4 catalogued 30+ inline cache use-sites across `Services/Ai/`, plus 2 specialist wrappers (`EmbeddingCache`, `InsightsPlaybookExecutionCache`) and a generic helper extension (`DistributedCacheExtensions.GetOrCreateAsync<T>` at `src/server/shared/Spaarke.Core/Cache/`). The helper's XML doc explicitly names `EmbeddingCache`, `ChatSessionManager`, and `InsightsPlaybookExecutionCache` as adoption targets — yet W1 Sub-Agent A found **0% adoption inside `Services/Ai/`**.

Cross-validation across W1+W2+W3 added 5 additional sites to the consolidation backlog (W2 Cat 1 + W2 Cat 3 + W3 Cat 5 each surfaced more inline cache calls). Total consolidation backlog: **~26 sites pending GetOrCreateAsync<T> adoption** across SprkChat, Insights, Workspace, Finance, Foundry, and Security teams.

A key finding: `OrchestratorPromptBuilder.cs:36-44` is the ONLY file in `Services/Ai/` that documents in-process `MemoryCache` usage per ADR-009's "MUST document ADR-009 exception" rule. W3 Cat 5 designates this XML-doc block as the gold-standard reference for the ADR-009 exception convention. The remaining in-process `MemoryCache` users lack the required XML doc — a binding ADR-009 documentation discipline gap.

A SECURITY ADJUDICATION SURFACE was surfaced: `Security/PrivilegeGroupResolver.cs` caches per-user privileges in `IMemoryCache`. ADR-009 forbids caching authorization DECISIONS but allows caching reference DATA — the boundary is owner-adjudication territory (routed to Security team per Q-003).

## Decision

1. **DESIGNATE canonical reference impls**:
   - `EmbeddingCache` (binary specialist; Singleton; 7-day TTL; SHA-256 keys; `Buffer.BlockCopy` float[]→byte[]; ADR-009 compliant)
   - `InsightsPlaybookExecutionCache` (peer specialist; stream-draining wrapper; D-P13 SPEC §3.1)
   - `Spaarke.Core.Cache.DistributedCacheExtensions.GetOrCreateAsync<T>` (generic helper for JSON payloads)

2. **REJECT proposing any new cache abstraction.** The existing helper is the canonical abstraction; the discipline gap is in ADOPTION, not in API design.

3. **DRIVE adoption of `GetOrCreateAsync<T>` at the 26+ pending sites** in phased multi-team migration. Promote from opt-in to MUST in ADR-009 amendment (W1-7 ADR candidate).

4. **KEEP 2 specialist wrappers** (`EmbeddingCache`, `InsightsPlaybookExecutionCache`) as non-merge candidates — binary/streaming payloads do not fit the JSON-only generic helper.

5. **CODIFY ADR-009 in-process exception XML-doc convention** using `OrchestratorPromptBuilder.cs:36-44` as the gold-standard reference (W3-2 ADR candidate).

6. **ROUTE PrivilegeGroupResolver adjudication to Security team** (DATA vs DECISIONS question, per §7.3 of canonical-architecture-decisions).

## Consequences

### Positive
- Eliminates 26+ inline cache use-sites that drift independently from ADR-009 discipline.
- Concentrates cache failure modes (graceful degradation, OTEL metrics) into a single helper code path.
- Codifies the existing-but-unused canonical helper — closes the 0%-adoption gap with no new code.
- ADR-009 exception XML-doc convention becomes reviewable in PR checklists.

### Negative
- Phased multi-team migration is a coordination tax (SprkChat 8+ sites, Insights, Workspace, Finance, Foundry).
- Risk of regression where inline cache paths embedded subtle TTL/key-construction nuances; each migration site needs careful review.
- PrivilegeGroupResolver adjudication may surface deeper ADR-009 ambiguity requiring ADR amendment.

### Migration impact
- **Cross-team coordination**: AIPL (`EmbeddingCache` canonical confirmation) + AIPU R1 (Orchestrator), AIPU R2 (CapabilityManifest keep-as-is), SprkChat (8+ sites in `Chat/`), Insights, Workspace, Finance Intelligence, Foundry, Security (PrivilegeGroupResolver adjudication). 6 teams concurrent.
- **Effort estimate**: **L (Large)** — 3-5 weeks phased; per-team site migration; per-site verification (TTL, key construction, OTEL metric continuity).
- **Sequencing**: Adopt-helper migration is independent of LATENT BUG remediation (DR-003) and can run in parallel. SPEC and gold-standard XML-doc precedent already exist; no blockers.

## Canonical naming (Q-004 — surfaced not locked)

- **Candidate**: "Spaarke Canonical Cache Stack" (NEW name; descriptive over existing pattern)
- **Reference impls**:
  - `EmbeddingCache` (`src/server/api/Sprk.Bff.Api/Services/Ai/EmbeddingCache.cs`)
  - `InsightsPlaybookExecutionCache` (peer specialist)
  - `Spaarke.Core.Cache.DistributedCacheExtensions.GetOrCreateAsync<T>` (`src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs`)
- **Pattern elements** (5):
  1. `IDistributedCache` substrate (Redis) — single-tier substrate; in-process `MemoryCache` only with EXPLICIT ADR-009 exception XML doc
  2. Typed wrapper for binary/streaming payloads (`EmbeddingCache` pattern) — opt-in
  3. Generic `GetOrCreateAsync<T>` for JSON payloads (canonical helper) — opt-in becoming MUST
  4. SHA-256 hash key construction with structured prefix (`sdap:embedding:{base64-sha256}`)
  5. Graceful degradation on cache failure + OTEL metrics (`InsightsCacheMetrics` precedent)
- **Gold-standard XML doc precedent**: `OrchestratorPromptBuilder.cs:36-44` (the ONLY ADR-009 exception XML doc in `Services/Ai/`)

## ADR candidates from this decision (Q-005 — bullets only)

- **W1-1** BFF Canonical Cache Stack — `IDistributedCache` only; `GetOrCreateAsync<T>` only; specialist wrappers for binary/streaming; `MemoryCache` with EXPLICIT ADR-009 exception XML doc — HIGH priority
- **W1-7** ADR-009 Amendment: promote `GetOrCreateAsync<T>` from opt-in to MUST — MEDIUM priority
- **W1-8** ADR-032 Amendment: clarify Null-Object ctor minimality (no cache deps) — LOW priority
- **W1-11** PrivilegeGroupResolver cache audit (depends on §7.3 Security adjudication) — LOW priority
- **W2-8** Shared Embedding-Cache Helper — `IEmbeddingCache.GetOrGenerateAsync` extension — LOW priority
- **W3-2** In-process MemoryCache XML-doc convention for ADR-009 exceptions — MEDIUM priority

## Open questions for owner review (Q-002)

1. **Adoption push** (canonical §11.2 Q-9): Drive adoption of `GetOrCreateAsync<T>` — promote from opt-in to MUST. Existing 26+ sites migrate (multi-team multi-week), or only new sites?
2. **Canonical naming lock** (canonical §11.3 Q-11): `EmbeddingCache` + `DistributedCacheExtensions.GetOrCreateAsync<T>` lock as canonical Cache Stack?
3. **PrivilegeGroupResolver adjudication** (canonical §7.3): Security team verdict — DATA (allowed) or DECISIONS (forbidden)?
4. **Sequencing**: 6-team concurrent migration vs sequential by team? Owner adjudicates per Q-003 sequential rule.

## References

- Source analysis: [`notes/phase2/analysis-cache.md`](../notes/phase2/analysis-cache.md) §1-§3
- Wave summaries: [`notes/phase2/wave-1-summary.md`](../notes/phase2/wave-1-summary.md), [`notes/phase2/wave-2-summary.md`](../notes/phase2/wave-2-summary.md) §2.4 (cache lever expanded), [`notes/phase2/wave-3-summary.md`](../notes/phase2/wave-3-summary.md) §2.5 (ADR-009 documentation convention)
- Canonical authority: [`notes/canonical-architecture-decisions.md`](../notes/canonical-architecture-decisions.md) §2.1 + §3 + §5.3 + §7.3 + §8 + §9 + §11.3 Q-11
- Related ADR candidates: W1-1 (HIGH), W1-7 (MEDIUM), W1-8/W1-11/W2-8 (LOW), W3-2 (MEDIUM)
- Related DRs: **DR-001** (lookup services consume the cache canonical), **DR-006** (search substrates share `EmbeddingCache` pattern), **DR-007** (`OrchestratorPromptBuilder` is gold-standard ADR-009 XML-doc reference)
- ADR cross-references: ADR-009 (cache discipline + amendment candidate), ADR-010 (interface budget cap), ADR-030 (Null-Object Kill-Switch)
- Inventory corrections from this category: §6 row 17 (32 inline cache consumers; 2 unclassified)
