# Current Task State

> **Last Updated**: 2026-11-12
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 018 — P1 deploy + E2E (`tasks/018-p1-deploy-dev-e2e.poml`) — PREP ONLY, nothing live |
| **Step** | — |
| **Status** | 001–007 ✅; Phase 1 code complete. Next is 018 (live: BFF deploy via uac-r2 + SPA dev deploy + E2E) |
| **Next Action** | Gates in `notes/task-018-notes.md`: (1) owner decides PR #2681 ready + merge to master; (2) uac-r2's task-002 review (requested 2026-11-12); then the uac-r2 deploy request for the master SHA. SPA rollback target `b083d62183` |

### Critical Context
- Record view: `components/record/RecordTemplateModal.tsx`; sections in `record/sections/`, action hosts in `record/actions/`.
- Per-record access: `components/record/recordAccess.ts` is the one place (create rights, "Your access", read-only lines,
  Submit invoice rule). Unknown = hidden and silent.
- Submit invoice (007): page param `?invoiceRecord=` (not `?record=`); router-free `pages/invoiceSubmitLink.ts`;
  `components/detail/RouterRecordOpener.tsx`.
- Task 018 browser checks carried from earlier tasks: 004 ui-tests + D-8 aria-hidden; 006 ui-tests (light/dark);
  007 ui-tests + #2658 aria-hidden after Submit invoice from a grid-opened record (steps in `notes/task-007-notes.md`).

## Open owner questions
- (Reminder, not a question) Tell ebilling-r1 the merge sha when PR #2681 reaches master (they asked, 2026-11-12).
- Post-merge R4 look at ebilling's `src/client/external-spa/**` changes in #2689 (ebilling-r1 asked; waits on owner go).
- D-6 #2703: send the Create Event type, or remove the select?
- D-4 #2693: should Copy link carry `?customer={activeKey}`?
- D-1 #2686: R4 follow-up task, or hand to the event/write-path owner?
- D-2 #2687: the `Location` header contract choice.
- 002 UAC review requested from uac-r2 2026-11-12 (four items); answer pending.
- PR #2681: mark ready and merge to master (gate for 018)?

## Quick Reference
- Project CLAUDE.md, tasks/TASK-INDEX.md, spec.md, notes/task-007-notes.md, notes/defer-issues.md (D-1..D-11)
- PR #2681 (draft, Auto-fix on); Project issue #2680; Epic #2099
