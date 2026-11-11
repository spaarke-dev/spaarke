# Current Task State

> **Last Updated**: 2026-11-11
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 003 — RecordTemplateConfig + record template shell (`tasks/003-record-template-shell.poml`) |
| **Step** | Step 10 done (POML completed, TASK-INDEX ✅, drift check exit 0) |
| **Status** | completed and committed |
| **Next Action** | Begin task 005 (`tasks/005-*.poml`, opus/high, sequential) via task-execute |

**Rigor Level:** FULL. Hot-path check done by main session: no watchlist trigger.

### Critical Context
- Task 003 output: `src/client/external-spa/src/components/record/` (types, configs, moved reads, header, modal),
  `recordLinkFor` in `detail/record-targets.ts`, `tests/RecordTemplateModal.test.tsx` (40 tests). Suite 1332/1332,
  typecheck and lint clean. Detail: `notes/task-003-notes.md`.
- RecordOpener still opens the R3 modal; task 005 switches it and supplies `renderSection`.
- Handoff to 005: Refresh while a section is busy + failed re-read unmounts the sections (review R3, K1).
- UI tests for 001 and 003 are listed for deploy task 018.

## Files Modified This Session (uncommitted)
- `src/client/external-spa/src/components/record/*` (new, 5 files)
- `src/client/external-spa/src/components/detail/RecordDetailModal.tsx`, `record-targets.ts`
- `src/client/external-spa/tests/RecordTemplateModal.test.tsx` (new)
- `projects/.../tasks/003-record-template-shell.poml`, `tasks/TASK-INDEX.md`, `notes/task-003-notes.md`, `notes/defer-issues.md` (D-4)

## Open owner questions
- D-4 #2693: should Copy link carry `?customer={activeKey}` like the grant email?
- D-1 #2686: R4 follow-up task, or hand to the event/write-path owner?
- D-2 #2687: the `Location` header contract choice.
- 002 UAC review PENDING (four items in `notes/task-002-notes.md`) before task 018.

## Quick Reference
- Project CLAUDE.md, tasks/TASK-INDEX.md, spec.md FR-01/FR-02, notes/task-003-notes.md
- PR #2681 (draft, Auto-fix on); Project issue #2680; Epic #2099
