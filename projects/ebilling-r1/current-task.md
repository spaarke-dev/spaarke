# Current Task State

> **Last Updated**: 2026-11-07 (checkpoint, session ebilling-r1-c7, authoritative). Work head `46c3504d67`, pushed. Dev BFF: **master `cc11294300`** (uac-r2 deploy 14:22Z; no ebilling code; `StoredFileReconcileEnabled=false`). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "proceed, parallel where possible". At most 4 agents. Merge each finished branch into work after its reviews. One adversarial pass per change set; two for auth/tenant/partner-readable; a scoped re-check (Sonnet adversarial-reviewer) when a fix changes the approach. FILTERED tests only. Every agent launch states a model. Never `--no-verify`. |
| **Public-repo rule (uac-r2)** | Identifiers are fine. Keep OUT of notes, comments, commits, PRs, issues: the mechanism of an access gap that is still open. 165 classification and evidence stay in `scratchpad\165a-inventory-private.md` until live. Owner accepted the existing history (no rewrite). |
| **Merged into work** | 166 (`ab95fe67d6`); **164a Add line** (`eeac33b1c2` + fix-up `682dc4a661`: the 166 guard lists `InvoiceLineAddService` as a Writer and `InvoiceFindingsDiff` as a Reader; `FinanceBaseValues` remarks; write-path doc). Approved by both adversarial passes, the re-check, uac-r2 and ontology. Gate: filtered 17853/0, ArchTests 2121/1, and the failing guard now passes 10/10. The 164a integration worktree is removed. |
| **Owner decisions 2026-11-06/07** | EB-D-202: partners see added lines and reviewer reasons while In Review (supersedes EB-D-192's firm view); task 130 is amended to carry it. EB-D-203: Return of a Blocked SPA submission needs no privilege. EB-D-204 (lead): the re-run stays pending until `assignFirmRerun.state = done`. EB-D-205: added lines count toward spend (D-130 amended; ontology D-211). DEF-056 (#2614): a line-decision repeat cannot finish its Decision Record. |
| **Owner go needed (live dev)** | (1) **096b data cleanup, NEXT**: delete all 11 invoices with no submission behind them. uac-r2 agreed: no Cancel route exists, so delete. Details below. (2) 166 apply (`Set-EbillingInvoiceMoneyFieldSecurity.ps1`, then -Verify) with the forms redeploy. (3) `sprk_sourceemaildocument` apply. (4) 096b role -WhatIf → apply. (5) 164a live check that a version-only invoice update is refused. Notify uac-r2 before and after each. |

## Agents running

| Item | State |
|---|---|
| **165a** (`task/ebilling-165a`, worktree agent-af9dcaf676990b71b, NOT pushed) | Fix round 2 done; squashed head `5e1c232946` (backup `task/ebilling-165a-pre-r2` = `9e6986875d`). Re-check: no F. **Running (Sonnet):** pre-merge wording pass on the pre-165a `SubmissionOverrideServiceTests` text, the repair remark, the known-limit notes, the doc nit, amended into the one commit (backup `-pre-r3`). **Then:** merge into work. Consolidate the own-submission helper: 164a's `ReadSubmissionAsync` filter must use 165a's `InvoiceBlockedHold` helper. Expect conflicts in `InvoiceCorrectionReevaluation`, `CorrectionWorld`, `ApprovalWorld` and `InvoiceCorrectionEndToEndTests`. Filtered gate: Finance, Submission, Invoice, ApExport, ExternalAccess, plus ArchTests. Close DEF-053/#2571 at merge. |
| **095d-2b-2** (`task/ebilling-095d2b2`, worktree agent-aa152e8e1480d32db, NOT pushed) | Fix round done (`a09adf1389`, EB-D-204 gate). Re-check: F4 liveness. **Running (Sonnet):** `QueueRerunAsync` also re-queues when the state isn't `done`; log texts; audit script check (g); notes (incl. ontology Q2 resolved, docs `e9652c27c5`). **Then:** merge. At merge, add an Add line test for the firm gate on state `committed` (`InvoiceLineAddService` already calls `InvoiceFirmGate.CheckAsync`). Send ontology the `SignalWriter.cs` doc-comment diff. |
| **096b** (agent worktree, branch `task/ebilling-096b` from `682dc4a661`) | **Running (Sonnet):** source scope 1-3: remove `sprk_invoice` from `CreateTables`; remove the ribbon's New billing event; model-driven create entry points in source; the app-only proof note; role source and script (no live run). **Then:** send uac-r2 the route diff and app-only proof BEFORE the role -WhatIf; review; merge. #1973 closes once the dev build is live. |

## 096b dev cleanup (owner go pending; uac-r2 conditions agreed)

The list is in `notes/096b-dev-cleanup.md`: 11 In Review invoices. Dependents, all checked:
- "INV-005" `4c0f4d12-9e9c-f111-b8de-7ced8ddc4cc6`: 1 line;
- "Invoice Wizard" `f749a11e-2a7b-f111-ab0e-7ced8ddc4cc6`: document `d36e37a2-3ccb-f111-a05d-0022482913fc`;
- "New invoice 3 7/9/2026" `55328b00-477b-f111-ab0e-7ced8ddc4cc6`: document `58328b00-477b-f111-ab0e-7ced8ddc4cc6` and communication `a62a5f02-477b-f111-ab0e-7ced8ddc4a05`.

Neither document has child documents, and the communication has no other documents.

**Plan:**
1. Delete the 2 documents through the BFF document delete path (`DELETE /api/documents/{documentId}`, file-first, so the file and the row go together).
2. Delete the line, the communication and the 11 invoices as a named admin with auditing on.
3. Send uac-r2 the times, ids and the count afterwards. Expected: 0 non-derived invoices, 6 derived.

Membership junction rows go stale until the 02:00 reconciliation run; uac-r2 checks them. **Still to settle:** how to call the dev BFF delete as an admin (token for the BFF scope), and which identity the Dataverse MCP uses (WhoAmI).

## Queued after the merges

- **165c**: the submission-link agreement in `ExternalInvoiceVisibility` (POML written). After 165a. Two adversarial passes; uac-r2 reviews.
- **165b**: origin, status-record, totals and pending-spend checks. After 165a and 164a.
- **095d-2b-3** (not written yet): on the post-Set-firm re-run, write the invoice supersedes link (`sprk_supersedesinvoice` to a Returned-to-Firm predecessor). Recommendation: in the commit, reusing the persister's lookup and the core-record guard.
- **Task 130**: firm projection, amended by EB-D-202. It includes uac-r2's pin test that Basic read stays Basic and that records have no partner-resolvable owner.
- **`InvoiceReview` `launch.ts` optional `lineId`**: before the master PR. Tell mcp-bff-api and ontology #2579 when it lands.
- **Customer-provisioning**: after 166 reaches master, send them the SHA and the profile grants by message. The 095a lock targets SpaarkeMaster 1.3.0.1 (ontology published 1.3.0.0 without it).
- **Dev deploy batch**: after 165a and 2b-2 merge, send uac-r2 the exact commit and the pre-deploy checklist. Each gate needs uac-r2's read-back plus the owner's go.

## Standing gates (unchanged)

- Creator-person `-Verify` exits 0 before any BFF deploy.
- Reconcile stays OFF until DEF-044 is on master AND L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0.
- No BFF with 095a anywhere before `sprk_sourceemaildocument` is field-secured there.
- One master PR (reviewers ontology-r1, uac-r2, R4) once 095d-2 and Add line are in. Pre-checks are in `git show 7a9f45c424:projects/ebilling-r1/current-task.md`.

## Next action

1. Ask the owner for go on the 096b cleanup plan above. If yes: WhoAmI, notify uac-r2, run the plan, then send times, ids and the count.
2. On agent notifications: merge 165a (consolidate the helper), merge 2b-2 (add the Add line gate test), review 096b.
3. Then launch 165c, write and launch 2b-3, then 165b.
