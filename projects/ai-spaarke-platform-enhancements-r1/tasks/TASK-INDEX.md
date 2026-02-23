# TASK-INDEX.md — AI Platform Foundation Phase 1

> **Auto-updated by task-execute skill on task completion**
> **Last Updated**: 2026-02-22
> **Project**: ai-spaarke-platform-enhancements-r1

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not started |
| 🚧 | In progress |
| ✅ | Complete |
| ❌ | Blocked |

---

## Phase 1 — Foundation & Environment (Week 1)

| ID | Task | Est. | Status | Blocks |
|----|------|------|--------|--------|
| AIPL-001 | Define and Create Dataverse Chat + Evaluation Entities | 4h | 🔲 | AIPL-051, AIPL-071 |
| AIPL-002 | Add Agent Framework NuGet Packages and Verify Build | 2h | 🔲 | AIPL-050 |
| AIPL-003 | Configure Two-Index Azure AI Search Architecture | 1h | 🔲 | AIPL-010, AIPL-016 |
| AIPL-004 | Wire Foundational DI Registrations and Audit Baseline | 1h | 🔲 | AIPL-010, AIPL-050 |

**Phase 1 Total Estimate**: 8 hours

---

## Phase 2 — Workstream A: Retrieval Foundation (Week 2-3)

| ID | Task | Est. | Status | Blocks |
|----|------|------|--------|--------|
| AIPL-010 | Implement RagQueryBuilder | 3h | 🔲 | AIPL-015, AIPL-017 |
| AIPL-011 | Implement SemanticDocumentChunker | 4h | 🔲 | AIPL-013 |
| AIPL-012 | Implement DocumentParserRouter + LlamaParseClient | 4h | 🔲 | AIPL-013 |
| AIPL-013 | Implement RagIndexingPipeline | 4h | 🔲 | AIPL-014, AIPL-015 |
| AIPL-014 | Implement RagIndexingJobHandler | 3h | 🔲 | AIPL-015 |
| AIPL-015 | Build KnowledgeBaseEndpoints | 4h | 🔲 | AIPL-018, AIPL-071 |
| AIPL-016 | Provision Two-Index Azure AI Search Schema | 2h | 🔲 | AIPL-018 |
| AIPL-017 | Add Retrieval Instrumentation (Recall@K logging) | 2h | 🔲 | AIPL-071 |
| AIPL-018 | Deploy Workstream A to Azure App Service | 1h | 🔲 | AIPL-070 |

**Phase 2 Total Estimate**: 27 hours
**Parallel Group**: AIPL-010, AIPL-011, AIPL-012 can start after AIPL-004 (independent)

---

## Phase 3 — Workstream B: Scope Library & Seed Data (Week 2-4, parallel with A)

| ID | Task | Est. | Status | Blocks |
|----|------|------|--------|--------|
| AIPL-030 | Create 8 Action Records (ACT-001–008) | 3h | 🔲 | AIPL-034, AIPL-036 |
| AIPL-031 | Create 10 Skill Records (SKL-001–010) | 2h | 🔲 | AIPL-034 |
| AIPL-032 | Create 10 Knowledge Source Records + Index to AI Search | 4h | 🔲 | AIPL-034, AIPL-070 |
| AIPL-033 | Create 8 Tool Records (TL-001–008) | 2h | 🔲 | AIPL-034, AIPL-036 |
| AIPL-034 | Create 10 Playbook Records (PB-001–010) | 3h | 🔲 | AIPL-037, AIPL-072 |
| AIPL-035 | Build ScopeConfigEditorPCF | 10h | 🔲 | AIPL-037 |
| AIPL-036 | Verify Handler Discovery API | 2h | 🔲 | AIPL-035, AIPL-037 |
| AIPL-037 | Deploy Workstream B (Dataverse + PCF) | 2h | 🔲 | AIPL-072 |

**Phase 3 Total Estimate**: 28 hours
**Parallel Group**: AIPL-030, AIPL-031, AIPL-032, AIPL-033 can run in parallel (different entities)
**Parallel with Phase 2**: Phase 3 can run fully parallel with Phase 2 (different files/owners)

---

## Phase 4 — Workstream C: SprkChat & Agent Framework (Week 3-5, parallel with B)

| ID | Task | Est. | Status | Blocks |
|----|------|------|--------|--------|
| AIPL-050 | C1: Integrate Agent Framework (IChatClient DI) | 2h | 🔲 | AIPL-051, AIPL-053 |
| AIPL-051 | C2: Implement SprkChatAgent + Factory + ContextProvider | 4h | 🔲 | AIPL-052, AIPL-053, AIPL-054 |
| AIPL-052 | C3: Implement ChatSessionManager + ChatHistoryManager | 4h | 🔲 | AIPL-054 |
| AIPL-053 | C4: Implement Chat Tools (4 tool classes) | 4h | 🔲 | AIPL-054 |
| AIPL-054 | C5: Build ChatEndpoints SSE | 4h | 🔲 | AIPL-057, AIPL-058 |
| AIPL-055 | C6: Build SprkChat React Shared Component | 8h | 🔲 | AIPL-056 |
| AIPL-056 | C7: Integrate SprkChat into AnalysisWorkspace PCF | 4h | 🔲 | AIPL-059 |
| AIPL-057 | C8: Build Agent Middleware (telemetry, cost, safety) | 3h | 🔲 | AIPL-058 |
| AIPL-058 | Deploy Workstream C — BFF API | 1h | 🔲 | AIPL-059, AIPL-070 |
| AIPL-059 | Deploy Workstream C — AnalysisWorkspace PCF | 1h | 🔲 | AIPL-070, AIPL-075 |

**Phase 4 Total Estimate**: 35 hours
**Dependencies**: AIPL-001 ✓ (Dataverse schema), AIPL-002 ✓ (Agent Framework packages)
**Parallel**: AIPL-051 and AIPL-055 can start in parallel after AIPL-050 (API vs UI work)

---

## Phase 5 — Workstream D: End-to-End Validation (Week 6-8)

| ID | Task | Est. | Status | Blocks |
|----|------|------|--------|--------|
| AIPL-070 | D1: Setup Test Document Corpus (10+ docs) | 4h | 🔲 | AIPL-071, AIPL-072 |
| AIPL-071 | D2: Build Evaluation Harness + EvalRunner CLI | 6h | 🔲 | AIPL-073, AIPL-075 |
| AIPL-072 | D3: Build E2E Tests for All 10 Playbooks | 6h | 🔲 | AIPL-073 |
| AIPL-073 | D4: Record Quality Baseline | 3h | 🔲 | AIPL-075 |
| AIPL-074 | D5: Run Negative Test Suite | 3h | 🔲 | AIPL-090 |
| AIPL-075 | D6: SprkChat Evaluation (accuracy, citations, latency) | 4h | 🔲 | AIPL-090 |

**Phase 5 Total Estimate**: 26 hours
**Prerequisites**: ALL of Phase 2 (A), Phase 3 (B), Phase 4 (C) must be complete
**Parallel Group**: AIPL-071, AIPL-072 can run in parallel after AIPL-070

---

## Phase 6 — Project Wrap-Up

| ID | Task | Est. | Status | Blocks |
|----|------|------|--------|--------|
| AIPL-090 | Project Wrap-Up — Final Validation and Archive | 2h | 🔲 | — |

**Phase 6 Total Estimate**: 2 hours

---

## Summary

| Phase | Tasks | Est. Hours | Status |
|-------|-------|-----------|--------|
| Phase 1: Foundation | 4 | 8h | 🔲 Not started |
| Phase 2: Workstream A (Retrieval) | 9 | 27h | 🔲 Not started |
| Phase 3: Workstream B (Scope Library) | 8 | 28h | 🔲 Not started |
| Phase 4: Workstream C (SprkChat) | 10 | 35h | 🔲 Not started |
| Phase 5: Workstream D (Validation) | 6 | 26h | 🔲 Not started |
| Phase 6: Wrap-Up | 1 | 2h | 🔲 Not started |
| **Total** | **38** | **~126h** | **🔲** |

---

## Dependency Graph

```
Phase 1 (Foundation)
├── AIPL-001 ──→ AIPL-052 (ChatSessionManager)
│              └→ AIPL-071 (evaluation schema)
├── AIPL-002 ──→ AIPL-050 (IChatClient DI)
├── AIPL-003 ──→ AIPL-010, AIPL-016
└── AIPL-004 ──→ all API tasks

Phase 2 (Workstream A) — parallel with Phase 3
├── AIPL-010 ──→ AIPL-015, AIPL-017
├── AIPL-011 ──→ AIPL-013
├── AIPL-012 ──→ AIPL-013
├── AIPL-013 ──→ AIPL-014 ──→ AIPL-015 ──→ AIPL-018
├── AIPL-016 ──→ AIPL-018
└── AIPL-018 ──→ AIPL-070

Phase 3 (Workstream B) — parallel with Phase 2
├── AIPL-030/031/032/033 (parallel) ──→ AIPL-034 ──→ AIPL-037
└── AIPL-035 ──→ AIPL-037 ──→ AIPL-072

Phase 4 (Workstream C) — can start after Phase 1
├── AIPL-050 ──→ AIPL-051 ──→ AIPL-052, AIPL-053
│              ──→ AIPL-055 ──→ AIPL-056
├── AIPL-054 ──→ AIPL-057, AIPL-058
└── AIPL-058/059 ──→ AIPL-070

Phase 5 (Workstream D) — requires A+B+C complete
├── AIPL-070 ──→ AIPL-071, AIPL-072
├── AIPL-071 ──→ AIPL-073 ──→ AIPL-075
└── AIPL-072 ──→ AIPL-073
    AIPL-074, AIPL-075 ──→ AIPL-090
```

---

## Critical Path

The critical path (longest dependency chain) is:

```
AIPL-001 → AIPL-052 → AIPL-054 → AIPL-058 → AIPL-059
→ AIPL-070 → AIPL-071 → AIPL-073 → AIPL-075 → AIPL-090
```

Estimated critical path length: ~35 hours of sequential work

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| P1A | AIPL-010, AIPL-011, AIPL-012 | AIPL-004 ✅ | Independent Workstream A services |
| P1B | AIPL-030, AIPL-031, AIPL-032, AIPL-033 | AIPL-004 ✅ | Independent Dataverse entities |
| P2 | AIPL-051, AIPL-055 | AIPL-050 ✅ | API vs UI work — different files |
| P3 | AIPL-071, AIPL-072 | AIPL-070 ✅ | Different evaluation types |
| P4 | AIPL-074, AIPL-075 | AIPL-073 ✅ | Independent test suites |

---

## High-Risk Items

| Risk | Task | Mitigation |
|------|------|-----------|
| Dataverse chat entity schema (AIPL-001) causes rework | AIPL-052, AIPL-054 | Complete schema in AIPL-001 before any C2 work |
| Agent Framework RC breaking change | AIPL-050 | Pin exact version; note in CLAUDE.md |
| DI count exceeds 15 (ADR-010) | AIPL-004, AIPL-050 | Audit after Phase 2 and Phase 4 |
| LlamaParse API not provisioned | AIPL-012 | Build fallback first; LlamaParse is enhancement |
| ScopeConfigEditorPCF bundle > 1MB | AIPL-035 | Use CodeMirror; verify bundle size in build step |

---

## Next Action

**Start with**: AIPL-001 — Define and Create Dataverse Chat + Evaluation Entities

```
work on task 001
```

Or to start Phase 2 and Phase 3 in parallel (after Phase 1 complete):
```
work on task 010  ← Workstream A: RagQueryBuilder
work on task 030  ← Workstream B: Action records (separate session/worktree)
```

---

*This index is updated by task-execute after each task completes. The status reflects actual task completion, not planned dates.*
