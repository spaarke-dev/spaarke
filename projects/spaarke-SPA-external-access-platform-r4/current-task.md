# Current Task State

> **Last Updated**: 2026-11-11
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none active — task 004 completed |
| **Step** | — |
| **Status** | 004 ✅ in TASK-INDEX and POML; review + fix round 1 done |
| **Next Action** | Begin task 006 (`tasks/006-*.poml`, per-record access, hide-don't-disable, "Your access") |

### Critical Context
- Record view: `components/record/RecordTemplateModal.tsx`; sections in `record/sections/`, action hosts in `record/actions/`.
- Create actions (task 004): lists offer Upload / New message / Add to-do / Add event to the toolbar through
  `CreateActionReporter` (`components/createAction.tsx`); inside the template the list has no own create button.
  Add to-do / Add event stay invisible in the template until task 006 removes `AccessLevel.ViewOnly` in
  `TasksEventsSection.tsx` (the section now keeps one busy hold per list).
- Esc in a child dialog: owner chose Path C (comply with the modal standard). Esc closes neither child nor record;
  Cancel/× close the child with focus back on the opener. Spec FR-03 amended.
- Hold: any section/host `onBusyChange(true)` holds paging, Close and Refresh (Refresh hidden).
- Master merged in `8ec9cff1aa` (ebilling #2689 included; five shared BFF files resolved).

## Open owner questions
- Post-merge R4 look at ebilling's `src/client/external-spa/**` changes in #2689 (ebilling-r1 asked; waits on owner go).
- D-6 #2703: send the Create Event type, or remove the select?
- D-4 #2693: should Copy link carry `?customer={activeKey}`?
- D-1 #2686: R4 follow-up task, or hand to the event/write-path owner?
- D-2 #2687: the `Location` header contract choice.
- 002 UAC review PENDING (four items in `notes/task-002-notes.md`) before task 018.

## Quick Reference
- Project CLAUDE.md, tasks/TASK-INDEX.md, spec.md FR-01..FR-07, notes/task-004-notes.md, notes/defer-issues.md (D-1..D-8)
- PR #2681 (draft, Auto-fix on); Project issue #2680; Epic #2099
