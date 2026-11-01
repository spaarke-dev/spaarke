# Current Task State

> **Last Updated**: 2026-10-29 ~19:40Z. merge-to-master for PR #1911: the CI timeout is fixed on work (aedb5c7dbf); waiting on CI.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Work branch** | origin/work/ebilling-r1 = **842110e6ab** (PR #1911, draft, MERGEABLE, BLOCKED by CI). It integrates 102, 103 @ dca71dfbed, storedfile-owner @ d3eb47376a, 095-errorcode @ d7218072fb, master 3d54ac845d and signal-close @ 7354384779, plus 842110e6ab (the retirement guard's ExcludedFiles list replaces a split-string constant). Local gate at f78afac072: unit 12,562/0/14, ArchTests 1,858, integration 199/0/8 and 353/0/23, Client lint ok. Route ledger = 146. |
| **Reviews** | **uac PASS** (storedfile Q1/Q2; 102's Adjustment ForChild 5e863d29bd). "#1911 can go ready from UAC". **ontology APPROVED** (PR comment; F2 closed on every caller incl. 103 approve; K1 closed). |
| **CI fix** | The office-scope "rest" leg was not hanging: email-save tests waited on a real token exchange (~22 s each) and a real Service Bus enqueue (5 s each). aedb5c7dbf (test code only) gives the Office test hosts a fake Graph factory and a loose job-submission mock: rest leg 12m21s → 7m32s locally, api-office 6m01s, 1,947/0 and 522/0/7. Not yet proven on CI. Follow-ups (not filed yet): test host working set ~5 GB; `test.search.windows.net` / `test.openai.azure.com` resolve to real Azure. |
| **Next action** | Watch CI on the pushed head. When all checks are terminal with zero fail: add the integration counts to the PR body, `gh pr ready 1911`, `gh pr merge 1911 --auto --merge`, watch it merge, then the A.6 post-merge sync of the main checkout (ff-only; never reset). |
| **CI monitor** | Use `gh pr checks 1911 --json name,bucket --jq '.[]\|"\(.bucket)\t\(.name)"'` and `grep -E` with `T=$'\t'`. NO bare jq, NO grep -P on this machine (memory: no-jq-use-gh-jq). The other checks on 842110e6ab were passing or pending at 17:40. |

## Running agents (2026-10-29 late)
- **ebill-reconcile-switch**: `Ebilling:StoredFileReconcileEnabled` (default false) on branch task/ebill-reconcile-switch. Merge into work before ebilling is switched on anywhere.
- **ebill-104**: task 104 on task/ebill-104-reject-return.
- **ebill-101**: resumed. Persister prep (provenance EB-D-170, Clean status Q2, #2455), then 101.

## Dev enablement (EB-D-173, owner approved 2026-10-29)
R3 deploys the dev BFF (needs uac to clear the 194b hold, plus the owner's direct approval in R3's session naming the SHA; R3 includes #1911 if merged). After R3's "gate passed, you can go" message (UAC 30-min check), WE apply the dev app settings: `Ebilling:Enabled=true` and the Finance file-store settings; `Ebilling:StoredFileReconcileEnabled=false`. Rollback: remove them. Then notify word-add-in that 095 is live on dev, and run the 015 column delete.

## After #1911 merges (on work, as a follow-up)
- ontology asks (non-blocking): (1) the 105-override-server.md known limits must name the principal-change case: a repeat by a different person without Write on the Signal subjects gets 403, so only the original decider's retry completes the closes. (2) SubmissionOverrideService runs RecordRefusal twice in its catch filter, so the warning logs twice.
- Tell word-add-in-r1 the master sha. Notify them again when 095 answers on dev (needs #1911 on master + SPA-r3's dev BFF deploy, which waits on uac's 194b hold, + the owner's OK to switch ebilling on in dev).
- 015: delete the dev column only after the next dev BFF deploy (R3 will notify).
- Mark 101/102/103 status in TASK-INDEX (102 and 103 are on work now; they complete when merged).

## Task branches / next work
| Task | State |
|---|---|
| 101 | Waiting (worktree agent-ae6eabaa0ec9d718e, branch task/ebill-101-corrections). Resume: rebase on work. Persister prep (EB-D-170 provenance + Q2: unflagged lines are written Clean, with an end-to-end persister → approve test) + #2455 (line decisions and corrections use SingleCoreOf; CorrectionRow no-core throw). Then build 101. |
| 086 / 092 / 089 | Merge work, then the content-team writers + the 086 fix round (uac F3-1, K1–K5; adversarial K2–K6). |
| After 103 | 104, 150 (its export writer must pass `requestedBy` to CreateForAppAsync, or it fails closed), 152. After 101: 161. |

## Peers
- uac-r2: PASS on everything sent. #2427 OutputLookups sha to come; 194b dev-deploy notice to come.
- ontology-r1: approved.
- R3: `sprk_invoicestatus` off the master allow-list (#2450). It will notify when the dev BFF deploys.
- word-add-in-r1: OK on the 095 contract fix; #2448 is on master. Owed: the master sha, then the dev notice.
- field-mapping: told that stash 3067d28d24 is ours and fully superseded. **Owner decision pending: OK to drop it** (re-find its stash@{n} by sha first).
- Filed: #2426 (flaky ProvisionProject test), #2454 (approval sweep), #2455 (CoreOf first-core / CorrectionRow throw).

## Owner decisions this session (logged)
EB-D-169 to EB-D-172 are in notes/decisions-log.md.

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices. StoredFileReconcileJob is not enabled anywhere before F4 merges and DEF-044 is fixed. After merges: build the 5 test projects, then run the broad filter and ArchTests before pushing. Remove worktrees only with Remove-WorktreeSafe.ps1. Max 4 concurrent subagents. Every agent launch states a model.
