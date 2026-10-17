# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-17 ~14:50Z (main session; owner answered Q1–Q7, D-161..D-165). State only — history is in git log,
> task `<completion>` blocks and `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path;
> D-123 secure family; D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-165). Deferred work: one self-contained GitHub issue per item, label
> `ontology-deferred`, plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-114**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Lane 102b (rule matter scope, D-165) running |
| **Next Action** | On the 102b notification: two adversarial passes (tenant-isolation) + uac-r2 → fix → re-check → merge into docs. Owner: 111 row 4 with R3's URL; CORS reload result to R3 |

## Lanes in flight

Shared brief: scratchpad `lane-brief.md`. Each lane: review → fix round → re-check → merge into `docs/ontology-platform-design`.

| Task | Worktree / branch | Model | State |
|---|---|---|---|
| 102b rule matter scope [tenant-isolation] | `C:\wts-102b` `task/ontology-102b` | opus/high (agent `lane-102b`) | Running. Closes DI-62 #1647 + DI-112 #2069. Matter scope only (subject `sprk_matter`), tenant stays blank. uac-r2 review focus: tenant never from the body, server-side scope in the evaluator, no Signal outside the matter, must-fire/must-not-fire tests |

## Open owner / peer items

1. **111 row 4** (owner): `/upload` needs `?entityType=sprk_project&entityId=<GUID>`; the GUID comes from opening a project in My Projects (`/project/<GUID>`). No in-app link to `/upload` is a known R3 limit (R3 task 042 notes). Then close 111 → 114.
2. **CORS on `/api/v1/external/notifications`** (R3 owns): the owner saw it, time unknown (probably after the 14:32Z deploy). Owner to reload and report; R3 wants owner approval for live reads (platform CORS, anonymous preflight). **Never dispatch `deploy-external-spa.yml` from master** (task 122) while R3's SPA work is off master.
3. **POL-MATTER-NOBUDGET** is on (D-163): the owner bulk-closes the dev Signals after the next evaluation.

## Next, in order

1. 102b lane → review → merge; then a docs→master sync (102b) and an R3 BFF deploy (fresh owner approval each time).
2. 105 UAT with the owner: 102 wizard browser tests + keyboard/screen-reader after a confirmation closes; 064 §6.1 non-admin checks and UI tests (D-164); the 055 Next step To Do.
3. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882), close #1882.
4. ebilling 070 PR re-check when it arrives (026 re-check was CLEAN, reported).
5. 114 after 111; 090 wrap-up.
6. Cleanup via `scripts/worktree/Remove-WorktreeSafe.ps1`: `C:\wts-eb070`, `C:\wts-eb026`, `C:\wts-sync`, `C:\wts-di107`, `C:\wts-049b`, `C:\wts-064`, `C:\wts-102a`, `C:\wts-102`, `C:\wb126-pub`, `C:\wts-126-pub`.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- BFF `1.0.0+9721819edc15…` (R3, 2026-10-17 14:32:19Z; master `88b3ecb1f` = 063 join, 102a routes + allow-list; UAC #2074). healthz 200, writer MI assigned.
- Console from master `88b3ecb1f` (07:32:56Z): rule wizard (102) + 064 code. **Landing tab = Worklist** since 14:38Z (D-164; revert `Set-DefaultWorkspaceLayout.ps1 -To 'Daily Briefing'`).
- Policies: six on, including POL-MATTER-NOBUDGET since ~14:36Z (D-163).
- Roles: Ontology Service 58 (§15); Ontology Administrator 38 (§16 +2 Append, §17 −DR Create −Signal Write; keeps Signal Assign/Delete, D-162); Console User 21 (§17).

## Promises to peers

- **R3** owns the dev BFF line and the external SPA: message first for any BFF change.
- **word-add-in**: before/after notices for any Console or BFF change (and before any landing-tab revert).
- **uac-r2**: before/after notices for live role changes; PRs for role XML; the 102b PR number for review.
- **ebilling**: 070 re-check when its range arrives.

## Peers (current session names)

uac-r2 `spaarke-wt-unified-access-control-r2-c8`; R3 `spaarke-wt-spa-external-access-platform-75`; ebilling `spaarke-wt-ebilling-r1-99`;
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-54`; provisioning `spaarke-wt-customer-provisioning-orchest-bb`.
