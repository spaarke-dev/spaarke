# Smart To Do — Decoupling from Events (R3) - AI Context

> **Purpose**: This file provides context for Claude Code when working on smart-todo-decoupling-r3.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning
- **Last Updated**: 2026-06-07
- **Current Task**: Not started
- **Next Action**: Execute Task 001 via `/task-execute projects/smart-todo-decoupling-r3/tasks/001-*.poml`

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (30 FRs / 12 NFRs / MUST + MUST-NOT rules)
- [`design.md`](design.md) — Source design document (preserved verbatim)
- [`README.md`](README.md) — Project overview and graduation criteria
- [`plan.md`](plan.md) — Implementation plan and WBS
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task tracker

### Project Metadata
- **Project Name**: smart-todo-decoupling-r3
- **Type**: Dataverse schema + shared-lib UI + BFF API + Outlook add-in + Microsoft Graph integration
- **Complexity**: High (multi-surface; bidirectional sync; pre-release schema cut)
- **Predecessors**: events-smart-todo-kanban-r1, events-smart-todo-kanban-r2

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

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
- ❌ No checkpointing — lost progress after compaction
- ❌ Skipped quality gates

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 050a, 050b, 050c (parent-form subgrids) → multiple task-execute calls in one message

**Hard cap**: 6 agents per wave. **Permission boundary**: tasks touching `.claude/` paths run sequentially (main-session only).

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

**For tasks modifying 4+ files, Claude Code MUST:**

1. **Decompose into dependency graph** — group files by module, identify dependencies, separate parallel-safe from sequential work
2. **Delegate to subagents in parallel where safe** — use Task tool with `subagent_type="general-purpose"`; one message with multiple Task tool calls; each subagent handles one module with explicit constraints
3. **Parallelize when**: files in different modules, no shared interfaces, no imports between
4. **Serialize when**: tight coupling (shared state, imports), one file must exist before another uses it, sequential logic required

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

**Binding rules from spec.md `Technical Constraints` section:**

- ✅ **MUST use `PolymorphicResolverService.applyResolverFields`** whenever any `sprk_regarding*` lookup is set or changed on `sprk_todo`. Never set the four resolver fields directly. (ADR-024)
- ✅ **MUST ensure at most one of the eleven `sprk_regarding*` lookups is populated at a time**. Switching regarding clears the previous lookup.
- ✅ **MUST use `@fluentui/react-components` v9.x** with semantic tokens and Griffel `makeStyles`. No Fluent v8, no inline styles, no CSS modules, no external CSS files. (NFR-01)
- ✅ **MUST place all Kanban primitives in `@spaarke/ui-components`**. SmartTodo Code Page imports from the shared lib only. (NFR-02)
- ✅ **MUST feature-gate Graph sync** via `Spaarke:Graph:TodoSync:Enabled` with Null-Object fallbacks. (ADR-032)
- ✅ **MUST validate Graph change notifications** — check `clientState`, respond to validation token within 10 seconds, verify `subscriptionId` matches active subscription.
- ✅ **MUST audit-log every sync operation** with correlation id. (NFR-06)
- ✅ **MUST measure BFF publish size on every BFF-touching task** and report delta vs. ~45.65 MB baseline. Single-task delta ≥+5 MB requires justification; cumulative ≥55 MB triggers architecture review; ≥60 MB is a hard stop. (NFR-03 + CLAUDE.md §10)
- ✅ **MUST add/update tests for every modified BFF service**. (NFR-11 + `bff-extensions.md` §F)
- ❌ **MUST NOT introduce backward-compatibility shims** for `sprk_eventtodo` or legacy event todo fields. (NFR-12)
- ❌ **MUST NOT migrate dev-environment data**. Fresh-start only. (OS-2)
- ❌ **MUST NOT use the legacy Outlook Tasks API** (`/me/outlook/tasks`) — deprecated. (OS-4)
- ❌ **MUST NOT integrate with Microsoft Planner** in this project. (OS-3)
- ❌ **MUST NOT use Dataverse native polymorphic `regardingobjectid` lookup**. Use the Spaarke multi-entity resolution pattern instead.
- ❌ **MUST NOT use a Dataverse Activity entity** for `sprk_todo`. Custom entity only.

**Cross-cutting**:
- BFF additions governed by CLAUDE.md §10 + `.claude/constraints/bff-extensions.md`. Every BFF-touching task MUST state placement decision, verify publish-size, run CVE check, add/update tests.
- Pre-release: no feature flags or shims for legacy entities (per FR-29 / NFR-12).

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->

- **2026-06-07**: ADR reference correction. Spec.md cited `ADR-030-bff-nullobject-kill-switch.md` but the actual file is `ADR-032-bff-nullobject-kill-switch.md` (ADR-030 is pane-event-bus). All four spec references corrected via `/project-pipeline`. CLAUDE.md §10 line 183 has the same stale link — added as a low-priority cleanup task in this project.
- **2026-06-07**: ADR-009 (Graph OBO) is ambiguous in spec. `ADR-009` file is `redis-caching.md`. OBO + token-caching content for Graph lives in `ADR-028-spaarke-auth-architecture.md`. Tasks reference ADR-028 for OBO compliance.

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

- **Phase 1 schema cut is destructive** — `sprk_eventtodo` is deleted entirely; four `sprk_event` fields are removed. Order matters: drop dependencies (subgrids, views, scripts referencing the to-be-removed fields/entity) before the schema change, or the solution import will fail.
- **Graph sync feature-gate**: by default, ship with `Spaarke:Graph:TodoSync:Enabled = false` until D-1 (Tasks.ReadWrite scope) + D-4 (Modern UCI app id) + D-5 (Service Bus `todosync` queue) are all confirmed in the target environment. Null-Object services (ADR-032) keep the BFF booting.
- **Loop prevention is multi-layer** (FR-23): synchash + thread-local skip-flag during inbound writes + per-field LWW. All three layers must work together; relying on just synchash is insufficient when both sides edit within ~10s.
- **Kanban hoist must preserve behavior**: NFR-10 mandates no a11y regression vs. R2 (keyboard nav, screen-reader labels, high-contrast). Snapshot accessibility tests recommended before the hoist.

---

## Resources

### Applicable ADRs
- **[ADR-001](../../.claude/adr/ADR-001-minimal-api.md)** — Minimal API pattern for new BFF endpoints (`/api/graph/webhooks/todo`, sync trigger)
- **[ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md)** — Endpoint filters for authorization; webhook MUST validate Graph's `clientState` + validation token
- **[ADR-024](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md)** — Multi-entity resolution (the binding rule for `sprk_todo` regarding shape)
- **[ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)** — SSO + token issuance; reuse cached OBO token flow for `Tasks.ReadWrite`
- **[ADR-032](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md)** — Null-Object pattern for Graph sync feature-gate

### Cross-cutting Constraints
- **[`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md)** — BFF binding governance (placement, publish-size, CVE, test obligation, asymmetric-registration anti-pattern)
- **[`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md)** — Publish-size per-task verification rule (NFR-01 / NFR-03)

### Applicable Skills
- **dataverse-create-schema** — Create `sprk_todo` entity, delete `sprk_eventtodo`, remove `sprk_event` fields
- **dataverse-deploy** — Deploy solution changes
- **fluent-v9-component** — Hoist Kanban primitives, build `MyTasksFilter`, simplify `TodoDetail`
- **bff-deploy** — Deploy BFF API after Graph sync engine additions
- **office-addins-deploy** — Deploy updated Outlook add-in
- **code-page-deploy** — Redeploy SmartTodo Code Page after `sprk_todo` repoint
- **adr-aware** — Auto-loaded by task-execute; ensures ADRs above are applied
- **code-review** — Quality gate at task-execute Step 9.5 for FULL-rigor tasks
- **repo-cleanup** — Final cleanup task (verifies no orphan files from migration)

### Existing Code to Reuse
- **[`PolymorphicResolverService.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts)** — Reuse as-is (no edits expected)
- **[`AssociateToStep.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/AssociateToStep.tsx)** — Extend entity targets to all eleven
- **[`GraphClientFactory.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs)** — Add `Tasks.ReadWrite` to scope list
- **[`GraphSubscriptionManager.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphSubscriptionManager.cs)** — Extend for `/me/todo/lists/{id}/tasks` resource
- **[`ServiceBusJobProcessor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs)** — Add `TodoSync` message type + `TodoGraphSyncHandler`
- **[`CreateRecordWizard/`](../../src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/)** — Wizard pattern; `CreateTodo` becomes a thin wrapper
- **[`KanbanBoard.tsx`](../../src/solutions/SmartTodo/src/components/shared/KanbanBoard.tsx)** — Currently SmartTodo-local; hoist to `@spaarke/ui-components` (Phase 2)

### Knowledge / Reference Documents
- **[`docs/architecture/event-to-do-architecture.md`](../../docs/architecture/event-to-do-architecture.md)** — Will be marked superseded; new `spaarke-todo-architecture.md` replaces it (FR-30)
- **[`docs/data-model/sprk_communication.md`](../../docs/data-model/sprk_communication.md)** — Canonical multi-entity resolution reference; mirror its regarding shape

### Related Projects
- **events-smart-todo-kanban-r1** — Original kanban implementation (`sprk_event.todoflag` model)
- **events-smart-todo-kanban-r2** — R2 refinements (still event-coupled)

### External Documentation
- **Microsoft Graph `/me/todo` API** — https://learn.microsoft.com/en-us/graph/api/resources/todo-overview
- **Microsoft To Do client apps** — for end-to-end verification

---

*This file should be kept updated throughout project lifecycle.*
