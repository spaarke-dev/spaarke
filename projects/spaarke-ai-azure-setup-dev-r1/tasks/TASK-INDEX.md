# Task Index — spaarke-ai-azure-setup-dev-r1

> **Project**: Spaarke AI Search Azure Setup (Dev Restoration + Canonicalization)
> **Status**: Ready for Implementation
> **Total Tasks**: 25 (1 pre-flight + 7 docs + 7 schemas + 2 deploy + 9 code-refactor + 4 deploy-validate + 1 wrap-up)
> **Last Updated**: 2026-06-26

---

## Project Summary

| Metric | Value |
|--------|-------|
| Project ID | spaarke-ai-azure-setup-dev-r1 |
| Phases | 5 + wrap-up |
| Total tasks | 25 |
| FULL rigor tasks | 11 |
| STANDARD rigor tasks | 9 |
| MINIMAL rigor tasks | 5 |
| Tasks touching `.claude/` (main-session-only) | 2 (007, 040) |

---

## Task Registry

| ID | Title | Phase | Status | Rigor | Dependencies | Parallel |
|----|-------|-------|--------|-------|--------------|----------|
| 001 | Pre-Phase-3 operational verification (10 checks FR-21) | 1 | ✅ | STANDARD | none | — |
| 002 | Create AI-SEARCH-INDEX-CATALOG.md | 1 | ✅ | STANDARD | 001 | Group A |
| 003 | Create ai-search-azure-setup.md | 1 | ✅ | STANDARD | 001, 002 | Group A |
| 004 | Update AI-ARCHITECTURE.md consumer map | 1 | ✅ | MINIMAL | 001, 002 | Group A |
| 005 | Stale doc cleanup (FR-04) | 1 | ✅ | MINIMAL | 001, 002 | Group A |
| 006 | Append §4.6 to SPAARKE-DEPLOYMENT-GUIDE.md | 1 | ✅ | MINIMAL | 001, 002, 003 | Group A |
| 007 | ADR pointer drift fix (FR-06) | 1 | ✅ | MINIMAL | 001 | — (touches .claude/) |
| 010 | Schema property policy patches (7 schemas) | 2 | ✅ | STANDARD | 002 | — |
| 011 | Atomic rename: spaarke-file-index → spaarke-files-index | 2 | ✅ | STANDARD | 002, 010 | — (NFR-07 atomic) |
| 012 | Consolidate schema files to infrastructure/ai-search/ | 2 | ✅ | STANDARD | 011 | — |
| 013 | Atomic rename: playbook-embeddings → spaarke-playbook-embeddings | 2 | 🔲 | STANDARD | 002, 010 | — (NFR-07 atomic) |
| 014 | Atomic rename: spaarke-invoices-dev → spaarke-invoices-index | 2 | 🔲 | STANDARD | 002, 010 | — (NFR-07 atomic) |
| 015 | Add tenantId field to spaarke-records-index | 2 | 🔲 | STANDARD | 010 | Group B |
| 016 | Fix spaarke-rag-references field-name bug | 2 | 🔲 | STANDARD | 010 | Group B |
| 020 | Write Deploy-AllIndexes.ps1 | 3 | 🔲 | FULL | 001, 002, 003, 010-016 | — |
| 021 | Validate Deploy-AllIndexes.ps1 (-DryRun + -VerifyOnly) | 3 | 🔲 | STANDARD | 020 | — |
| 030 | BFF Configuration options classes (AiSearchOptions + AnalysisOptions) | 4 | 🔲 | FULL | 011, 013, 014 | — |
| 031 | BFF refactor: RAG pipeline services | 4 | 🔲 | FULL | 030 | Group C |
| 032 | BFF refactor: file indexing + reference services | 4 | 🔲 | FULL | 030 | Group C |
| 033 | BFF refactor: PlaybookEmbedding services | 4 | 🔲 | FULL | 013, 030 | Group C |
| 034 | BFF refactor: KnowledgeDeployment + KnowledgeBaseEndpoints | 4 | 🔲 | FULL | 030 | Group C |
| 035 | BFF refactor: Job handlers (Rag + Invoice) | 4 | 🔲 | FULL | 014, 030 | Group C |
| 036 | BFF refactor: records-index tenantId writer + reader | 4 | 🔲 | FULL | 015, 030 | Group C |
| 037 | BFF doc-comment cleanup (4 BFF files) | 4 | 🔲 | MINIMAL | 030 | Group C |
| 038 | appsettings + templates cleanup (FR-13 + FR-14 + FR-20) | 4 | 🔲 | STANDARD | 030 | Group D |
| 039 | Frontend doc-comments cleanup (4 client files) | 4 | 🔲 | MINIMAL | 011 | Group D |
| 040 | .claude/ doc updates (FR-13) | 4 | 🔲 | MINIMAL | 011 | — (touches .claude/) |
| 041 | Dev BFF KV-reference migration (FR-15) | 4 | 🔲 | FULL | 001, 030, 038 | — |
| 042 | MANDATORY test-fixture sweep (NFR-14) | 4 | 🔲 | FULL | 030-041 | — (gate) |
| 045 | BFF publish-size delta verification (NFR-04) | 4 | 🔲 | STANDARD | 030-042 | — |
| 046 | Phase 4 final grep verification | 4 | 🔲 | STANDARD | 031-041 | — |
| 050 | Deploy 7 schemas to spaarke-search-dev (FR-16) | 5 | 🔲 | FULL | 020, 021, 036, 041, 042, 046 | — |
| 051 | FR-17 verification: rag-references golden-reference roundtrip | 5 | 🔲 | STANDARD | 016, 050 | Group E |
| 052 | Data ingestion for 4 ingestible indexes (FR-18) | 5 | 🔲 | STANDARD | 050, 051 | Group E |
| 054 | Dev BFF functional verification (5 endpoints FR-19) | 5 | 🔲 | FULL | 050, 051, 052 | — |
| 090 | Project Wrap-up (quality gates + cleanup + README) | Wrap-up | 🔲 | FULL | 054 | — |

**Status legend**: 🔲 not-started · 🟡 in-progress / blocked · ✅ completed · ⏭️ deferred

---

## Parallel Execution Plan

Tasks in the same group can run simultaneously once prerequisites are met. The parent skill (project-pipeline / task-execute) handles the actual parallel invocation.

### Phase 1 Parallel Groups

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| (sequential) | 001 | none | notes/pre-phase-3-verification.md | — (foundational, blocks all Phase 3) |
| **Group A** | 002, 003, 004, 005, 006 | 001 (+ 002 for 003/004/005/006) | Different docs (catalog, guide, AI-ARCHITECTURE, stale docs, deployment guide) | ✅ Yes — different files (note: 003 depends on 002 logically but can start in parallel after 002 minimal skeleton exists) |
| (main-session-only) | 007 | 001 | .claude/ + docs/architecture/rag-architecture.md | ❌ No — `.claude/` write boundary |

### Phase 2 Sequential

NFR-07 mandates atomic per-rename PRs; schemas are touched by overlapping renames; sequential gate.

| Wave | Tasks | Notes |
|------|-------|-------|
| Wave 2-1 | 010 | Property policy patches first (touches all 7 schemas) |
| Wave 2-2 | 011 | Rename 1: files-index (1 atomic PR) |
| Wave 2-3 | 012 | Consolidation after rename 1 |
| Wave 2-4 | 013 | Rename 2: playbook-embeddings (1 atomic PR) |
| Wave 2-5 | 014 | Rename 3: invoices-index (1 atomic PR) |
| Wave 2-6 (parallel) | **Group B**: 015, 016 | Independent fields/files |

### Phase 3 Sequential

| Wave | Tasks | Notes |
|------|-------|-------|
| Wave 3-1 | 020 | Write Deploy-AllIndexes.ps1 (+ retire 6 scripts in same PR) |
| Wave 3-2 | 021 | Validate -DryRun + -VerifyOnly |

### Phase 4 Parallel Groups

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| (sequential) | 030 | 011, 013, 014 | Configuration options classes (DI-relevant; must complete first) | — |
| **Group C** | 031, 032, 033, 034, 035, 036, 037 | 030 (+ 013/014/015 where applicable) | Different BFF service files; no overlap | ✅ Yes — different files; consumer services + handlers + tenant code + doc-comments |
| **Group D** | 038, 039 | 030 (+ 011) | appsettings/templates + frontend doc-comments — separate file trees | ✅ Yes |
| (main-session-only) | 040 | 011 | .claude/patterns/ai/indexing-pipeline.md | ❌ No — `.claude/` write boundary |
| (sequential gate) | 041 | 001, 030, 038 | Live App Service config; KV-ref migration; security-sensitive | — |
| (sequential gate) | **042** | 030-041 | **NFR-14 test fixture sweep — preventive gate** | — (binding sequential) |
| (sequential gate) | 045, 046 | 030-042 | Verification: publish-size + grep | — |

### Phase 5 Mixed

| Wave | Tasks | Notes |
|------|-------|-------|
| Wave 5-1 | 050 | Deploy 7 schemas (sequential — depends on Phases 3+4) |
| Wave 5-2 (parallel) | **Group E**: 051, 052 | FR-17 roundtrip + ingestion — independent post-deploy work |
| Wave 5-3 | 054 | Functional verification (depends on ingestion data) |

### Wrap-up

| Wave | Tasks | Notes |
|------|-------|-------|
| Wave Final | 090 | Quality gates + cleanup + README + lessons learned |

---

## Critical Path

Longest sequential dependency chain (no parallelization possible):

```
001 → 002 → 010 → 011 → 012 → 013 → 014 → 020 → 021 → 030 → 041 → 042 → 045 → 046 → 050 → 052 → 054 → 090
                                                                                                          (18 sequential tasks)
```

**Estimated critical-path effort**: ~30 hours (with 17 parallel tasks running concurrently as 5-7 hours wall-clock additional).

---

## High-Risk Items

| Risk | Tasks Affected | Mitigation |
|------|----------------|------------|
| **NFR-14 test-fixture sweep** | 042 | Binding gate; preventive sweep IS the mitigation; Redis project hit 337 failures from analogous DI tightening |
| **FR-17 bug fix coordination** | 016, 051 | Schema + PS writer landed in Phase 2 (016); Phase 5 (051) verifies via golden-reference roundtrip |
| **FR-13 BFF refactor (~20 files)** | 030-037, 045, 046 | Sequential 030 first (DI-relevant); Group C parallel (Independent files); 046 grep gate confirms completeness |
| **FR-20 embedding model alignment** | 038, 052 | 038 changes EmbeddingModelName; FR-21 #4 (task 001) verifies deployment exists; 052 ingestion would fail without alignment |
| **FR-15 KV-reference migration** | 041 | Security-sensitive; depends on FR-21 #1 (key freshness) + FR-21 #2 (BFF MI role); Redis project established canonical pattern |
| **Atomic per-rename PRs (NFR-07)** | 011, 013, 014 | Sequential schema renames; each touches schema + JSON name + BFF code + script + runbook simultaneously |

---

## How to Execute

### Standard Workflow

```bash
# Execute next pending task via task-execute skill
# When user says "continue" or "work on task NNN", invoke:
#   Skill tool with skill="task-execute" and task path

# Parallel execution example for Group A (Phase 1 docs):
# Send ONE message with MULTIPLE Skill invocations:
#   - task-execute tasks/002-ai-search-index-catalog.poml
#   - task-execute tasks/003-ai-search-azure-setup-guide.poml
#   - task-execute tasks/004-ai-architecture-consumer-map.poml
#   - task-execute tasks/005-stale-doc-cleanup.poml
#   - task-execute tasks/006-deployment-guide-phase-1-5.poml
```

### Phase Gates

- **Phase 1 → Phase 2**: 007 ✅ (catalog drives schema work)
- **Phase 2 → Phase 3**: 010-016 ✅ (schemas finalized; Bicep paths updated)
- **Phase 3 → Phase 4**: 021 ✅ (deployer validated)
- **Phase 4 → Phase 5**: 042 ✅ (test fixture sweep) + 045 ✅ (publish size) + 046 ✅ (grep)
- **Phase 5 → Wrap-up**: 054 ✅ (functional verification)
- **Wrap-up complete**: 090 ✅ → PROJECT COMPLETE

---

## References

- [`../spec.md`](../spec.md) — Authoritative spec (21 FRs, 14 NFRs)
- [`../design.md`](../design.md) — Design rationale + 5-phase plan
- [`../plan.md`](../plan.md) — Implementation plan with discovered resources
- [`../CLAUDE.md`](../CLAUDE.md) — Project AI context + binding rules
- [`../README.md`](../README.md) — Project overview + graduation criteria
- [`../current-task.md`](../current-task.md) — Active task state (context recovery)
