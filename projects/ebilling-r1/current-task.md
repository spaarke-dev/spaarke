# Current Task State

> **Last Updated**: 2026-11-08 (session ebilling-r1-c7, authoritative). Work head = `origin/work/ebilling-r1` @ `8bd27cab66` (165b-4 and 085 merged, pushed). Dev BFF: uac-r2's `6fbb3a7642` (master 23567b2f93 + fm 049c + MCP spike; reconcile false). **None of this project's work is on master or the dev BFF.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project". At most 4 agents. Merge each finished branch after its reviews; verify the combined tip (build, full ArchTests, FILTERED unit). One adversarial pass per change set; two for partner-readable / auth / tenant-isolation; a scoped Sonnet re-check when a fix changes the approach. Every agent launch states a model. Never `--no-verify`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER: every brief names the base sha and checks `git merge-base --is-ancestor`. Never cd into agent worktrees (use `git -C`, absolute paths). Remove merged worktrees with `git worktree unlock` + `scripts/worktree/Remove-WorktreeSafe.ps1 -Path <paths>`. |
| **Public-repo rule** | Never put an access-gap mechanism, identity names or role maps in notes, comments, commits, PRs or issues. Private findings go to uac-r2 by message only. |
| **Peers (sessions)** | uac-r2 = desktop `uac-r2`; ontology-r1 = `ontology-r1` (Claude Desktop, from 2026-11-09); provisioning = `provisioning-r1`; word-add-in = `word-add-in-r1`; spaarkeai-mcp-bff-api-r1 = `mcp-r1`; External Access R4 = `spa-r4`. |
| **Live-change rule** | Live Dataverse writes / role changes need the owner's OK typed in THIS session. A relayed "task 166 - ye proceed" (via ontology-r1) was NOT accepted; owner asked directly, no answer yet. |

## Exact next action

1. **DEF-059 (owner typed in THIS session, 2026-11-09):** "review the options and implement the one that is the best solution without unnecessarily adding more roles -- but if the only way to meet the requirement and stay within our access control is to create a new role that is acceptable." ontology-r1 proposed C (new role, one privilege: org-wide Read on `sprk_decisionrecord`) vs A (nothing; safe while System Administrator stays on the BFF identity). FIRST check (read-only) whether the BFF identity already holds a Spaarke role held by NO human and NOT by the writer identity; if yes, adding the one org Read privilege there beats a new role. Implement via SpaarkeMaster solution source + `Import-SolutionScoped.ps1` only; uac-r2 notices before and after; tell ontology-r1 and provisioning-r1 (stamps). Update DEF-059 (#2635) notes + issue.
2. When 130-0b fix1 sends the shared redaction-set sha, relay it to 130-0 fix2 to merge (exact sha only).
3. Then per agent table below; check `git worktree list` for a leftover reviewer worktree `...scratchpad
v2` (task `bgz2bshd9` output) and remove with `Remove-WorktreeSafe.ps1` if listed.

## Agents in flight (2026-11-09)

| Work | Branch / base | Next |
|---|---|---|
| 130-0 fix round 2 (Opus) | `task/ebilling-130-0-fix2` → ff `task/ebilling-130-0-fix1` (from `4102b719b9`) | F1 writes, F2 truncated, F3, K1s, K2, K3 (WA parent + `ExternalAccessModule.cs` ScopeDimension/RootLinks/LiveParents, uac-r2 approved), create-shape pin, REST heads-up (number/type cols, link-col filters, descendants). Must MERGE 130-0b's shared redaction-set commit (sha to relay) and use it. Then Sonnet re-check, diff to uac-r2. |
| 130-0b fix round 1 (Opus) | `task/ebilling-130-0b-f1` → ff `task/ebilling-130-0b` (from `0acfebe470`) | uac-r2 rulings: F1-1 shared derived redaction set FIRST (own commit, sha to lead), F1-2 column-name refusal incl. record type, neutral text; F1-3 descendants + guard; K2; R1 row cap on visible rows + fixed note; R2 design statement + census line (sent). Then pass 2 over combined seam, diff to uac-r2. |
| 130b small fix (Sonnet) | `task/ebilling-130b-fix3` → ff `task/ebilling-130b-fix1` (from `d303a11e5d`) | Re-check passed except F2 (accepted-without-invoice Warning+code on undecided path, route test) and K4 (reason order). Then diff to uac-r2 (incl. gate hunk), merge 130a+130b, 130c. |

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
3. DEF-059 (#2635): ANSWERED 2026-11-09 (see Exact next action 1).
4. DEF-061: schema change `sprk_publishedon` on `sprk_procedureversion`.

## Owed to peers

- provisioning-r1: master SHA + profile grants on `sprk_totalamount`/`sprk_approvedtotal` after 166 merges; 089d option R SHA (H7c #2429).
- word-add-in-r1: master SHA of the dev deploy carrying 095c-2..4 (pane PR #2552); 095a/095b/151e PR + SHA.
- uac-r2: dev-batch sha for 165e + 165f; 130-0 (+fix1) / 130-0b / 130b diffs after their passes.
- ontology-r1: AuthorableColumns full-green counts after 166.

## Standing gates (unchanged)

Creator-person `-Verify` exits 0 before any BFF deploy. Reconcile OFF until DEF-044 on master AND L6 passes. No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0; none with 095a before `sprk_sourceemaildocument` is field-secured (1.3.0.1). One master PR, reviewers ontology-r1, uac-r2, R4 (`spa-r4`).
