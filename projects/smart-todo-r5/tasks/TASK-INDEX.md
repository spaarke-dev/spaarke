# Task Index — Smart To Do R5

> **Generated**: 2026-08-15 · **Tasks**: 28 · **Status legend**: 🔲 not-started · 🔄 in-progress · ✅ complete · ⛔ blocked

## Registry

| # | Title | Phase | FR | Tier/Effort | Parallel | Deps | Status |
|---|---|---|---|---|---|---|---|
| 001 | Absorb PR #508 boundary fix on SmartTodo.Components | 1 | FR-01 | opus/xhigh | P | — | 🔲 |
| 002 | Hoist 13-file rich Kanban subtree → shared lib | 1 | FR-01 | opus/xhigh | none | 001 | 🔲 |
| 003 | LegalWorkspace SmartToDo → thin shim; parity verify | 1 | FR-01 | sonnet/high | none | 002 | 🔲 |
| 010 | Create sprk_priority + sprk_effort choice columns | 2 | FR-02/03 | sonnet/high | P | — | 🔲 |
| 011 | Auto-score handler (Option B) + wizard/quick-add parity | 2 | FR-02/03 | sonnet/high | none | 010 | 🔲 |
| 012 | Priority/effort per-card UI in shared lib | 2 | FR-02/03 | sonnet/high | none | 003,010 | 🔲 |
| 013 | RegardingResolver wiring + full regarding field set on form | 2 | FR-04 | opus/high | none | 010 | 🔲 |
| 014 | Deploy schema+form; real-DV resolver smoke | 2 | FR-04/20 | sonnet/high | none | 011,012,013 | 🔲 |
| 020 | Code Page top-bar redesign (Filter/New Task/overflow) | 3 | FR-05 | sonnet/high | Q | 003 | 🔲 |
| 021 | Filter pane (Priority/Status/Due/Assigned-To; clear-all) | 3 | FR-06 | sonnet/high | none | 020 | 🔲 |
| 022 | Completed status + Show-Completed toggle | 3 | FR-07 | sonnet/high | Q | 003 | 🔲 |
| 023 | Subtle channel coloring + yellow-contrast audit | 3 | FR-08 | sonnet/high | Q | 003 | 🔲 |
| 024 | Widget default = side-by-side columns | 3 | FR-09 | sonnet/high | none | 003 | 🔲 |
| 025 | Deploy code page + widget; visual QA | 3 | deploy | sonnet/high | none | 020-024 | 🔲 |
| 030 | + New Task opens OOB main form (create) modal | 4 | FR-10 | sonnet/high | none | 013 | 🔲 |
| 031 | Open shares same launch mechanism | 4 | FR-11 | sonnet/high | none | 030 | 🔲 |
| 032 | Full-cover sizing + hide main-form header | 4 | FR-12/13 | sonnet/high | none | 031 | 🔲 |
| 033 | Save & Close dismiss + kanban refresh (interceptor) | 4 | FR-14 | opus/xhigh | none | 032 | 🔲 |
| 034 | Migrate browse consumer → BrowseModal | 4 | FR-15 | sonnet/high | none | 003 | 🔲 |
| 035 | Deploy + modal QA | 4 | deploy | sonnet/high | none | 033,034 | 🔲 |
| 040 | vitest expansion + new coverage | 5 | FR-16 | sonnet/high | R | 011,021,022 | 🔲 |
| 041 | Playwright NFR suite (perf/a11y/orientation) | 5 | FR-17 | sonnet/high | R | 024,025 | 🔲 |
| 042 | R-10 handleEmail seam + un-skip; RegardingResolver S1/N1 | 5 | FR-18 | sonnet/high | R | — | 🔲 |
| 050 | Refresh Matter ribbon icon + RibbonDiff | 6 | FR-19 | sonnet/high | S | — | 🔲 |
| 051 | 5 per-entity ribbon solutions (Create To Do) | 6 | FR-19 | sonnet/high | none | 050 | 🔲 |
| 052 | Deploy + smoke each parent button | 6 | deploy | sonnet/high | none | 051 | 🔲 |
| 060 | PROC-1 real-DV smoke gate (skill checklist) | 7 | FR-20 | sonnet/high | none | — | 🔲 |
| 090 | Project wrap-up (test-diet, close #508, archive) | 7 | wrap-up | sonnet/high | none | all | 🔲 |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| P | 001, 010 | — | Boundary fix + schema columns — independent surfaces |
| Q | 020, 022, 023, 024 | 003 | Independent Code Page / widget visual surfaces (023/024 need hoist) |
| R | 040, 041, 042 | their targets exist | Independent test surfaces (042 has no target dep) |
| S | 050 → 051 | — | Ribbon (051 needs 050's icon) |

## Critical Path
`001 → 002 → 003 → {012, 020, 023, 024, 034} → 025/030 → 031 → 032 → 033 → 035 → 040/041 → 090`

Serial spines: **001→002→003** · **030→031→032→033** · **010→011** · **050→051→052**.

## High-Risk Items
- **001/002** — package-boundary + 13-file hoist; incoherent moves break `Spaarke.SmartTodo.Components` build for all consumers. `opus/xhigh`. Land in small PRs; `/conflict-check` first.
- **033** — Save&Close interceptor coordination with an OOB `navigateTo` dialog (MDA-owned close). `opus/xhigh`.
- **013** — RegardingResolver form wiring; real-DV smoke (014) gates correctness (PROC-1 hazard: mock hid entity-name bug in R4).
- **Shared-lib contention** — 19 active worktrees touch shared libs; `Spaarke.SmartTodo.Components` is hot.

## Goal-Eligibility (per wave)
- **Phase 3 visuals (020/022/023/024)** and **Phase 5 tests (040/041/042)** are candidate `goal-eligible` waves (machine-verifiable, low-ambiguity, not deploy/irreversible). Phases 1/2/4/6 involve deploys, schema, or irreversible modal wiring → NOT goal-eligible; dispatch normally.
