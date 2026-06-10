# Smart To Do — UX Enhancement (R4) - AI Context

> **Purpose**: This file provides context for Claude Code when working on smart-todo-r4.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning → Foundation (Phase 0)
- **Last Updated**: 2026-06-10
- **Current Task**: Not started (project initialized today via `/project-pipeline`)
- **Next Action**: Begin Phase 0 audit tasks (parallel — A audit, D audit, G spike, useLaunchContext decision)

---

## Quick Reference

### Key Files

- [`spec.md`](spec.md) - Original design specification (3,491 words) — **source of truth for FRs/NFRs/MUST rules**
- [`design.md`](design.md) - Original human-authored design doc (v3) — rationale + trade-off discussions
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan, phase breakdown, discovered resources
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task registry, dependencies, parallel groups

### Project Metadata

- **Project Name**: smart-todo-r4
- **Type**: Client-side UI (React Code Page + shared lib components + possibly new PCF) + Dataverse configuration (4 chart defs, form designer)
- **Complexity**: Medium-High (7 workstreams, ~36-45 tasks)
- **Branch**: `work/smart-todo-r4` (worktree at `c:\code_files\spaarke-wt-smart-todo-r4`)
- **Predecessor**: smart-todo-decoupling-r3 (PR #373 + #374 merged)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, FRs/NFRs, MUST rules, success criteria
4. **Reference plan.md** for phase structure, discovered resources, parallel coordination
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
- ❌ Missing ADR constraints
- ❌ No checkpointing - lost progress after compaction
- ❌ Skipped quality gates

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 020, 021, 022 in parallel → Three separate task-execute calls in one message

**R4-specific parallel groups** (see [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md)):
- **Wave 0 (audits)**: All 4 audit tasks parallel (A audit, D audit, G spike, useLaunchContext decision)
- **Wave 2a**: A widget rebuild, B Code Page overhaul, D resolver implementation, G chart def records
- **Wave 2b**: C SmartTodo modal wiring, E card affordances, F orientation toggle (file-ownership care — may need to serialize within `src/solutions/SmartTodo/`)
- **Wave 2c**: G form additions (4 parent forms — parallel-safe per form)

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

**For tasks modifying 4+ files, Claude Code MUST:**

1. **Decompose into dependency graph** — Group files by module/component; identify dependencies; separate parallel-safe from sequential.
2. **Delegate to subagents in parallel where safe** — Use Agent tool with `subagent_type="general-purpose"`; send ONE message with MULTIPLE Agent calls for independent work.
3. **Parallelize when** — Files in different modules, no shared interfaces, no imports between them.
4. **Serialize when** — Files have tight coupling, sequential creation order matters.

**R4 specific**: Tasks 010 (shell extract) + 011 (RichFilePreviewDialog refactor) MUST be sequential because 011 depends on 010's output. Tasks within Wave 2a are independent across `src/solutions/`, `src/client/pcf/`, and Dataverse — fully parallel-safe.

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

**From applicable ADRs** (loaded automatically via adr-aware):

- **ADR-024** (Polymorphic Resolver) — D MUST wrap `PolymorphicResolverService.applyResolverFields`; no re-implementation of FR-13 mutual-exclusivity logic.
- **ADR-021** (Fluent UI v9) — All UI work MUST use Fluent v9 + Griffel `makeStyles` + semantic tokens. No v8 components. No inline styles. No CSS modules.
- **ADR-022** (PCF Platform Libraries) — If D chooses PCF, React 16 boundary + platform-provided Fluent applies.
- **ADR-006** (PCF over Web Resources / UI Surface Architecture) — Decision tree authoritative for D audit; informs all client-surface placement.
- **ADR-012** (Shared Component Library) — All new shared primitives MUST hoist to `@spaarke/ui-components`. No inline component definitions in solution-specific source.
- **ADR-026** (Code Page Build Standard) — Vite + `vite-plugin-singlefile` + React 19 for all Code Page builds.
- **ADR-028** (Spaarke Auth v2) — Verify only (R4 should not touch BFF; if it does, use `useAuth()` / `authenticatedFetch`).
- **ADR-030** (PaneEventBus Pattern) — Applies to A widget if it dispatches workspace events.
- **ADR-032** (Null-Object Kill-Switch Pattern) — Verify only (R4 should not touch BFF DI).

**From spec.md MUST/MUST NOT rules**:

- ✅ MUST use `@spaarke/ui-components` for all shared UI primitives (NFR-02)
- ✅ MUST follow `/fluent-v9-component` skill for all styling (FR-11, NFR-01)
- ✅ MUST use HYBRID modal pattern — `<RecordNavigationModalShell>` + iframe-embedded OOB MDA form (no pure-React form re-implementation)
- ✅ MUST be multi-environment portable — no hardcoded env URLs, app IDs, container IDs, or chart definition IDs in source (NFR-03)
- ✅ MUST rebuild + redeploy affected solutions after source changes (NFR-09)
- ✅ MUST comply with `BUILD-A-NEW-WORKSPACE-WIDGET.md` decision tree for A (FR-03)
- ❌ MUST NOT query `sprk_event` for `sprk_todoflag` (field removed in R3)
- ❌ MUST NOT reimplement save / BPF / business rules / statuscode in a custom React form
- ❌ MUST NOT introduce v8 Fluent components or inline styles
- ❌ MUST NOT retain `TodoDetailPanel` side-pane (FR-18)
- ❌ MUST NOT drill-through Visual Host to entity list view — must open SmartTodo Code Page modal (FR-34)

**BFF binding** (per repo CLAUDE.md §10): R4 spec explicitly states "purely client-side + Dataverse config" (line 205). If any task discovers a BFF call is needed, [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) governs — placement justification, publish-size check, test obligation, asymmetric-registration sub-rules. Default expectation: **NO-OP for R4**.

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

| Date | Decision | Rationale | Source |
|------|----------|-----------|--------|
| 2026-06-10 | Hybrid modal (Code Page wrapper + iframe-embedded OOB form) over pure-React form | Save / BPF / business rules / statuscode kept native; lowest maintenance | spec.md §C, design.md trade-off table |
| 2026-06-10 | Wrap `PolymorphicResolverService.applyResolverFields`; no re-implementation | One source of truth for FR-13 mutual-exclusivity | spec.md FR-21 |
| 2026-06-10 | Filter modes reduced to "Assigned to Me" only (drop "My Tasks") | UAT confusion; BU-owned ownerid field | spec.md OD-2, FR-07 |
| 2026-06-10 | Drill-through opens Code Page modal (NOT entity list view) | Preserves curated Kanban UX | spec.md FR-34 |
| 2026-06-10 | Only 4 parent forms get Visual Host card (Matter / Project / Invoice / WorkAssignment) | Event + Communication explicitly excluded | spec.md OQ-3 |
| 2026-06-10 | D winner deferred to Phase 0 audit | Genuine trade-offs PCF vs Web Resource vs Code Page on multi-env stability | spec.md §D, Assumptions |
| 2026-06-10 | Pattern D (dual-use shared-lib widget + thin LW shim) assumed for A; Pattern A (composable section) is acceptable fallback | Calendar widget is the canonical Pattern D worked example | spec.md Assumptions |

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

### Discovery Gaps Surfaced 2026-06-10

1. **`useLaunchContext` hook** — spec cites `src/solutions/SmartTodo/src/hooks/useLaunchContext.ts` but file does not exist. Existing hooks: `useFeedTodoSync`, `useKanbanColumns`, `useTodoItems`, `useTodoScoring`, `useUserPreferences`. Phase 0 task must decide: implement new or repurpose existing.
2. **`@spaarke/events-components` source location** — present only in `node_modules`; consumed as published package, not local lib. If A chooses Pattern D, A creates new `@spaarke/smart-todo-components` peer package.
3. **D PCF placement** — `src/client/pcf/RegardingResolver/` doesn't exist (expected, since audit pending). Phase 0 D audit gates implementation directory + tech choice.

### Parallel Branch Coordination

| Branch / PR | Overlap Area | Status |
|---|---|---|
| **PR #372** `feature/ai-spaarke-ai-workspace-UI-r1` | `Spaarke.AI.Widgets` (overlaps A) | Active — coordinate at A task time |
| **work/spaarke-datagrid-framework-r1** | `Spaarke.UI.Components` heavy (overlaps C shell + B toolbar primitives) | Active (55 unmerged, no PR) — sequence C shell hoist if datagrid merges first |
| **work/matter-ui-r1-v1.1.72-vh-polish** | Visual Host (adjacent to G, not same files) | Monitor |

---

## Resources

### Applicable ADRs

- [`ADR-024`](../../.claude/adr/ADR-024-polymorphic-resolver.md) — Polymorphic Resolver Pattern (D)
- [`ADR-021`](../../.claude/adr/ADR-021-fluent-v9-design-system.md) — Fluent UI v9 Design System (B/C/E/F)
- [`ADR-022`](../../.claude/adr/ADR-022-pcf-platform-libraries.md) — PCF Platform Libraries (D if PCF)
- [`ADR-006`](../../.claude/adr/ADR-006-pcf-over-webresources.md) — PCF over Web Resources / UI Surface Architecture (D decision tree)
- [`ADR-012`](../../.claude/adr/ADR-012-shared-component-library.md) — Shared Component Library (B/C/F hoist)
- [`ADR-026`](../../.claude/adr/ADR-026-code-page-build-standard.md) — Code Page Build Standard (B/C/F)
- [`ADR-028`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Spaarke Auth v2 (verify only)
- [`ADR-030`](../../.claude/adr/ADR-030-pane-event-bus.md) — PaneEventBus Pattern (A if it dispatches events)
- [`ADR-032`](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — Null-Object Kill-Switch (verify only)

### Related Projects

- **smart-todo-decoupling-r3** (predecessor, PR #373 + #374 merged) — `projects/smart-todo-decoupling-r3/` — `sprk_todo` entity introduction + R3 UAT outputs
- **ai-spaarke-ai-workspace-UI-r1** (active PR #372) — overlaps R4 A workspace area
- **spaarke-datagrid-framework-r1** (active branch, no PR) — overlaps R4 C/B shared-lib work

### External Documentation

- [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — Binding for A (FR-03)
- [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — Authoritative wrapper model
- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — Workspace pipeline
- [`docs/architecture/spaarke-todo-architecture.md`](../../docs/architecture/spaarke-todo-architecture.md) — Smart To Do architecture (merged 2026-06-10)
- [`docs/architecture/LEGALWORKSPACE-RETIREMENT.md`](../../docs/architecture/LEGALWORKSPACE-RETIREMENT.md) — Standalone LegalWorkspace Code Page retired (OC-R4-05)
- [`docs/architecture/event-to-do-architecture.md`](../../docs/architecture/event-to-do-architecture.md) — Entity model after R3
- [`docs/standards/CODING-STANDARDS.md`](../../docs/standards/CODING-STANDARDS.md) — Fluent v9 + PCF React 16 + Code Page React 19 rules
- [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](../../docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) — `Xrm.WebApi` vs BFF (relevant if D needs Dataverse access strategy decision)
- [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md) — Mandatory styling pattern
- [`.claude/patterns/dataverse/polymorphic-resolver.md`](../../.claude/patterns/dataverse/polymorphic-resolver.md) — Full pattern guide for D
- [`.claude/patterns/webresource/code-page-wizard-wrapper.md`](../../.claude/patterns/webresource/code-page-wizard-wrapper.md) — Pattern for C iframe wrapper
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — Verify only (R4 should not touch BFF)

---

*This file should be kept updated throughout project lifecycle.*
