# Current Task State

> **Last Updated**: 2026-11-11
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none active — task 005 completed (uncommitted) |
| **Step** | — |
| **Status** | 005 ✅ in TASK-INDEX and POML; drift check exit 0 |
| **Next Action** | Begin task 004 (`tasks/004-*.poml`, child dialogs inside the parent SprkModal) |

### Critical Context
- Record view is now `components/record/RecordTemplateModal.tsx` for all six types (grid, deep link, bell, intake link).
  Sections: `components/record/sections/` (`renderRecordSection`). Toolbar actions with their own flow (Download,
  Delete request) are action hosts: `components/record/actions/` (`RECORD_ACTION_HOSTS`, keyed by toolbar action id).
- Hold: any section/host `onBusyChange(true)` holds paging, Close and Refresh (Refresh hidden). Task 004 child dialogs
  should report busy the same way if they post.
- Create actions (Upload, New message, Add to-do, Add event) are still hidden: task 004 wires them; task 006 does
  visibility and removes the `AccessLevel.ViewOnly` props in `TasksEventsSection.tsx`.
- Preview (task 022) and invoice Files (task 021) are P1 interims in `sections/PreviewSection.tsx` / `FilesSection.tsx`.
- `RecordDetailModal.tsx` is deleted. The project CLAUDE.md §6 gotcha line numbers that cite it are now historical.

## Files Modified (uncommitted, task 005)
- New: `src/client/external-spa/src/components/record/sections/*` (9 files), `.../record/actions/*` (3 files),
  `src/client/external-spa/tests/RecordTemplateSections.test.tsx`
- Modified: `RecordTemplateModal.tsx`, `recordTemplate.types.ts`, `recordReads.ts` (comment), `detail/RecordOpener.tsx`,
  `detail/ServiceRequestActions.tsx`, `detail/RecordMessages.tsx`, `detail/record-targets.ts` (comment),
  `shell/NotificationBell.tsx` (comment), `widgets/GridWidgetBody.tsx`
- Tests modified: `RecordTemplateModal`, `DetailDocumentsDelete`, `DetailEventsTodosRoots`, `DocumentDownload`,
  `DocumentDetail.hasFile`, `RecordDeepLink`, `NotificationBell`, `NotificationBell.noInteraction`,
  `App.notificationBell`, `App.deepLinkSignIn`
- Deleted: `src/components/detail/RecordDetailModal.tsx`, `tests/RecordDetailModal.test.tsx`
- `tests/Spaarke.ArchTests/InvoiceStatusRetirementGuardTests.cs` (walk path literal)
- Project: `tasks/005-*.poml`, `tasks/TASK-INDEX.md`, `notes/task-005-notes.md`, `notes/defer-issues.md` (D-5)

## Open owner questions
- D-4 #2693: should Copy link carry `?customer={activeKey}` like the grant email?
- D-1 #2686: R4 follow-up task, or hand to the event/write-path owner?
- D-2 #2687: the `Location` header contract choice.
- D-5 #2699: test files outside `npm run typecheck` (filed).
- 002 UAC review PENDING (four items in `notes/task-002-notes.md`) before task 018.

## Quick Reference
- Project CLAUDE.md, tasks/TASK-INDEX.md, spec.md FR-01/FR-02/FR-03, notes/task-005-notes.md
- PR #2681 (draft, Auto-fix on); Project issue #2680; Epic #2099
