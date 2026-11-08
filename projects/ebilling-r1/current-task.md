# Current Task State

> **Last Updated**: 2026-11-07 late (checkpoint, session ebilling-r1-c7, authoritative). Work head `59b5f756b3`, pushed. Dev BFF: uac-r2's `22487c6537` (master + 049b + MCP spike, 20:06Z). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project" (and earlier "proceed, parallel where possible"). At most 4 agents. Merge each finished branch after its reviews. One adversarial pass per change set; two for partner-readable/auth; a scoped Sonnet re-check when a fix changes the approach. FILTERED tests only. Every agent launch states a model. Never `--no-verify`. |
| **Public-repo rule** | Never put the mechanism of an open access gap in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Merged into work today** | 095d-2b-3 (`c2239d9d58`); 165a + 095d-2b-2 (`2fac72b8a5`; #2571 closed; ontology OK'd the SignalWriter doc diff); 164b Add line client (`01854564a3`). |
| **Decisions today** | EB-D-206 (2b-3: the re-run writes the supersedes link in its commit; AMENDED: the predecessor's Return must predate the invoice, plus a predecessor lock); EB-D-207 (fm-032 allow-list + secure/No Access gate, uac-r2 agreed; partners have no invoice search; task 132 renders only ReadableColumns; `sprk_billingexport` writer-only → 165d). |

## Agents / branches in flight

| Item | Branch / worktree | State → next action |
|---|---|---|
| **096b** | `task/ebilling-096b` @ `7c8c5fb7f3` (wt agent-a15eb2fe1fbf31eaa) | Review → fix (chat create blocked, subgrid New hidden, wizard on core-record hosts via `NATIVE_NEW_CLOSED` + template StringParameter) → re-check → follow-ups. **Waiting only on uac-r2's OK of the secure-child template change.** Then merge, remove the wt. D3 role change deferred (R1 confirm route, R2 Office quick-create task 096, R4 launcher display rule). Live: ribbon + web resource re-import (web resource first) in the dev deploy, then `-Verify`. |
| **165c** | `task/ebilling-165c` @ `13a10bcbfc` (wt agent-aa5057f557106a2d1) | Pass 1 → fix round done (one rule `IsOwnSubmission` at all sites + ArchTest guard; form links read-only + pin). **Pass 2 (Opus) running.** Open with uac-r2 (private): fold in the pre-existing gap close (hide an invoice with no linked own submission) and a `sprk_document` binding task (165e?) and field security on the submission link columns. Then merge. |
| **165b** | `task/ebilling-165b` @ `f0017580b7` (wt agent-a8494c782d7ce1f7b) | Item 1 (line of unknown origin) built; **adversarial pass (Opus) running**, then merge. Items 3-4 → task 165b-2; item 2 + a completion-path fix (private, sent to uac-r2) → task 165b-3. Proposed EB-D-208 (counted statuses need the writer-created approve record; Paid has no record of its own in R1) awaits uac-r2. No ontology hunks yet. |
| **165d** | `task/ebilling-165d` @ `998c22b365` (wt agent-a087b7b20f22d544d) | Built (10207/0, Arch 2129/0, +1 KB). **Adversarial pass (Opus) running.** U1 question sent to uac-r2: column-only check at the five person-identity writers (A, recommended) or accept + U5. Refusal code `record_update.changed_only_by_its_writer.column` (prefix match). |

The 164b and 092 wts are removed (deregistered; empty folders still held open by a process, harmless). 165d is built (`998c22b365`): the adversarial pass is running, and uac-r2 was asked to decide U1 for the five person-identity writers (A: column-only check at those sites, recommended; B: accept them and rely on U5 field security). Tell uac-r2 to match the refusal code by prefix.

## Queued

- **164c** ribbon (billing event Add line hides) after 096b merges (both touch `sprk_billingevent` hides).
- **165b-2** (totals + pending spend; ontology reviews FinanceBaseValues hunks) then **165b-3** (completion fix; part B after uac-r2 on EB-D-208), serial after 165b merges.
- **Task 130** firm projection (EB-D-202) with uac-r2's pin test. Deps 083 (082 partial).
- **`InvoiceReview` `launch.ts` optional `lineId`** before the master PR.
- **Dev deploy batch** (after this wave merges): send uac-r2 the commit and checklist. Owner go needed for:
  - the BFF deploy;
  - 166 field-security apply plus forms;
  - `sprk_sourceemaildocument` apply;
  - the ribbons and web resource (096b);
  - the forms script (165c read-only links);
  - optional field security on `sprk_billingexport` (165d U5) and on the submission link columns.

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
