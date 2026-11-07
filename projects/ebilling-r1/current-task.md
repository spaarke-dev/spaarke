# Current Task State

> **Last Updated**: 2026-11-07 ~20:15Z (checkpoint, session ebilling-r1-c7, authoritative). Work head `e418803715`, pushed. Dev BFF: **master `cc11294300`** (uac-r2 deploy 14:22Z; no ebilling code; `StoredFileReconcileEnabled=false`). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "proceed, parallel where possible". At most 4 agents. Merge each finished branch into work after its reviews. One adversarial pass per change set; two for auth/tenant/partner-readable; a scoped re-check (Sonnet adversarial-reviewer) when a fix changes the approach. FILTERED tests only. Every agent launch states a model. Never `--no-verify`. |
| **Public-repo rule (uac-r2)** | Identifiers are fine. Keep OUT of notes, comments, commits, PRs, issues: the mechanism of an access gap that is still open. 165 classification and evidence stay in `scratchpad\165a-inventory-private.md` until live. Owner accepted the existing history (no rewrite). |
| **Merged into work** | 166 (`ab95fe67d6`); 164a Add line (`eeac33b1c2` + fix-up `682dc4a661`). |
| **Done on dev 2026-11-07** | 096b scope 4 cleanup (owner go): 2 documents via BFF delete, 1 line, 1 communication, 11 invoices deleted at 20:03Z. Dev now has 6 invoices, all derived, 0 non-derived (the 165c baseline). uac-r2 told before and after. Record: `notes/096b-dev-cleanup.md`. |
| **Owner decisions 2026-11-06/07** | EB-D-202 (partners see added lines and reasons while In Review; task 130 amended), EB-D-203 (SPA Return of Blocked needs no privilege), EB-D-204 (re-run pending until `assignFirmRerun.state = done`), EB-D-205 (added lines count toward spend). DEF-056 (#2614). |
| **Owner go still needed (live dev)** | (1) 166 apply (`Set-EbillingInvoiceMoneyFieldSecurity.ps1`, then -Verify) with the forms redeploy. (2) `sprk_sourceemaildocument` apply. (3) 096b role -WhatIf → apply (after D1–D3). (4) 164a live check that a version-only invoice update is refused. Notify uac-r2 before and after each. |

## Agents running (launched ~20:10Z)

| Item | State |
|---|---|
| **Merge 165a + 2b-2** (Opus implementer, own worktree, branch `merge/ebilling-165a-2b2` from `e418803715`) | Merges `task/ebilling-165a` (`1864e1ed69`) and then `task/ebilling-095d2b2` (`617c740c8e`). Consolidates 164a's own-submission read onto 165a's `InvoiceBlockedHold` helper, adds Add line EB-D-204 gate tests (`committed` refused / `done` adds), and marks DEF-053 resolved in defer-issues. Pushes the branch only. **On return:** check the gate numbers, fast-forward work, push, close #2571 (`gh`), send ontology the `SignalWriter.cs` doc-comment diff, remove the 165a/2b-2 agent worktrees (Remove-WorktreeSafe), then send uac-r2 the dev-deploy batch commit and checklist. |
| **096b adversarial review** (Opus, read-only) | Reviews `405cdaebb2` (worktree agent-a15eb2fe1fbf31eaa): the closed route, client list, ribbon JS, RibbonDiffs, removed FR-05 tests, and the R1–R6 / D1–D3 reasoning. **On return:** fix round if F-class; send uac-r2 the route diff and app-only proof; settle D1–D3 (owner if needed); merge. |
| **field-mapping 032 design review** (Opus, read-only) | Recommended answers to fm Q1–Q6 and the must-protect `sprk_invoice` columns. **On return:** reply to field-mapping by message. |

## Queued after the merges

- **165c**: the submission-link agreement in `ExternalInvoiceVisibility` (POML written). After 165a. Two adversarial passes; uac-r2 reviews. Dev baseline 0 mismatches.
- **095d-2b-3** (POML not written): on the post-Set-firm re-run, write the invoice supersedes link. Recommendation: in the commit.
- **165b**: origin, status-record, totals and pending-spend checks. Update its criterion text to `AddedLineIdFor(invoiceId, decisionId)`.
- **Task 130**: firm projection (EB-D-202), including uac-r2's pin test.
- **`InvoiceReview` `launch.ts` optional `lineId`**: before the master PR.
- **Customer-provisioning**: after 166 reaches master, send the SHA and profile grants. The 095a lock targets SpaarkeMaster 1.3.0.1.

## Standing gates (unchanged)

- Creator-person `-Verify` exits 0 before any BFF deploy.
- Reconcile stays OFF until DEF-044 is on master AND L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0.
- No BFF with 095a anywhere before `sprk_sourceemaildocument` is field-secured there.
- One master PR (reviewers ontology-r1, uac-r2, R4) once 095d-2 and Add line are in. Pre-checks are in `git show 7a9f45c424:projects/ebilling-r1/current-task.md`.

## Next action

On each agent notification, act per its "On return" cell above. Then launch 165c, write and launch 2b-3, then 165b.
