# Current Task State

> **Last Updated**: 2026-11-15 (checkpoint after the Event forms decisions)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | None in progress. Next: **014** (BFF Event forms, FR-35–FR-37). FULL, opus high, two adversarial passes |
| **Step** | 014 not started. Its gates: (1) PR #2847 merged — **done** (`ce6c241964`, hold released); (2) uac-r2's UAC review of the proposal (`notes/event-forms-design.md` §4) recorded BEFORE any BFF code |
| **Status** | 018 ✅ (retest round 3 passed). Event forms: owner decided Q-1–Q-8, S-1–S-4; spec FR-35–FR-37 written; tasks 014/015/016 created; 036 superseded |
| **Next Action** | When the owner says go: start 014 via task-execute; first send uac-r2 the proposal (design §4) and wait for its review. Phase 2 (020/022/023/024/028) is still open and independent of the event work |

## Event forms (decided 2026-11-15)
- Design and every file:line: `notes/event-forms-design.md`. Spec: FR-35 (create), FR-36 (event record: read, deep link, edit, status), FR-37 (assignable people); FR-23 superseded; out-of-scope line narrowed (events are editable).
- **Owner decisions:**
  - all active event types the caller has AppendTo on (fallback config: Task + Reminder);
  - type required, and **not editable** after create;
  - priority, assignee and description on create;
  - edit and **any** status by Collaborate/Full Access on every parent;
  - assignees = contacts granted on the record (incl. me);
  - deep link yes;
  - assignee column `sprk_assignedto`.
  - Q-9 (existing type-less events) not answered → leave as "—".
- **BFF builds on `RequestAccessMemo`** (`Infrastructure/ExternalAccess/RequestAccessMemo.cs`, #2847):
  - get-or-add with a `remember` predicate; own key prefix covering every input;
  - lock `context.Items` inside a composition;
  - clear on any in-request access-state write (the PATCH).

## Live state
- **Dev BFF:** **`f451e0874d`** (master `ce6c241964` incl. #2845 latency fix + ebilling #2846, + spike), live about 00:45Z 2026-11-16. Rollback: BFF `aa7d6345f3` alone (still pairs with the current SPA). uac-r2's 30-minute gate with request timings is running.
- **Dev SPA:** master `c1eb6e4a8e` (run 43747335450; tasks 012/013/021). SPA rollback `04dbd8df05` only together with BFF rollback `5a9b687a0b` (the old SPA calls the retired invoice-document routes).

## Open items / issues
- D-17 #2845: fixed by uac-r2 (#2847 merged); expect each external request to drop from 3–4 s once `f451e0874d` is live. Ask the owner to recheck speed.
- D-13 #2800, D-14 #2809, D-15 #2810, D-16 #2823.
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, #2786 backfill (uac-r2).
- Preview not loading = by design until task 022 (FR-14).
