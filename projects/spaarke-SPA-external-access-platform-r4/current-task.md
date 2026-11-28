# Current Task State

> **Last Updated**: 2026-11-28 ~04:30Z (tasks 044, 045 and 047 merged; none of them on dev yet)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | No task in progress. 044, 045 and 047 are DONE and merged. 046 is DONE with no fix needed (2026-11-28; on this branch, not yet on master). Records: `notes/task-044-notes.md`, `task-045-notes.md`, `task-046-notes.md`, `task-047-notes.md`. |
| **In flight** | 044 + 047 squash `4f9e371445` (PR #3311) and 045 squash `f0f1e810bd` (PR #3322, reviewed by ontology-r1 and uac-r2) are on uac-r2's NEXT dev BFF candidate list. They go live only on the owner's go naming the SHA in uac-r2's session. |
| **Next action** | Next task per TASK-INDEX on the owner's go: 048 (Teams mobile), then 058. 016 stays wip. |

## Live on dev
- **BFF:** `b84fa5f061` (= `af8a5547ef` + the mcp-r2 file probe), LIVE since 2026-11-28 ~04:15Z (uac-r2); the gate is ~04:43Z; rollback `af8a5547ef`. It does NOT contain 044/045/047.
- **SPA:** `c438119bca` (043 merge), LIVE since 2026-11-27 ~14:29Z. Rollback tag `external-spa-dev-24d53fb333`. Dispatch SPA deploys from the tag `external-spa-dev-<sha>` so the build is exactly the approved SHA, and check the run's headSha.

## acme (FR-31, tasks 046/047/058)
- 2026-11-28: the owner's "skip ACME until provisioning-r1 indicates it is stable" needs no change.
  - acme's registry row names its own BFF, so the dev BFF skips it. The fix is uac-r2's #2761 / #2791, live since 2026-11-13 20:40Z; there has been no acme fault since (40,910 fault rows from 2026-10-14 to 11-13).
  - provisioning-r1 says acme is stable. Its setup status must not be changed.
  - Detail: `notes/task-047-notes.md` "Resolved 2026-11-28".
- 046 closed with no fix needed and #2578 closed as fixed by #2791 (owner, 2026-11-28: "if they aren't needed then close them"). #2163 was already closed as its duplicate.
- provisioning-r1 FYI, not this project's: at 03:45Z a dev path met an acme SPE container (both stamps use the Model 1 container type), and the ownership guard refused it, as intended. Which dev path enumerated it is unknown.
- Registration default (`DATAVERSE_URL`): it keeps the Warning (owner, 2026-11-27).

## Owner questions open
- Track the raw filing-reference footer seen in an older PRJ-000046 system email?
- Track the residual export-hardening gaps privately?
- Docx preview as an Idea?
- "The client is reviewing this invoice." beyond the Information field?
- `useHostRowSource` row in `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-42 #3201; D-43 #3264; D-44 #3265; D-47 #3315.

## Housekeeping
- The publish-size worktrees `C:\wt045m` and `C:\wt045b` are removed.
- This state commit is on `work/spaarke-SPA-external-access-platform-r4-state`.

## Merge trap (seen 2026-11-22, again 2026-11-25)
Master holds older squashes of this project's notes. On a master merge, keep the branch's copies of `projects/spaarke-SPA-external-access-platform-r4/**` (`git checkout --ours`). After this branch's PR is squash-merged, the next master merge conflicts on the same source lines. Take ours only after checking that the squash is master's only commit to those files.
