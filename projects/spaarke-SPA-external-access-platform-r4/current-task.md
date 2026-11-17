# Current Task State

> **Last Updated**: 2026-11-17 (task 015 started)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **015** SPA event forms (FR-35–FR-37), `tasks/015-spa-event-forms.poml`. Then **016** (SPA deploy + owner E2E) |
| **Step** | Step 9.5 fix round. Implementation DONE (uncommitted, ~35 files under `src/client/external-spa/**` + shared `RecordCommandBar/recordActionIcons.ts` and its test). Checks passed: typecheck, lint, vitest 63 files / 1793, build, jest 14/14. Opus adversarial review done (one full pass): no F1–F3 |
| **Status** | in-progress. Rigor FULL; model tier sonnet @ high; steps directional |
| **Now** | All review rounds done (re-check F1/F2 fixed; vitest 1799). Notes: `notes/task-015-notes.md`. 015 = `[wip]` until the PR merges. Committing, then PR to master; owner gives the merge go; then mark 015 done and start 016 (SPA dev deploy needs owner go + rollback) |
| **Fix round** | DONE: items 1–3, 6, 7 below applied by the implementer; plus `RecordTemplateModal` now mounts action hosts outside the ready branch (`hostRow` = shown or last-ready record for the same key) so a 404 keeps the Edit dialog open. vitest 63/1797, typecheck, lint, build pass. A scoped Sonnet re-check of that fix diff is RUNNING |
| **Next Action** | When the re-check reports: fix any F-class; then write `notes/task-015-notes.md`, set POML 015 completed + TASK-INDEX ✅ + drift check, commit on the branch, PR to master (like #2837), ask the owner for the merge go. (Original fix list, done:) Apply review fixes: (1) F4 deep-link token — amend POML 015 goal/constraint/criterion 3, spec FR-36, design §3 from `?record=sprk_event:{id}` to `?record=event:{id}` (the code's token, same convention as project:/matter:), tell the owner; (2) `events/eventSave.ts` 404 → `closeDialog:false` (update test `EventRecord.test.tsx` ~:750); (3) `events/useAssignablePeople.ts` ApiError 403 → ready `[]` (field hidden, no line) + test; (6) add test: after Edit in a child + Close, the parent Events row shows the new value; (7) spec FR-05 / FR-34(a) icon map +`edit` (Edit20Regular) +`changeStatus` (ArrowSwap20Regular). Then scoped re-check (Sonnet) of the fix diff, re-run vitest, commit, PR to master like #2837, notes file `notes/task-015-notes.md`, POML/TASK-INDEX/drift. Known limits to record: K2 Edit can't mark due date required (DTO has no requiresDueDate; server 400 shown); K2 Copy link on a K5 multi-parent event opens not-found; multi-parent assignee 400 shown as message |

## Branch
- Merged origin/master `3c9a2ff4e1` into the branch (`6b60ae9a83`, `--no-verify` master-merge). Project-doc conflicts kept the branch copies.

## Design decisions (task 015)
- **Event reads.** A list row opens its event from the ROW DATA (seed; 014 K5). Refresh of a list-opened event re-reads the PARENT LIST and finds the row (same read as the list, so K5 holds). A deep link `?record=event:{id}` reads `GET /events/{id}` (404 → the existing "no longer available" state).
- **Template plumbing.** `RecordDetailEntity.read?` (entity default reader), `RecordTemplateModal` props `seed?` / `readRecord?`, `OpenChildRecord` options `{ seed, read }`, `RecordViewContext.refreshRecord`.
- **PATCH answers 204 with no body**, so after Edit / Change status the record re-reads and the parent Events section re-reads on child close.
- **After a create, the list re-reads** (the create's return row has no formatted names or rights).
- **Add event hidden when `creatableEventTypes` is empty** (014 K3). The Task radio and `canCreateTask` use are removed; the SPA no longer sends `eventType: "task"`.
- **Icons.** New `edit` and `changeStatus` entries in the shared `RECORD_ACTION_ICONS` (FR-05 map; reviewed change; its pinning test updated).
- **Delete.** `delete` action id → a dispatcher host (service request vs event), so the modal stays entity-free.
- **One-level rule.** Regarding is a link only in a top-level event (opens the parent as a child); in a child event it is plain text. Event rows open a child only inside a top-level record.
- **Multi-parent assignee (K).** The SPA knows only one parent (list root / GET `regarding`), so it offers that parent's assignable people; the PATCH's 400 `assignee_not_allowed` is shown as a message. No client intersection is possible without the parent list on the wire.

## Live state
- **Dev BFF** `e46b6b453b` (uac-r2 deploy, live since 2026-11-17T03:40Z, `/healthz` 200): master `3c9a2ff4e1` (014a/b/c, #2845 round 2) plus mcp spike-v2. Rollback = `be0420d763`. uac-r2 asks spa-r4 before any restart.
- **Dev SPA** master `c1eb6e4a8e`. Roll back to `04dbd8df05` only together with BFF `5a9b687a0b`.

## Coordination
- uac-r2 (session `uac-r2`) reviews BFF PRs as a gate. The owner gives the merge go each time. Peer messages are not owner approval.

## Open items
- D-13 #2800, D-14 #2809, D-15 #2810, D-16 #2823; D-19 #2854, D-20 #2860.
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, #2786 backfill.
- Phase 2 (020/022/023/024/028) still open.
- CI: office-scope legs sit at their 20-min cap (known #1474/#1718/#2767). Re-run on a cancel; not ours.
