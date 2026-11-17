# Current Task State

> **Last Updated**: 2026-11-17 (task 014 closed)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **014 COMPLETE** (BFF event forms FR-35–FR-37). Next: **015** (SPA event forms), `tasks/015-*.poml`, then **016** (deploy via uac-r2 + owner E2E) |
| **Step** | 015 not started. Run it through task-execute |
| **Status** | 014 history + reviews: `notes/task-014-notes.md` |
| **Next Action** | Invoke task-execute for 015. The SPA work uses the 014 contracts below. A dev deploy of the BFF (014a+b+c) waits on uac-r2's next candidate and the owner's SHA go |

## Task 014 merged
- **014a** #2852 squash `16771d1051`: `RecordContactAccessService`; `QueryStrictAsync`.
- **014b** #2869 squash `fc9803419c`: event types, create fields, list fields, `GET`/`PATCH /events/{id}`, assignable-people. Legacy create shapes are tagged `LEGACY-EVENT-CREATE-R4` (remove in R5).
- **014c** #2872 squash `3c9a2ff4e1`: PATCH `assignedToContactId` = internal reassign (Reassigned status, reassigned-by, date). Open work only (400 `sdap.event.reassign_not_open`). Assignee must be in every parent's Known set (400 `assignee_not_allowed`, Unknown → 503). Contact ceiling → 403 `event_assign`. `canReassign` on list rows and GET.
- Issues: D-19 #2854 (picker omits workforce standing access), D-20 #2860 (lenient QueryAsync, owner uac-r2).

## Notes for task 015 (SPA)
- Open event rows from the LIST row data; use `GET /events/{id}` only for deep links (K5).
- Hide Add event when `creatableEventTypes` is empty (K3).
- New create sends `eventTypeId` (new shape). Assignee picker comes from `GET /{root}/{id}/assignable-people`.
- Reassign: show the picker only when `canReassign`. Never re-derive open work or the ceiling client-side.
- Multi-parent events: offer the INTERSECTION of each parent's assignable-people, or PATCH answers 400 `assignee_not_allowed`.
- Edit and Change status follow `canEdit`, `canChangeStatus` and `allowedStatuses` from the row.

## Live state
- **Dev BFF** `be0420d763` (carries 014a only). 014b and 014c (+ #2867) are on master from `3c9a2ff4e1`. uac-r2 builds the next dev candidate; the owner gives the go per exact SHA; uac-r2 asks spa-r4 before a restart.
- **Dev SPA** master `c1eb6e4a8e`. Roll back to `04dbd8df05` only together with BFF `5a9b687a0b`.

## Coordination
- uac-r2 (session `uac-r2`) reviews BFF PRs as a gate. The owner gives the merge go each time. Peer messages are not owner approval.

## Open items
- D-13 #2800, D-14 #2809, D-15 #2810, D-16 #2823.
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, #2786 backfill.
- Phase 2 (020/022/023/024/028) still open.
- CI: office-scope legs sit at their 20-min cap (known #1474/#1718/#2767). Re-run on a cancel; not ours.
