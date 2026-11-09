# Current Task State

> **Last Updated**: 2026-11-08 (session ebilling-r1-c7, authoritative). Work head = `origin/work/ebilling-r1` @ `8bd27cab66` (165b-4 and 085 merged, pushed). Dev BFF: uac-r2's `6fbb3a7642` (master 23567b2f93 + fm 049c + MCP spike; reconcile false). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project". At most 4 agents. Merge each finished branch after its reviews; verify the combined tip (build, full ArchTests, FILTERED unit). One adversarial pass per change set; two for partner-readable / auth / tenant-isolation; a scoped Sonnet re-check when a fix changes the approach. Every agent launch states a model. Never `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER: every brief names the base sha and checks `git merge-base --is-ancestor`. Never cd into agent worktrees (use `git -C`, absolute paths). Remove merged worktrees with `git worktree unlock` + `scripts/worktree/Remove-WorktreeSafe.ps1 -Path <paths>`. |
| **Public-repo rule** | Never put an access-gap mechanism, identity names or role maps in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Peers (sessions)** | uac-r2 = desktop `uac-r2`; ontology-r1 = `spaarke-wt-spaarke-ontology-platform-r1-38`; provisioning = `provisioning-r1`; word-add-in = `word-add-in-r1`; External Access R4 = `spa-r4`. |
| **Live-change rule** | Live Dataverse writes / role changes need the owner's OK typed in THIS session. A relayed "task 166 - ye proceed" (via ontology-r1) was NOT accepted; owner asked directly, no answer yet. |

## Exact next action

1. Wait for the agents below. Act on each notification.
2. **130-0 fix1 done**: adversarial pass 2 (Opus, adversarial-reviewer) over `64cdfbe304..task/ebilling-130-0-fix1`. It must check the census (every external reader of todo/event/document: through the seam or a one-line reason) per uac-r2. Then send the diff to uac-r2 (private).
3. **130-0b done**: two adversarial passes (tenant isolation). Its base is `64cdfbe304`, not fix1: merge fix1 into it (or both into work) before pass 2 so the review sees the combined seam. Then send the diff to uac-r2.
4. **Merge 130-0 + fix1 + 130-0b into work together**, verify the tip, mark TASK-INDEX 130-0 and 130-0b ✅, and remove worktrees `agent-ab27afb0265db7790` and the agents' worktrees.
5. **130b done**: two adversarial passes over 130a+130b, the diff to uac-r2, merge both. Then 130c (FR-18).

**Merge note (uac-r2):** 130-0 fix1 and 130b fix1 both edit `WorkforceRoleCeilingGate.cs`. 130-0 defines `ReadInvoiceSubmissionsWithInvoice` (submission + invoice, for both GET `/invoice-submissions` routes); 130b adds `BillingEventTable` + `ReadInvoiceOutcome` (invoice + billingevent) and the submission-outcome set. On the second merge into work: one definition per set, reuse `InvoiceTable`/`BillingEventTable`, no duplicates; uac-r2 reviews both gate hunks together. Workforce role on dev already holds Read on invoice, billingevent and submission (checked read-only).

## Agents in flight

| Work | Branch / base | Next |
|---|---|---|
| 130-0 fix round 2 (Opus) | `task/ebilling-130-0-fix2` → pushes ff to `task/ebilling-130-0-fix1` (from `4102b719b9`) | Pass 2 done (reads clean; merge with 130-0b head conflicts only in notes). Fixing: F1 writes (PATCH/DELETE todo, DELETE event, DELETE document relatedinvoice) per uac-r2 write ruling; F2 message/feed fault → truncated; F3 dead redaction test; K1 DTO `required`; K1 Read before seam; K2 message cols from `InvoiceLinks`; K3 (work-assignment parent over-hide) asked uac-r2. Then scoped Sonnet re-check, diff to uac-r2. |
| 130-0b step 3 of 3 (Opus): root link redaction on assistant tools | `task/ebilling-130-0b` @ `3201e7e84b` (steps 1-2 done) | Incl. blanking search `content` snippets on a root hit with hidden invoice. Then two adversarial passes (second over combined seam after merging 130-0 fix), diff to uac-r2. |
| 130b fix round 2 (Opus) | `task/ebilling-130b-fix2` → ff to `task/ebilling-130b-fix1` (from `7a962ffd9d`) | Pass 2: F1 TOCTOU (`InvoiceDecided`), F4b ruled (declare decisionrecord+signal; citations only as firm flag publishes, fail closed, no ids, InternalStandard never), K null statuscode → Unavailable, K wording, ceiling route table. Then scoped re-check, diff to uac-r2, merge 130a+130b. Then 130c. |

**Merge plan for the 130 batch:** merge 130-0 fix (incl. 130-0b helper `446a40bb4e`), 130-0b head, 130b fix1 into work; expect conflicts in the 130-0 notes file (combine both sections by hand) and `WorkforceRoleCeilingGate.cs` (adjacent sets; remove `ReadInvoiceSubmissions` if unused); after merge replace fix1's `InvoiceLinkedChildAttributes` (`ExternalModuleDataEndpoints.cs` ~:502) with 130-0b's `ExternalInvoiceVisibility.IsInvoiceLinkedChildTable`. Verify tip, TASK-INDEX 130-0/130-0b/130a/130b ✅, remove agent worktrees.

## Decisions made this stretch (2026-11-08)

- **165b-4** merged `5ebe26a03d` + K1/K2 `24641fdf1a`; tip clean, filtered 2167/0, ArchTests 2189/0; TASK-INDEX ✅; ontology-r1 told. **Deploy gate:** AuthorableColumns full-green on dev after 166's apply, counts to ontology-r1, before any BFF carrying 165b-4 reaches dev.
- **085** merged `052307dc0b` (uac-r2 + ontology-r1 approved; ontology D-213); native-write K-line in notes (uac-r2's service-role condition checked read-only on dev: holders are all application users; detail sent privately); tip clean, filtered 1531/0, ArchTests 2189/0; TASK-INDEX ✅; ontology-r1 told about `approvedRate.rank`.
- **D-70 final owner ruling** (relayed by uac-r2): own firms only; no firm sees none; a fault hides all. In project CLAUDE.md.
- **130 split**: 130-0, 130-0b, 130a (built), 130b, 130c. **130-0 scope widened** by uac-r2 (same batch).
- **085 F1-b**: ruling (c); durable fix DEF-061 (owner-gated schema).
- Filed: DEF-060 #2637, DEF-061 #2640, DEF-062 #2641.

## Open owner questions

1. Typed go for task **166** dev apply (field security incl. invoice totals + forms), with uac-r2 before/after notices, `Import-SolutionScoped.ps1` only.
2. OK to run `FinanceBaseValuesSeamTests` live (creates and sweeps `zz-025b-` rows on dev).
3. DEF-059 (#2635): BFF identity Decision Record read via a SpaarkeMaster role (uac-r2 yes, ontology-r1 no).
4. DEF-061: schema change `sprk_publishedon` on `sprk_procedureversion`.

## Owed to peers

- provisioning-r1: master SHA + profile grants on `sprk_totalamount`/`sprk_approvedtotal` after 166 merges; 089d option R SHA (H7c #2429).
- word-add-in-r1: master SHA of the dev deploy carrying 095c-2..4 (pane PR #2552); 095a/095b/151e PR + SHA.
- uac-r2: dev-batch sha for 165e + 165f; 130-0 (+fix1) / 130-0b / 130b diffs after their passes.
- ontology-r1: AuthorableColumns full-green counts after 166.

## Standing gates (unchanged)

Creator-person `-Verify` exits 0 before any BFF deploy. Reconcile OFF until DEF-044 on master AND L6 passes. No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0; none with 095a before `sprk_sourceemaildocument` is field-secured (1.3.0.1). One master PR, reviewers ontology-r1, uac-r2, R4 (`spa-r4`).
