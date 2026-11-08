# Current Task State

> **Last Updated**: 2026-11-07 late (checkpoint, session ebilling-r1-c7, authoritative). Work head `59b5f756b3`, pushed. Dev BFF: uac-r2's `22487c6537` (master + 049b + MCP spike, 20:06Z). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project" (and earlier "proceed, parallel where possible"). At most 4 agents. Merge each finished branch after its reviews. One adversarial pass per change set; two for partner-readable/auth; a scoped Sonnet re-check when a fix changes the approach. FILTERED tests only. Every agent launch states a model. Never `--no-verify`. |
| **Public-repo rule** | Never put the mechanism of an open access gap in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Merged into work today** | 165a + 095d-2b-2 (`2fac72b8a5`; #2571 closed; ontology OK'd the SignalWriter doc diff); 164b Add line client (`01854564a3`). |
| **Decisions today** | EB-D-206 (2b-3: the re-run writes the supersedes link in its commit; AMENDED: the predecessor's Return must predate the invoice, plus a predecessor lock); EB-D-207 (fm-032 allow-list + secure/No Access gate, uac-r2 agreed; partners have no invoice search; task 132 renders only ReadableColumns; `sprk_billingexport` writer-only → 165d). |

## Agents / branches in flight

| Item | Branch / worktree | State → next action |
|---|---|---|
| **096b** | `task/ebilling-096b` @ `7c8c5fb7f3` (wt agent-a15eb2fe1fbf31eaa) | Review → fix (chat create blocked, subgrid New hidden, wizard on core-record hosts via `NATIVE_NEW_CLOSED` + template StringParameter) → re-check → follow-ups. **Waiting only on uac-r2's OK of the secure-child template change.** Then merge, remove the wt. D3 role change deferred (R1 confirm route, R2 Office quick-create task 096, R4 launcher display rule). Live: ribbon + web resource re-import (web resource first) in the dev deploy, then `-Verify`. |
| **165c** | `task/ebilling-165c` @ `13a10bcbfc` (wt agent-aa5057f557106a2d1) | Pass 1 → fix round done (one rule `IsOwnSubmission` at all sites + ArchTest guard; form links read-only + pin). **Pass 2 (Opus) running.** Open with uac-r2 (private): fold in the pre-existing gap close (hide an invoice with no linked own submission) and a `sprk_document` binding task (165e?) and field security on the submission link columns. Then merge. |
| **095d-2b-3** | `task/ebilling-095d2b3` @ `57bf9bb156` (wt agent-adae31b11a091fecf) | Review → **fix round running** (narrowing (c) Return-before-createdon from the Return Decision Record; predecessor lock + re-check; SqlGuid tie test). It may STOP if no reliable Return timestamp exists. Then a scoped re-check, then merge. |
| **165b** | `task/ebilling-165b` (new wt) | Running (Opus): (1) unknown-origin line refusal, (2) status needs its server-written record (STOP if live data would be refused), (3) totals check, (4) pending spend excludes held invoices. ontology reviews its `FinanceBaseValues` hunks. Then one adversarial pass. |
| **165d** | `task/ebilling-165d` (new wt) | Running (Sonnet): `WriterOnlyColumns` + `RefuseAppOnlyWrite` hook (every identity) + `MappingProtectedColumns` + U3 coverage pin + U4 AP export pin. Then one pass, uac-r2 diff review. |

The 164b wt (agent-a4e878be64ad99f30) is merged but locked by an old agent pid. Remove it later with `scripts/worktree/Remove-WorktreeSafe.ps1`.

## Queued

- **164c** ribbon (billing event Add line hides) after 096b merges (both touch `sprk_billingevent` hides).
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
