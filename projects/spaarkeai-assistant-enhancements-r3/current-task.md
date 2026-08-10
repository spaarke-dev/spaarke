# Current Task — spaarkeai-assistant-enhancements-r3

> **Reset by**: project-pipeline (2026-08-10) at task generation.
> This file tracks ONLY the active task. History lives in `tasks/TASK-INDEX.md` + per-task `.poml`.

---

## Active Task

- **Task**: 011 — Trim prompt block + thread active-item handle (server)
- **Status**: not-started
- **Rigor**: FULL · **Tier**: opus @ xhigh · **Step mode**: directional
- **Next action**: Begin Step 1 of task 011 (`tasks/011-trim-prompt-thread-handle.poml`).

## Blocking / pre-execution notes

- **Master re-sync DONE** (2026-08-10): branch merged `origin/master` (was 5 behind → 0), pushed. Precondition cleared.
- **Coordination**: `/conflict-check` before every BFF / `ConversationPane` PR. Consume `Services/Ai/PublicContracts/` seams (no fork).
- **Task 010 discovered architecture gap** (see task 010 POML `<notes>` + TASK-INDEX): `BuildWorkspaceStateBlock`'s only caller reads via `IWorkspaceStateService.GetTabsAsync`, whose WRITE path was retired by AIR2-075 (no writer anywhere in the repo). The REAL, live tab persistence is `ISessionPersistenceService.SaveTabsAsync`/`StoredWorkspaceTab` (NFR-09, task 065) — a separate, disconnected system. Task 011 (FR-03/FR-04, wiring the active-item handle) should explicitly decide whether/how to reconcile these two systems as part of its threading work.

## Decisions this task
- **2026-08-10 — OWNER DECISION (root-cause fix, Option A)**: `BuildWorkspaceStateBlock` is fed by `IWorkspaceStateService.GetTabsAsync`, whose WRITE path was retired by AIR2-075 (read-only; nothing writes it → runtime-inert). The LIVE open-tabs are `session.Tabs` (`StoredSession.Tabs`, written by the client `PATCH /sessions/{id}/tabs`, NFR-09/task-065), already in scope at `CreateAgentAsync`. **Task 011 re-points the tab source to `session.Tabs`** (unioned with any pre-existing pinned durable rows from `IWorkspaceStateService`), in addition to trim + thread. This is the linchpin fix for the R2 UAT gap ("Assistant couldn't see the tabs"). Confirmed by owner via AskUserQuestion.

## Steps completed this task
- (none yet — task 010 completed; see tasks/010-layout-tab-visibility.poml for its full completion notes)

## Files modified this task
- (none yet)
