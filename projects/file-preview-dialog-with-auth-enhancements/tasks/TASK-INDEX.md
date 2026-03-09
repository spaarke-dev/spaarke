# Task Index — File Preview Dialog with Auth Enhancements

> **Total Tasks**: 35
> **Phases**: 8 + wrap-up
> **Max Parallel Agents**: 5 (after Phase 1)

## Task Registry

### Phase 1: @spaarke/auth Shared Package (Foundation — Sequential)

| # | Task | Status | Rigor | Depends On | Parallel Group |
|---|------|--------|-------|------------|----------------|
| 001 | Scaffold @spaarke/auth Package | ✅ | FULL | — | — |
| 002 | Implement Token Acquisition Strategies | ✅ | FULL | 001 | — |
| 003 | Implement Authenticated Fetch | ✅ | FULL | 002 | — |
| 004 | Implement Token Bridge Utilities | ✅ | STANDARD | 001 | pg-1a (with 002) |
| 005 | Implement initAuth API and Options | ✅ | FULL | 002, 003, 004 | — |
| 006 | Unit Tests for @spaarke/auth | ✅ | STANDARD | 005 | — |
| 007 | Migrate LegalWorkspace to @spaarke/auth | ✅ | FULL | 006 | — |

### Phase 2: FilePreviewDialog Component (Group A)

| # | Task | Status | Rigor | Depends On | Parallel Group |
|---|------|--------|-------|------------|----------------|
| 010 | Create FilePreviewDialog Component | ✅ | FULL | 007 | A |
| 011 | Implement Open File + Open Record Actions | ✅ | FULL | 010 | A (parallel with 012) |
| 012 | Implement Copy Link + Workspace Toggle | ✅ | FULL | 010 | A (parallel with 011) |
| 013 | Build & Verify FilePreviewDialog | ✅ | STANDARD | 011, 012 | A |

### Phase 3: FilePreviewDialog Integration (Group A continued)

| # | Task | Status | Rigor | Depends On | Parallel Group |
|---|------|--------|-------|------------|----------------|
| 020 | Integrate into FindSimilarResultsStep | ✅ | FULL | 013 | A (parallel with 021) |
| 021 | Integrate into DocumentCard | ✅ | FULL | 013 | A (parallel with 020) |
| 022 | Build & Verify Phase 3 Integration | ✅ | STANDARD | 020, 021 | A |

### Phase 4: CreateDocumentDialog Code Page (Group B)

| # | Task | Status | Rigor | Depends On | Parallel Group |
|---|------|--------|-------|------------|----------------|
| 030 | Scaffold CreateDocument Code Page | ✅ | FULL | 007 | B |
| 031 | Build FileUploadStep | ✅ | FULL | 030 | B (parallel 031-035) |
| 032 | Build DocumentDetailsStep | ✅ | FULL | 030 | B (parallel 031-035) |
| 033 | Build NextStepsStep | ✅ | STANDARD | 030 | B (parallel 031-035) |
| 034 | Implement Upload Service | ✅ | FULL | 030 | B (parallel 031-035) |
| 035 | Implement Record Creation Service | ✅ | FULL | 030 | B (parallel 031-035) |
| 036 | Wire Up Wizard + Feature Flag | ✅ | FULL | 031-035 | B |
| 037 | Build & Verify CreateDocumentDialog | ✅ | STANDARD | 036 | B |

### Phase 5: Code Page Migration — Function-Based (Group C)

| # | Task | Status | Rigor | Depends On | Parallel Group |
|---|------|--------|-------|------------|----------------|
| 040 | Migrate AnalysisWorkspace Auth | ✅ | FULL | 007 | C (all parallel) |
| 041 | Migrate PlaybookBuilder Auth | ✅ | FULL | 007 | C (all parallel) |
| 042 | Migrate SprkChatPane Auth | ✅ | FULL | 007 | C (all parallel) |

### Phase 6: Code Page Migration — Class-Based (Group D)

| # | Task | Status | Rigor | Depends On | Parallel Group |
|---|------|--------|-------|------------|----------------|
| 050 | Migrate SemanticSearch Code Page Auth | ✅ | FULL | 007 | D (all parallel) |
| 051 | Migrate DocumentRelationshipViewer Code Page Auth | ✅ | FULL | 007 | D (all parallel) |

### Phase 7: PCF Migration — Pilot (Group E)

| # | Task | Status | Rigor | Depends On | Parallel Group |
|---|------|--------|-------|------------|----------------|
| 060 | Migrate SpeDocumentViewer PCF Auth (Pilot) | ✅ | FULL | 007 | E (all parallel) |
| 061 | Migrate SpeFileViewer PCF Auth (Pilot) | ✅ | FULL | 007 | E (all parallel) |

### Phase 8: PCF Migration — Complete (Group F)

| # | Task | Status | Rigor | Depends On | Parallel Group |
|---|------|--------|-------|------------|----------------|
| 070 | Migrate UniversalDatasetGrid PCF Auth | ✅ | FULL | 060 | F (all parallel) |
| 071 | Migrate SemanticSearchControl PCF Auth | ✅ | FULL | 060 | F (all parallel) |
| 072 | Migrate DocumentRelationshipViewer PCF Auth | ✅ | FULL | 060 | F (all parallel) |
| 073 | Migrate EmailProcessingMonitor PCF Auth | ✅ | FULL | 060 | F (all parallel) |
| 074 | Migrate AnalysisWorkspace PCF Auth + Scope Reconciliation | ✅ | FULL | 060 | F (all parallel) |

### Wrap-up

| # | Task | Status | Rigor | Depends On | Parallel Group |
|---|------|--------|-------|------------|----------------|
| 090 | Project Wrap-Up | 🔲 | MINIMAL | 022, 037, 042, 051, 074 | — |

---

## Parallel Execution Groups

After Phase 1 (tasks 001-007) completes, **up to 5 parallel agents** can work simultaneously:

| Group | Tasks | Prerequisite | Owner (Agent) | Notes |
|-------|-------|--------------|---------------|-------|
| **A** | 010-022 | 007 complete | Agent 1 | FilePreviewDialog → integration (sequential within group) |
| **B** | 030-037 | 007 complete | Agent 2 | CreateDocumentDialog (tasks 031-035 parallel within group) |
| **C** | 040, 041, 042 | 007 complete | Agent 3 | Code page migration: function-based (all 3 independent) |
| **D** | 050, 051 | 007 complete | Agent 4 | Code page migration: class-based (both independent) |
| **E** | 060, 061 | 007 complete | Agent 5 | PCF pilot (both independent) |
| **F** | 070-074 | 060 complete | Agents 3-5 (reuse) | Remaining PCF migration (all 5 independent) |

### Within-Group Parallelism

| Group | Parallel Opportunities |
|-------|----------------------|
| A | Tasks 011 + 012 parallel; Tasks 020 + 021 parallel |
| B | Tasks 031 + 032 + 033 + 034 + 035 parallel (all depend only on 030) |
| C | Tasks 040 + 041 + 042 all parallel |
| D | Tasks 050 + 051 parallel |
| E | Tasks 060 + 061 parallel |
| F | Tasks 070 + 071 + 072 + 073 + 074 all parallel |

---

## Critical Path

```
001 → 002 → 003 → 005 → 006 → 007 → [Groups A-E in parallel] → [Group F] → 090
         └→ 004 ──────┘
```

**Longest path**: 001 → 002 → 003 → 005 → 006 → 007 → 010 → 011 → 013 → 020 → 022 → 090 (12 sequential tasks)

**With parallel execution**: Critical path reduces to Phase 1 (7 tasks) + longest parallel branch (Group A: 6 tasks or Group B: 4+1 tasks) + wrap-up.

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 060-061 | PCF React 16 compatibility with @spaarke/auth | Pilot before full rollout (Phase 7 before 8) |
| 074 | Scope reconciliation (SDAP.Access vs user_impersonation) | Investigate early; may need Azure AD admin input |
| 034-035 | CreateDocumentDialog partial failure handling | Feature flag parallel deployment |
| 007 | LegalWorkspace regression after auth swap | Thorough manual UAT |

---

## Lines Removed Tracking

| Phase | Target Lines Removed | Actual |
|-------|---------------------|--------|
| Phase 1 (LegalWorkspace) | ~430 | — |
| Phase 5 (3 code pages) | ~1,627 | — |
| Phase 6 (2 code pages) | ~601 | — |
| Phase 7 (2 PCF pilot) | ~478 | — |
| Phase 8 (5 PCF controls) | ~3,861 | — |
| **Total** | **~6,997** | — |

> Note: Additional lines removed from CreateDocumentDialog (Phase 4) replacing UniversalQuickCreate PCF auth (~1,326 lines) brings total closer to ~8,149.
