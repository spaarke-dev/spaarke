# SpaarkeAI Assistant Enhancements R2 - AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarkeai-assistant-enhancements-r2.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Development (tasks generated, not started)
- **Last Updated**: 2026-08-05
- **Current Task**: Not started
- **Next Action**: Begin Phase 1 (task 001) via task-execute

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized specification (27 FRs / 6 NFRs) — permanent reference
- [`design.md`](design.md) - Source human design doc
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker + dependencies + parallel groups

### Project Metadata
- **Project Name**: spaarkeai-assistant-enhancements-r2
- **Type**: Frontend (SpaarkeAi code page + shared widgets) + BFF API (Services/Ai/Chat)
- **Complexity**: Medium-High (read/wiring/reliability over existing machinery; two high-risk tasks)
- **Predecessor**: spaarkeai-assistant-enhancements-r1 (shipped dispatch spine + catalog — do not reopen)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for FRs, acceptance criteria, ADR tensions, MUST rules
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

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

task-execute ensures: knowledge files loaded (ADRs, constraints, patterns) · context tracked in current-task.md · checkpointing every 3 steps · quality gates (code-review + adr-check) at Step 9.5 · recoverable after compaction.

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute — one message with multiple Skill invocations. **This project's contention rule**: `ConversationPane.tsx` is edited by E/A/B/D, so those client tasks are `parallel-safe:false` among themselves (sequential spine). BFF concerns, `HistoryOverlay.tsx`, shared-lib types, and catalog data parallelize alongside that spine. See TASK-INDEX.md Parallel Groups.

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files: decompose into a dependency graph, parallelize independent modules via subagents, serialize tightly-coupled files. See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md).

---

## Execution Model & Tiering (Sonnet-5)

- **Planning** (this pipeline): Opus 4.8 / Fable 5.
- **Execution** (task-execute per task): default **Sonnet 5 @ effort `high`**.
- **Per-task tier + effort**: each POML carries `<model-tier>` + `<effort>`. Escalated to **opus / xhigh** for: **012** (ADR-015 privacy boundary), **033** (Cosmos retention/TTL data-loss risk), **035** (rich-restore overwrite hazard), **042** (email visible-state into record hook).
- **Coverage-first gates**: code-review + adr-check stay unconditional on FULL-rigor tasks; orchestrator filters findings.

---

## Key Technical Constraints

- **ADR-039**: proactive chips (B) + title-gen (D) are the *same* grounded agent turn / a cheap label — **MUST NOT** add a classifier/reranker/keyword map.
- **ADR-015 (Path A exception)**: the *active* tab is content-visible (compact-ambient + full-on-demand); **background tabs metadata-only**. Bounded shape; owner-approved 2026-08-05.
- **ADR-040**: reuse Cosmos `StoredSession` — **no new persistence store**, no move back to Dataverse. Reliability/retention stay within the 3-tier cascade.
- **Seam rule**: inject active-tab context via the existing `onDecorateOutboundBody` seam — **MUST NOT** fork `SprkChat`.
- **Dispatch rule**: reuse the existing dispatch seam + catalog — **no new BFF dispatch endpoint**.
- **Chip surface rule**: render B's chips via the **reactive** surface (`useConsumerChips` / `sprk_chiptransitions`) — **MUST NOT** resurrect the removed spine-driven `useSuggestionCards` (which E deletes).
- **Preserve on E**: `notificationsBootstrap.ts` / `getNotificationsClient`, `sprk_notificationoutbox`, `OutboxService`, `/api/notifications/*`, Daily Briefing.
- **BFF §10**: publish size ≤60 MB compressed (baseline ~49.63 MB incl. PDBs). Measure per BFF-touching task. No new packages anticipated. `/conflict-check` before `Services/Ai` PRs.

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->

- 2026-08-05: Phasing E→A→B→D→C accepted (owner). — Reason: E de-noises first; A/B build the surface-awareness; D is the largest; C depends on merged email-r5.
- 2026-08-05: FR-D10 retention prefers per-doc Cosmos TTL extension on filing; fallback = remove container TTL + `expiresAt` + scheduled cleanup. — Reason: owner-directed; spike TTL feasibility before implementing the fallback.

---

## Implementation Notes

<!-- Gotchas, workarounds, learnings discovered during implementation -->

- `ConversationPane` does **not** subscribe to `active_widget_changed` today (broadcast by `WorkspacePane.tsx:586`, consumed only by `ReviewCompleteToast`). A must add the subscriber.
- Server "active tab" is currently the `UpdatedAt`-max heuristic (`SprkChatAgentFactory.cs:1467`); the doc-comment already flags "explicit active-tab state is a separate follow-up" — that's FR-A3.
- `handleSelectHistorySession` (`ConversationPane.tsx:2314`) is a **simple text-reload** today; the rich path (`SessionRestoreManager`/`useSessionRestore`/`/tabs`) exists but History doesn't use it — FR-D1 routes it through.
- Cosmos write is fire-and-forget (`ChatSessionManager.FireAndForgetCosmosPersist:574` / `SessionPersistenceService.UpsertToCosmosAsync:863`); no per-doc TTL (container-level only) — FR-D2/D10.
- `EmailWorkspaceWidget` holds **no** email data itself (unused `data` prop; drives its own reads via `useEmailWorkspaceRecord`) — FR-C1 must derive the compact shape from that hook, not the widget wrapper.
- `EmailStubWidget` is **absent** from this worktree (deferred to email-r5) — no R2 task.
- `SerializedWidgetState` (client) + `WorkspaceTabVisibleState` (server) both have 4 variants with 1:1 discriminator guards — the Email variant (C) must be added to both + the guards updated.

---

## Deferrals & Issues — tracking obligation

Track deferred work + newly-discovered issues in BOTH `notes/defer-issues.md` (source of truth) AND GitHub Issues (visibility). File via `/project-defer-issue-tracking` (alias `/defer`) — writes to both in one step. CLAUDE.md §11 rule applies: every entry must name a concrete behavior/contract that fails without the work. `push-to-github` blocks push on entries without GitHub URLs.

---

## Resources

### Applicable ADRs
- ADR-039 (grounded catalog), ADR-015 (metadata-only — Path A tension), ADR-040 (Cosmos ledger), ADR-024 (regarding), ADR-047 (spine — keep), ADR-030 (PaneEventBus), ADR-007 (SpeFileStore), ADR-049 (Compose redline), ADR-042 (memory — do not conflate).

### Related Projects
- `spaarkeai-assistant-enhancements-r1` (predecessor — shipped spine + catalog)
- `spaarke-notification-spine-r1` (⚠️ merge-order overlap: adds a suggestion renderer E removes)
- `ai-advanced-capabilities-analysis-hub-r1` (⚠️ edits ConversationPane + Services/Ai/Chat session binding)
- `email-communication-solution-r5` (prerequisite — merged; email widgets + `eml-render`)

### External Documentation
- `docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`, `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md`, `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`, `.claude/constraints/bff-extensions.md`.

---

*Keep this file updated throughout the project lifecycle.*
