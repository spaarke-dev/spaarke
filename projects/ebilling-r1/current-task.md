# Current Task State

> **Last Updated**: 2026-11-08 (checkpoint at 86% context, session ebilling-r1-c7, authoritative). Work head `adfb7aeec8`, pushed. Dev BFF: uac-r2's `6fbb3a7642` (master 23567b2f93 + fm 049c + MCP spike; reconcile false; gate PASS). **None of this project's work is on master or the dev BFF.**

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
| **165b-3** | ✅ merged `5ae5590b66`, tip verified (2174/0, 11236/0), wt removed | Re-check K1-K4 in notes 6.4. |
| **165b-4** | `task/ebilling-165b4` (Opus, from `fd628e75c5`) | Design approved: uac-r2 §7.12 (b); ontology-r1 §7.13 (b) + conditions (null-only coalesce, read-1 snapshot K1, dev before/after per parent before deleting `ReadSpendPartsAsync`, parity + 1-vs-50 count tests, writer id injected only when spend is read, live Global-depth dev test, rollup calls `ReadPendingSpendAsync` + unlinked count log). Precondition: null-findings linked own submissions == 0, else STOP → ontology-r1. Running. Next: one adversarial pass → ontology-r1 reviews FinanceBaseValues/Signals hunks → merge. Edits `InvoiceApprovalService.InvoicesWithApproveRecordAsync` body only (165b-2 is in `CheckApprovableAsync`). |
| **165e + 165f** | ✅ merged; guard follow-up merged `6ab63efbf7` (ArchTests 2181/0, unit 4976/0) | **Ship together; send uac-r2 the dev-batch sha** when the batch is cut. |
| **165b-2** | `task/ebilling-165b2` (Opus, from `c66b6933cf`) | Running. T1/T2 totals at approval and AP export, seeds, dev counts; pending spend out (165b-4). Next: one adversarial pass → merge. |
| **083** | `task/ebilling-083` @ `94c2969b60` (wt agent-abce3e9bf2270020c) | Built: `GetFirmClauseAsync(policyVersionId, invoiceId, ct)` reads own submission's `procedureSnapshot`, text only if version APPLIED; label firm-facing name / "Billing instructions for this record"; rule set Published or Retired (deviation); 3 seeds OK; ArchTests 2159/0, unit 6916/0; +1.7 KB. 135 criteria ~141-142 keep old wording (fix at merge). Pass 1: 3 F (F1 POML criteria 130/134/135 ask for "rule set name" = Procedure internal Name; F2 own-submission test order; F3 ListLogger drops exceptions) + K1-K7. **Fix round 1 (Sonnet) running** → pass 2 (Opus) → merge → 130. |
| **085** | `task/ebilling-085` (Sonnet, from `d58e3a3554`) | Stopped at escalation, lead answered (2026-11-08): may edit `InvoiceContextService.cs` (panel = versions `RulesOn` picks per distinct line date, else invoice date; one resolver read; fault → empty, no fallback) and `ApprovedRateHandler.cs` (`approvedRate.scope` → `approvedRate.rank`, invariant, no ids/names). Resumed. Next: one adversarial pass → merge → tell ontology-r1 of the evidence-key change. |

## Queued

- **165b-4** spend paths (after 165b-3): ontology-r1 O1-O8 in the POML; step 1 is a design note to ontology-r1 before code; reuse `WriterCreatedRecord` (with the key match).
- **083** firm clause (after 082d), then **130** firm projection (EB-D-202, uac-r2 pin test).
- **fm-032b:** fm's design note answered (E1-E6). Our 032b-2/032b-3 branch from work after 082d merges. uac-r2 condition for 032b-3: the job payload carries the committed core (table, id); gate 2 requires invoice core == payload core == submission core; must-fire re-point test. uac U3 may drop the grant step.
- **Dev deploy batch** (after this wave; owner go needed): the BFF deploy (uac-r2 deploys; creator-person `-Verify` first); 166 field-security apply + forms; `sprk_sourceemaildocument`; 096b web resource then ribbon + `-Verify`; the forms script (165c read-only links); the field-security bundle (submission `sprk_invoice`/`sprk_document`/`sprk_resubmissionof`/**`sprk_channel`** (165f pass 2: the channel still decides; #2372), `sprk_billingexport`, portion `sprk_decidedby`/`sprk_decidedon`/`sprk_approvedamount`; list every writer first); a private read-only count of Ontology Administrator holders to uac-r2; the D3 role change (R1, R2, R4, then the privilege) **plus the question whether user Create on `sprk_billingevent` is removed too**. **164c -Apply only after its source removal is on MASTER**: `pwsh -File scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply`, then `-Verify`, then send uac-r2 both read-backs (our -Verify with 11 hides stored; SecureChild -Verify on the other 8 tables) and tell them when the master merge lands.
- **Task 112** carries: replace the stale `sprk_createinvoicewizard` bundle in SpaarkeMaster source (checked by content).

## Owner questions

- **DEF-059 (#2635):** should environment setup grant the BFF identity Decision Record read through a SpaarkeMaster role? uac-r2 yes (least privilege), ontology-r1 no (live test enough; role change needs owner). Not blocking.

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
