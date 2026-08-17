# Smart To Do R5

> **Status**: **Substantially complete (2026-08-17) — NOT marked Complete.** Core shipped + merged to master (`c3b80eece`) + deployed to spaarkedev1. **Deferred/pending** (see `notes/defer-issues.md` D-10..D-13): ribbon expansion to 5 more entities (051/052), OOB header-hide (032, design constraint), Playwright real-env run (041), INBOUND BFF deploy (D-13). True project-close (README→Complete, `/test-diet`, GH-issue filing) happens when those land.
> **Type**: Shared library + Dataverse schema + Code Page + PCF form-wiring + Ribbon
> **Complexity**: Medium-High (multi-surface; BFF=N, SpaarkeAi=Y)
> **Branch**: `work/smart-todo-r5`

Completes the To Do experience shipped in R4: hoists the rich Kanban feature set into the shared `@spaarke/smart-todo-components` package (absorbing stale PR #508's boundary fix), adds Priority/Effort fields with auto-scoring, redesigns the Code Page top bar + filter pane, polishes visuals (subtle coloring, side-by-side columns, contrast), makes the To Do main-form modal (create + open) behave correctly, wires the canonical RegardingResolver PCF for `sprk_todo`, expands the "Create To Do" ribbon to 5 parent entities, and adds test infrastructure + a real-Dataverse smoke gate.

## Documents
- [`spec.md`](spec.md) — AI-optimized specification (20 FRs, 6 NFRs, 1 ADR tension)
- [`design.md`](design.md) — design backlog + full refinement history
- [`plan.md`](plan.md) — 7-phase WBS (28 tasks)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task registry + parallel groups
- Mockups: `to-do-header-revision.jpg`, `to-do-main-form-modal.jpg`

## Scope at a glance
- **FR-01** shared-lib hoist (+ absorbs PR #508 boundary fix)
- **FR-02/03** `sprk_priority` / `sprk_effort` choice fields → auto-set existing score fields (effort = Option B quick-wins-first)
- **FR-04** RegardingResolver wiring on the To Do form + full regarding field set
- **FR-05/06/07** top-bar redesign · filter pane · Completed status
- **FR-08/09** subtle channel coloring + contrast · widget side-by-side-column default
- **FR-10–15** OOB main-form modal (create/open/hide-header/full-cover/save-close) · BrowseModal migration
- **FR-16/17/18** vitest expansion · Playwright NFR suite · test-honesty + defensive fixes
- **FR-19** ribbon expansion (5 parent entities)
- **FR-20** real-DV smoke gate

## Graduation Criteria (project is Complete when)
> Assessed at partial wrap-up 2026-08-17. `[x]` met · `[~]` changed by UAT / partial · `[ ]` unmet (deferred).
1. [x] Rich Kanban components live in `@spaarke/smart-todo-components`; LegalWorkspace consumes them (no duplicated logic); PR #508 closed as superseded. *(tasks 001–003; #508 CLOSED 2026-08-17)*
2. [x] Priority/Effort choices auto-populate score fields (Option B); composite score unchanged for existing data. *(tasks 010–012)*
3. [x] RegardingResolver populates all regarding fields on a real To Do created from a parent (real-DV verified). *(tasks 013/014 + UAT D-8 auto-associate, operator-confirmed)*
4. [~] Top bar matches mockup (Filter · New Task · overflow); ~~filter pane + Completed toggle~~ → **UAT-changed to single expanding text-search** (name/description/assigned-to/date); **FR-07 Completed toggle removed** (operator-accepted, D-7). Old 'Search' label gone.
5. [x] No white-on-yellow surface; subtle channel coloring; light/dark parity. *(task 023; dark-on-yellow axe pass authored in 041, real-env run pending)*
6. [x] Widget defaults to side-by-side columns; flip preserves drag-drop + selection. *(task 024; flip behavior asserted by 041, real-env run pending)*
7. [~] `+ New Task` and Open both open the OOB main form as a full-cover modal ~~with header hidden~~; Save & Close dismisses + refreshes. **Header-hide (032) DEFERRED** — hiding the OOB command bar removes Save (design constraint, D-11). Create/open/save-close work.
8. [x] Browse consumer uses `BrowseModal` (single-title-source). *(task 034, verified already-resolved)*
9. [ ] "Create To Do" ribbon button on all 6 parent entities with correct context. **UNMET** — Matter done (050 + MatterRibbons v1.0.0.1); 5 others **deferred** (051/052, D-10).
10. [ ] vitest green (incl. un-skipped `handleEmail` 78/78 + new coverage); Playwright NFR suite runs; `/test-diet` reconciled. **PARTIAL** — Jest/vitest green (040/042); Playwright suite authored+wired but **not run green** (041, D-12); `/test-diet` deferred to true close.

## Out of scope
Mobile/responsive, i18n, Outlook ribbon parity, notifications integration, resurrecting AssociationResolver (retired), changing the composite score formula.
