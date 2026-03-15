# Code Quality and Assurance R2 — Implementation Plan

> **Version**: 1.0
> **Created**: 2026-03-14
> **Source**: spec.md

## Architecture Context

### Decomposition Strategy

```
CURRENT STATE                          TARGET STATE
─────────────                          ────────────

OfficeService.cs (2,907 lines)    →    OfficeService.cs (<500 lines, orchestrator)
  7 responsibilities                     ├── OfficeEmailEnricher.cs
  1 God class                            ├── OfficeDocumentPersistence.cs
                                         ├── OfficeJobQueue.cs
                                         └── OfficeStorageUploader.cs

AnalysisOrchestrationService.cs   →    AnalysisOrchestrationService.cs (orchestrator)
  20 DI dependencies                     ├── AnalysisDocumentLoader.cs
  890-line method                        ├── AnalysisRagProcessor.cs
                                         └── AnalysisResultPersistence.cs

IDataverseService.cs (63 methods) →    IDataverseService (composite)
  1 bloated interface                    ├── IDocumentDataverseService (13)
                                         ├── IAnalysisDataverseService (4)
                                         ├── IGenericEntityService (9)
                                         ├── IProcessingJobService (8)
                                         ├── IEventDataverseService (9)
                                         ├── IFieldMappingDataverseService (7)
                                         ├── IKpiDataverseService (2)
                                         ├── ICommunicationDataverseService (9)
                                         └── IDataverseHealthService (2)

AnalysisWorkspaceApp.tsx          →    AnalysisWorkspaceApp.tsx (<400 lines)
  1,564 lines, 29 useState              ├── useAuth.ts
                                         ├── useDocumentResolution.ts
                                         ├── useAnalysisData.ts
                                         ├── useAnalysisExecution.ts
                                         ├── useWorkingDocumentSave.ts
                                         ├── useChatState.ts
                                         └── usePanelResize.ts
```

### Discovered Resources

#### Applicable ADRs

| ADR | Title | Applies To |
|-----|-------|-----------|
| ADR-001 | Minimal API + BackgroundService | HttpClient factory |
| ADR-002 | Plugin constraints | BaseProxyPlugin assessment |
| ADR-003 | Lean authorization seams | IDataverseService consumer migration |
| ADR-007 | SpeFileStore facade | OfficeService decomposition |
| ADR-008 | Endpoint filters for auth | AnalysisOrchestrationService dep reduction |
| ADR-009 | Redis-first caching | Memory leak fixes |
| ADR-010 | DI minimalism | Service registration patterns |
| ADR-012 | Shared component library | Hook extraction conventions |
| ADR-021 | Fluent UI v9 design system | Frontend decomposition |
| ADR-022 | PCF platform libraries | React 18→16 fix |

#### Applicable Skills

| Skill | Purpose |
|-------|---------|
| adr-aware | Load ADR constraints for each task |
| code-review | Quality gate at step 9.5 |
| adr-check | Validate compliance after changes |

#### Existing Patterns

| Pattern | Location | Usage |
|---------|----------|-------|
| DI module extension | `Infrastructure/DI/AnalysisServicesModule.cs` | Template for new registrations |
| Shared hook | `Spaarke.UI.Components/src/hooks/useSseStream.ts` | Hook file conventions |
| Logger utility | `AnalysisWorkspace/control/utils/logger.ts` | Shared logger template |
| Auth migration | `SemanticSearchControl/authInit.ts` | @spaarke/auth pattern |

## Phase Breakdown

### Phase 1: Quick Wins (All 4 tasks run in parallel)

**Objective**: Fix resource safety issues and eliminate dead code. All tasks are independent — no dependencies between them.

| # | Task | Estimate | Tags |
|---|------|----------|------|
| 001 | Fix 3 unbounded static dictionaries (memory leaks) | 3h | remediation, dotnet, memory, caching |
| 002 | Replace `new HttpClient()` with IHttpClientFactory | 1h | remediation, dotnet, graph |
| 003 | Fix no-op arch tests + add plugin assembly coverage | 3h | testing, dotnet, architecture |
| 004 | Delete dead MsalAuthProvider.ts + create shared logger | 5h | remediation, typescript, pcf, cleanup |

**Parallel Group A**: Tasks 001, 002, 003, 004 — all independent, run simultaneously.

---

### Phase 2: Backend Structural Decomposition

**Objective**: Decompose God classes and segregate interfaces. Tasks 010 and 011 are independent (different files). Task 012 depends on both completing (interface changes affect service files).

| # | Task | Estimate | Tags | Dependencies |
|---|------|----------|------|-------------|
| 010 | Decompose OfficeService.cs → 4 focused services | 8h | refactoring, dotnet, bff-api | 001 |
| 011 | Decompose AnalysisOrchestrationService → 3 services | 8h | refactoring, dotnet, bff-api, ai | 001 |
| 012 | Segregate IDataverseService → 9 focused interfaces | 5h | refactoring, dotnet, architecture | — |
| 013 | Migrate IDataverseService consumers to narrow interfaces | 4h | refactoring, dotnet, bff-api | 010, 011, 012 |
| 014 | Build verification + integration test pass | 2h | testing, dotnet, verification | 013 |

**Parallel Group B**: Tasks 010, 011, 012 — different files, run simultaneously after Phase 1.
**Sequential**: Task 013 after Group B completes (needs all decompositions done). Task 014 after 013.

---

### Phase 3: Frontend Structural Decomposition

**Objective**: Decompose the AnalysisWorkspace God component into focused hooks.

| # | Task | Estimate | Tags | Dependencies |
|---|------|----------|------|-------------|
| 020 | Extract useAuth + useDocumentResolution hooks | 3h | refactoring, typescript, pcf | 004 |
| 021 | Extract useAnalysisData + useAnalysisExecution hooks | 3h | refactoring, typescript, pcf | 020 |
| 022 | Extract useWorkingDocumentSave + useChatState hooks | 3h | refactoring, typescript, pcf | 020 |
| 023 | Extract usePanelResize + finalize component | 2h | refactoring, typescript, pcf | 021, 022 |
| 024 | PCF build verification | 1h | testing, typescript, verification | 023 |

**Parallel Group C**: Tasks 021, 022 — independent hooks, run simultaneously after 020.
**Sequential**: Task 020 first (auth/resolution are foundational). Task 023 after 021+022. Task 024 after 023.

---

### Phase 4: Architecture Compliance

**Objective**: Fix ADR violations and document legacy issues.

| # | Task | Estimate | Tags | Dependencies |
|---|------|----------|------|-------------|
| 030 | Fix ADR-022 violations — React 18→16 in 3 PCF controls | 3h | remediation, typescript, pcf, adr | 004 |
| 031 | Document BaseProxyPlugin ADR-002 violations | 2h | documentation, dotnet, architecture | 003 |
| 032 | Final quality scorecard + lessons learned | 2h | documentation, quality | 014, 024, 030, 031 |

**Parallel Group D**: Tasks 030, 031 — independent, run simultaneously.
**Sequential**: Task 032 after everything else completes.

---

### Phase 5: Project Wrap-Up

| # | Task | Estimate | Tags | Dependencies |
|---|------|----------|------|-------------|
| 090 | Project wrap-up (TASK-INDEX reconcile, archive, README update) | 1h | documentation, cleanup | 032 |

---

## Execution Timeline (Parallel)

```
TIME    PHASE 1 (parallel)          PHASE 2 (parallel)              PHASE 3          PHASE 4    P5
────    ──────────────────          ──────────────────              ────────          ────────   ──
T1      001 ─┐
T1      002 ─┤ All 4 parallel
T1      003 ─┤
T1      004 ─┘
T2                                  010 ─┐
T2                                  011 ─┤ 3 parallel
T2                                  012 ─┘
T3                                  013 (sequential)
T3                                  014 (verify)
T4                                                                  020
T5                                                                  021 ─┐ parallel
T5                                                                  022 ─┘
T6                                                                  023
T6                                                                  024 (verify)
T7                                                                                  030 ─┐
T7                                                                                  031 ─┘
T8                                                                                  032
T9                                                                                         090

Total wall-clock: ~23-26h (vs ~55h sequential)
```

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| OfficeService decomposition breaks upload flow | Medium | High | Behavior-preserving extraction. Verify with integration tests. |
| IDataverseService consumer migration breaks DI | Low | High | Composite interface preserves backward compat. Build + test after. |
| React 16 downgrade breaks PCF rendering | Medium | Medium | SemanticSearchControl is reference (already React 16). Test in form. |
| Hook extraction breaks state flow | Medium | High | Extract one hook at a time. PCF build after each. |

## References

- [Design Document](design.md)
- [Specification](spec.md)
- [R1 Lessons Learned](../code-quality-and-assurance-r1/notes/lessons-learned.md)
- ADRs: `.claude/adr/ADR-{001,002,003,007,008,009,010,012,021,022}.md`
- Patterns: `.claude/patterns/`
- Constraints: `.claude/constraints/`
