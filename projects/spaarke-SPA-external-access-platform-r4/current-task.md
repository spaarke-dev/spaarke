# Current Task State

> **Last Updated**: 2026-11-11
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none active — task 006 completed |
| **Step** | — |
| **Status** | 006 ✅ in TASK-INDEX and POML; two adversarial passes + fix round 1 + scoped re-check done; D-9 follow-up (ProjectPage) done |
| **Next Action** | Begin task 007 (`tasks/007-*.poml`, Submit invoice record action, FR-10) |

### Critical Context
- Record view: `components/record/RecordTemplateModal.tsx`; sections in `record/sections/`, action hosts in `record/actions/`.
- Per-record access (task 006): `components/record/recordAccess.ts` is the one place for create rights, "Your access"
  and the read-only lines. Create actions follow each list's `canCreate`; unknown = hidden and silent.
- Create actions are offered to the toolbar by the lists through `CreateActionReporter` (`components/createAction.tsx`);
  child dialogs open with `useChildDialog`. Esc in a child follows the modal standard (owner, Path C).
- Hold: any section/host `onBusyChange(true)` holds paging, Close and Refresh (Refresh hidden).

## Open owner questions
- Post-merge R4 look at ebilling's `src/client/external-spa/**` changes in #2689 (ebilling-r1 asked; waits on owner go).
- D-6 #2703: send the Create Event type, or remove the select?
- D-4 #2693: should Copy link carry `?customer={activeKey}`?
- D-1 #2686: R4 follow-up task, or hand to the event/write-path owner?
- D-2 #2687: the `Location` header contract choice.
- 002 UAC review PENDING (four items in `notes/task-002-notes.md`) before task 018.

## Quick Reference
- Project CLAUDE.md, tasks/TASK-INDEX.md, spec.md FR-01..FR-07, notes/task-006-notes.md, notes/defer-issues.md (D-1..D-11)
- PR #2681 (draft, Auto-fix on); Project issue #2680; Epic #2099
