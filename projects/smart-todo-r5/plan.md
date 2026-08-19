# Smart To Do R5 — Implementation Plan

> **Status**: Ready for execution
> **Created**: 2026-08-15
> **Source**: [`spec.md`](spec.md) (20 FRs, 6 NFRs, 1 ADR tension)
> **Sequencing note (owner)**: All items are in scope; phase order is a suggested grouping, **not** a priority ranking. The one hard ordering constraint is technical: **Phase 1 (hoist + #508) before Phase 2 (priority/effort card UI)** — don't author cards in LW-local then re-hoist.

---

## Architecture Context

### Hot-Path Declaration
- **BFF = N** — no `Sprk.Bff.Api` touches; all Dataverse via host-context `Xrm.WebApi`. No publish-size impact.
- **SpaarkeAi = Y** — the SmartTodo widget renders in the SpaarkeAi/LegalWorkspace workspace (FR-01 changes mounted components; FR-09 changes its default orientation). Registered in `projects/INDEX.md`.
- **CI-workflows / skill-directives / root-CLAUDE = N.**

### Discovered Resources

**Applicable ADRs** (loaded via `adr-aware` at task time):
- **ADR-012** — Shared component library (FR-01 hoist target; host-agnostic boundary; NFR-05).
- **ADR-021** — Fluent UI v9 (all visual work; semantic tokens; dark mode; NFR-04).
- **ADR-024** — Polymorphic Resolver pattern (FR-04 RegardingResolver wiring).
- **ADR-050** — Canonical Modal Shell (FR-15 BrowseModal; the reason F-7/F-8 stay OOB).
- **ADR-038** — Testing strategy (FR-16/FR-17 KEEP paths, mock boundaries, coverage-as-observation).
- **ADR-028** — Spaarke Auth (context: Assigned-To typeahead uses `Xrm.WebApi`, no BFF auth added).
- **ADR-030 / ADR-031** — PaneEventBus / stage lifecycle (FR-09 widget default via workspace pane mount).

**Applicable skills**: `fluent-v9-component`, `dataverse-create-schema` / `dataverse-deploy`, `code-page-deploy`, `pcf-deploy`, `ribbon-edit`, `ui-test`, `code-review`, `adr-check`, `adr-aware`, `spaarke-conventions`, `context-handoff`, `test-diet`.

**Applicable standards/guides**: `MODAL-DESIGN-SYSTEM.md` (§3/§8 BrowseModal), `MODAL-DECISION-CRITERIA.md` (OOB vs proprietary), `DATA-ACCESS-DECISION-CRITERIA.md` (`Xrm.WebApi` primacy), `SPAARKE-FIELD-MAPPING-FRAMEWORK.md` (resolver ↔ engine), `spaarke-todo-architecture.md`, `CODING-STANDARDS.md` / `ANTI-PATTERNS.md` / `TEST-ARCHITECTURE.md`.

**Canonical references to copy**:
- `src/client/shared/Spaarke.SmartTodo.Components/src/hooks/useKanbanColumns.ts` — the R4-102 host-agnostic hoist precedent.
- `src/client/shared/Spaarke.SmartTodo.Components/src/utils/todoScoring.ts` — locked scoring contract.
- `src/client/pcf/RegardingResolver/RegardingResolver/handlers/ResolverWriteHandler.ts` — resolver write path.
- `src/solutions/spaarke_insights/Entities/sprk_Matter/RibbonDiff.xml` — Matter ribbon template to clone (FR-19).
- PR #508 body — the boundary-fix recipe absorbed by FR-01.

### ADR Tension in effect
**ADR-050 Path A (project exception)** — FR-10/11/12/13/14 keep the OOB `navigateTo` main form for To Do create/open (not a proprietary `FormModal`), because the owner requires the real Dataverse form (native business rules, Save/Save&Close, F-4/F-5 fields). SprkModal does not govern OOB dialogs, so this is a deliberate family choice per MODAL-DECISION-CRITERIA, cited at code review. FR-15 explicitly complies with ADR-050.

---

## Phase Breakdown (WBS)

### Phase 1 — Shared-lib foundation: hoist + absorb PR #508 (FR-01)
> Critical path. Everything with card UI depends on this. Mostly sequential (same package files).

| Task | Title | FR | Parallel |
|---|---|---|---|
| 001 | Absorb PR #508 boundary fix on `Spaarke.SmartTodo.Components` (rewrite relative imports → `@spaarke/ui-components/…`; add dep + peerDep + tsconfig paths) | FR-01 (prereq) | none (serial) |
| 002 | Hoist 13-file rich Kanban subtree LW-local → `@spaarke/smart-todo-components` (host-agnostic, bit-for-bit parity) | FR-01 | none (after 001) |
| 003 | Convert LegalWorkspace `SmartToDo/` to a thin shim consuming the package; build + visual parity; verify no `src/solutions/…` reach-in | FR-01 | none (after 002) |

### Phase 2 — Schema + scoring + resolver on the `sprk_todo` form (FR-02, FR-03, FR-04)
> Depends on Phase 1 for the card UI (012). Schema tasks (010) can start in parallel with Phase 1.

| Task | Title | FR | Parallel |
|---|---|---|---|
| 010 | Create `sprk_priority` + `sprk_effort` choice columns on `sprk_todo` (Dataverse schema) | FR-02/03 | group P (with 001) |
| 011 | Auto-score handler (form OnChange) — single source of truth: priority→`sprk_priorityscore`, effort→`sprk_effortscore` (Option B); parity in CreateTodoWizard + quick-add | FR-02/03 | after 010 |
| 012 | Priority/effort per-card UI in shared lib (glyph + `PriorityScoreCard`/`EffortScoreCard`) | FR-02/03 | after 003 + 010 |
| 013 | Wire RegardingResolver on `sprk_todo` form (bind `sprk_regardingrecordtype`, `entity="sprk_todo"`); place full regarding field set on form; presave JS present | FR-04 | after 010 |
| 014 | Deploy schema + form; **real-DV smoke** (resolver populates fields; field-mapping inherits at create) | FR-04/20 | after 011,012,013 |

### Phase 3 — Code Page top bar + filter + visuals (FR-05, FR-06, FR-07, FR-08, FR-09)

| Task | Title | FR | Parallel |
|---|---|---|---|
| 020 | Top-bar redesign (Filter pill · `+ New Task` · `⋮` overflow = Settings→ThresholdSettings / Layout→orientation toggle / Refresh→reload) | FR-05 | group Q |
| 021 | Filter pane (Priority multi-select · Status · Due-date · Assigned-To typeahead via `Xrm.WebApi`; default Status={Open,In Progress}; Clear-all) | FR-06 | after 020 |
| 022 | Surface `statuscode` Completed in kanban + filter; hidden by default with "Show Completed" toggle | FR-07 | group Q |
| 023 | Subtle channel urgency coloring (red=Today/yellow=Tomorrow/green=later) + yellow-contrast audit sweep; semantic tokens only | FR-08 | after 003 (group Q) |
| 024 | SpaarkeAi widget default = side-by-side columns; survives NFR-08 flip | FR-09 | after 003 |
| 025 | Deploy code page + widget; visual QA | deploy | after 020-024 |

### Phase 4 — Main-form modal + browse modal (FR-10..FR-15)
> ADR-050 Path A applies. FR-10/11/12/13/14 are one coordinated modal-behavior stream.

| Task | Title | FR | Parallel |
|---|---|---|---|
| 030 | `+ New Task` opens `sprk_todo` OOB main form (create) as a modal (`navigateTo` `target:2`); pre-fill regarding/context | FR-10 | after 013 |
| 031 | Open existing To Do uses the SAME launch mechanism (one code path, sizing, chrome, close/refresh) | FR-11 | after 030 |
| 032 | Full-cover sizing + hide OOB main-form header band | FR-12/13 | after 031 |
| 033 | Save & Close dismisses inner dialog AND refreshes kanban (extend parent-side interceptor) | FR-14 | after 032 |
| 034 | Migrate SmartTodo preview/browse consumer → `BrowseModal` (`nav`+`onBeforeNavigate`; retire direct `RecordNavigationModalShell`) | FR-15 | parallel with 030-033 |
| 035 | Deploy + modal QA (create/open/save-close/browse) | deploy | after 033,034 |

### Phase 5 — Tests + hygiene (FR-16, FR-17, FR-18)

| Task | Title | FR | Parallel |
|---|---|---|---|
| 040 | vitest expansion — 22 `useLaunchContext` shims → passing; coverage for auto-score (011), filter logic (021), Completed toggle (022) | FR-16 | group R |
| 041 | Playwright NFR suite — load<3s (NFR-02), axe-core WCAG AA + keyboard (NFR-01), orientation flip (NFR-03) | FR-17 | group R |
| 042 | R-10 hygiene — `ToolbarActions` injectable `navigate` seam + un-skip `handleEmail` (78/78); RegardingResolver S1 race guard + N1 `warn`→`error` | FR-18 | group R |

### Phase 6 — Ribbon "Create To Do" expansion (FR-19)

| Task | Title | FR | Parallel |
|---|---|---|---|
| 050 | Refresh Matter ribbon button to `sprk_ToDoCheckmark32/16.svg`; update `sprk_Matter/RibbonDiff.xml`; redeploy `spaarke_insights` | FR-19 | group S |
| 051 | 5 dedicated per-entity ribbon solutions (Project/Event/Invoice/WorkAssignment/Communication) cloned from Matter pattern → `openCreateTodoWizard` | FR-19 | after 050 |
| 052 | Deploy + smoke each parent button (opens wizard with correct entity context) | deploy | after 051 |

### Phase 7 — Cross-cutting + wrap-up (FR-20 + close-out)

| Task | Title | FR | Parallel |
|---|---|---|---|
| 060 | PROC-1 real-DV smoke gate — add checklist item to `/push-to-github` or `/merge-to-master` (widget Dataverse changes require ≥1 real create+read pre-merge) | FR-20 | none |
| 090 | Project wrap-up — README→Complete, lessons-learned, `/test-diet`, close PR #508 as superseded, `projects/INDEX.md` update, archive | wrap-up | last |

---

## Parallel Execution Groups (summary — full detail in tasks/TASK-INDEX.md)

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| P | 001, 010 | — | Boundary fix + schema columns are independent (different surfaces) |
| Q | 020, 022, 023, 024 | 003 for 023/024 | Independent Code Page / widget visual surfaces |
| R | 040, 041, 042 | their targets exist | Independent test surfaces |
| S | 050 → 051 | — | Ribbon (051 needs 050's icon) |

Serial spines: **001→002→003** (Phase 1), **030→031→032→033** (modal), **010→011** (schema→handler).

---

## Estimated Effort

| Phase | Tasks | Rough effort |
|---|---|---|
| 1 Hoist + #508 | 3 | 3–4 days |
| 2 Schema + scoring + resolver | 5 | 4–6 days |
| 3 Top bar + filter + visuals | 6 | 1–1.5 weeks |
| 4 Modal | 6 | 1 week |
| 5 Tests + hygiene | 3 | 1 week |
| 6 Ribbon | 3 | 2–3 hrs core + deploy |
| 7 Cross-cutting + wrap-up | 2 | 0.5 day |
| **Total** | **28** | **~4–5 weeks** |

---

## References

- [`spec.md`](spec.md) — requirements (FR/NFR/ADR-tension/owner-clarifications)
- [`design.md`](design.md) — full design backlog + refinement history
- `to-do-header-revision.jpg` / `to-do-main-form-modal.jpg` — UX mockups
- [`docs/architecture/spaarke-todo-architecture.md`](../../docs/architecture/spaarke-todo-architecture.md)
- [`docs/standards/MODAL-DESIGN-SYSTEM.md`](../../docs/standards/MODAL-DESIGN-SYSTEM.md) · [`MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md)
- [`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](../../docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md)
