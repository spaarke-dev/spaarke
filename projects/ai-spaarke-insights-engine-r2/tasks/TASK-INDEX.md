# Task Index — Spaarke Insights Engine Phase 1.5 (r2)

> **Generated**: 2026-05-31 via /project-pipeline Step 3
> **Source**: [spec.md](../spec.md) §Scope + [plan.md](../plan.md) §"Phase breakdown"
> **Total tasks**: 29 (6 B + 6 A + 5 C + 7 D + 4 E + 1 wrap-up)
> **Universal task driver**: `task-execute` per root CLAUDE.md §4 (MANDATORY)

---

## Status legend

🔲 not-started · 🔄 in-progress · ✅ complete · 🚧 blocked · ⏭️ deferred (Phase 2+)

---

## Wave sequencing (per owner direction WB-1)

**Wave B FIRST** → A → C → D → E → wrap-up.

---

## Wave-organized index

### Wave B — Unblock synthesis (~1–2 days; sequenced FIRST; re-scoped per D-01 path-b 2026-06-02)

> **Re-scoped**: per [decisions/D-01](../decisions/D-01-wave-b-root-cause-corrected.md), Wave B was expanded from 6 tasks/~½ day to 6 tasks/~1–2 days. Original framing ("create 6 rows") was the right *direction* but missed the configjson-wipe issue + actionType-vs-actionCode mismatch. Tasks 001 + 002 were re-scoped; tasks 003, 004, 006 have addenda. Per owner direction, JPS authoring goes through `/jps-action-create` + `/jps-playbook-design` skills.

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [001](001-create-insights-action-rows.poml) | B1 | **Investigate playbook-architecture + scope-model-index** (resolve D-01 Q1+Q2+Q3 via authoritative docs) | ✅ | 2h | ❌ | — |
| [002](002-update-playbook-json-action-refs.poml) | B2 | **Create 6 sprk_analysisaction rows via `/jps-action-create` skill** (one per Insights ActionType) — INS-FACT/IDXR/EVID/GRND/DECL/RART live in Spaarke Dev | ✅ | 4h | ❌ | 001 |
| [003](003-deploy-playbook-action-lint.poml) | B3 | Deploy-Playbook.ps1 lint check (action-code + playbook JSON updated) | ✅ | 2h | ✅ | — |
| [004](004-redeploy-playbook-with-action-refs.poml) | B4 | **Delete + re-deploy 8 nodes** (clean clobbered configjson per D-01 §2.4) | 🔲 | 1h | ❌ | 002, 003 |
| [005](005-live-smoke-predict-matter-cost.poml) | B5 | Live smoke — SC-01 MET: real DeclineResponse (reason=insufficient-evidence, confidenceInDecline=0.95, structured suggestedActions) | ✅ | 1h | ❌ | 004 |
| [006](006-update-phase1-verification-doc.poml) | B6 | D-01 closed; smoke results documented; Designer-no-open rule pending Wave A2 | ✅ | 1h | ❌ | 005 |

---

### Wave A — Foundations (design docs; ~4 days; all parallel-safe)

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [010](010-architecture-overview-refresh.poml) | A1 | Architecture overview refresh (Phase 1.5 framing) | ✅ | 4h | ✅ | — |
| [011](011-operator-guide-refresh.poml) | A2 | Operator/developer guide refresh | ✅ | 4h | ✅ | — |
| [012](012-2d-taxonomy-design.poml) | A3 | 2D taxonomy design + initial 3 practice areas | ✅ | 1d | ✅ | — |
| [013](013-prompt-variant-versioning-design.poml) | A4 | Prompt-variant + versioning + per-tenant-override design | ✅ | 1d | ✅ | — |
| [014](014-universal-ingest-jps-refactor-design.poml) | A5 | Universal-ingest JPS refactor design | ✅ | 6h | ✅ | — |
| [015](015-multi-entity-subject-design.poml) | A6 | Multi-entity subject design | ✅ | 6h | ✅ | — |

---

### Wave C — JPS compliance refactor (~4–6 days)

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [020](020-universal-ingest-jps-playbook.poml) | C1 | Author universal-ingest@v1 JPS playbook | ✅ | 2d | ❌ | 013, 014 |
| [021](021-prompts-to-jps-storage.poml) | C2 | Migrate prompts from .txt → sprk_analysisaction.sprk_systemprompt | ✅ | 1d | ✅ | 013 |
| [022](022-retire-ingest-orchestrator.poml) | C3 | Retire IngestOrchestrator.cs | ✅ | 4h | ❌ | 020, 023 |
| [023](023-iinsightsai-facade-rewire.poml) | C4 | Update IInsightsAi.RunIngestAsync to invoke JPS playbook | ✅ | 4h | ❌ | 020 |
| [024](024-universal-ingest-parameterization.poml) | C5 | Universal-ingest parameterization | ✅ | 4h | ✅ | 020 |

---

### Wave D — 2D taxonomy + multi-entity (~1.5–2 weeks)

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [030](030-document-type-and-matrix-schema.poml) | D1 | sprk_documenttype_ref + sprk_practicearea_documenttype N:N | ✅ | 1d | ❌ | 012 |
| [031](031-per-practice-area-layer1-prompts.poml) | D2 | Per-practice-area Layer 1 (3 areas) | 🔲 | 3d | ✅ | 030, 021 |
| [032](032-per-area-doctype-layer2-schemas.poml) | D3 | Per-(area, doc-type) Layer 2 schemas (3–5 pairs) | 🔲 | 3d | ✅ | 030, 021 |
| [033](033-universal-ingest-per-area-routing.poml) | D4 | Universal-ingest routes by practice-area | 🔲 | 1d | ❌ | 020, 031, 032 |
| [034](034-multi-entity-resolvers.poml) | D5 | Multi-entity subject schemes + per-entity ILiveFactResolver | 🔲 | 2d | ✅ | 015, 023 |
| [035](035-index-scope-shape-migration.poml) | D6 | spaarke-insights-index scope shape generalization | 🔲 | 2d | ✅ | 015 |
| [036](036-synthetic-test-fixtures.poml) | D7 | Synthetic test fixtures (LLM-generated) | 🔲 | 2d | ✅ | 031, 032, 034 |

---

### Wave E — Hybrid consumption + Assistant (~1.5–2 weeks)

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [040](040-insights-search-rag-endpoint.poml) | E1 | POST /api/insights/search (wraps existing IRagService — re-scoped 2026-06-02) | 🔲 | 1.5d | ✅ | 035 |
| [041](041-intent-classifier.poml) | E2 | Intent classifier (LLM-based) | 🔲 | 2d | ✅ | 040 |
| [042](042-spaarke-assistant-integration.poml) | E3 | Spaarke Assistant integration (contract first) | 🔲 | 1w | ❌ | 040, 041 |
| [043](043-playbook-vs-rag-decision-tree.poml) | E4 | Playbook-vs-RAG decision-tree doc | 🔲 | 4h | ✅ | 040, 041 |

---

### Wrap-up

| ID | Wave-item | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [090](090-project-wrap-up.poml) | — | Lessons-learned + Phase 2 outline + archive | 🔲 | 4h | ❌ | all prior |

---

## Parallel Execution Groups

Tasks within a group can be dispatched in parallel via Skill tool calls (one per task in a single message). Cross-group dependencies enforce serial execution between groups.

| Group | Wave | Tasks | Prerequisite |
|---|---|---|---|
| B-G1 | B | 001 + 003 (003 parallel with 001) | — |
| B-G2 | B | 002 | 001 complete |
| B-G3 | B | 004 | 002 + 003 complete |
| B-G4 | B | 005 | 004 complete |
| B-G5 | B | 006 | 005 complete |
| **A-G1** | **A** | **010, 011, 012, 013, 014, 015 (ALL parallel)** | Wave B complete |
| C-G1 | C | 021, 024 (in parallel after 020) | A4 (013), A5 (014) |
| C-G2 | C | 020 (serial — no parallel) | A4, A5 |
| C-G3 | C | 023 | 020 complete |
| C-G4 | C | 022 | 020 + 023 complete |
| D-G1 | D | 030 | A3 (012) complete |
| D-G2 | D | 031, 032 (parallel) | 030 + C2 (021) complete |
| D-G3 | D | 033 | 031 + 032 + C1 (020) complete |
| D-G4 | D | 034, 035, 036 (parallel; 036 needs 031/032/034) | 015, 023 (D5); 015 (D6); 031+032+034 (D7) |
| E-G1 | E | 040 (serial) | 035 (D6) complete |
| E-G2 | E | 041, 043 (parallel after 040) | 040 complete |
| E-G3 | E | 042 (serial — long-running w/ cross-team coordination) | 040, 041 complete |
| Wrap | — | 090 | all prior |

**Max concurrency per wave**: 6 agents (per project-pipeline Step 5 hard limit; A-G1 is the largest at 6 parallel agents).

**Permission boundary** (per root CLAUDE.md §3): Tasks touching `.claude/` paths run sequentially in the main session. Wave A may touch `.claude/patterns/` — main session executes those.

---

## Critical path

`001 → 002 → 004 → 005 → 006` (Wave B, ~½ day)  
→ `014 (A5) + 013 (A4)` (Wave A, ~1.5 days)  
→ `020 (C1)` (~2 days)  
→ `023 (C4)` (~½ day)  
→ `030 (D1)` (~1 day)  
→ `031 (D2)` or `032 (D3)` (~3 days)  
→ `033 (D4)` (~1 day)  
→ `035 (D6)` (~2 days)  
→ `040 (E1)` (~3 days)  
→ `041 (E2)` (~2 days)  
→ `042 (E3)` (~1 week)  
→ `090` (~½ day).

**Critical path ≈ 4–5 weeks.**

---

## High-risk items (carried from spec.md §Risks)

| Risk | Affected tasks | Mitigation |
|---|---|---|
| `PlaybookExecutionEngine` doesn't support Insights conditional-branch semantics | 014 (A5), 020 (C1) | Wave B exercises engine; A5 surfaces gaps; patches stay in engine (don't fork) |
| Prompt migration changes runtime behavior subtly | 021 (C2) | Side-by-side comparison; new prompts get @v2; per-playbook cutover |
| Per-practice-area prompt authoring more time per area than estimated | 031 (D2) | Initial scope = 3 areas; expand per customer cadence |
| Index migration disrupts Phase 1 Observations | 035 (D6) | Hybrid backward-compat (keep scope.matterId + add scope.entityType/entityId); coordinate with infra |
| Spaarke Assistant contract authoring lengthens E3 | 042 (E3) | Sub-task A in E3 is contract authoring; coordinate early |
