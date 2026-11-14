# Current Task State

> **Last Updated**: 2026-11-13
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Post-E2E rework 008–011 ✅ (all committed). Next: PR → merge → deploys → task 018 owner retest |
| **Step** | Merge `origin/master` (77 behind, clean `merge-tree`) → push → open a NEW PR (PR #2681 already merged) |
| **Status** | Tasks 001–011 ✅; 018 🔲 (redeploy + retest). Branch clean after the part-3 commit. |
| **Next Action** | `git merge origin/master` → BFF build + external-spa typecheck → push → `gh pr create` (PR body: 008–011 summary, uac-r2 PASS 008, ebilling-r1 PASS 010, Placement Justification, publish +4,861 B, no CVE) → ask the owner "merge after CI?" → after merge: send the master SHA to uac-r2 (BFF deploy, owner go per SHA) and ebilling-r1 (asked for the merge SHA, their 095 notes) → owner-approved SPA deploy (rollback `b083d62183` via a temp tag) → owner retest |

## Commits this rework (branch `work/spaarke-SPA-external-access-platform-r4`)
- `9328c3d7eb`: POMLs 008–011. `15dca35e03`: master merge. `45dae7a7b9`: D-13 decision.
- `c9b9492c86`, **008 BFF**:
  - `/me` `records[{recordType, recordId, accessLevel}]`;
  - `eventType: "task"` (closed set; B1 AppendTo probe; 400 `sdap.event.type_not_allowed`);
  - `isTask` and `canCreateTask` on the event lists;
  - shared `EventTypeRef` constant.
  - **uac-r2 PASS.**
- `ab62c2049e`, **009 SPA**: section actions; To Do removed; empty states; D-12 Documents columns.
- `519158cae8` and `9b10699efe`, **010 parts 1–2**: wizard `lockedRecord`; child SprkModal over the record. **ebilling-r1 PASS** (re-checked).
- `15e3ebbbbc`, **011**:
  - Manage Access labels; Tasks / Events split; Add task;
  - the label is hidden for `writeAccessPerRecord` callers;
  - shared lib bumped to 3.0.0 (`addTodo` → `addTask`).
- Latest commit, **010 part 3**: DataGrid `hostCommands` (never replaces a configured or built-in item); Submit invoice in the Invoices grid toolbar.

## Owner decisions this session (also in project CLAUDE.md §3)
- Keep the external to-do BFF routes for now → D-13 #2800.
- Invoices grid toolbar: option A, the generic DataGrid host-command prop (approved on behalf of the grid owners).
- Main-session decision (review K4): the "Your access" label is hidden for `writeAccessPerRecord` callers.

## Live state
- **Dev BFF:** `b100effac3` (uac-r2; gate passed, #2761 fixed). It does NOT yet contain 008.
- **Dev SPA:** master `2ef4a17915` build, pre-rework.
- **Deploy order:** BFF first (Add task needs `canCreateTask`; an older BFF just hides it), then the SPA.
- **Never deploy the BFF directly.** uac-r2 orchestrates; the owner gives the go per SHA.

## Open items / issues
- Filed: D-13 #2800, D-14 #2809 (Task GUID copies), D-15 #2810 (dead header icons).
- Close after merge: D-6 #2703, D-11 #2718, D-12 #2769.
- WA Submit invoice stays in the header (no Invoices section) — owner to confirm at retest.
- ebilling 095e (`work/ebilling-r1` `7953d052d9`): invoice names become "{firm} - {number}" once it reaches dev.
- Retest also covers the unfinished E2E items in `notes/task-018-notes.md` and the CMRCL-788888 Submit invoice icon.
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, the #2786 backfill (uac-r2).

## Uncommitted
- none
