# Current Task State

> **Last Updated**: 2026-11-27 ~14:30Z (task 043 merged and live on dev: BFF + SPA)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | **Task 043 DONE** (FR-28 #2269), PR open — see In flight. Next task per TASK-INDEX after the owner's go. Full record: `notes/task-043-notes.md`. |
| **In flight** | Task 043 PR #3267 MERGED (squash `c438119bca`, 2026-11-26 19:54Z). UAC APPROVED (uac-r2 2026-11-26); its note 1 fixed in 043 (sign-in Forbidden bind/create → 403 create_unavailable), note 2 = comment; #3264 narrowed to the resolver catch arms. BFF with 043 LIVE (see below); 30-min gate PASSED 13:45Z. SPA `c438119bca` LIVE (owner's go). |
| **Next action** | Next task per TASK-INDEX, on the owner's go. Optional: owner E2E of the 503 retry on dev. |

## Live on dev
- **BFF:** `5cd40984b0` (master `765a199922` + the unmerged specialist spike tools; contains 043) LIVE since 2026-11-27 ~13:14Z (uac-r2, owner's go); DLL hash verified, /healthz 200; 30-min gate PASSED 13:45Z (40 req, no 5xx). Rollback `d307d67e36`.
- **SPA:** `c438119bca` (043 merge) LIVE since 2026-11-27 ~14:29Z (run 46016029559, headSha checked, owner's go). Rollback tag `external-spa-dev-24d53fb333`. Dispatch SPA deploys from tag `external-spa-dev-<sha>` to build exactly the approved SHA; check the run's headSha.

## Owner questions open
- Track the raw filing-reference footer seen in an older PRJ-000046 system email?
- Track the residual export-hardening gaps privately?
- Docx preview as an Idea?
- "The client is reviewing this invoice." beyond the Information field?
- `useHostRowSource` row in `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-42 #3201; D-43 #3264; D-44 #3265.

## Housekeeping
- Task 043 publish worktrees removed. PR #3267 open; portfolio synced (35/46).

## Merge trap (seen 2026-11-22, again 2026-11-25)
Master holds older squashes of this project's notes. On a master merge, keep the branch's copies of `projects/spaarke-SPA-external-access-platform-r4/**` (`git checkout --ours`). After a squash merge of this branch's PR, the next master merge conflicts on the same source lines; take ours only after checking that master's only commit to those files is that squash.
