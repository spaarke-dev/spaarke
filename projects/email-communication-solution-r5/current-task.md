# Current Task — `email-communication-solution-r5`

> Tracks ONLY the active task. History lives in `tasks/TASK-INDEX.md` + per-task `.poml`.

---

## Active Task

- **Task**: none (between waves — Phase 3 complete)
- **Status**: not-started
- **Next Action**: Execute **task 040** (assemble the shared `EmailWorkspace` component — Pattern D source of truth)

## Progress

- ✅ Phase 0: 001 (sanitizer), 002 (archiving contract)
- ✅ Phase 1: 010 (eml-render endpoint + Ganss.Xss §6.5 Path-A)
- ✅ Phase 2: 020, 021, 022, 023 (Layer-1 extractions)
- ✅ Phase 3: 030, 031, 032 (list + view-selector + shell), 033, 034, 035, 036 (reading-pane sub-views)
- ⏭️ Phase 4: 040 (assembly) → 041 (widget) ‖ 042 (code page)
- ⏭️ Phase 5: 050 (verify) → 051 (deploy) → 090 (wrap-up)

## Deferrals accumulated (sync via /defer at task 090)

- **031**: List/Thread toggle omitted (spec "only if cheap" — thread field not guaranteed in maker FetchXML).
- **034**: promote a shared `CommunicationHeader` into `@spaarke/communication-components` (currently private to `communication-page`, wrong dep direction per ADR-012).
- **036**: add `initialCc?: string[]` to `ISendEmailDialogProps` in `@spaarke/ui-components` (composer supports it; dialog type doesn't declare it).

---

## How to continue

Say **"continue"** — invokes `task-execute` on task 040. See `CLAUDE.md` §Task Execution Protocol.
