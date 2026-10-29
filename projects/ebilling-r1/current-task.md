# Current Task State

> **Last Updated**: 2026-10-29 (context-handoff). The owner asked: commit/push → update from master → /conflict-check → /merge-to-master for work/ebilling-r1 (PR #1911). Steps 1 and 3 are done. Step 2 is in progress (agent). Step 4 is blocked on the 194b writer fix, then CI and ontology-r1 review.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Work branch** | origin/work/ebilling-r1 = cb20efc537. It carries master a4b6459da3, 098, and the UAC RecordFamilyEdges ruling. PR #1911 is draft and CONFLICTING (master moved), so no CI has run. Its body carries "UAC-r2 review: accepted at cb20efc537 …". |
| **Unpushed integration tip** | Worktree `C:\code_files\spaarke\.claude\worktrees\agent-ae9910636512ed719`, local branch `ebill-master-sync` @ bf9f6ae88d, 50 ahead of work. It holds 3723edfad9 (merge of master 2cd0f62e2c = uac 194b #2432) and 7a8fa8f0a7 (merge of 094 @ 600079fd11, uac READY). ArchTests: 1 fail, "194b R3b-2 … ForRow …", on 6 ebilling writers: RuleSetService, ProcedureService, InvoiceLineDecisionService, RateCardImportService, InvoiceSubmissionPersister, InvoiceSubmissionService. |
| **Running agents** | **ebill-194b-writers** (opus, in that worktree): ForRow/ForChild writers plus ordinary/secure tests, merge master (~127 ahead), full gate incl. both integration suites and Client lint, push to work, add "UAC-r2 review: 094 READY at 600079fd11" to the #1911 body. It reports any ratchet entries; uac must accept them. **ebill-102** (sonnet, worktree agent-a67053723ffe4e2ba): local commit 0bae22d224, finishing and pushing `task/ebill-102-adjustments`. |
| **Do not push to work** | Until ebill-194b-writers has pushed (its push must fast-forward). This checkpoint commit is local only; push it after. |
| **Next action** | (1) Owner decisions on 103 F1 and Q1 (below). (2) Send the 103 fix round. (3) File the Q2 defect plus its fix. (4) When the writers agent is green: confirm the #1911 checks are terminal-green, request ontology-r1 review (project CLAUDE.md §4), get UAC acceptance of any ratchet entries, put the full integration counts in the PR body, mark it ready, then `gh pr merge 1911 --auto --merge`. |

## Task branches (not on work)

| Task | Branch @ head | State |
|---|---|---|
| 101 | task/ebill-101-corrections (local, worktree agent-ae6eabaa0ec9d718e) | Stopped at its POML trigger. Owner chose (EB-D-170, to log): store per-line provenance first (analyzer id/version and Action code/version in `sprk_extractionconfidence`, plus header provenance); old dev lines refuse extracted-field corrections with a stable 409. Resume after 094 is on work, rebased on work. |
| 102 | task/ebill-102-adjustments | Finishing (agent). |
| 103 | task/ebill-103-approval @ ea3eb20269 | Adversarial review done; findings below. Needs a fix round. |
| 086 / 092 / 089 | existing branches | Merge work (once it carries 194b), then the content-team writers and the 086 fix round (uac F3-1, K1–K5; adversarial K2–K6). |

## 103 adversarial findings (ea3eb20269)

- **F1 (security; owner decision):** eligibility matches `contact.sprk_systemuser == caller` (EligibleApproverResolver.cs:323-341). That column is not secured and not audited, so anyone with contact Write can make themselves an approver. Options: (a) match the assignment contact against the caller's own contact via `IIdentityNormalizationService.ResolveAsync(caller).ContactId` (recommended); (b) field-secure and audit `sprk_systemuser`; (c) require both.
- **F2:** an approve retry after a failed bulk accept loses the line Decision Records (InvoiceApprovalService.cs:409-417). Finish the accept-clean decision first, or answer 409 until it is complete. Add a test.
- **F3:** approve does not require exactly one core lookup (`CoreOf` takes the first one), and the recorder's filing check runs after the transition. Fix: use `InvoiceFamilyCoreRecordGuard.CoreRecordLookups` exactly-one before the claim, ideally `CoreAncestorResolver.ResolveStampsAsync`.
- **F4:** GET `canApprove` ignores Write on the invoice (InvoiceApprovalService.cs:654-665).
- **K (fix cheaply):** make the claim conditional on decidedby being empty or the caller; clear the claim fields on Refused; the job reads the core from the invoice, not the portion; put the approved amount in the record Values; reject an unknown portion status. File a follow-up for "Approved with no Decision Record" (sweep).
- **Q2 (real blocker, in 049's persister):** no line is ever written Clean (InvoiceSubmissionPersister.cs:812 writes only Pending/Flagged), so no invoice can be approved. Fix in the persister: unflagged → Clean. File it via /project-defer-issue-tracking and fix it in scope.
- **Q1 (owner):** should approve close invoice-level Signals (D-122 pattern), or leave that to 104/later?

## Peers

- uac-r2: 094 READY at 600079fd11. RecordFamilyEdges accepted at cb20efc537. 194b is on master (2cd0f62e2c) but NOT yet deployed to dev (waits on the dev BU arrangement); uac will notify. #2427 OutputLookups sha to come.
- word-add-in-r1: has every 095 contract change up to 97c793670b. Waits on our notice when 094 merges to work, and when dev is live.
- R3: dev BFF = master 136568cf70 (no ebilling code). On dev, ebilling is OFF (no Ebilling/Finance settings), so ebilling code on master is inert there (503 ebilling.disabled; the reconcile job skips).
- Filed: #2426 (flaky ProvisionProject timeout test).

## Owner decisions this session (to log in decisions-log once work is pushable)

- EB-D-169 logged (faulted visibility read → 503).
- EB-D-170 not yet logged: per-line AI provenance for corrections (101).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices (uac-r2, R3, ontology as relevant). StoredFileReconcileJob is not enabled anywhere before F4 merges and DEF-044 is fixed. After merges: build the 5 test projects, then run the broad filter and ArchTests before pushing. Remove worktrees only with Remove-WorktreeSafe.ps1. Max 4 concurrent subagents. Scratchpad queue: scratchpad/queue.md.
