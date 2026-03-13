# Task Index — Production Performance Improvement R1

> **Last Updated**: 2026-03-11
> **Total Tasks**: 35 + 1 wrap-up
> **Parallel Execution**: Enabled — see Parallel Groups below

## Status Legend
- 🔲 Not Started
- 🔄 In Progress
- ✅ Complete
- ⛔ Blocked

## Task Overview

### Phase 1: Beta Blockers (Must Complete Before User Access)

| # | Task | Domain | Status | Parallel Group | Dependencies |
|---|------|--------|--------|---------------|-------------|
| 001 | Bicep tier corrections | C | 🔲 | P1-A | None |
| 002 | DataverseWebApiService thread-safety fixes | B | 🔲 | P1-A | None |
| 003 | Secure unauthenticated AI endpoints | F | 🔲 | P1-B | None |
| 004 | Production safety fixes (CORS, Console.WriteLine, debug) | F | 🔲 | P1-B | None |
| 005 | Implement 37 Office authorization filters | F | 🔲 | P1-C | Task 033 design |

### Phase 2: Quick Performance Wins

| # | Task | Domain | Status | Parallel Group | Dependencies |
|---|------|--------|--------|---------------|-------------|
| 010 | Parallelize RAG knowledge searches | E | 🔲 | P2-ALL | Phase 1 |
| 011 | Document Intelligence timeout + circuit breaker | E | 🔲 | P2-ALL | Phase 1 |
| 012 | OpenAI parameter tuning | E | 🔲 | P2-ALL | Phase 1 |
| 013 | Replace ColumnSet(true) with explicit columns | B | 🔲 | P2-ALL | Phase 1 |
| 014 | GraphServiceClient singleton pooling | A | 🔲 | P2-ALL | Phase 1 |
| 015 | Debug endpoint removal | A | 🔲 | P2-ALL | Phase 1 |
| 016 | Remove [DEBUG] log tags | G | 🔲 | P2-ALL | Phase 1 |

### Phase 3: Core Caching

| # | Task | Domain | Status | Parallel Group | Dependencies |
|---|------|--------|--------|---------------|-------------|
| 020 | Cache extracted document text | E | 🔲 | P3-A | 010 (E1) |
| 021 | Cache RAG search results | E | 🔲 | P3-A | 010 (E1) |
| 022 | Graph metadata cache | A | 🔲 | P3-B | Phase 2 |
| 023 | Authorization data snapshot cache | A | 🔲 | P3-B | Phase 2 |
| 024 | Dataverse request batching ($batch) | B | 🔲 | P3-C | 013 (B1) |
| 025 | Pagination support for unbounded queries | B | 🔲 | P3-C | 013 (B1) |

### Phase 4: Code Quality, Logging & Workspace

| # | Task | Domain | Status | Parallel Group | Dependencies |
|---|------|--------|--------|---------------|-------------|
| 030 | Guard serialization in log calls | G | 🔲 | P4-ALL | Phase 3 |
| 031 | Batch loop logging | G | 🔲 | P4-ALL | Phase 3 |
| 032 | Production log level configuration | G | 🔲 | P4-ALL | Phase 3 |
| 033 | Remove string allocations from log parameters | G | 🔲 | P4-ALL | Phase 3 |
| 034 | Remove obsolete tool handlers | F | 🔲 | P4-ALL | Phase 3 |
| 035 | Implement real Workspace Dataverse queries | F | 🔲 | P4-ALL | Phase 3 |
| 036 | Delete deprecated AI Search index | C | 🔲 | P4-ALL | Phase 3 |

### Phase 5: Infrastructure Hardening

| # | Task | Domain | Status | Parallel Group | Dependencies |
|---|------|--------|--------|---------------|-------------|
| 040 | VNet creation + private endpoints | C | 🔲 | P5-A | Phase 4 |
| 041 | App Service autoscaling | C | 🔲 | P5-A | Phase 4 |
| 042 | Deployment slot configuration | C | 🔲 | P5-A | Phase 4 |
| 043 | Redis VNet injection + RDB persistence | C | 🔲 | P5-B | 040 |
| 044 | Key Vault hardening | C | 🔲 | P5-B | 040 |
| 045 | Storage account hardening | C | 🔲 | P5-B | 040 |
| 046 | OpenAI capacity + network hardening | C | 🔲 | P5-B | 040 |

### Phase 6: CI/CD Maturity & Refactoring

| # | Task | Domain | Status | Parallel Group | Dependencies |
|---|------|--------|--------|---------------|-------------|
| 050 | Test suite re-enablement | D | 🔲 | P6-A | Phase 5 |
| 051 | Bicep IaC deployment in CI/CD | D | 🔲 | P6-A | Phase 5 |
| 052 | Environment promotion with approval gates | D | 🔲 | P6-B | 051 |
| 053 | Deployment slot swap in CI/CD | D | 🔲 | P6-B | 042, 051 |
| 054 | Refactor ScopeResolverService god class | F | 🔲 | P6-A | Phase 5 |

### Wrap-Up

| # | Task | Domain | Status | Parallel Group | Dependencies |
|---|------|--------|--------|---------------|-------------|
| 090 | Project wrap-up | — | 🔲 | — | All tasks |

## Parallel Execution Groups

**Maximized for Claude Code task agents** — launch all tasks in a group simultaneously.

| Group | Phase | Tasks | Agent Count | Prerequisite | File Conflict Risk |
|-------|-------|-------|------------|-------------|-------------------|
| **P1-A** | 1 | 001, 002 | 2 | None | None (Bicep vs Dataverse) |
| **P1-B** | 1 | 003, 004 | 2 | None | Low (different endpoint files) |
| **P1-C** | 1 | 005 | 1 | Task 033 design | None |
| **P2-ALL** | 2 | 010-016 | **7** | Phase 1 complete | None (all different services/files) |
| **P3-A** | 3 | 020, 021 | 2 | Task 010 | Low (different cache targets) |
| **P3-B** | 3 | 022, 023 | 2 | Phase 2 | None (Graph vs Auth) |
| **P3-C** | 3 | 024, 025 | 2 | Task 013 | Low (different Dataverse methods) |
| **P4-ALL** | 4 | 030-036 | **7** | Phase 3 complete | Low (logging=100 files split, code=different handlers) |
| **P5-A** | 5 | 040, 041, 042 | 3 | Phase 4 | None (different Bicep modules) |
| **P5-B** | 5 | 043-046 | 4 | Task 040 | None (different Bicep modules) |
| **P6-A** | 6 | 050, 051, 054 | 3 | Phase 5 | None (CI/CD vs C# code) |
| **P6-B** | 6 | 052, 053 | 2 | Tasks 051, 042 | Low (different workflow sections) |

### Maximum Parallelism Summary

| Phase | Max Parallel Agents | Sequential Bottleneck |
|-------|--------------------|-----------------------|
| 1 | 4 (P1-A + P1-B) + 1 (P1-C after) | F2 depends on Task 033 design |
| 2 | **7** (all tasks independent) | None |
| 3 | 6 (P3-A + P3-B + P3-C) | P3-A needs E1, P3-C needs B1 |
| 4 | **7** (all tasks independent) | None |
| 5 | 3 (P5-A) then 4 (P5-B) | C1 VNet must complete first |
| 6 | 3 (P6-A) then 2 (P6-B) | D2 must complete first |

### Critical Path

```
C0 (001) ─┐
B3 (002) ─┤
F1 (003) ─┼─→ Phase 2 (7 parallel) ─→ Phase 3 (6 parallel) ─→ Phase 4 (7 parallel) ─→ Phase 5 ─→ Phase 6 ─→ 090
F6 (004) ─┤
F2 (005) ─┘
```

**Longest path**: Phase 1 → Phase 2 → E1(010) → E2(020) → Phase 4 → C1(040) → C4(043) → D2(051) → D3(052) → 090

## Statistics

| Metric | Value |
|--------|-------|
| Total tasks | 36 (35 + wrap-up) |
| Phases | 6 |
| Max parallel agents per phase | 7 (Phases 2 and 4) |
| Tasks with no dependencies | 4 (Phase 1: 001-004) |
| Critical path length | ~12 sequential task groups |

---

*Generated by project-pipeline skill. Optimized for Claude Code task agent parallel execution.*
