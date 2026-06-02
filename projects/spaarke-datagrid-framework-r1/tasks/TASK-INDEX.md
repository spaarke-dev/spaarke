# Task Index — spaarke-datagrid-framework-r1

> **Last Updated**: 2026-06-01
> **Status**: Ready for execution
> **Total Tasks**: 39 (Phase A: 9, Phase B: 8, Phase C: 7, Phase D: 6, Phase E: 3, Phase F: 5, Wrap-up: 1)

---

## Task Registry

### Phase A — Foundation (9 tasks)

| ID | Title | Rigor | Status | Dependencies | Parallel | Blocks |
|---|---|---|---|---|---|---|
| 001 | Foundation contracts: IDataverseClient + DataGridConfiguration + tokens | FULL | ✅ | none | — | 002,003,004,005,006,007,008,009 |
| 002 | XrmDataverseClient implementation | FULL | ✅ | 001 | A1 | 005,009 |
| 003 | DataGrid core (scaffold + lazy infinite-scroll + useDataGridContext) | FULL | ✅ | 001 | A1 | 004,005,006,007,008,009 |
| 004 | Lift ColumnHeaderMenu + ColumnFilterHeader with applyStylesToPortals fix | FULL | ✅ | 003 | A2 | 009 |
| 005 | LookupMultiFilterChip (async Combobox + debounce + cache) | FULL | ✅ | 002,003 | A2 | 009 |
| 006 | OptionSetMultiFilterChip (metadata-driven + status colors) | FULL | ✅ | 003 | A2 | 009 |
| 007 | DateRange + Text + Bool filter chips (3 bundled) | FULL | ✅ | 003 | A2 | 009 |
| 008 | CommandBar (6 actions + custom registry + CSV export) | FULL | ✅ | 003 | A2 | 009 |
| 009 | Storybook coverage + MDA pixel-parity gate + axe a11y | STANDARD | ✅ | 002,003,004,005,006,007,008 | — | 030,033 |

### Phase B — BFF Passthrough (8 tasks)

| ID | Title | Rigor | Status | Dependencies | Parallel | Blocks |
|---|---|---|---|---|---|---|
| 010 | BFF Placement Justification + ADR-008 filter shape | FULL | ✅ | none | — | 011,012,013,014,015,016,017 |
| 011 | SavedQueryService + 2 endpoints (savedquery + savedqueries) — 1h cache + shared infra | FULL | ✅ | 010 | B1 | 015,016,017 |
| 012 | MetadataService + metadata endpoint — 6h cache | FULL | ✅ | 010 | B1 | 015,016,017 |
| 013 | FetchService + fetch endpoint (cross-entity privilege check) | FULL | ✅ | 010 | B1 | 015,016,017 |
| 014 | RecordService + record endpoint ($select projection) | FULL | ✅ | 010 | B1 | 015,016,017 |
| 015 | BffDataverseClient (authenticatedFetch via DI) | FULL | ✅ | 011,012,013,014 | B2 | 017,023,024,041,052 |
| 016 | BFF integration tests (happy path + 403 + cache + cross-entity bypass) | STANDARD | ✅ | 011,012,013,014 | B2 | 017 |
| 017 | Phase B deploy (bff-deploy skill) | STANDARD | ⏸ | 015,016 | — | 023,024,026,033,041 |

### Phase C — Matter UI Drill-throughs (7 tasks)

| ID | Title | Rigor | Status | Dependencies | Parallel | Blocks |
|---|---|---|---|---|---|---|
| 020 | Author Matter-context savedqueries (KPI + Invoice) — UQ-04 | STANDARD | ✅ | none | — | 021,022,025 |
| 021 | sprk_gridconfiguration record for sprk_kpiassessment | STANDARD | ✅ | 020 | C1 | 023,025 |
| 022 | sprk_gridconfiguration record for sprk_invoice — UQ-05 deferred to R2 | STANDARD | ✅ | 020 | C1 | 023,025 |
| 023 | Build Custom Pages sprk_kpiassessmentspage + sprk_invoicespage | FULL | 🔲 | 015,021,022 | — | 025 |
| 024 | Update VisualHost chart-def sprk_drillthroughtarget (2 records) | MINIMAL | 🔲 | 023 | — | 025 |
| 025 | Phase C deploy (Custom Pages + Dataverse solution) | STANDARD | 🔲 | 021,022,023,024 | — | 026 |
| 026 | Phase C UAT (Matter Health + Budget Performance drill-through) | STANDARD | 🔲 | 025 | — | — |

### Phase D — EventsPage Migration (6 tasks)

| ID | Title | Rigor | Status | Dependencies | Parallel | Blocks |
|---|---|---|---|---|---|---|
| 030 | sprk_gridconfiguration record for sprk_event (anchor) | STANDARD | 🔲 | 009 | — | 031,033,034 |
| 031 | Rewrite EventsPage/App.tsx as ~150-line thin host | FULL | 🔲 | 009,030 | — | 032,034 |
| 032 | Retire @spaarke/events-components/{GridSection,AssignedToFilter,RecordTypeFilter,StatusFilter} | STANDARD | 🔲 | 031 | D1 | 034 |
| 033 | SpaarkeAi Calendar widget migrate to new DataGrid — UQ-06 | FULL | 🔲 | 030,031 | D1 | 034 |
| 034 | Phase D deploy (EventsPage + sprk_event record + Calendar widget) | STANDARD | 🔲 | 031,032,033 | — | 035 |
| 035 | Phase D UAT (4 modes + record-link bug closure) | STANDARD | 🔲 | 034 | — | — |

### Phase E — SemanticSearch Migration (3 tasks)

| ID | Title | Rigor | Status | Dependencies | Parallel | Blocks |
|---|---|---|---|---|---|---|
| 040 | Migrate SemanticSearch sprk_gridconfiguration to v1.0 schema | STANDARD | 🔲 | 009 | — | 041,042 |
| 041 | Refactor SearchResultsGrid.tsx to consume new DataGrid | FULL | 🔲 | 015,040 | — | 042 |
| 042 | Phase E deploy + UAT (visual diff zero regression) | STANDARD | 🔲 | 040,041 | — | — |

### Phase F — Legacy Retirement (5 tasks)

| ID | Title | Rigor | Status | Dependencies | Parallel | Blocks |
|---|---|---|---|---|---|---|
| 050 | Audit DatasetGrid consumers + migration list | STANDARD | 🔲 | 009 | — | 051,052 |
| 051 | Remove DatasetGrid/{GridView,CardView,ListView,VirtualizedGridView,VirtualizedListView} | STANDARD | 🔲 | 050 | F1 | 054 |
| 052 | Migrate SpeAdminApp/DashboardPage from UDG PCF to new DataGrid | FULL | 🔲 | 015,050 | F1 | 053 |
| 053 | Retire UniversalDatasetGrid PCF + solution version bump | STANDARD | 🔲 | 052 | — | 054 |
| 054 | Phase F deploy + UAT (DashboardPage visual diff) | STANDARD | 🔲 | 051,053 | — | 090 |

### Post-Phase-A Operational (1 task — added 2026-06-01)

| ID | Title | Rigor | Status | Dependencies | Parallel | Blocks |
|---|---|---|---|---|---|---|
| 080 | Storybook hosting + living style guide workflow | STANDARD | 🔲 | 009 | — | 090 |

### Wrap-up (1 task)

| ID | Title | Rigor | Status | Dependencies | Parallel | Blocks |
|---|---|---|---|---|---|---|
| 090 | Project wrap-up (code-review + adr-check + repo-cleanup + lessons-learned) | FULL | 🔲 | 009,017,026,035,042,054,080 | — | — |

---

## Status Legend

- 🔲 not-started
- 🔄 in-progress
- ✅ completed
- 🚫 blocked
- ⏸ deferred

## Rigor Distribution

| Rigor | Count | Examples |
|---|---|---|
| FULL | 22 | Code implementation, framework core, BFF endpoints, EventsPage rewrite, wrap-up |
| STANDARD | 16 | Tests, deploys, Dataverse records, UAT, audit |
| MINIMAL | 1 | VisualHost chart-def data update (task 024) |

---

## Parallel Execution Plan

The pipeline's project-pipeline Step 5 executes parallel groups concurrently. Hard cap: **6 agents per wave** (API overload guard).

### Phase A waves

| Wave | Tasks | Prerequisite | Files Touched | Parallel-Safe |
|---|---|---|---|---|
| A-Wave-0 | 001 | none | `src/services/IDataverseClient.ts`, `src/types/GridConfigJson.ts`, `src/components/DataGrid/tokens.ts` | — (serial foundation) |
| A-Wave-1 (Group A1) | 002, 003 | 001 ✅ | Separate files (services/ vs DataGrid/) | ✅ 2 agents |
| A-Wave-2 (Group A2) | 004, 005, 006, 007, 008 | 003 ✅ (002 for 005) | Separate primitive files | ✅ 5 agents |
| A-Wave-3 | 009 | 002-008 all ✅ | Storybook coverage gate | — (serial close) |

### Phase B waves

| Wave | Tasks | Prerequisite | Parallel-Safe |
|---|---|---|---|
| B-Wave-0 | 010 | none | — (serial design) |
| B-Wave-1 (Group B1) | 011, 012, 013, 014 | 010 ✅ | ✅ 4 agents (separate service files + endpoints) |
| B-Wave-2 | 015 | 011-014 ✅ | — (serial client) |
| B-Wave-3 | 016 | 011-014 ✅ | — (tests) |
| B-Wave-4 | 017 | 015, 016 ✅ | — (deploy) |

### Phase C waves

| Wave | Tasks | Prerequisite | Parallel-Safe |
|---|---|---|---|
| C-Wave-0 | 020 | none | — (UQ-04 owner interview) |
| C-Wave-1 (Group C1) | 021, 022 | 020 ✅ | ✅ 2 agents (separate Dataverse records) |
| C-Wave-2 | 023 | 015, 021, 022 ✅ | — (both Custom Pages bundled) |
| C-Wave-3 | 024 | 023 ✅ | — (chart-def data update) |
| C-Wave-4 | 025 | 021, 022, 023, 024 ✅ | — (deploy) |
| C-Wave-5 | 026 | 025 ✅ | — (UAT) |

### Phase D waves

| Wave | Tasks | Prerequisite | Parallel-Safe |
|---|---|---|---|
| D-Wave-0 | 030 | 009 ✅ | — |
| D-Wave-1 | 031 | 009, 030 ✅ | — (single file rewrite) |
| D-Wave-2 (Group D1) | 032, 033 | 031 ✅ for 032; 030, 031 ✅ for 033 | ✅ 2 agents (separate codebases) |
| D-Wave-3 | 034 | 031, 032, 033 ✅ | — (deploy) |
| D-Wave-4 | 035 | 034 ✅ | — (UAT) |

### Phase E (serial — small phase)

| Wave | Tasks | Prerequisite |
|---|---|---|
| E-Wave-0 | 040 | 009 ✅ |
| E-Wave-1 | 041 | 015, 040 ✅ |
| E-Wave-2 | 042 | 040, 041 ✅ |

### Phase F waves

| Wave | Tasks | Prerequisite | Parallel-Safe |
|---|---|---|---|
| F-Wave-0 | 050 | 009 ✅ | — (audit) |
| F-Wave-1 (Group F1) | 051, 052 | 050 ✅ + 015 for 052 | ✅ 2 agents (separate surfaces) |
| F-Wave-2 | 053 | 052 ✅ | — |
| F-Wave-3 | 054 | 051, 053 ✅ | — (deploy + UAT) |

### Wrap-up

| Wave | Tasks | Prerequisite |
|---|---|---|
| Wrap-up | 090 | 009, 017, 026, 035, 042, 054 ✅ | — |

---

## Critical Path

The longest dependency chain determines the project floor:

```
001 (Foundation) → 003 (DataGrid core) → 004/005/006/007/008 (primitives wave) → 009 (Phase A gate)
   → 030 (sprk_event config) → 031 (EventsPage rewrite) → 034 (deploy) → 035 (UAT)
      → 090 (Wrap-up)
```

Plus parallel BFF track (010 → 011/012/013/014 wave → 015 → 016 → 017) ending at 017 which is also a prerequisite for Phase C/D/E consumers.

**Estimated minimum sequential effort**: ~80 hours (assuming ideal parallelization within waves).

---

## High-Risk Tasks

| ID | Risk | Mitigation |
|---|---|---|
| 003 | DataGrid core scaffold — largest Phase A task; blocks all primitives | Mandatory `/fluent-v9-component` Step 0.5; Storybook story per acceptance criterion |
| 008 | Command bar + CSV + Dialog confirmation — most behavioral surface in Phase A | Unit tests for CSV quoting; manual Excel + LibreOffice verification |
| 013 | FetchService cross-entity privilege check — security-critical | Mandatory `/code-review` + integration test in task 016 |
| 031 | EventsPage rewrite — refactor ~1500 → ~150 lines | UAT all 4 modes (task 035) |
| 033 | SpaarkeAi Calendar widget migrate — UQ-06 escalation | Widget owner sign-off BEFORE changes |
| 052 | SpeAdminApp DashboardPage migrate — visual diff gate | Pre/post screenshots; visual diff verification |

---

## How to Execute

### Serial (one task at a time)
- Read the next 🔲 task POML; invoke `task-execute` skill with its path.

### Parallel waves
- Identify the next wave from the table above where all prerequisites are ✅.
- Send ONE message with MULTIPLE `Skill` tool invocations — one per task in the wave (one `task-execute` call per task).
- Wait for ALL to complete; update statuses; proceed to next wave.

### Hard rules
1. Tasks touching `.claude/` paths are FORCED `parallel-safe: false`. (None in this project, but the rule applies repo-wide.)
2. Max 6 agents per wave (API overload guard).
3. Build verification between waves: if a wave modifies `.cs` files, run `dotnet build src/server/api/Sprk.Bff.Api/` after; if `.ts/.tsx`, run `npm run build` in affected package. STOP if build fails.

---

*This file is the authoritative tracker of task state. Update it as each task completes (🔲 → ✅) and reflect status in `current-task.md`.*
