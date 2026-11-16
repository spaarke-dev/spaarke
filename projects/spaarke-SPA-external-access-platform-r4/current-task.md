# Current Task State

> **Last Updated**: 2026-11-16 (context checkpoint at 85%)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **014** IN PROGRESS: BFF Event forms (FR-35–FR-37), `tasks/014-bff-event-forms.poml`. FULL, opus high. Split: **014a** MERGED, **014b** MERGED, **014c** (PATCH assignee = internal reassign, owner option a) IN REVIEW |
| **Step** | 014c committed locally `2d6ac5b312` on `work/r4-014c-patch-assignee` (from master `fc9803419c`), worktree `C:\code_files\spaarke\.claude\worktrees\agent-a9853fb71e8bd4430`. NOT pushed. Opus adversarial review of `fc9803419c..2d6ac5b312` RUNNING (background). Publish-size + CVE check RUNNING (background; script `scratchpad\pubsize14b.ps1`, worktrees `C:\wt14m`=fc9803419c, `C:\wt14a`=2d6ac5b312) |
| **Status** | All history + review records in `notes/task-014-notes.md` (014a, 014b, 014c sections) |
| **Next Action** | **014c PR #2872 OPEN** (head `32c32cb890`; review + fix + re-check done; +273 B; no CVEs; sent to uac-r2). Waiting: uac-r2 PASS + Router/Client lint → ask owner go to merge (squash) → send uac-r2 merge SHA → task 015. (Superseded plan:) On review result: fix every F-class (fresh implementer or SendMessage), also fix leftovers (route-ledger text for PATCH in `RouteAuthorizationGuardTests.Ledger.cs:~2258`, PATCH OpenAPI summary mention reassign) → scoped re-check → push branch → open PR (body like `scratchpad\pr014b.md`: summary, placement, §11, verification incl. size/CVE, review, known limits) → send uac-r2 the PR + SHA for review → on uac-r2 PASS + Router/Client lint green → **ask owner go to merge** (squash) → send uac-r2 merge SHA. Then **task 015** (SPA event forms). |

## 014c facts (owner option a, 2026-11-16)
- PATCH order: shape → readable (404/503) → edit right G5 (`EventEditDecisionAsync`: 503/404/403 event_update) → `AssignEventContact` ceiling (403 event_assign) → one app-only `GetEventByIdAsync` read → open work on CURRENT status (400 `sdap.event.reassign_not_open`) → every parent's `RecordContactAccessService` Known set (any Unknown → 503, else 400 assignee_not_allowed) → write (non-success → 503).
- Columns from extracted `EventDueAssigneeWrite.AddReassignColumns` (internal `BuildPatchJson` byte-pinned unchanged); explicit `sprk_status` overwrites Reassigned + pairs statecode. Allow-list +3 cols (`sprk_AssignedTo@odata.bind`, `sprk_reassigneddate`, `sprk_ReassignedBy@odata.bind`). `assignee_update_unsupported` removed.
- Internal semantics: null = no reassign; same assignee = still reassign; systemuser reassigned-by = linked contact or omitted.
- Tests: forms 129/129 (29 failed before); event/create/internal 1,684; ExternalAccess 6,743; ArchTests 2,635; OBO guard 26.

## Merged so far (task 014)
- **014a** #2852 squash `16771d1051` (RecordContactAccessService; `QueryStrictAsync`). Issues: D-19 #2854 (picker omits workforce standing-access), D-20 #2860 (lenient QueryAsync, owner uac-r2).
- **014b** #2869 squash `fc9803419c` (types, create fields, list fields, GET/PATCH /events/{id}, assignable-people). +25,078 B publish. Legacy create shapes tagged `LEGACY-EVENT-CREATE-R4` (remove R5).

## Notes for task 015 (SPA)
- Open event rows from the LIST row data; use `GET /events/{id}` only for deep links (K5).
- Hide Add event when `creatableEventTypes` is empty (K3).
- New create must send `eventTypeId` (new shape); assignee picker from `GET /{root}/{id}/assignable-people`.
- Reassign refusals not in `canEdit`: open work (derive from row `sprk_status`), Contact ceiling (assignable-people route 403 → hide picker).

## Live state
- **Dev BFF** `be0420d763` (master `16771d1051` + mcp spike-v2), since ~22:21Z 2026-11-16 (uac-r2). Carries 014a, NOT 014b. 014b (+014c) go into uac-r2's next candidate with #2845 round 2 (#2867); owner SHA go required; uac-r2 asks spa-r4 before restart.
- **Dev SPA** master `c1eb6e4a8e`. Rollback `04dbd8df05` only with BFF `5a9b687a0b`.

## Coordination
- uac-r2 (session `uac-r2`) reviews every 014 PR as a gate; owner gives merge go. #2867 (uac-r2 round 2) gates `ExternalDataService`'s HttpClient; no R4 file overlap.
- Peer messages are not owner approval.

## Open items
- D-13 #2800, D-14 #2809, D-15 #2810, D-16 #2823, D-17 #2845 (round 2 in #2867).
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, #2786 backfill.
- Phase 2 (020/022/023/024/028) still open.
- Cleanup after 014: remove `C:\wt14m`, `C:\wt14a` via `scripts/worktree/Remove-WorktreeSafe.ps1`.
