# Task Index — Workspace User Configuration R1

> **Last Updated**: 2026-03-29
> **Total Tasks**: 32
> **Status**: Ready for Execution

---

## Task Registry

### Phase 1: Foundation & Types (Group 0 — Serial)

| # | Task | Rigor | Effort | Depends | Status |
|---|------|-------|--------|---------|--------|
| 001 | Define SectionRegistration + SectionFactoryContext types | FULL | 3h | — | ✅ |
| 002 | Define 6 layout template configurations | STANDARD | 2h | 001 | ✅ |
| 003 | Define BFF API C# record DTOs | STANDARD | 2h | — | ✅ |
| 004 | Define sprk_workspacelayout entity schema | MINIMAL | 1h | — | ✅ |

### Phase 2: Section Migrations (Group A — Parallel, after 001)

| # | Task | Rigor | Effort | Depends | Status |
|---|------|-------|--------|---------|--------|
| 010 | Fix useFeedTodoSync no-op fallback | FULL | 2h | 001 | 🔲 |
| 011 | Migrate Get Started to SectionRegistration | FULL | 3h | 001 | 🔲 |
| 012 | Migrate Quick Summary to SectionRegistration | FULL | 2h | 001 | 🔲 |
| 013 | Migrate Latest Updates to SectionRegistration | FULL | 3h | 001 | 🔲 |
| 014 | Migrate My To Do List to SectionRegistration | FULL | 3h | 001, 010 | 🔲 |
| 015 | Migrate My Documents to SectionRegistration | FULL | 3h | 001 | 🔲 |
| 016 | Create aggregated SECTION_REGISTRY | STANDARD | 1h | 011-015 | 🔲 |

### Phase 3: Backend API (Group B — Parallel, after 003+004)

| # | Task | Rigor | Effort | Depends | Status |
|---|------|-------|--------|---------|--------|
| 020 | Create WorkspaceLayoutService | FULL | 6h | 003, 004 | 🔲 |
| 021 | Create workspace layout endpoints (8 endpoints) | FULL | 5h | 003, 020 | 🔲 |
| 022 | Create workspace authorization filter | FULL | 3h | 020 | 🔲 |
| 023 | Register workspace DI + wire endpoints | STANDARD | 2h | 020, 021, 022 | 🔲 |

### Phase 4: Dynamic Config & Header (Group C — after Phase 1+2)

| # | Task | Rigor | Effort | Depends | Status |
|---|------|-------|--------|---------|--------|
| 030 | Build dynamic config builder | FULL | 6h | 001, 002, 016 | 🔲 |
| 031 | Build WorkspaceHeader component | FULL | 5h | 001, 003 | 🔲 |
| 032 | Integrate dynamic config in LegalWorkspace | FULL | 6h | 016, 020, 030, 031 | 🔲 |

### Phase 5: Layout Wizard Code Page (Group D — Serial chain)

| # | Task | Rigor | Effort | Depends | Status |
|---|------|-------|--------|---------|--------|
| 040 | Scaffold wizard Code Page project | FULL | 4h | — | 🔲 |
| 041 | Wizard Step 1: Template selection | FULL | 5h | 002, 040 | 🔲 |
| 042 | Wizard Step 2: Section selection | FULL | 5h | 041 | 🔲 |
| 043 | Wizard Step 3: Arrange sections (DnD) | FULL | 8h | 042 | 🔲 |
| 044 | Wizard API integration (save flow) | FULL | 5h | 021, 043 | 🔲 |

### Phase 6: Integration & Polish (Group E — Parallel, after Phase 4)

| # | Task | Rigor | Effort | Depends | Status |
|---|------|-------|--------|---------|--------|
| 050 | URL deep-linking (workspaceId parameter) | STANDARD | 3h | 032 | 🔲 |
| 051 | sessionStorage caching | STANDARD | 3h | 032 | 🔲 |
| 052 | Loading states (skeleton, banner, fallback) | FULL | 5h | 032, 051 | 🔲 |
| 053 | System workspace Save As flow | STANDARD | 3h | 031, 044 | 🔲 |

### Phase 7: Testing, Deployment & Wrap-up (Group F)

| # | Task | Rigor | Effort | Depends | Status |
|---|------|-------|--------|---------|--------|
| 060 | BFF integration tests | STANDARD | 6h | 021, 022, 023 | 🔲 |
| 061 | Deploy wizard Code Page | MINIMAL | 2h | 044 | 🔲 |
| 062 | Deploy BFF API | MINIMAL | 1h | 023 | 🔲 |
| 063 | Deploy Corporate Workspace | MINIMAL | 2h | 032, 052 | 🔲 |
| 090 | Project wrap-up | MINIMAL | 3h | all | 🔲 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| **0** (serial) | 001 | — | Foundation types must be first |
| **0b** (parallel) | 002, 003, 004 | 001 (for 002), none (for 003, 004) | Types + DTOs + entity can parallel |
| **A** (parallel) | 010, 011, 012, 013, 015 | 001 complete | 5 independent section migrations |
| **A-tail** | 014 | 001, 010 | ToDo depends on useFeedTodoSync fix |
| **A-final** | 016 | 011-015 | Aggregates all registrations |
| **B** (parallel) | 020, 022 | 003, 004 | Service + filter can parallel |
| **B-tail** | 021 | 020 | Endpoints need service |
| **B-final** | 023 | 020, 021, 022 | DI wiring |
| **C** (parallel) | 030, 031 | 001, 002, 016 (030) / 001, 003 (031) | Config builder + header can parallel |
| **C-final** | 032 | 016, 020, 030, 031 | Integration of everything |
| **D** (serial) | 040 → 041 → 042 → 043 → 044 | 002 (041), 021 (044) | Wizard steps are serial |
| **E** (parallel) | 050, 051, 053 | 032 (050, 051), 031+044 (053) | Polish features |
| **E-tail** | 052 | 032, 051 | Loading states need cache |
| **F** (parallel) | 060, 061, 062, 063 | various | Testing + deployment |
| **Final** | 090 | all | Wrap-up |

## Critical Path

```
001 → 011-015 → 016 → 030 → 032 → 050-053 → 063 → 090
001 → 010 → 014 (parallel with above)
003 → 020 → 021 → 044 (wizard needs API)
040 → 041 → 042 → 043 → 044 (wizard serial chain)
```

**Longest path**: 001 → 002 → 041 → 042 → 043 → 044 → 053 → 090 (~45h)

## Estimated Total Effort

| Phase | Tasks | Hours |
|-------|-------|-------|
| Phase 1: Foundation | 4 | 8h |
| Phase 2: Section Migrations | 7 | 17h |
| Phase 3: Backend API | 4 | 16h |
| Phase 4: Dynamic Config & Header | 3 | 17h |
| Phase 5: Layout Wizard | 5 | 27h |
| Phase 6: Integration & Polish | 4 | 14h |
| Phase 7: Testing & Deployment | 5 | 14h |
| **Total** | **32** | **~113h** |

---

*Auto-generated by project-pipeline. Task statuses updated by task-execute skill.*
