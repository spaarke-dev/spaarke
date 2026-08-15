# Smart To Do R5 — AI Context

> **Purpose**: Context for Claude Code when working on smart-todo-r5.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning complete → ready for execution
- **Last Updated**: 2026-08-15
- **Current Task**: Not started (see [`current-task.md`](current-task.md))
- **Next Action**: Run `task-execute` against task 001 (Phase 1 — absorb PR #508 boundary fix)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (20 FRs, 6 NFRs, 1 ADR tension) — permanent reference
- [`design.md`](design.md) — design backlog + refinement history (2026-06-23 → 2026-08-15)
- [`README.md`](README.md) — overview + graduation criteria
- [`plan.md`](plan.md) — 7-phase WBS (28 tasks)
- [`current-task.md`](current-task.md) — **active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker + parallel groups
- Mockups: `to-do-header-revision.jpg`, `to-do-main-form-modal.jpg`

### Project Metadata
- **Type**: Shared library + Dataverse schema + Code Page + PCF form-wiring + Ribbon
- **Complexity**: Medium-High

### Hot-Path Declaration
- **BFF**: N — no `Sprk.Bff.Api` touches; all Dataverse via `Xrm.WebApi`.
- **SpaarkeAi**: Y — SmartTodo widget renders in the workspace (FR-01 mounted components; FR-09 orientation default). Coordinate via `projects/INDEX.md`.
- **CI workflows / Skill directives / Root CLAUDE.md**: N.

---

## Context Loading Rules

1. **Always load this file first** when starting any task.
2. **Check `current-task.md`** for active state (especially after compaction / new session).
3. **Reference `spec.md`** for requirements + acceptance criteria (20 FRs, 6 NFRs, ADR tension).
4. **Load the relevant task file** from `tasks/`.
5. **Apply ADRs**: 012, 021, 024, 028, 030, 031, 038, 050 (loaded via `adr-aware`).

**Context Recovery**: [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" / "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

### Why This Matters
task-execute ensures: knowledge files loaded (ADRs/constraints/patterns), context tracked in current-task.md, checkpointing every 3 steps, quality gates (code-review + adr-check) at Step 9.5, recoverable progress. Bypassing → missing ADR constraints, lost progress, skipped gates.

### Parallel Task Execution
Tasks in a parallel group (see TASK-INDEX.md) each STILL use task-execute: ONE message, MULTIPLE Skill invocations. Never author POMLs directly.

---

## Execution Model & Tiering (Sonnet-5)
- **Planning** (design-to-spec, project-pipeline): Opus 4.8 / Fable 5.
- **Execution** (task-execute): default **Sonnet 5 @ effort `high`**; each POML carries `<model-tier>`/`<effort>` — `opus` + `xhigh` only for the high-blast-radius tasks (001 boundary+hoist, 033 interceptor, 013 resolver wiring). project-pipeline dispatches per POML.
- **Authoring discipline**: POMLs are explicit — exact files, cite the canonical ref, closed-set acceptance criteria incl. negative cases. FULL-rigor gates unconditional + coverage-first.

---

## Key Technical Constraints

### MUST Rules
- ✅ **MUST** keep the composite score formula + weights in `todoScoring.ts` unchanged; only set choice→score mappings (FR-02/03).
- ✅ **MUST** place hoisted components in `@spaarke/smart-todo-components` with no `src/solutions/…` reach-in (ADR-012 / NFR-05).
- ✅ **MUST** use the `@spaarke/ui-components/…` package import pattern (never the `../../../src/…` bypass) — the PR #508 fix FR-01 absorbs.
- ✅ **MUST** use Fluent v9 semantic tokens only — zero hex / `'1px'` / inline color (ADR-021 / ADR-050 / NFR-04).
- ✅ **MUST** route To Do main-form create/open through ONE launch mechanism (FR-10/11).
- ✅ **MUST** use `Xrm.WebApi` (not BFF) for the Assigned-To contact typeahead (DATA-ACCESS-DECISION-CRITERIA).

### MUST NOT Rules
- ❌ **MUST NOT** add a `chromeMode` API to `RecordNavigationModalShell` — use `BrowseModal` `nav`/`onBeforeNavigate` (ADR-050).
- ❌ **MUST NOT** create or resurrect an `AssociationResolver` PCF — RegardingResolver is the sole canonical resolver (confirmed on `master`).
- ❌ **MUST NOT** add any BFF endpoint/service/DI registration — spec NFR (BFF=N).
- ❌ **MUST NOT** change the composite score formula/weights — only the choice→score mapping tables.

### ADR Tension in Effect (per CLAUDE.md §6.5)
**ADR-050 Path A exception**: FR-10/11/12/13/14 keep the OOB `navigateTo` main form for To Do create/open (not a proprietary `FormModal`). Owner requires the real Dataverse form (native Save/Save&Close, business rules, F-4/F-5 score fields). SprkModal does not govern OOB dialogs, so this is a deliberate family choice per MODAL-DECISION-CRITERIA — cite in the FR-10..14 PRs. FR-15 (BrowseModal) explicitly complies with ADR-050.

---

## Decisions Made

- **2026-08-15**: F-5 effort mapping = **Option B (quick-wins-first)** — Low→25, Medium→50, High→75, Very High→100, None→50. Formula unchanged.
- **2026-08-15**: R-9 ribbon expansion (5 parent entities) = **in R5** (Phase 6).
- **2026-08-15**: U-3 overflow menu = **Settings→ThresholdSettings · Layout→orientation toggle · Refresh→reload**.
- **2026-08-15**: RegardingResolver is the sole canonical resolver; **AssociationResolver retired** (SRFR-045, confirmed absent on `master`).
- **2026-08-15**: F-7/F-8 modal = **Option 1** (keep OOB main form, full-cover + hidden header) — via U-4/5/6.
- **2026-08-15**: Stale PR #508 boundary fix **absorbed into FR-01** (task 001); #508 to be closed as superseded at wrap-up.
- **2026-08-15**: F-1 white-on-yellow **already fixed in code** → FR-08 is a verification sweep + subtle-coloring work.

---

## Implementation Notes

- **`sprk_priorityscore` / `sprk_effortscore` already exist** — FR-02/03 add only the 2 choice columns (`sprk_priority`, `sprk_effort`) and auto-populate the existing score fields.
- **PR #508 recipe** (FR-01 task 001): rewrite `../../../../Spaarke.UI.Components/src/…` imports → `@spaarke/ui-components/…` in `kanban.ts`, `SmartTodoWidget.tsx`, `SmartTodoKanban.tsx`; add dep + peerDep + tsconfig `paths` per the `Spaarke.AI.Widgets`/`DailyBriefing.Components` (PR #506) pattern. Runtime unchanged.
- **U-2 orientation** (FR-09): verify which `orientation` enum value yields side-by-side columns before setting the widget default — the `'horizontal'`/`'vertical'` naming is ambiguous.
- **U-6 header hide** (FR-12): mechanism TBD at task time (form header config vs dedicated modal form vs `navigateTo` option).
- **Shared-lib contention**: 19 active worktrees touch shared libs — land `Spaarke.SmartTodo.Components` changes in small PRs; run `/conflict-check` before each.

### Codebase-drift reconciliations (surfaced during task authoring 2026-08-15 — code wins per §2)
- **`SmartTodoModal.tsx` was DELETED** by `ai-spaarke-ai-workspace-UI-r2` (2026-07-01). The spec/design "round-9 interceptor in SmartTodoModal.tsx" framing (FR-14/task 033) is stale. Today's open path is `openSprkTodoAsLayout1()` in `SmartTodoApp.tsx` + `FeedSyncBridgeHost.handleOpenTodo()` in `todo.registration.ts` (fire-and-forget, no close/save signal). Task 033 is written against this reality.
- **`+ New Task` currently opens `CreateTodoWizard`**, not the OOB form — FR-10 (task 030) is a genuine behavior swap. Reuse the existing `navigateToEntityRecordSurfaceAsync()` in `wizardLaunchers.ts` (already wired to the Assistant surface-launch registry) rather than inventing a launcher (§11).
- **No live `RecordNavigationModalShell` consumer** found in SmartTodo/LegalWorkspace-SmartToDo — FR-15 (task 034) may already be resolved; it's written verify-first with an "already-resolved" exit.
- **Test runner is JEST, not "vitest"** (spec's FR-16/TEST-1 wording is stale). `Spaarke.SmartTodo.Components` has NO test runner (only `tsc --noEmit`) — task 040 wires Jest there. Playwright + `@axe-core/playwright` are ALREADY devDeps with a `tests/e2e/` harness — task 041 extends it (zero new packages).
- **R-10 drift** (task 042): `handleSelectRecord` was renamed `handlePickerSelect`; the N1 `console.warn` is now ~`RegardingResolverApp.tsx:1259` (adjacent `console.error` at ~1272). Re-verify line numbers at task time.
- **`sprk_todo` form XML is NOT in the repo** (Dataverse-hosted/live-configured) — tasks 013/030/031/032 treat the form as an MCP/maker-portal target, not a local file edit.

---

## Deferrals & Issues — tracking obligation

Track deferred work + newly-discovered issues in BOTH `notes/defer-issues.md` (source of truth) AND GitHub Issues (visibility). File via `/project-defer-issue-tracking` (alias `/defer`). §11 rule applies — every entry names a concrete failing behavior/contract, not "flexibility". `push-to-github` blocks on entries lacking a GitHub URL.

---

## Resources

### Applicable ADRs
- [ADR-012 — Shared Component Library](../../.claude/adr/ADR-012-shared-component-library.md)
- [ADR-021 — Fluent UI v9 Design System](../../.claude/adr/ADR-021-fluent-ui-design-system.md)
- [ADR-024 — Polymorphic Resolver Pattern](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md)
- [ADR-028 — Spaarke Auth Architecture v2](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — context only (BFF=N)
- ADR-030 / ADR-031 — PaneEventBus / stage lifecycle (widget mount)
- [ADR-038 — Testing Strategy](../../.claude/adr/ADR-038-testing-strategy.md)
- [ADR-050 — Canonical Modal Shell](../../.claude/adr/ADR-050-canonical-modal-shell.md) — **Path A exception in spec.md**

### Applicable Skills
`fluent-v9-component`, `dataverse-create-schema` / `dataverse-deploy`, `code-page-deploy`, `pcf-deploy`, `ribbon-edit`, `ui-test`, `code-review`, `adr-check`, `adr-aware`, `spaarke-conventions`, `context-handoff`, `test-diet`.

### Related Projects
- **smart-todo-r4** — the R4 baseline this completes (PR #406).
- **set-regarding-and-field-mapping-resolver-r2** — RegardingResolver + field-mapping framework (FR-04 builds on it).
- **spaarke-modal-system** — `SprkModal` / ADR-050 (FR-15).
- **code-quality-and-assurance-r3** — active, SpaarkeAi=Y, shared-lib overlap (coordinate).

### External Docs
- Fluent UI v9: https://react.fluentui.dev/
- `Xrm.Navigation.navigateTo`: https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto

---

*Keep this file updated throughout the project lifecycle.*
