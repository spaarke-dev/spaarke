# Current Task State

> **Last Updated**: 2026-11-06 (checkpoint, session ebilling-r1-c7, authoritative). Work head `533fd54c26`, pushed. Dev BFF: **master `20669df6a9`** (uac-r2 deploy 16:44Z, gate PASSED; rollback `0f53e053bf`; `StoredFileReconcileEnabled=false`). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "proceed, parallel where possible". At most 4 agents. Merge each finished branch into work after its reviews. One adversarial pass per change set; two for auth/tenant/partner-readable work; a scoped main-session re-check when a fix changes the approach. FILTERED tests only. Every agent launch states a model. Never `--no-verify` (slipped once on a 166 amend, redone with hooks). |
| **Public-repo rule (uac-r2, 2026-11-06)** | Identifiers (app ids, profile grants in XML, admin `-Verify` checks) are fine. What stays OUT of notes, comments, commits, PRs, issues: the mechanism of an access gap still open ("role X can change Y today, so Z"). Classification tables and per-column evidence for 165 stay in the scratchpad until 165a/165b are live; then a neutral after-the-fact line. **Owner question pending: rewrite work-branch history (the pre-`533fd54c26` 165/EB-D-200 wording) or accept it once 165a is live** (uac-r2 suggests accept if 165a lands within days). |
| **Merged into work today** | 095d-2b-1 (`c05ef65135`; re-run job + partner withhold on `sprk_firmhold`; uac-r2 approved `4f113d72e2` + fix round `415407f3a5`; gate ArchTests 2112/0, filtered 13017/0). DEF-055 = #2600 (retry backoff), DEF-054 = #2601 (operator re-queue). |
| **Owner decisions today** | EB-D-198..201 in the decisions log. **EB-D-201**: an invoice exists only through submission; 165a's hold stands; new task **096b** closes the direct-create paths (generic child-record route for `sprk_invoice`/`sprk_billingevent` + the ribbon's New billing event in the same change, the MDA New/quick-create, user Create on `sprk_invoice` -WhatIf only, dev cleanup of the 11 non-submission invoices with owner go). uac-r2 accepted 096b's scope (7 points in the POML). D-208 (ontology): "Finding" is the user word for a billing Work Item. |
| **Owner go needed (live dev)** | 166 apply (`Set-EbillingInvoiceMoneyFieldSecurity.ps1`, then -Verify; forms redeploy with it); `sprk_sourceemaildocument` apply; 096b role change and data cleanup. Notices to uac-r2 before/after each. |

## Agents and branches

| Item | State |
|---|---|
| **165a** (`task/ebilling-165a`, worktree agent-af9dcaf676990b71b) | Fix round RUNNING (opus): own-submission helper for the corrections-pending reader and the display readers; message text for the unlinked verdict; tests; return-stamp drop in the re-run; Override classification from the recorded outcome; inventory rows; publish delta. Told: DEF-053/#2571 is what 165a closes; keep the inventory table PRIVATE in the scratchpad (`165a-inventory-private.md`), neutral repo notes and commit messages. After it: 2nd adversarial pass (auth task), then merge. Merge note: 164a-4a moved `ReadSubmissionAsync` into `InvoiceFindingsDiff`; whichever merges second calls the helper there. |
| **166** (`task/ebilling-166`, worktree agent-a643db449036d7617) | DONE and reviewed (1 adversarial pass + fix round; ontology-r1 APPROVED the guard change and OK'd the two Services remarks, done). Squashed to ONE neutral commit `4c342a83b5` (backup branch `task/ebilling-166-presquash`, local only). Pre-cleanup merged-tree gate: ArchTests 2122/0, filtered 18064 passed with only the known flaky `ProvisionProjectIdempotencyTests`, Pester 15/0. **Next: merge into work, re-gate, push.** After 166 reaches master: send customer-provisioning BY MESSAGE the master SHA and the exact profile grants on `sprk_totalamount`/`sprk_approvedtotal` (they add a stamp check that fails if not secured or the evaluator can't read). |
| **164a** (integration `task/ebilling-164a` = `de22834d0d`, worktree `C:\code_files\spaarke-wt-ebilling-164a-int`) | 4a + 4b DONE (Add line steps 0-9, route `POST /api/finance/invoices/{id}/lines`). Two adversarial reviews RUNNING (correctness; access). Then: fix round, uac-r2 + ontology review (tell ontology I-1 changed, D-130; `InvoiceRuleSignalReconciler` moved code; `SignalEpisodeLifecycle` log context), merge into work, remove the integration worktree, fix `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`. Open: live check that a row-version-only invoice update is refused when changed (dev verification). 164b (client) and 164c (ribbon) not started. Expect a 049b conflict in `ChildRecordEndpoints.CreateTables`. |
| **095d-2b-2** (`task/ebilling-095d2b2` from `415407f3a5`, own worktree) | RUNNING (opus): after-commit Signals in `InvoiceSubmissionRerunJobHandler.AfterCommitAsync`. Then review (access pass ×2 per partner-readable rule if it touches the withhold) and uac-r2/ontology. |
| **165b** | After 165a and 164a: origin check (moved from 164), status-record check, totals check, pending-spend exclusion (ontology review). |
| **096b** | After the 164a merge. |

## Coordination state

- **field-mapping 032**: fill-only core-record mapping inside `InvoiceSubmissionPersister` (owner ruling). Our 6 conditions accepted and recorded in their `notes/task-032-design-inputs.md`; they send the POML + design to us and uac-r2 before code.
- **mcp-bff-api**: invoice decisions in Copilot are link-outs to `sprk_invoicereviewpage` with `invoiceId` only; tell them (and ontology #2579) when optional `lineId` lands in `src/solutions/InvoiceReview/src/services/launch.ts` (still to build, before the master PR).
- **ontology**: reviewing nothing open from us except 164a (to send).

## Standing gates (unchanged)

- Creator-person `-Verify` exits 0 before any BFF deploy (dev args in the project CLAUDE.md / earlier notes). Reconcile stays OFF until DEF-044 on master AND L6 passes. No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0. No BFF with 095a anywhere before `sprk_sourceemaildocument` is field-secured there. One master PR (reviewers ontology-r1, uac-r2, R4) once 095d-2 and Add line are in; pre-checks and PR-body items as listed in the previous checkpoint (git show `7a9f45c424:projects/ebilling-r1/current-task.md`, rows "Gate / master PR" and "Post-merge order on dev").

## Next action

1. Merge `task/ebilling-166` (`4c342a83b5`) into work; filtered gate (Finance, Submission, Invoice, FieldSecurity, ParentLineage, AccessTable, RecordCreation, Signals, ExternalAccess) + ArchTests + Pester; push.
2. Ask the owner: history rewrite vs accept (see Public-repo rule row).
3. On each agent notification: 165a fix → 2nd adversarial pass; 164a reviews → fix round; 2b-2 → reviews.
4. Retry `Remove-WorktreeSafe.ps1` on agent-a44f091c8b2428d50 and the merged agent worktrees (095d2b1 agent-a627d378759c7d82a once its branch is merged: it is).
