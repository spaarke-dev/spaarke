# Current Task State

> **Last Updated**: 2026-11-08 (checkpoint at 86% context, session ebilling-r1-c7, authoritative). Work head `91871f6a3d` + this checkpoint, pushed. Dev BFF: uac-r2's `6fbb3a7642` (master 23567b2f93 + fm 049c + MCP spike; reconcile false; gate PASS). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project". At most 4 agents. Merge each finished branch after its reviews, verifying the combined tip (build, full ArchTests, filtered unit). One adversarial pass per change set; two for partner-readable/auth; a scoped Sonnet re-check when a fix changes the approach. FILTERED unit tests only. Every agent launch states a model. Never `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER: every brief names the base sha ("create the task branch from `origin/work/ebilling-r1` at <sha>") and checks `git merge-base --is-ancestor`. Never cd into agent worktrees (use `git -C`, absolute paths; jest via `node <wt>/node_modules/jest/bin/jest.js --config <wt>/jest.config.cjs --rootDir <wt>`). Remove merged worktrees with `git worktree unlock` + `scripts/worktree/Remove-WorktreeSafe.ps1`. |
| **Public-repo rule** | Never put the mechanism of an open access gap in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Merged into work (2026-11-07/08)** | Invoice Review optional `lineId` (`b7c8a3c58d`, EB-D-194); 164c (`931fe905b0`); 165d (`66b7c31315`); 165e (`7061729544`); 096b (`1a316a2ca0`); 165b item 1 (`b49465d972`); 165c (`1aff454e34`); 095d-2b-3 (`c2239d9d58`); 165a + 095d-2b-2 (`2fac72b8a5`); 164b (`01854564a3`). |
| **Decisions** | EB-D-206 (amended), EB-D-207, EB-D-208 (counted statuses need the writer-created approve record). |

## Agents / branches in flight

| Item | Branch / worktree | State → next action |
|---|---|---|
| **165b-3** | `task/ebilling-165b3` @ `b81098c4b6` (wt agent-a2dcae0b9ac285d6e) | Part A + its fix approved by uac-r2. Part B built (`WriterCreatedRecord` in Services/Signals + single-source guard; approval and AP export need the record). uac-r2 approved B except **F1 key match** (batched `InvoicesWithApproveRecordAsync` must match `BillingDecisionKey.IdOf(sprk_invoice, X, DecisionIdFor(portion))` via `sprk_steps`). Opus pass: currency allow-list falsely refuses (fix: `BillingCurrency.Normalise`, refuse only >2-decimal ISO codes, same check before the claim); "already in target" must read the record; guard baseline citations + patterns; Mark Paid needs the record check; FR-30 test, doc comment, chunk 200. **Fix round (Opus) running.** Next: scoped Sonnet re-check → send uac-r2 the fix diff (they re-check F1 only) → merge + verify tip → launch **165b-2** and the **165b-4 design note** (to ontology-r1 before code). |
| **165f** | `task/ebilling-165f` @ `2cb3b5f186` (wt agent-a5862c01de1af9249) | Built + fix round 1 (pass 1: no access gap; two no-firm tests fixed; guard parentheses; stray/>50 tests). **uac-r2 APPROVED (conditional on pass 2 having no F).** **Pass 2 (Opus, fresh) running.** Next: if no F → merge + verify tip → tell uac-r2 the sha when 165e/f go into the dev batch (their gate watches `own_submission_missing`, `document_link_not_own`). **165e and 165f ship to dev/master in the same batch.** Known limit: the existence read is not in the per-request cost log. |
| **082d** | `task/ebilling-082d` @ `6d0f00ec3d` (wt agent-a5648fd3189b27f3d) | Two passes done. Fix round 2 `8c93c425ca` (snapshot written only if stored deep-equals Build(before) and no decided line differs, else null; absent stays absent; DR facts {version, submissionId, member, gaps}; over-cap drops the member then refuses) + `6d0f00ec3d` (re-run writer drops the member before refusing). Re-check clean (no F). **uac-r2 APPROVED** (follow-up needs no re-review unless it changes what is stored/projected). **K follow-ups (Sonnet) running**: real-evaluator round-trip test, add-line too_large wording, enum order. Next: merge + verify tip → send field-mapping the merge sha → launch **083** (then 130). |

## Queued

- **165b-2** totals at approval and AP export (after 165b-3).
- **165b-4** spend paths (after 165b-3): ontology-r1 O1-O8 in the POML; step 1 is a design note to ontology-r1 before code; reuse `WriterCreatedRecord` (with the key match).
- **083** firm clause (after 082d), then **130** firm projection (EB-D-202, uac-r2 pin test).
- **fm-032b:** fm's design note answered (E1-E6). Our 032b-2/032b-3 branch from work after 082d merges. uac-r2 condition for 032b-3: the job payload carries the committed core (table, id); gate 2 requires invoice core == payload core == submission core; must-fire re-point test. uac U3 may drop the grant step.
- **Dev deploy batch** (after this wave; owner go needed): the BFF deploy (uac-r2 deploys; creator-person `-Verify` first); 166 field-security apply + forms; `sprk_sourceemaildocument`; 096b web resource then ribbon + `-Verify`; the forms script (165c read-only links); the field-security bundle (submission `sprk_invoice`/`sprk_document`/`sprk_resubmissionof`, `sprk_billingexport`, portion `sprk_decidedby`/`sprk_decidedon`/`sprk_approvedamount`; list every writer first); a private read-only count of Ontology Administrator holders to uac-r2; the D3 role change (R1, R2, R4, then the privilege) **plus the question whether user Create on `sprk_billingevent` is removed too**. **164c -Apply only after its source removal is on MASTER**: `pwsh -File scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply`, then `-Verify`, then send uac-r2 both read-backs (our -Verify with 11 hides stored; SecureChild -Verify on the other 8 tables) and tell them when the master merge lands.
- **Task 112** carries: replace the stale `sprk_createinvoicewizard` bundle in SpaarkeMaster source (checked by content).

## Open with peers

- **uac-r2:** 165b-3 part B fix diff (F1 only); 082d review; 165f pass-2 result. DEF-057 (#2631) on their backlog.
- **ontology-r1:** 165b-4 design note (before code); DI-144 (#2632) adopts `WriterCreatedRecord`; DEF-058 (#2634, filed with ebilling-r1 label; hand off when they accept): only the billing recorder writes billing action codes.
- **field-mapping:** owed the 082d merge sha; payload shape (committed core) sent.

## Standing gates (unchanged)

- Creator-person `-Verify` exits 0 before any BFF deploy (dev args in `git show 7a9f45c424:projects/ebilling-r1/current-task.md`).
- Reconcile stays OFF until DEF-044 is on master AND L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0.
- No BFF with 095a anywhere before `sprk_sourceemaildocument` is field-secured there; it targets SpaarkeMaster 1.3.0.1.
- One master PR (reviewers ontology-r1, uac-r2, R4).

## Next action

On each notification: act per the "next" column above. Free slot → 165b-2 / 165b-4 design note after 165b-3 merges; 083 after 082d merges. Keep TASK-INDEX rows current at each merge (165b-3, 165f, 082 rows).
