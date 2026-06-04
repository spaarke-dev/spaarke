# Spaarke DataGrid Framework (R1) - AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarke-datagrid-framework-r1.
> **Always load this file first** when working on any task in this project.

---

## 🚨 MANDATORY: Use Deploy-AllDataGridConsumers for framework changes

When any file in `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/` (or transitive deps like `services/XrmDataverseClient`) changes, EVERY Custom Page that mounts `<DataGrid>` ships a stale framework copy until rebuilt + redeployed. There is NO runtime sharing — each Vite bundle bakes its own copy.

**Always use [`scripts/Deploy-AllDataGridConsumers.ps1`](../../scripts/Deploy-AllDataGridConsumers.ps1)** after framework changes. Do NOT run individual `Deploy-EventsPage.ps1` / `Deploy-CorporateWorkspace.ps1` scripts unless you have a documented reason (e.g. only one consumer changed).

The script:
- Enumerates every consumer (registry at top of the script — extend it when a new Custom Page mounts `<DataGrid>`)
- Runs `npm run build` in each
- PATCHes the matching web resource
- Single `PublishXml` at the end (atomic cache flush)

```powershell
$env:DATAVERSE_URL = "https://spaarkedev1.crm.dynamics.com"
.\scripts\Deploy-AllDataGridConsumers.ps1
```

**Why this exists**: task 035 UAT iteration 5 (2026-06-04). Operator directive: "we do not want to go through this same UI review and back/forth every time we deploy the dataset grid — this is always going to be part of the grid". Individual consumer redeploys after every framework iteration was the wrong shape — the script makes "framework change → ALL consumers updated" a single command.

**Current registry**:
- `EventsPage` → `sprk_eventspage.html`
- `sprk_invoicespage` → `sprk_invoicespage.html`
- `sprk_kpiassessmentspage` → `sprk_kpiassessmentspage.html`
- `LegalWorkspace` → `sprk_corporateworkspace` (no `.html` suffix — historical naming)

---

## Project Status

- **Phase**: Ready for execution (Phase A — Foundation)
- **Last Updated**: 2026-06-01
- **Current Task**: Not started — 39 POML tasks generated; ready to execute
- **Next Action**: Start task 001 (Foundation contracts: IDataverseClient + GridConfigJson + tokens) via task-execute skill
- **Task Count**: 39 total (Phase A: 9, B: 8, C: 7, D: 6, E: 3, F: 5, Wrap-up: 1)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized implementation specification (permanent reference)
- [`design.md`](design.md) - Source design document (~770 lines)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (will be created by task-create)

### Project Metadata
- **Project Name**: spaarke-datagrid-framework-r1
- **Type**: Shared component library + BFF endpoints + Code Pages + Dataverse configuration (cross-stack)
- **Complexity**: High (6 phases, ~36 tasks, framework refactor + migrations + retirement)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, and acceptance criteria
4. **Reference design.md** when spec.md points to it for specific section details (e.g. §6.3 schema, §11.5 patterns)
5. **Load the relevant task file** from `tasks/` based on current work
6. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5
- ✅ Progress is recoverable after compaction

**Bypassing this skill leads to**:
- ❌ Missing ADR constraints (especially ADR-021 Fluent v9 dark mode, ADR-028 auth)
- ❌ No checkpointing - lost progress after compaction
- ❌ Skipped quality gates
- ❌ `/fluent-v9-component` skill NOT invoked for UI tasks (mandatory per FR-DG-13)

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: 5 filter chip tasks (002–006) in parallel → 5 separate task-execute calls in one message

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

**For tasks modifying 4+ files, Claude Code MUST:**

1. **Decompose into dependency graph**:
   - Group files by module/component
   - Identify which changes depend on others
   - Separate parallel-safe work from sequential work

2. **Delegate to subagents in parallel where safe**:
   - Use Task tool with `subagent_type="general-purpose"`
   - Send ONE message with MULTIPLE Task tool calls for independent work
   - Each subagent handles one module/component
   - Provide each subagent with specific files and constraints

3. **Parallelize when**:
   - Files are in different modules → CAN parallelize
   - Files have no shared interfaces → CAN parallelize
   - Work is independent (no imports between files) → CAN parallelize

4. **Serialize when**:
   - Files have tight coupling (shared state, imports)
   - One file must be created before another uses it
   - Sequential logic required

**Example for this project**: Building 5 filter chip primitives (FR-DG-07)
- Phase 1 (serial): `IDataverseClient` interface (used by `LookupMultiFilterChip`)
- Phase 2 (parallel): 5 subagents — one per filter chip primitive (no cross-dependencies)

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

Extracted from `spec.md` Technical Constraints section + ADRs:

### Mandatory ADRs
- **ADR-008** — Endpoint authorization filter pattern on all 5 BFF endpoints (FR-BFF-07)
- **ADR-012** — `@spaarke/ui-components` is the canonical home for the framework; `IDataverseClient` lives here
- **ADR-019** — ProblemDetails on BFF endpoint errors (FR-BFF-01..05)
- **ADR-021** — Fluent v9 only, NO `@fluentui/react` v8, NO raw hex, dark mode required, tokens-only styling
- **ADR-022** — Framework code is React-16-safe (no `useId`, no `useSyncExternalStore`, no `createRoot`); Custom Page hosts use React 18
- **ADR-026** — Full-page Custom Page standard (Vite + `vite-plugin-singlefile` + React 19) for `sprk_kpiassessmentspage` and `sprk_invoicespage`
- **ADR-028** — `BffDataverseClient` uses `@spaarke/auth.authenticatedFetch` ONLY (FR-BFF-06)
- **ADR-029** — BFF publish hygiene (framework-dependent linux-x64, sourcemap exclusion, transitive CVE override, size baseline ratchet)

### MUST Rules (from spec.md)
- ✅ MUST build on Fluent v9 native `<DataGrid>` primitive — no hand-rolled `<table>` (FR-DG-11)
- ✅ MUST match MDA Power Apps grid UI exactly — codified in `tokens.ts` (NFR-01, FR-DG-10)
- ✅ MUST invoke `/fluent-v9-component` skill as Step 0.5 on every UI task (FR-DG-13)
- ✅ MUST set `applyStylesToPortals={true}` on every FluentProvider hosting a popover-bearing primitive (NFR-03)
- ✅ MUST use `tokens.*` for all colors / spacing / radius — NO raw hex (NFR-02)
- ✅ MUST use `IDataverseClient` for all Dataverse access in framework code (FR-DG-02)
- ✅ MUST use `@spaarke/auth.authenticatedFetch` for BFF calls in `BffDataverseClient` (FR-BFF-06)
- ✅ MUST use `Promise.all` for bulk operations with progress feedback (FR-DG-14)
- ❌ MUST NOT touch `SemanticSearchControl` PCF code (NFR-06)
- ❌ MUST NOT touch `VisualHost` PCF code — only chart-def `sprk_drillthroughtarget` data (NFR-06)
- ❌ MUST NOT use React-18-only APIs in framework code (NFR-05)
- ❌ MUST NOT use `@fluentui/react` v8 (ADR-021)
- ❌ MUST NOT introduce per-user grid state persistence (R1 non-goal)
- ❌ MUST NOT use `teamsHighContrastTheme` — Windows HC is automatic in Fluent v9 (FR-DG-10)
- ❌ MUST NOT write `window.confirm` for confirmations — use Fluent v9 `<Dialog>` (FR-DG-14)

### Binding Constraints
- **`.claude/constraints/bff-extensions.md`** — Required Placement Justification for every Phase B task that adds endpoints / services / DI / packages to `Sprk.Bff.Api`. PR description must include the section.
- **`.claude/skills/fluent-v9-component/SKILL.md`** — MANDATORY Step 0.5 invocation for every task that authors or modifies UI primitives. Applies to all of Phase A, parts of Phase C/D/E.
- **`src/client/pcf/CLAUDE.md`** §6.0 — Fluent v9 mandate.

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

*No decisions recorded yet — initial state. See README.md "Key Decisions" for design-phase decisions captured from spec OC-XX clarifications.*

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

*No notes yet — initial state.*

**Anticipated gotchas to watch for** (from spec/design):
- Fluent v9 portal dark mode bug: every `Popover`/`Menu` surface needs `applyStylesToPortals={true}` on its FluentProvider (NFR-03)
- DateRangeFilterChip LOCAL → UTC bounds conversion — port `localDateToUtcBounds` from `GridSection.tsx:358`
- FetchXML `<row id>` extraction must replace hardcoded `sprk_eventid` in lifted `FetchXmlService` (line 205)
- Lazy-load paging cookie expiry (60min Dataverse default) — refetch from page 1 after idle
- Selection state across lazy-load pages: preserve `Set<string>` across page boundaries
- `executeBulkStatusUpdate` lift: must preserve `Promise.all` + global notification pattern
- Record-link bug: side pane opens behind dialog — use `Xrm.Navigation.navigateTo({pageType:"webresource"})` not side pane

---

## Resources

### Applicable ADRs
- [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) — PCF only for form binding; Code Pages default for new UI
- [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) — Endpoint authorization filter pattern
- [ADR-012](../../.claude/adr/ADR-012-shared-components.md) — `@spaarke/ui-components` shared library
- [ADR-019](../../.claude/adr/ADR-019-problemdetails.md) — ProblemDetails on BFF errors
- [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) — Fluent v9 + dark mode
- [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) — React version boundaries
- [ADR-026](../../.claude/adr/ADR-026-full-page-custom-page-standard.md) — Full-page Custom Page standard
- [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Spaarke Auth v2
- [ADR-029](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — BFF publish hygiene

### Related Projects
- `projects/spaarke-matter-ui-enhancement-r1/` — The drill-through requirement that triggered this framework
- `projects/spaarke-ai-platform-unification-r4/` — Workspace widget consumer of this framework (Calendar widget per FR-MIG-05)
- `projects/sdap.bff.api-test-suite-repair-r2/` — Recent precedent for BFF test discipline; informs Phase B test approach

### External Documentation
- [Fluent UI v9 `DataGrid` API](https://react.fluentui.dev/?path=/docs/components-datagrid--default) — selection, sort, resize, focus mode, density
- [Dataverse SavedQuery entity](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/savedquery) — for FR-BFF-01/02 endpoint payload shapes
- [Dataverse EntityMetadata](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/use-web-api-metadata) — for FR-BFF-03 metadata projection

---

*This file should be kept updated throughout project lifecycle*
