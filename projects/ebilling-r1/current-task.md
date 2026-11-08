# Current Task State

> **Last Updated**: 2026-11-08 (checkpoint at 85% context, session ebilling-r1-c7, authoritative). Work head `4f52907883`, pushed. Dev BFF: uac-r2's `6fbb3a7642` (master 23567b2f93 + fm 049c + MCP spike, 04:00:55Z; reconcile false; gate PASS). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project" (earlier "proceed, parallel where possible"). At most 4 agents. Merge each finished branch after its reviews. One adversarial pass per change set; two for partner-readable/auth; a scoped Sonnet re-check when a fix changes the approach. FILTERED tests only. Every agent launch states a model. Never `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER. Every brief names the base sha and says "create the task branch from `origin/work/ebilling-r1` at <sha>" and checks `git merge-base --is-ancestor`. |
| **Public-repo rule** | Never put the mechanism of an open access gap in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Merged into work (2026-11-07/08)** | 164c (`931fe905b0`; -Apply after master + uac-r2 read-backs (5→11 hides in ribbondiffs; SecureChild -Verify on the other 8 tables) + owner go); 165d (`66b7c31315`) and 165e (`7061729544`), verified together at `a3d1dfebad` (ArchTests 2142/0, filtered unit 14674/0); 096b (`1a316a2ca0`; 098 guard listing for `IsInvoiceCreate` on work `e14f95181c`); 165b item 1 (`b49465d972`); 165c (`1aff454e34`); 095d-2b-3 (`c2239d9d58`); 165a + 095d-2b-2 (`2fac72b8a5`); 164b (`01854564a3`). Each verified on the combined tip (build, full ArchTests, filtered unit). |
| **Decisions** | EB-D-206 (amended), EB-D-207, **EB-D-208** (counted statuses need the writer-created approve record; writer = createdby in the BFF writer allow-list, app-only, createdonbehalfby empty; Paid has no own record in R1). |

## Agents / branches in flight

| Item | Branch / worktree | State → next action |
|---|---|---|
| **165b-3** | `task/ebilling-165b3` @ `b81098c4b6` (wt agent-a2dcae0b9ac285d6e) | Part A + fix approved by uac-r2. **Part B built** `b81098c4b6`: `Services/Signals/WriterCreatedRecord` (static; Filter/Link/FetchXML forms; Guid.Empty throws) + single-source guard (baseline = ontology's 6 DI-144 sites); approval state needs the record; decidedon ≥ invoice createdon; currency allow-list USD/EUR/GBP/CAD (**under review: precision-based may be right**); AP export option 1 (candidates omit; 400 per invoice; 409 re-check), batched; unit 11107/0, Arch 2137/0; dev 2/2 have writer records; +0.28 MB vs master. **Opus pass running.** uac-r2: approved except **F1** — the batched `InvoicesWithApproveRecordAsync` must match the decision key like completion does (select `sprk_steps`; `ReviewIdOf(row) == BillingDecisionKey.IdOf(sprk_invoice, X, DecisionIdFor(portion of X))`; ≤2 reads per batch; must-fire: writer row with another key not counted). Option (b) (writer refuses billing codes outside the recorder) → file for ontology-r1. Currency allow-list acceptable to uac-r2. Then one fix round (pass + uac F1), uac-r2 re-checks the fix diff, merge, then 165b-2 and the 165b-4 design note. |
| **165f** | `task/ebilling-165f` @ `a9cb5bbd6d` (wt agent-a5862c01de1af9249) | Built: readers 1 and 2 use the document link only via `IsExistingDocumentSubmissionIdOf`; K1 (no-link submission takes its existing derived invoice's verdict; key-only read per 50); guard extended; unit 12079/0, Arch 2146/0; seeds S1-S4; dev counts 0 and 0. **Pass 1 (Opus) running.** Then fix, pass 2, uac-r2, merge. **Ships in the same batch as 165e.** |
| **082d** | `task/ebilling-082d` @ `eb42e5c8db` (wt agent-a5648fd3189b27f3d) | Built from `ee0b173288`: `procedureSnapshot` v1 internal member in findings JSON, DR facts summary, ratchet (baseline 3 ontology files), golden "101 B2a" appended; unit 7701/0, Arch 2143/0, NFR-01 83 ms, publish +6.9 KB (41.5 MB). Pass 1 (Opus): F4 snapshot stale after a correction (101) or a line add (164), F3 ratchet baseline ADR citation, K1 DR exclude counts dropped, K2 doc wording, K3 ratchet limits noted. Fix round 1 done `4eb96c3d61` (`ForPatchedFindings`: re-run snapshot or null when a decided line keeps old findings; DR exclude counts dropped, `gaps` kept; ADR-038 A1 + model 7.2 citations). Pass 2 (Opus): F4 the patched snapshot could contradict stored findings (patch keeps old entries), F3 tests locked it in; K DR counts, K size cap on patch paths. **Fix round 2 (Sonnet) running**: write Build(after) only if stored != null AND stored == Build(before) AND no decided line differs, else null (absent stays absent); DR facts = pointer + gaps only; over-cap drops the member then refuses explicitly. Then scoped Sonnet re-check (approach changed), then uac-r2. Critical path: 082d → 083 → 130 → 131-135. Then two passes + uac-r2 review. On merge: send field-mapping the sha (fm-032b branches from it). |

## Queued

- **165b-2** totals at approval and AP export (after 165b-3).
- **165b-4** spend paths (after 165b-3): ontology-r1 conditions O1-O8 in the POML; step 1 is a design note to ontology-r1 BEFORE code.
- **083** firm clause (after 082d), then **130** firm projection (EB-D-202, uac-r2 pin test).
- **fm-032 option B:** field-mapping writes the 032b design note (after-step: grant then columns; no ebilling reader of invoice copies; Secure maps nothing) to us + uac-r2; implementation branches from work after 082d merges.
- **`InvoiceReview` optional `lineId`** (EB-D-194; refresh-on-close is the Console's, no postMessage): **running (Sonnet)** on `task/ebilling-review-lineid` from `340795dbd1`. Then one pass, merge.
- **Dev deploy batch** (after this wave): send uac-r2 the commit and checklist. Owner go needed for: the BFF deploy; 166 field-security apply + forms; `sprk_sourceemaildocument` apply; 096b web resource then ribbon re-import + `-Verify`; the forms script (165c read-only links); ONE field-security question (uac-r2 bundle: a "BFF writer only" profile on submission `sprk_invoice`/`sprk_document`/`sprk_resubmissionof`, `sprk_invoice.sprk_billingexport`, approval portion `sprk_decidedby`/`sprk_decidedon`/`sprk_approvedamount`; list every legitimate writer per column first). Also: re-count invoices without a linked own submission (0 on 2026-11-08), a read-only count of Ontology Administrator holders (send uac-r2, private), then the 096b D3 role change (R1, R2, R4, then the privilege). **164c -Apply** (after master): `pwsh -File scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply`, then -Verify, then send uac-r2 both read-backs. **Add to the D3 question (164c review K1):** is user Create on `sprk_billingevent` removed the same way (ribbon hides incl. Import/Copy are not a privilege boundary)?

## Open with peers

- **uac-r2:** 165b-3 part A + fix APPROVED; part B diff next; 165f review. 165d and 165e approved and merged. DEF-057 (#2631) is on their backlog.
- **ontology-r1:** 165b-4 design note (before code). DI-144 (#2632) adopts our `WriterCreatedRecord`.
- **field-mapping:** 032b design note answered 2026-11-08 (E1 job, E2 yes, E3 fm→master first, E4 accepted with the admin-attention writer and no status write, E5 yes, E6 signed off with the condition that neither column joins external ReadableColumns or firm rendering). Still owed: the 082d merge sha. **uac-r2 condition for 032b-3 (our after-step 3a):** the job payload carries the core (table, id) from the persister's committed `stored` result; job gate 2 requires invoice core == payload core == submission core; must-fire test: re-point the invoice's or submission's core after enqueue → nothing read from the new core, nothing written. uac U3 (do invoice-level assignee columns grant at all) is open with fm and may drop the grant step.

## Standing gates (unchanged)

- Creator-person `-Verify` exits 0 before any BFF deploy (dev args in `git show 7a9f45c424:projects/ebilling-r1/current-task.md`).
- Reconcile stays OFF until DEF-044 is on master AND L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0.
- No BFF with 095a anywhere before `sprk_sourceemaildocument` is field-secured there; it targets SpaarkeMaster 1.3.0.1.
- One master PR (reviewers ontology-r1, uac-r2, R4).

## Next action

On each notification:
- **165b-3 fix round** → (re-check if approach changed) → uac-r2 fix diff → launch part B (Opus) on the same branch.
- **165f built** → pass 1 → fix → pass 2 → uac-r2 → merge.
- **164c built** → one pass → merge (no -Apply).
- **082d done** → pass 1 → fix → pass 2 → uac-r2 → merge → field-mapping sha → launch 083.
- Free slot → 165b-2 after 165b-3 merges; 165b-4 design note after 165b-3 merges; 083 after 082d merges.

Keep TASK-INDEX statuses current at each merge (165e row still says running; 165d row says review running).
