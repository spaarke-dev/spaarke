# Current Task State

> **Last Updated**: 2026-11-28 (task 048 done; PR open, waiting on the owner's merge go)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | None. Task 048 (phone layout ≤ 480px, FR-32, #2466) is done: code, review (2 fixes), notes, status. |
| **Branch** | `work/spaarke-SPA-external-access-platform-r4-048` (cut from the state branch at `30decd737e`), pushed with a PR to master. The PR merges only on the owner's go. |
| **Next action** | After the owner's merge go: merge the 048 PR. Then task 058 (P4 deploy + E2E). Its POML notes now carry 048's follow-up checks: real record sections and widget bodies at 375px, high-contrast and Teams dark, overflow-menu dialogs. |

## Task 048 result
- One breakpoint module `src/client/external-spa/src/components/phoneLayout.ts` (480px, matchMedia hook, an app-wide 44px static rule). Shell, tab strip, header, Quick Start, widget library and record template follow it. Secondary record actions go to RecordCommandBar's existing overflow (API unchanged).
- Suite: 78 files, 2,183 tests. Notes: `notes/task-048-notes.md` (measurements, decisions, review, K-limits).
- Found in passing: D-48 #3331 (local mock mode redirects to CIAM sign-in once a grid widget loads).

## In flight elsewhere
- Squashes `4f9e371445` (044 + 047) and `f0f1e810bd` (045) are on uac-r2's NEXT dev BFF candidate list. They go live only on the owner's go in uac-r2's session.
- Commit `30decd737e` (046 closed; #2578 closed citing #2791) is on the state branch and in the 048 branch; it reaches master with the 048 PR.

## Live on dev
- BFF `b84fa5f061` (rollback `af8a5547ef`).
- SPA `c438119bca` (rollback tag `external-spa-dev-24d53fb333`). Dispatch from tag `external-spa-dev-<sha>`; check the run's headSha.

## Next tasks
- 058 (P4 deploy + E2E), then 080, 090. 016 stays wip.
- Board: Tasks Completed = 40/46 (item `PVTI_lAHODW0Pv84BEgWuzhHt-CE`, field `PVTF_lAHODW0Pv84BEgWuzhWPlLY`, project `PVT_kwHODW0Pv84BEgWu`).

## Owner questions open
- Track the raw filing-reference footer seen in an older PRJ-000046 system email?
- Track the residual export-hardening gaps privately?
- Docx preview as an Idea?
- "The client is reviewing this invoice." beyond the Information field?
- `useHostRowSource` row in `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-42 #3201; D-43 #3264; D-44 #3265; D-47 #3315; D-48 #3331.

## Merge trap
Master holds older squashes of this project's notes. On a master merge, keep the branch's `projects/spaarke-SPA-external-access-platform-r4/**` (`git checkout --ours`), after checking that the squash is master's only commit to those files.
