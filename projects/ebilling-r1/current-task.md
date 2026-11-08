# Current Task State

> **Last Updated**: 2026-11-08 (checkpoint at 86% context, session ebilling-r1-c7, authoritative). Work head = origin/work/ebilling-r1 (+ the in-progress 165b-2 merge). Dev BFF: uac-r2's `6fbb3a7642` (master 23567b2f93 + fm 049c + MCP spike; reconcile false; gate PASS). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project". At most 4 agents. Merge each finished branch after its reviews, verifying the combined tip (build, full ArchTests, filtered unit). One adversarial pass per change set; two for partner-readable/auth; a scoped Sonnet re-check when a fix changes the approach. FILTERED unit tests only. Every agent launch states a model. Never `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER: every brief names the base sha ("create the task branch from `origin/work/ebilling-r1` at <sha>") and checks `git merge-base --is-ancestor`. Never cd into agent worktrees (use `git -C`, absolute paths; jest via `node <wt>/node_modules/jest/bin/jest.js --config <wt>/jest.config.cjs --rootDir <wt>`). Remove merged worktrees with `git worktree unlock` + `scripts/worktree/Remove-WorktreeSafe.ps1`. |
| **Public-repo rule** | Never put the mechanism of an open access gap in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Merged into work (2026-11-07/08)** | Invoice Review optional `lineId` (`b7c8a3c58d`, EB-D-194); 164c (`931fe905b0`); 165d (`66b7c31315`); 165e (`7061729544`); 096b (`1a316a2ca0`); 165b item 1 (`b49465d972`); 165c (`1aff454e34`); 095d-2b-3 (`c2239d9d58`); 165a + 095d-2b-2 (`2fac72b8a5`); 164b (`01854564a3`). |
| **Decisions** | EB-D-206 (amended), EB-D-207, EB-D-208 (counted statuses need the writer-created approve record). |

## Agents / branches in flight

> 165b-2 merged `cd35ae8fc3` and verified (ArchTests 2181/0, unit 7476/0). 085 fix round 1 running with uac-r2 conditions (publish filter APPROVED: 403 on fault/unknown/no-version/no-Procedure; filter passes the checked version id, handler 409 if it differs; dev count of version-less Draft cards; K4 panel = DESIGN DECISION, pinned by test).

| Item | Branch / worktree | State → next action |
|---|---|---|
| **165b-2** | ✅ merged `cd35ae8fc3` | Done. |
| **083** | fix1 `task/ebilling-083-fix1` @ `9a4835f157`; **fix2 `task/ebilling-083-fix2` (Sonnet, running)** | Pass 1 (3 F) fixed; pass 2: code/tests clean, 2 F4 in POMLs (130:126 flag-off label; 135 step 1 still calls GetFirmClauseAsync) + K1 Warning gets invoiceId/policyVersionId (no exception) + K3 last-linked test. Next: merge fix2 (no 3rd pass) → verify → TASK-INDEX 083 ✅ → launch **130** (Sonnet/Opus per POML; partner-readable → 2 passes). Remove wts agent-abce3e9bf2270020c, agent-a7ba828dad62a6665 + fix2's. |
| **085** | `task/ebilling-085` @ `417889cce0` (on `ecc8a73639`) | Done (ArchTests 2159/0, unit 8669/0, +4.3 KB; new filter `RequireWriteOnTheVersionsProcedure` + ledger; `RateProcedureWorld.cs`). **Adversarial pass 1 (Opus) running** → fix → pass 2 → merge → tell ontology-r1 `approvedRate.scope` → `approvedRate.rank`. wt agent-aab0af68fc90e3222. |
| **165b-4** | `task/ebilling-165b4` (Opus, from `fd628e75c5`, running) | Design approved (notes §7.12 uac-r2, §7.13 ontology-r1). Precondition null-findings count must be 0 else STOP. Next: one adversarial pass → ontology-r1 reviews FinanceBaseValues/Signals hunks → merge. |
| **165e + 165f** | ✅ merged; guard follow-up `6ab63efbf7` | Ship together; send uac-r2 the dev-batch sha when the batch is cut. |
| **fm-032b** | fm's | Round 2 `9083f5df93` confirmed by us + uac-r2 (inline, U3 (c), E7). fm implements 032b-2/3 from the CURRENT work tip and sends us diffs before merge into work. |

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
- **field-mapping:** fm-032b round 2 (`9083f5df93`, notes/task-032b-design.md) **confirmed by us and uac-r2**: U3 (c) (sprk_invoice leaves the access registry; copies are display only), inline in `PersistCoreAsync` before `BuildInvoiceCreateRequest`, E7 (fault maps nothing, invoice created + ids-only note). We asked: branch 032b-2 from the CURRENT work tip (re-anchor line numbers), add 082d snapshot tests to the regression list, T-E7 asserts identical `sprk_findingsjson`. fm owns owner questions O1 (placement; our view inline) and O2 (`Repair.cs` move). fm sends us the 032b-2/3 diffs before merge into work; we verify the tip.

## Standing gates (unchanged)

- Creator-person `-Verify` exits 0 before any BFF deploy (dev args in `git show 7a9f45c424:projects/ebilling-r1/current-task.md`).
- Reconcile stays OFF until DEF-044 is on master AND L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0.
- No BFF with 095a anywhere before `sprk_sourceemaildocument` is field-secured there; it targets SpaarkeMaster 1.3.0.1.
- One master PR (reviewers ontology-r1, uac-r2, R4).

## Next action

On each notification: act per the "next" column above. Free slot → 165b-2 / 165b-4 design note after 165b-3 merges; 083 after 082d merges. Keep TASK-INDEX rows current at each merge (165b-3, 165f, 082 rows).
