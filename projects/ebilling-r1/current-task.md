# Current Task State

> **Last Updated**: 2026-11-08 (checkpoint at 85% context, session ebilling-r1-c7, authoritative). Work head `4f52907883`, pushed. Dev BFF: uac-r2's `6fbb3a7642` (master 23567b2f93 + fm 049c + MCP spike, 04:00:55Z; reconcile false; gate PASS). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project" (earlier "proceed, parallel where possible"). At most 4 agents. Merge each finished branch after its reviews. One adversarial pass per change set; two for partner-readable/auth; a scoped Sonnet re-check when a fix changes the approach. FILTERED tests only. Every agent launch states a model. Never `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER. Every brief names the base sha and says "create the task branch from `origin/work/ebilling-r1` at <sha>" and checks `git merge-base --is-ancestor`. |
| **Public-repo rule** | Never put the mechanism of an open access gap in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Merged into work (2026-11-07/08)** | 096b (`1a316a2ca0`; 098 guard listing for `IsInvoiceCreate` on work `e14f95181c`); 165b item 1 (`b49465d972`); 165c (`1aff454e34`); 095d-2b-3 (`c2239d9d58`); 165a + 095d-2b-2 (`2fac72b8a5`); 164b (`01854564a3`). Each verified on the combined tip (build, full ArchTests, filtered unit). |
| **Decisions** | EB-D-206 (amended), EB-D-207, **EB-D-208** (counted statuses need the writer-created approve record; writer = createdby in the BFF writer allow-list, app-only, createdonbehalfby empty; Paid has no own record in R1). |

## Agents / branches in flight

| Item | Branch / worktree | State → next action |
|---|---|---|
| **165b-3** | `task/ebilling-165b3` @ `276c1201af` (wt agent-a2dcae0b9ac285d6e) | Part A built; uac-r2 APPROVED A (conditional on our pass). Opus pass: F1 (In Review + portion says Approved → "alreadyApproved"), F2 (decided-on from a user column), F3 adjustments control, K3/K4 guards. **Fix round running (Sonnet).** Then scoped re-check if approach changes → send uac-r2 the fix diff → **part B on the same branch**: B1 approval + move `AddWriterCreatedConditions` to `Services/Signals/WriterCreatedRecord` (ontology O2) holding the writer allow-list; B2 AP export (new code e.g. `invoice_approval_not_recorded`, batched `In` read); + uac-r2 notes (non-2-decimal currency fails closed test; client shows 503 as "try again"). One pass on part B, uac-r2 diff, merge. |
| **165d** | `task/ebilling-165d` @ `cac4e6e25e` (wt agent-a087b7b20f22d544d) | Fix round done (create path both branches, node mapping, message, pin normalisation, option A at the person sites, Exempt = push only, K1: no generic batch-row writer). Full ArchTests 2142/0. **Scoped Sonnet re-check running** (filtered counts, 2 seeds, OfficeService.cs:2431 quick create, merge-tree vs work). Then send uac-r2 the diff, merge. |
| **165e** | `task/ebilling-165e` @ `76d751eb5f` (wt agent-a88cf50c0d1bce1fc) | Built: reader 2 hides `own_submission_missing`, reader 1 withholds; 18 new tests, 59 fixtures moved to `InvoiceWorld.OwnedInvoice()`; dev re-count 0. **Adversarial pass 1 (Opus) running.** Then fix, pass 2, uac-r2 diff, merge, then **165f**. POML status / index not yet updated. |
| **082d** | `task/ebilling-082d` @ `eb42e5c8db` (wt agent-a5648fd3189b27f3d) | Built from `ee0b173288`: `procedureSnapshot` v1 internal member in findings JSON, DR facts summary, ratchet (baseline 3 ontology files), golden "101 B2a" appended; unit 7701/0, Arch 2143/0, NFR-01 83 ms, publish +6.9 KB (41.5 MB). **Pass 1 (Opus) running.** Critical path: 082d → 083 → 130 → 131-135. Then two passes + uac-r2 review. On merge: send field-mapping the sha (fm-032b branches from it). |

## Queued

- **164c** ribbon (billing event Add line hides): unblocked; launch when a slot frees.
- **165f** `sprk_document` binding (after 165e; two passes; uac-r2 reviews).
- **165b-2** totals at approval and AP export (after 165b-3).
- **165b-4** spend paths (after 165b-3): ontology-r1 conditions O1-O8 in the POML; step 1 is a design note to ontology-r1 BEFORE code.
- **083** firm clause (after 082d), then **130** firm projection (EB-D-202, uac-r2 pin test).
- **fm-032 option B:** field-mapping writes the 032b design note (after-step: grant then columns; no ebilling reader of invoice copies; Secure maps nothing) to us + uac-r2; implementation branches from work after 082d merges.
- **`InvoiceReview` `launch.ts` optional `lineId`** before the master PR.
- **Dev deploy batch** (after this wave): send uac-r2 the commit and checklist. Owner go needed for: the BFF deploy; 166 field-security apply + forms; `sprk_sourceemaildocument` apply; 096b web resource then ribbon re-import + `-Verify`; the forms script (165c read-only links); ONE field-security question (uac-r2 bundle: a "BFF writer only" profile on submission `sprk_invoice`/`sprk_document`/`sprk_resubmissionof`, `sprk_invoice.sprk_billingexport`, approval portion `sprk_decidedby`/`sprk_decidedon`/`sprk_approvedamount`; list every legitimate writer per column first). Also: re-count invoices without a linked own submission (0 on 2026-11-08), a read-only count of Ontology Administrator holders (send uac-r2, private), then the 096b D3 role change (R1, R2, R4, then the privilege).

## Open with peers

- **uac-r2:** part A fix diff and part B diffs (165b-3); 165d diff; 165e/165f reviews. DEF-057 (#2631) is on their backlog.
- **ontology-r1:** 165b-4 design note (before code). DI-144 (#2632) adopts our `WriterCreatedRecord`.
- **field-mapping:** 032b design note answered 2026-11-08 (E1 job, E2 yes, E3 fm→master first, E4 accepted with the admin-attention writer and no status write, E5 yes, E6 signed off with the condition that neither column joins external ReadableColumns or firm rendering). Still owed: the 082d merge sha.

## Standing gates (unchanged)

- Creator-person `-Verify` exits 0 before any BFF deploy (dev args in `git show 7a9f45c424:projects/ebilling-r1/current-task.md`).
- Reconcile stays OFF until DEF-044 is on master AND L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0.
- No BFF with 095a anywhere before `sprk_sourceemaildocument` is field-secured there; it targets SpaarkeMaster 1.3.0.1.
- One master PR (reviewers ontology-r1, uac-r2, R4).

## Next action

On each notification:
- **165d re-check** clean → send uac-r2 the diff → merge (verify the combined tip: build, full ArchTests, filtered unit) → remove the wt.
- **165b-3 fix round** → (re-check if approach changed) → uac-r2 fix diff → launch part B (Opus) on the same branch.
- **165e pass 1** → fix round → pass 2 → uac-r2 → merge → launch 165f.
- **082d done** → pass 1 → fix → pass 2 → uac-r2 → merge → field-mapping sha → launch 083.
- Free slot → 164c.

Keep TASK-INDEX statuses current at each merge (165e row still says running; 165d row says review running).
