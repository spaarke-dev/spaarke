# Current Task State

> **Last Updated**: 2026-11-26 ~19:00Z (task 043 done, PR open)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | **Task 043 DONE** (FR-28 #2269), PR open — see In flight. Next task per TASK-INDEX after the owner's go. Full record: `notes/task-043-notes.md`. |
| **In flight** | Task 043 PR (pushed; merge only on the owner's go). UAC APPROVED (uac-r2 2026-11-26); its note 1 fixed in 043 (sign-in Forbidden bind/create → 403 create_unavailable), note 2 = comment; #3264 narrowed to the resolver catch arms. After merge: BFF deploy via uac-r2 (owner go per SHA), then SPA deploy (owner's exact-SHA go). |
| **Next action** | Owner: merge go for the 043 PR; then request the dev BFF deploy through uac-r2 and an SPA deploy SHA go. Remove `C:\wt043m` and `C:\wt043b` with `scripts/worktree/Remove-WorktreeSafe.ps1`. |

## Live on dev
- **BFF:** `d307d67e36` LIVE since ~18:00Z (uac-r2, owner's go); 30-min gate PASSED 18:31Z. Rollback `140c01219a`. Task 042's BFF part live since `6a2a569ac9`. Task 043 NOT deployed (not merged).
- **SPA:** master `24d53fb333` LIVE since 2026-11-26 ~15:50Z. Rollback tag `external-spa-dev-rollback-0267ee64a2`. Dispatch SPA deploys from tag `external-spa-dev-<sha>` to build exactly the approved SHA; check the run's headSha.

## Owner questions open
- Track the raw filing-reference footer seen in an older PRJ-000046 system email?
- Track the residual export-hardening gaps privately?
- Docx preview as an Idea?
- "The client is reviewing this invoice." beyond the Information field?
- `useHostRowSource` row in `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-42 #3201; D-43 #3264; D-44 #3265.

## Housekeeping
- Short-path publish worktrees `C:\wt043m` (master 51ce01a116) and `C:\wt043b` exist; remove with `scripts/worktree/Remove-WorktreeSafe.ps1` after the PR.

## Merge trap (seen 2026-11-22, again 2026-11-25)
Master holds older squashes of this project's notes. On a master merge, keep the branch's copies of `projects/spaarke-SPA-external-access-platform-r4/**` (`git checkout --ours`). After a squash merge of this branch's PR, the next master merge conflicts on the same source lines; take ours only after checking that master's only commit to those files is that squash.
