# TASK-INDEX — Code Quality and Assurance R2

> **Project**: code-quality-and-assurance-r2
> **Branch**: feature/code-quality-and-assurance-r2
> **Created**: 2026-03-14
> **Total Tasks**: 18

## Task Registry

### Phase 1: Quick Wins

| # | Task | Status | Tags | Dependencies | Est |
|---|------|--------|------|--------------|-----|
| 001 | Fix 3 unbounded static dictionaries (memory leaks) | ✅ | remediation, dotnet, memory, caching | — | 3h |
| 002 | Replace `new HttpClient()` with IHttpClientFactory | ✅ | remediation, dotnet, graph | — | 1h |
| 003 | Fix no-op arch tests + add plugin assembly coverage | ✅ | testing, dotnet, architecture | — | 3h |
| 004 | Delete dead MsalAuthProvider.ts + create shared logger | ✅ | remediation, typescript, pcf, cleanup | — | 5h |

### Phase 2: Backend Structural Decomposition

| # | Task | Status | Tags | Dependencies | Est |
|---|------|--------|------|--------------|-----|
| 010 | Decompose OfficeService.cs → 4 focused services | ✅ | refactoring, dotnet, bff-api | 001 | 8h |
| 011 | Decompose AnalysisOrchestrationService → 3 services | ✅ | refactoring, dotnet, bff-api, ai | 001 | 8h |
| 012 | Segregate IDataverseService → 9 focused interfaces | ✅ | refactoring, dotnet, architecture | — | 5h |
| 013 | Migrate IDataverseService consumers to narrow interfaces | ✅ | refactoring, dotnet, bff-api | 010, 011, 012 | 4h |
| 014 | Build verification + integration test pass | ✅ | testing, dotnet, verification | 013 | 2h |

### Phase 3: Frontend Structural Decomposition

| # | Task | Status | Tags | Dependencies | Est |
|---|------|--------|------|--------------|-----|
| 020 | Extract useAuth + useDocumentResolution hooks | ✅ | refactoring, typescript, pcf | 004 | 3h |
| 021 | Extract useAnalysisData + useAnalysisExecution hooks | ✅ | refactoring, typescript, pcf | 020 | 3h |
| 022 | Extract useWorkingDocumentSave + useChatState hooks | ✅ | refactoring, typescript, pcf | 020 | 3h |
| 023 | Extract usePanelResize + finalize component | ✅ | refactoring, typescript, pcf | 021, 022 | 2h |
| 024 | PCF build verification | ✅ | testing, typescript, verification | 023 | 1h |

### Phase 4: Architecture Compliance

| # | Task | Status | Tags | Dependencies | Est |
|---|------|--------|------|--------------|-----|
| 030 | Fix ADR-022 violations — React 18→16 in 3 PCF controls | ✅ | remediation, typescript, pcf, adr | 004 | 3h |
| 031 | Document BaseProxyPlugin ADR-002 violations | ✅ | documentation, dotnet, architecture | 003 | 2h |
| 032 | Final quality scorecard + lessons learned | ✅ | documentation, quality | 014, 024, 030, 031 | 2h |

### Phase 5: Project Wrap-Up

| # | Task | Status | Tags | Dependencies | Est |
|---|------|--------|------|--------------|-----|
| 090 | Project wrap-up (TASK-INDEX reconcile, archive, README) | 🔲 | documentation, cleanup | 032 | 1h |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 002, 003, 004 | None | All Phase 1 tasks are fully independent — run simultaneously |
| B | 010, 011, 012 | 001 complete (010, 011); none (012) | Different files, no overlap — run simultaneously |
| C | 021, 022 | 020 complete | Independent hook extractions — run simultaneously |
| D | 030, 031 | 004 complete (030); 003 complete (031) | Different areas — run simultaneously |

## Critical Path

```
001 ──→ 010 ──→ 013 ──→ 014 ──→ 032 ──→ 090
001 ──→ 011 ──┘
        012 ──┘

004 ──→ 020 ──→ 021 ──→ 023 ──→ 024 ──→ 032
              → 022 ──┘

004 ──→ 030 ──→ 032
003 ──→ 031 ──→ 032
```

**Longest path**: 001 → 010 → 013 → 014 → 032 → 090 (~20h sequential)
**With parallelism**: ~9 time slots (see plan.md timeline)

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 010 | OfficeService decomposition breaks upload flow | Behavior-preserving extraction; integration test verification |
| 013 | Consumer migration breaks DI resolution | Composite interface preserves backward compat; build + test after |
| 020-023 | Hook extraction breaks state flow | Extract one hook at a time; PCF build after each |
| 030 | React 16 downgrade breaks PCF rendering | SemanticSearchControl is reference (already React 16) |

## Summary

- **Total tasks**: 18
- **Parallelizable**: 11 tasks across 4 groups
- **Sequential**: 7 tasks on critical path
- **Estimated wall-clock**: ~23-26h (vs ~55h sequential)
