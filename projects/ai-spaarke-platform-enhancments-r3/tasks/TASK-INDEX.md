# Task Index — AI Resource Activation & Integration (R3)

> **Project**: ai-spaarke-platform-enhancments-r3
> **Total Tasks**: 18
> **Created**: 2026-03-04

---

## Phase 1: Index Architecture & Cleanup (10h)

| # | Task | Est. | Status | Deps | Parallel Group |
|---|------|------|--------|------|----------------|
| 001 | Create Golden Reference Index (`spaarke-rag-references`) | 3h | ✅ | none | A |
| 002 | Remove Deprecated Indexes | 1h | ✅ | none | A |
| 003 | Populate Records Index + tenantId Field | 3h | 🔲 | none | A |
| 004 | Validate Discovery-Index Dual-Write | 2h | 🔲 | none | B |
| 005 | Validate Invoice Index | 1h | 🔲 | none | B |

## Phase 2: Golden Reference Deployment (12h)

| # | Task | Est. | Status | Deps | Parallel Group |
|---|------|------|--------|------|----------------|
| 010 | Deploy Knowledge Source Records to Dataverse | 2h | ✅ | none | C |
| 011 | Build ReferenceIndexingService + Admin Endpoints | 4h | ✅ | 001 | C |
| 012 | Index All 10 Knowledge Sources | 2h | 🔲 | 010, 011 | D |
| 013 | Wire Reference Retrieval into RagService | 3h | 🔲 | 011 | D |

## Phase 3: Knowledge-Augmented Execution (14h)

| # | Task | Est. | Status | Deps | Parallel Group |
|---|------|------|--------|------|----------------|
| 020 | Wire L1 Knowledge Retrieval into Execution | 4h | 🔲 | 013 | E |
| 021 | Configurable Knowledge Retrieval per Action | 3h | 🔲 | 020 | E |
| 022 | Wire Optional L2 Customer Document Context | 3h | 🔲 | 020 | F |
| 023 | Wire Optional L3 Entity Context | 2h | 🔲 | 020 | F |
| 024 | Knowledge Retrieval Result Caching | 2h | 🔲 | 020 | F |

## Phase 4: Model Selection Integration (8h)

| # | Task | Est. | Status | Deps | Parallel Group |
|---|------|------|--------|------|----------------|
| 030 | Verify/Deploy Azure OpenAI Models | 2h | ✅ | none | G |
| 031 | Fix Hardcoded Model Selection | 3h | ✅ | none | G |
| 032 | Document Model Selection Guidelines | 1h | 🔲 | 031 | H |
| 033 | Add Model Selection to Playbook Builder | 2h | 🔲 | 031 | H |

## Phase 5: Embedding Governance (3h)

| # | Task | Est. | Status | Deps | Parallel Group |
|---|------|------|--------|------|----------------|
| 040 | Document Embedding Strategy | 1h | ✅ | none | I |
| 041 | Clean Up Legacy Vector Field Writes | 1h | ✅ | none | I |
| 042 | Document Embedding Change Protocol | 1h | ✅ | none | I |

## Deployment

| # | Task | Est. | Status | Deps | Parallel Group |
|---|------|------|--------|------|----------------|
| 050 | Deploy BFF API with Knowledge Augmentation | 2h | 🔲 | 020-024, 031 | — |

## Wrap-Up

| # | Task | Est. | Status | Deps | Parallel Group |
|---|------|------|--------|------|----------------|
| 090 | Project Wrap-Up | 2h | 🔲 | 050 | — |

---

## Parallel Execution Groups

Tasks within the same group can run simultaneously using Claude Code task agents.

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| **A** | 001, 002, 003 | None | Independent Azure index operations |
| **B** | 004, 005 | None | Independent validation tasks |
| **C** | 010, 011 | 001 (for 011) | Dataverse deploy + service scaffolding |
| **D** | 012, 013 | 010, 011 | Index sources + wire retrieval |
| **E** | 020, 021 | 013 | L1 wiring + config (sequential within group) |
| **F** | 022, 023, 024 | 020 | L2, L3, caching — independent extensions |
| **G** | 030, 031 | None | Model verification + handler fix |
| **H** | 032, 033 | 031 | Model docs + UI dropdown |
| **I** | 040, 041, 042 | None | Documentation + cleanup |

### Maximum Parallelism Schedule

```
Wave 1 (can start immediately):
  Group A: 001, 002, 003
  Group B: 004, 005
  Group C: 010 (+ 011 after 001)
  Group G: 030, 031
  Group I: 040, 041, 042

Wave 2 (after Wave 1 completes):
  Group D: 012, 013
  Group H: 032, 033

Wave 3 (after Wave 2):
  Group E: 020, 021

Wave 4 (after Wave 3):
  Group F: 022, 023, 024

Wave 5 (after all implementation):
  050 (Deploy)
  090 (Wrap-up)
```

---

## Critical Path

```
001 → 011 → 013 → 020 → 021 → 050 → 090
                         ↘ 022 ↗
                         ↘ 023 ↗
                         ↘ 024 ↗
```

**Longest chain**: 001 → 011 → 013 → 020 → 050 → 090 (18h on critical path)

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Pending |
| 🔄 | In Progress |
| ✅ | Complete |
| ⏭️ | Skipped |
| ❌ | Blocked |
