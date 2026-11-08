# Current Task State

> **Last Updated**: 2026-11-07 late (checkpoint, session ebilling-r1-c7, authoritative). Work head `59b5f756b3`, pushed. Dev BFF: uac-r2's `22487c6537` (master + 049b + MCP spike, 20:06Z). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project" (and earlier "proceed, parallel where possible"). At most 4 agents. Merge each finished branch after its reviews. One adversarial pass per change set; two for partner-readable/auth; a scoped Sonnet re-check when a fix changes the approach. FILTERED tests only. Every agent launch states a model. Never `--no-verify`. |
| **Public-repo rule** | Never put the mechanism of an open access gap in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Merged into work today** | 165b item 1 (`b49465d972`); 165c (`1aff454e34`); 095d-2b-3 (`c2239d9d58`); 165a + 095d-2b-2 (`2fac72b8a5`; #2571 closed; ontology OK'd the SignalWriter doc diff); 164b Add line client (`01854564a3`). |
| **Decisions today** | EB-D-206 (2b-3: the re-run writes the supersedes link in its commit; AMENDED: the predecessor's Return must predate the invoice, plus a predecessor lock); EB-D-207 (fm-032 allow-list + secure/No Access gate, uac-r2 agreed; partners have no invoice search; task 132 renders only ReadableColumns; `sprk_billingexport` writer-only → 165d). |

## Agents / branches in flight

| Item | Branch / worktree | State → next action |
|---|---|---|
| **096b** | `task/ebilling-096b` @ `7c8c5fb7f3` (wt agent-a15eb2fe1fbf31eaa) | Review → fix (chat create blocked, subgrid New hidden, wizard on core-record hosts via `NATIVE_NEW_CLOSED` + template StringParameter) → re-check → follow-ups. **Waiting only on uac-r2's OK of the secure-child template change.** Then merge, remove the wt. D3 role change deferred (R1 confirm route, R2 Office quick-create task 096, R4 launcher display rule). Live: ribbon + web resource re-import (web resource first) in the dev deploy, then `-Verify`. |
| **165b-3** | `task/ebilling-165b3` (new wt) | **Running (Opus):** A = approval completion re-runs the checks, amount = line total, "complete" from the writer-created record; B = EB-D-208 (uac-r2 OK; condition: writer = createdby in the BFF writer allow-list, app-only, createdonbehalfby empty; user-created record test). Send uac-r2 the part A diff. Then one adversarial pass, merge, then 165b-2. |
| **082d** | `task/ebilling-082d` (new wt) | **Running (Opus):** 7.4 snapshot in findings JSON (internal part vs `ForReaders()`, uac-r2 binding design), DR summary, K6 PrintMembers, ratchet ArchTest, NFR-01, publish delta. Critical path: 082d -> 083 -> 130 -> 131-135. Then two adversarial passes + uac-r2 review. |
| **165d** | `task/ebilling-165d` @ `998c22b365` (wt agent-a087b7b20f22d544d) | Review: 3 F1 (create path never asks the column refusal; node maps the code to InternalError; doubled code in message), F3 pin bypass, K1 batch-side collection bind, TopicRegistry exempt reason wrong. **Fix round running (Sonnet).** Then a scoped re-check (create-path approach), uac-r2 diff + DEF-057 note, merge. U1 = option A (uac-r2): column-only check at the five person sites; added to the running fix round. uac-r2 gate query now contains-matches `changed_only_by_its_writer`. |

The 164b and 092 wts are removed (deregistered; empty folders still held open by a process, harmless). 165d is built (`998c22b365`): the adversarial pass is running, and uac-r2 was asked to decide U1 for the five person-identity writers (A: column-only check at those sites, recommended; B: accept them and rely on U5 field security). Tell uac-r2 to match the refusal code by prefix.

## Queued

- **164c** ribbon (billing event Add line hides) after 096b merges (both touch `sprk_billingevent` hides).
- **165e** running (uac-r2 confirmed + (a) fault hides, (b) own submission linked elsewhere hides, (c) re-count). Then **165f** `sprk_document` binding (uac-r2 assigned to us; two passes; uac-r2 reviews).
- **165b-3** part A next (approval completion; includes the origin check at completion), part B after uac-r2 on EB-D-208; then **165b-2** (totals + pending spend; ontology reviews FinanceBaseValues hunks). Serial (both edit InvoiceApprovalService). ontology filed the createdonbehalfby limit as DI-144 (#2632), 7 sites, one shared helper later; ours stays consistent.
- **Task 130** firm projection (EB-D-202) with uac-r2's pin test. Deps 083 (082 partial).
- **`InvoiceReview` `launch.ts` optional `lineId`** before the master PR.
- **Dev deploy batch** (after this wave merges): send uac-r2 the commit and checklist. Owner go needed for:
  - the BFF deploy;
  - 166 field-security apply plus forms;
  - `sprk_sourceemaildocument` apply;
  - the ribbons and web resource (096b);
  - the forms script (165c read-only links);
  - ONE owner question (uac-r2 bundle): a "BFF writer only" field-security profile (BFF app users write, default teams read; the sprk_createdbyperson pattern) on: submission `sprk_invoice`, `sprk_document`, `sprk_resubmissionof`; `sprk_invoice.sprk_billingexport`; approval portion `sprk_decidedby`, `sprk_decidedon`, `sprk_approvedamount`. Before the go: list every legitimate writer of each column; any person-identity writer moves to the BFF first or that column stays out.

  Also: re-count 0 invoices without a linked own submission, then the 096b D3 role change.

## Standing gates (unchanged)

- Creator-person `-Verify` exits 0 before any BFF deploy (dev args in `git show 7a9f45c424:projects/ebilling-r1/current-task.md`).
- Reconcile stays OFF until DEF-044 is on master AND L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0.
- No BFF with 095a anywhere before `sprk_sourceemaildocument` is field-secured there; it targets SpaarkeMaster 1.3.0.1.
- One master PR (reviewers ontology-r1, uac-r2, R4).

## Next action

On each notification:
- **2b-3 fix done** → scoped Sonnet re-check → merge.
- **165c pass 2** → fix if needed → merge after uac-r2.
- **165b / 165d done** → one adversarial pass each.
- **uac-r2 OK on 096b** → merge 096b → launch 164c.

Keep TASK-INDEX statuses current at each merge.
