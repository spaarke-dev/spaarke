# Daily Briefing — SpaarkeAi Pattern D Migration (R2) — AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarke-daily-update-service-r2`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning → Implementation handoff
- **Last Updated**: 2026-06-18
- **Current Task**: Not started
- **Next Action**: Run `task-create` to decompose plan into POML task files

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — Original AI-optimized specification (permanent reference; 21 FRs + 7 NFRs + 14 SCs)
- [`design.md`](design.md) — Human-friendly design doc (problem statement + solution approach)
- [`README.md`](README.md) — Project overview and graduation criteria
- [`plan.md`](plan.md) — Implementation plan and WBS
- [`shared-lib-hygiene-proposal.md`](shared-lib-hygiene-proposal.md) — Deferred 10 lower-proximity findings (separate follow-on project; NOT in R2 scope)
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task tracker (will be created by `task-create`)

### Project Metadata
- **Project Name**: `spaarke-daily-update-service-r2`
- **Branch**: `work/spaarke-daily-update-service-r2`
- **Predecessor**: [`projects/spaarke-daily-update-service/`](../spaarke-daily-update-service/) (R1 — producer layer + standalone code page; no `lessons-learned.md`)
- **Type**: Multi-surface (BFF API endpoint mod + new shared lib package + Code Page shrink + LegalWorkspace shim + de-duplication)
- **Complexity**: Medium-High (6 workstreams, 21 FRs, touches 9 distinct file regions)
- **Rigor Level**: FULL (per spec NFR-07: must pass `code-review` + `adr-check` at task-execute Step 9.5)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, and acceptance criteria (FR-01..FR-21, NFR-01..NFR-07, SC1..SC14)
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via `adr-aware`)
6. **Reference [bff-extensions.md](../../.claude/constraints/bff-extensions.md)** before modifying anything in `src/server/api/Sprk.Bff.Api/` (binding constraint; see Sections A, F, F.1, F.2, F.3)

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
- Example: Tasks P2b + P3 + DD-MicrosoftToDoIcon in parallel → Three separate task-execute calls in one message

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

**Example for this project**: P2 hoist tasks
- Phase 1 (serial): Create new package scaffold (`package.json`, `tsconfig.json`)
- Phase 2 (parallel): 3 subagents hoist components, hooks, services
- Phase 3 (serial): Shrink standalone code page (depends on hoist being complete)

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

**From spec.md MUST Rules**:

- ✅ MUST create new package `@spaarke/daily-briefing-components` per Calendar + Smart Todo precedent (NOT extend `@spaarke/ui-components`)
- ✅ MUST split `useNotificationData` into 3 independent hooks per the contract (FR-06)
- ✅ MUST validate AI-returned `primaryEntityId` server-side against supplied `regardingId`; null-out invalid responses (FR-17)
- ✅ MUST use supplied `regardingId` (not AI output) for per-item sub-row hyperlinks (FR-12)
- ✅ MUST preserve the standalone Daily Briefing code page as a working surface
- ✅ MUST follow Pattern D dual-use per [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md)
- ✅ MUST update `SPAARKEAI-COMPONENT-MODEL.md` and `SPAARKEAI-WORKSPACE-ARCHITECTURE.md` to reflect the new package
- ❌ MUST NOT resurrect `WorkspaceHomeTab.tsx` (deleted in PR #375)
- ❌ MUST NOT introduce new BFF endpoints (use existing `/narrate`)
- ❌ MUST NOT retire the standalone Daily Briefing code page in R2
- ❌ MUST NOT bundle the 10 lower-proximity duplications from `shared-lib-hygiene-proposal.md`

**Cross-cutting (from CLAUDE.md §10 — BFF Hygiene)**:

- BFF publish-size: ≤ +1 MB delta per task (spec NFR-04); baseline ~45.65 MB; hard ceiling 60 MB
- Every BFF-touching PR MUST verify size via `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`
- Every BFF-touching PR MUST verify no new HIGH-severity CVE via `dotnet list package --vulnerable --include-transitive`
- Test update obligation (BFF §10 bullet 6): PRs modifying `Services/` or `Api/` MUST add/update tests in `tests/unit/Sprk.Bff.Api.Tests/`
- Asymmetric-registration rule (BFF §10 §§F.1–F.3): inspect fixture config before assuming DI issue; empirically reproduce before applying ledger fixes; apply ADR-032 Null-Object Kill-Switch Pattern for new conditional services

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

- **2026-06-18**: Use existing `work/spaarke-daily-update-service-r2` branch (not create new `feature/` branch). Reason: matches Spaarke `work/` convention; current branch is already set up. Who: project initialization session.
- **2026-06-18**: Stop after task generation (don't auto-execute task 001). Reason: 6-workstream project benefits from review before execution kicks off. Who: project initialization session.
- **2026-06-18**: Skip carrying forward env-provisioning-app lessons-learned. Reason: different domain (environment provisioning vs UI hoist). Who: project initialization session.

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

- **R1 has NO `lessons-learned.md`** — this is a gap. When R2 wraps, author one in `notes/lessons-learned.md` to fill the gap AND capture R2 learnings.
- **BFF baseline captured at project init**: see `notes/bff-baseline.md` for the pre-implementation publish-size measurement.
- **`shared-lib-hygiene-proposal.md`** is intentionally a sibling file, NOT in R2 scope. Treat as input to a future `spaarke-shared-lib-hygiene-r1` project.
- **Pattern D precedent**: Calendar (`@spaarke/events-components`) is the cleanest reference; Smart Todo (`@spaarke/smart-todo-components`) is the second example. When in doubt, mirror Calendar's structure first.

---

## Resources

### Applicable ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| **ADR-001** | BFF Minimal API + BackgroundService | P2b (`/narrate`) and P3 (`CreateNotificationNodeExecutor`) stay inside `Sprk.Bff.Api`; no Azure Functions, no separate services |
| **ADR-006** | UI Surface Architecture | Standalone Daily Briefing remains Vite + React 19 Code Page; Pattern D dual-use |
| **ADR-008** | Endpoint-filter auth | `/narrate` follows existing endpoint-filter convention |
| **ADR-010** | DI minimalism | No new BFF DI registrations needed for R2 |
| **ADR-012** | Shared component library | Pattern D dual-use canonical shape; new package per Calendar + Smart Todo precedent |
| **ADR-013** | AI architecture | `/narrate` change stays in BFF; no new AI endpoint |
| **ADR-021** | Fluent v9 design system | All hoisted components use semantic tokens; dark mode required |
| **ADR-024** | sprk_todo 11-entity regarding | `useInlineTodoCreate` uses `TODO_REGARDING_CATALOG`; preserve verbatim during hoist |
| **ADR-026** | Code Page build standard | Standalone shell uses Vite + `vite-plugin-singlefile` + React 19 |
| **ADR-027** | Subscription isolation | `appnotification` is CORE; no schema changes |
| **ADR-028** | Spaarke Auth v2 | `@spaarke/auth` is canonical entry point; new `createCodePageAuthInitializer` factory standardizes consumption |

### Binding Constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — Sections A, F, F.1, F.2, F.3 (binding for all BFF-touching tasks: P2b, P3)
- [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md)
- [`.claude/patterns/ui/fluent-v9-theming.md`](../../.claude/patterns/ui/fluent-v9-theming.md)
- [`.claude/patterns/ui/fluent-v9-host-visual-fit.md`](../../.claude/patterns/ui/fluent-v9-host-visual-fit.md)
- [`.claude/patterns/api/endpoint-definition.md`](../../.claude/patterns/api/endpoint-definition.md)
- [`.claude/patterns/api/endpoint-filters.md`](../../.claude/patterns/api/endpoint-filters.md)
- [`.claude/patterns/api/service-registration.md`](../../.claude/patterns/api/service-registration.md)
- [`.claude/patterns/dataverse/web-api-client.md`](../../.claude/patterns/dataverse/web-api-client.md)
- [`.claude/patterns/dataverse/polymorphic-resolver.md`](../../.claude/patterns/dataverse/polymorphic-resolver.md)

### Related Projects

- [`projects/spaarke-daily-update-service/`](../spaarke-daily-update-service/) — R1 predecessor (producer layer + standalone code page; no `lessons-learned.md`)
- Future: `spaarke-shared-lib-hygiene-r1` — captures the 10 lower-proximity duplications surfaced in audit
- `spaarke-ai-workspace-UI-r1` follow-up backlog — Item 1 (Modal-preview record-open standard) overlaps with R2's per-item modal UX; coordinate during P2a

### Reference Code

- `src/client/shared/Spaarke.Events.Components/` — Pattern D precedent (Calendar)
- `src/client/shared/Spaarke.SmartTodo.Components/` — Pattern D precedent (Smart Todo)
- `src/solutions/EventsPage/` — Thin host shell precedent
- `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` — 62-line registration shim precedent

### External Documentation

- [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — Pattern D §1.3 codifies one-package-per-dual-use-widget convention
- [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md) — Must be updated in SC14
- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — Daily Briefing section must be updated in SC14

---

*This file should be kept updated throughout project lifecycle*
