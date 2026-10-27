# Current Task State

> **Last Updated**: 2026-10-27, after word-add-in-r1's review of #2342. The 095 path is urgent (EB-D-158). The 095 contract is agreed with word-add-in-r1. 118a is done.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Branch** | work/ebilling-r1, draft PR #1911. Tip 5a89ea5e2b (pushed). |
| **Running (4 agents, the cap)** | **089** (task/ebill-089-assignments), **092** (task/ebill-092-submission-files), **098** (task/ebill-098-external-marker), **095** (task/ebill-095-from-email; early start with a seam into 094, adapter onto today's intake or NotYetAvailable; includes the §8 firms route and EB-D-160 retry-after-Failed). uac-r2's conditions went to 089 and 098. 095 was told to merge fd70b8e1f6 and build marks_invalid `invoice_not_pdf`. |
| **Master PR #2342** | Branch feat/ebilling-118a-invoice-attachment-marking, worktree C:/code_files/wt-118a-master. word-add-in-r1 asked for one change; the fix is pushed as 7c9ab7e9ef ('disabled-attachment-marked'; 24/24 tests) and cherry-picked into work as 22da5af476. Waiting on their re-approval and green CI, then merge, then remove the worktree with scripts/worktree/Remove-WorktreeSafe.ps1. |
| **word-add-in-r1** | AGREED the 123 contract (notes/123-upload-contract.md), `invoice_not_pdf` and EB-D-160. Their condition: any renamed code goes to them before merge. #2342's lint fix (no-unnecessary-condition) is pushed as dc533168bd (work: 3e7b366293); they approve once CI is green. |
| **Next actions** | (1) Merge #2342 once word-add-in-r1 re-approves and CI is green. (2) As agents report: review, send uac-r2 the review request, merge after review, build the 5 test projects plus the broad filter. (4) 089 reports: run its live steps (owner pre-approved, EB-D-158; notices to uac-r2 before and after), then launch **086**. (5) 092 pushed: launch **094** from 092's result (it does not wait for 086); 094 plugs into 095's seam. (6) After 086 and 092: launch **123**. (7) After uac-r2 task 194 merges: add a test that a re-owned invoice's family rows reach the same BU through the SecureChildLineage walk (uac-r2's ask). |

## 095 contract
`notes/095-from-email-contract.md`. word-add-in-r1 AGREED the whole file (§1-§9, OP-1..OP-8) at 717cf238d9. Since then: EB-D-160 (§5, re-attempt after Failed), not yet sent to them. Their task 133 releases in slices: A (client plus view), B (adopts 118a; needs it on master), and C (removes Invoice from Save; only after from-email works on dev, with the owner's go; 096 follows one release later). G3 (correction prefill) is deferred to 097.

## Owner decisions 2026-10-27
EB-D-153 (schema run, done, -Verify 0), 154 (dev uses sysadmin; Billing Admin gets the Procedure tables in 121), 155 (corrections like new versions; flagged, applied on re-check unless pinned), 156 (suppression kept), 157 (visibility follows type), 158 (urgency; chain overlap; 089 and 086 live steps pre-approved), 159 (118a split), 160 (retry after Failed on from-email), 161 (task 123 upload route). Open: whether the explicit re-check button (R2) should come into R1 — asked, no answer yet.

## Status
- **Done:** 080, 081, 088, 090, 091, 099, 118a (merged into work at cd10657469), 119, 120, 122 ✅. Master PR #2342 (feat/ebilling-118a-invoice-attachment-marking) is open for word-add-in-r1 review; its worktree C:/code_files/wt-118a-master is to be removed with Remove-WorktreeSafe.ps1 after the merge.
- Creator-person schema live on dev (notes/creator-person-dev-run-2026-10-27.md); uac-r2 closed 090 K1; the HARD RULE is met on dev.
- ontology DI-128 / #2336: ontology-authored policies record no creator person (deferred, theirs).

## Unit suite
Gate: ArchTests, integration and the broad filter (Finance, Billing, Signals, RuleSet, Invoice, Compliance, Rejection, Decision, AccessTable, ParentLineage, Ebilling, Procedure, SecureChild, Submission). The full 35k suite times out under load.

## Peer state
- **uac-r2:** turns reviews around quickly, 095's path first. Reviews owed: 086, 089, 094, 095, 098, 117, 123; and the 091 items (what-if, live Test-EbillingFileStoreRole.ps1, tag-write proof). Invoice family follows BU moves via SecureChildLineage; no CoreAncestorResolver change.
- **word-add-in-r1:** builds against the contract (task 133). Waiting for the 118a PR and the EB-D-160 note.
- **R3:** ebilling OK given for dev BFF 3ff2c04bf5.

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices. StoredFileReconcileJob is not enabled anywhere before F4 merges and DEF-044 is fixed. After merges: build the 5 test projects and run the broad filter before pushing. Remove worktrees only with Remove-WorktreeSafe.ps1.
