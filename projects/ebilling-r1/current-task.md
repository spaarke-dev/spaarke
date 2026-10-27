# Current Task State

> **Last Updated**: 2026-10-27, after word-add-in-r1's review of #2342. The 095 path is urgent (EB-D-158). The 095 contract is agreed with word-add-in-r1. 118a is done.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Branch** | work/ebilling-r1, draft PR #1911. Master de38296499 (uac 194) merged in at ffe7ee3a2f; gates green (broad 9,473 / Arch 1,557). Invoice-family move test at 730b7a654d, sent to uac-r2 for review. |
| **092 built** | task/ebill-092-submission-files at bbf9cd04e8 (pushed, NOT merged). Gates green in the agent. Its adversarial review has NOT run (it hit the cap): run one adversarial-reviewer when a slot frees, then merge. On merge, the main session must also update `.claude/constraints/provisioning.md:269` (retired `Finance__EbillingOutputContainerId`). Its 3 new StoredFileService methods need 090-owner acceptance (us). Escalation for the owner: the 3 `ProcedureLibraryContainerId` readers (RuleSetService.cs:406, :655; RateCardImportService.cs:158) have no owning task. DEF-047 / #2351 filed. 095 was told 092's SubmissionSnapshot rule means from-email stages its PDF and leaves sprk_document empty. |
| **Running (4 agents, the cap)** | **089** (task/ebill-089-assignments), **094** (task/ebill-094-submit-invoice, from 092's tip plus work; 098 and 086 merge later), **098** (task/ebill-098-external-marker), **095** (task/ebill-095-from-email; early start with a seam into 094, adapter onto today's intake or NotYetAvailable; includes the §8 firms route and EB-D-160 retry-after-Failed). uac-r2's conditions went to 089 and 098. 095 was told to merge fd70b8e1f6 and build marks_invalid `invoice_not_pdf`. |
| **Master PR #2342** | MERGED 2026-10-27 as 5911eba1ff (word-add-in-r1 approved when green). Worktree and branch removed. |
| **word-add-in-r1** | AGREED the 123 contract (notes/123-upload-contract.md), `invoice_not_pdf` and EB-D-160. Their condition: any renamed code goes to them before merge. They have 118a on master for slice B. |
| **Next actions** | (1) As agents report: review, send uac-r2 the review request, merge after review, build the 5 test projects plus the broad filter. (2) 089 reports: run its live steps (owner pre-approved, EB-D-158; notices to uac-r2 before and after), then launch **086**. (3) Adversarial review of 092 when a slot frees, then merge 092. When 098 lands, tell 094 to merge it. (4) After 086 and 092: launch **123**. (5) When sprk_invoicelineitem (uac #2348) reaches our lineage, add it to TheInvoiceFamily_FollowsAnOrdinaryMove_ThroughTheInvoice. |

## 095 contract
`notes/095-from-email-contract.md`. word-add-in-r1 AGREED the whole file (§1-§9, OP-1..OP-8) at 717cf238d9. Since then: EB-D-160 (§5, re-attempt after Failed), not yet sent to them. Their task 133 releases in slices: A (client plus view), B (adopts 118a; needs it on master), and C (removes Invoice from Save; only after from-email works on dev, with the owner's go; 096 follows one release later). G3 (correction prefill) is deferred to 097.

## Owner decisions 2026-10-27
EB-D-153 (schema run, done, -Verify 0), 154 (dev uses sysadmin; Billing Admin gets the Procedure tables in 121), 155 (corrections like new versions; flagged, applied on re-check unless pinned), 156 (suppression kept), 157 (visibility follows type), 158 (urgency; chain overlap; 089 and 086 live steps pre-approved), 159 (118a split), 160 (retry after Failed on from-email), 161 (task 123 upload route). Open: whether the explicit re-check button (R2) should come into R1 — asked, no answer yet.

## Status
- **Done:** 080, 081, 088, 090, 091, 099, 118a (merged into work at cd10657469), 119, 120, 122 ✅. 118a is also on master (#2342, 5911eba1ff).
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
