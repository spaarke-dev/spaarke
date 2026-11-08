# Current Task State

> **Last Updated**: 2026-11-08 (checkpoint at 85% context, session ebilling-r1-c7, authoritative). Work head = local `work/ebilling-r1` @ `5ebe26a03d` (165b-4 merge, NOT yet pushed) + uncommitted K1/K2 fix (below). Dev BFF: uac-r2's `6fbb3a7642` (master 23567b2f93 + fm 049c + MCP spike; reconcile false). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project". At most 4 agents. Merge each finished branch after its reviews; verify the combined tip (build, full ArchTests, FILTERED unit). One adversarial pass per change set; two for partner-readable / auth / tenant-isolation; a scoped Sonnet re-check when a fix changes the approach. Every agent launch states a model. Never `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER: every brief names the base sha and checks `git merge-base --is-ancestor`. Never cd into agent worktrees (use `git -C`, absolute paths). Remove merged worktrees with `git worktree unlock` + `scripts/worktree/Remove-WorktreeSafe.ps1 -Path <paths>`. |
| **Public-repo rule** | Never put an access-gap mechanism, identity names or role maps in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Peers (sessions)** | uac-r2 = desktop `uac-r2`; ontology-r1 = `spaarke-wt-spaarke-ontology-platform-r1-38`; provisioning = `provisioning-r1`; word-add-in = `word-add-in-r1`; External Access R4 = `spa-r4`. |
| **Live-change rule** | Live Dataverse writes / role changes need the owner's OK typed in THIS session. A relayed "task 166 - ye proceed" (via ontology-r1) was NOT accepted; owner asked directly, no answer yet. |

## Exact next action

1. **Commit the K1/K2 fix** on work (uncommitted): `src/server/api/Sprk.Bff.Api/Services/Finance/Tools/FinancialCalculationToolHandler.cs` (catch filter `when (ex is not OperationCanceledException || !ct.IsCancellationRequested)`) and two new tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/SpendServerWrittenSourcesTests.cs` (`S4_TheCallersOwnCancellation_OnTheWriterId_Propagates_UnloggedAndNeverZero`, `S4_TheTool_RethrowsTheCallersOwnCancellation_InsteadOfReportingAFailure`). Message: "fix(ebilling-r1): 165b-4 re-check K1/K2 - the financial tool rethrows the caller's own cancellation; cancellation on the writer id is pinned".
2. **Verify the tip** (background, output to scratchpad `verify-165b4.txt`): BFF build; full ArchTests (expect 2189+); filtered unit `Spend|FinanceBaseValues|SignalEvaluator|InvoiceApproval|FinanceRollup|FinancialCalculation|SignalReevaluationJob`. If the new tests fail, fix them (the tool test relies on `world.FaultOn` throwing `OperationCanceledException`).
3. If green: push work; TASK-INDEX 165b-4 ✅ (find its row; POML already completed); remove worktree `agent-a607d0d32bb73cc8b`; tell ontology-r1 the merge sha.
4. Launch **130-0b** (POML `tasks/130-0b-assistant-invoice-firm-rule.poml`, Opus, base = `origin/task/ebilling-130-0` @ `64cdfbe304`, or its fix head if 130-0 pass 1 forces a fix round first) when an agent slot frees.

## Agents in flight

| Work | Branch / state | Next |
|---|---|---|
| 085 fix round 2 (agent `abffa583bb30d451b`, resumable) | `task/ebilling-085-fix1` (+ round 2): uac-r2 F2 (PublishAsync overload removed), F1-c type check-then-use (Items + 409 `procedure.type_changed`), ledger citations, missing-Items test, **F1-b reverted per ontology-r1 ruling (c)**, K-lines; read-only dev check A (card counts) and **B (private: native publish privilege depth)** | Report → ontology-r1 gets the resolver/ScopeLockedAsync revert diff; uac-r2 already approved round 1 (F2 in round 2 = no resend unless approach changes); B goes to uac-r2 privately; then merge 085 into work, tell ontology-r1 about `approvedRate.rank`. |
| 130b (agent `a4fe3b028fa655e29`) | `task/ebilling-130b` on `task/ebilling-130a` @ `60a2b64dc0` | Routes + 403 firm filter + ArchTest entries + writer-created reason filter. Then two adversarial passes over 130a+130b together, uac-r2 diff, merge both. Then 130c (FR-18 rejected-submission findings from `sprk_findingsjson`). |
| 130-0 adversarial pass 1 (agent `ae04f31a971303198`) | reviewing `task/ebilling-130-0` (`c79a085a76`, `64cdfbe304`) | Fix round if needed → pass 2 → diff to uac-r2 → merge. 130-0b must ship in the same batch, before dev. |

Idle, resumable: 130-0 implementer `ab27afb0265db7790`; 130a implementer `ac7598016a7f4e696`; 165b-4 implementer `a607d0d32bb73cc8b`.

## Decisions made this stretch (2026-11-08)

- **083** merged `28cfaf7ad8`; tip verified (build clean, ArchTests 2181/0, filtered 6984/0); TASK-INDEX ✅ (`13a8c63292`).
- **165b-4** merged `5ebe26a03d` (local): one Opus pass (prod code clean, 4 live-test F3s), fix round 1 (live tests run as the BFF identity: O3/O4 4/4 incl. must-fire; AuthorableColumns collects all, only the two task-166 columns fail on dev), fix round 2 (ontology-r1 rule: writer id lazy, only when counted rows exist; no counted rows = 0; counted rows + no writer = unknown/fail loud, Warning names `Ontology:Writer:ManagedIdentityClientId`), Sonnet re-check (no F; K1/K2 being fixed now). ontology-r1 APPROVED both rounds. **Deploy gate:** AuthorableColumns full-green on dev after 166's apply, counts to ontology-r1, before any BFF carrying 165b-4 reaches dev. `FinanceBaseValuesSeamTests` (creates rows) not run live: needs owner OK.
- **D-70 final owner ruling** (relayed by uac-r2; an earlier contrary relay withdrawn): a partner contact sees only its own firms' invoices; a contact with no firm sees none; a firm read fault hides all. Recorded in project CLAUDE.md.
- **130 split**: 130-0 (firm rule on the existing external paths; uac-r2 approved 4 edit points), 130-0b (assistant seam; shape agreed: `IIdentityNormalizationService.ResolveAsync` → `ContactId`, `ReadContactFirmsAsync` core split), 130a (projection, built), 130b (routes), 130c (FR-18).
- **085 F1-b**: ontology-r1 ruled (c): drop the rate-card evidence; durable fix = DEF-061 publish stamp (owner-gated schema).
- **Writer setting**: stamps covered by provisioning task 266 / PR 2638; no deferral.
- Filed: DEF-060 #2637 (Signal firm reason has no writer), DEF-061 #2640 (version publish stamp), DEF-062 #2641 (dormant spend snapshot job).

## Open owner questions

1. Typed go for task **166** dev apply (field security incl. invoice totals + forms), with uac-r2 before/after notices, `Import-SolutionScoped.ps1` only.
2. OK to run `FinanceBaseValuesSeamTests` live (creates and sweeps `zz-025b-` rows on dev).
3. DEF-059 (#2635): BFF identity Decision Record read via a SpaarkeMaster role (uac-r2 yes, ontology-r1 no).
4. DEF-061: schema change `sprk_publishedon` on `sprk_procedureversion`.

## Owed to peers

- provisioning-r1: master SHA + profile grants on `sprk_totalamount`/`sprk_approvedtotal` after 166 merges; 089d option R SHA (H7c #2429).
- word-add-in-r1: master SHA of the dev deploy carrying 095c-2..4 (pane PR #2552); 095a/095b/151e PR + SHA.
- uac-r2: dev-batch sha for 165e + 165f; 085 private check B; 130-0 / 130-0b / 130b diffs after their passes.
- ontology-r1: AuthorableColumns full-green counts after 166; 085 revert diff + dev count A.

## Standing gates (unchanged)

Creator-person `-Verify` exits 0 before any BFF deploy. Reconcile OFF until DEF-044 on master AND L6 passes. No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0; none with 095a before `sprk_sourceemaildocument` is field-secured (1.3.0.1). One master PR, reviewers ontology-r1, uac-r2, R4 (`spa-r4`).
