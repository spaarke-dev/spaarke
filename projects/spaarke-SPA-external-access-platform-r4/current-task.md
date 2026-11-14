# Current Task State

> **Last Updated**: 2026-11-14
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 018: redeploy plus owner E2E retest of the rework (008–011 ✅, merged) |
| **Step** | Waiting for uac-r2's "BFF live". Then run the owner-approved dev SPA deploy |
| **Status** | #2818 merged into master as `bd43110169` (2026-11-14 06:45Z). uac-r2 is deploying from that SHA (five 045f client parts, then the BFF swap on its owner's go, then the 30-minute gate, then "BFF live" sent to us). |
| **Next Action** | On "BFF live": dispatch `.github/workflows/deploy-external-spa.yml` (workflow_dispatch, environment dev, ref master). The owner approved this on 2026-11-13. Rollback: build `b083d62183` from a temporary tag (dispatch takes a branch or tag, not a SHA). Verify the deploy, give the owner the retest list below, and tell uac-r2 when the E2E is done. |

## Done after merge
- The merge SHA was sent to ebilling-r1 (for their 095 notes) and acknowledged to uac-r2.
- Closed D-6 #2703, D-11 #2718 and D-12 #2769, each with a merge reference.
- The last pre-merge commit `593c75cdb9` adds a reasoned eslint-disable on the DataGrid host-command catch (CI client-lint ratchet). Filed D-16 #2823: two older silent catches in the shared DataGrid. Both are in the suppression baseline.

## Owner retest list (after the SPA deploy)
- Section actions on matter / project / WA records. No To Do anywhere.
- The Submit invoice modal over the record, with the record fixed. On a WA it stays in the header (the owner confirms this).
- Submit invoice in the Invoices grid toolbar, with no standalone button.
- Access labels: View Only / Collaborate / Full Access. "Can contribute" never appears.
- Tasks / Events split, Add task, and Add event without the type select.
- Documents columns and tooltips.
- Unfinished E2E items in `notes/task-018-notes.md`, plus the CMRCL-788888 Submit invoice icon.
- ebilling 095e: invoice names become "{firm} - {number}" once it reaches dev.

## Live state
- **Dev BFF:** the swap to `bd43110169` plus the spike is in progress (uac-r2). Until then the SPA hides Add task and shows no Task/Event split or matter/WA labels.
- **Dev SPA:** master `2ef4a17915` build, from before the rework.
- **Never deploy the BFF directly.** uac-r2 orchestrates the BFF deploy, and the owner gives the go per SHA.

## Open items / issues
- D-13 #2800 (retire the to-do routes after the retest), D-14 #2809 (Task GUID copies), D-15 #2810 (dead header icons), D-16 #2823 (silent DataGrid failures).
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, and the #2786 backfill (uac-r2).

## Uncommitted
- current-task.md (this checkpoint)
