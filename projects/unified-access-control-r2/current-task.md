# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-06 ~07:00Z (checkpoint #98: #2560 committed locally, two adversarial reviews running; a test-host gap from #2577 is being identified).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge needs Router + "Client lint (type-aware ratchet)": `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; a peer's go is never the owner's. |
| **Active work** | **#2560** (a mirrored share on a hard-deny row carries Read only). Worktree C:/wt2560, branch `task/uac-2560-hard-deny-mirror-read`, local commit **`0bf3adb613`** (NOT pushed). It adds `ChildMirrorMask(table, mask)` and `InheritedRootMirrorMask`, a per-child-table mirror in `SecureChildShareSynchronizer`, and a `[SECURE-CHILD-SHARES] HardDenyRowShareWiderThanRead` finding. Tests: 25 new must-fire; affected classes 499/499; ArchTests 1950; integration 199/8 skipped and 353/23 skipped. **Two Opus adversarial reviews are running (A: correctness, B: security).** Known limits stated by the implementer: no census for hard-deny rows outside the lineage, and direct Manage Access shares on non-Secure hard-deny rows are not narrowed. |
| **Also in flight** | **A test-host gap from MY #2577:** about 18 unit tests in `Sprk.Bff.Api.Tests` fail with "Failure to infer one or more parameters" because some test hosts don't register `CallerRecordAccessProbe`, which POST /api/v1/documents now injects. Production registers it unconditionally (`ExternalAccessModule.cs:192`) and master CI is green, so these are tests CI doesn't run. The implementer was asked for the test names and host file:line. Then fix it in its own small PR, or in #2560's if it's tiny. |
| **Dev state** | BFF = **0f53e053bf** (gate PASS, `notes/deploy-gate-2026-11-05-0f53e053bf.md`). **G-8 revoke-on-change ON** since 04:02:31Z (owner go; watch PASS; #2574 closed via docs PR #2589). 046b additions + R1 live. Thread filing sweep: the dry run found nothing; the apply was skipped (no-op). |
| **Next action** | 1. When the reviews return: fix any F-class findings (re-check the fix diff), then rebase on origin/master, measure the publish-size delta (fresh master vs branch, short-path worktrees), push, and open the PR. 2. Fix the #2577 test-host gap. 3. Review fm 049 (round 3: Basic not counted on the rights gate; the 049b/c history rewrite) when the SHA arrives. 4. Then ONE combined deploy (owner's go naming the SHA; every peer re-OKs). |

## The next dev deploy (owner: wait for 049)
- Contents: master incl. `0f6618094b` (043), #2577 (`f34993dc78`), #2592 (046b, auto-merge), #2582 (audit flags), #2589 (docs), plus 049; code pages: LegalWorkspace, SpaarkeAi, CreateProjectWizard, AI.Widgets.
- **Pre-deploy check:** master must NOT carry ontology 174/175 (#2580/#2579; held unmerged until ebilling 112 is on dev). Stop if 174 is there.
- Gate: gate0f5.sh set + fm 043 gates; ontology: `table_unsupported` per table (signal/decisionrecord/budgetrevision) baseline 0, writer codes 0, Undetermined 0, census unchanged (**5** since 2026-11-06 00:30Z); ebilling: `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exits 0, `Ebilling__StoredFileReconcileEnabled` stays false.

## Reviews in flight / conditions sent
- **fm (`-69`):**
  - 046b PR #2592 APPROVED (role diff matches dev exactly; auto-merge on).
  - G13 thread Deep withdrawn (private threads); thread Read stays Basic.
  - 049: rights gate option (b): Basic never counts for a team-owned target; invert the two 049-only tests.
  - 049: notes passage removed, and the branch history rewritten (squash + force-with-lease).
  - 046c (Basic User read-only): conditions 1–7 in its POML; inventory and dry run come to me privately before the owner.
- **ebilling (`-c7`):**
  - 164a create-only refusal: separate flag + derived `GenericCreateRefused`, conditions accepted.
  - EB-D-196: quote may stay in Signal evidence (B), with the containment test.
  - `sprk_firmhold` re-run-pending value OK. Conditions: unknown value fails closed, only the re-run clears it, no human Update profile (verified on dev: only the 2 BFF app users), tests. Backfill count 0.
  - #2591 field-security APPROVED (additions only; 43 IsSecured flips, 42 in Readers, blobpath writer-only).
  - Master PR checklist unchanged (095d-2 hold, option-A conditions, EB-D-188 G5).
  - Then the P2 migration, then my flip of `Ebilling__StoredFileReconcileEnabled` (owner go HERE).
- **ontology (`-38`):**
  - 0f53e053bf read CLOSED (gate, Worklist, nightly).
  - #2580 (174) replay conditions met.
  - 174 and 175 are held unmerged and ship as a pair after ebilling 112.
  - #2582 audit flags merged; H6 covers org auditing; audit history stays SysAdmin only.

## Owner items outstanding
- Should customer environments turn on revoke-on-change (G-8) through provisioning?
- #2590: a project created app-only with no person lands in the root BU. Choose the rule (refuse vs route) and whether to move the dev row.
- Column auditing on the writer-only tables' lookups (covered by ontology's flags? verify), 044 field-security profile, the person-path refusal uniformity question (private), the AP diagnostics batch, the VS Code agent-dir cleanup, word-add-in Office saves, 095a, click-test 195, Q1/Q4 (private), older: #2343, #2245, #2250, #2260, #2270, #2262, #2396, 191 F1/S4, 190 Apply, #2382, #2505; tasks 140/165/153.

## Private items (never in issues/PRs/commits)
- Core pair, lineage and update paths: merged in #2568 and deployed.
- The push runs as the caller (#2253 K2); round-77 class Ks.
- 049: secure-from-creation reasoning, the stale-add/removal race wording, and the Basic team-grant over-count stay in peer channels only.

## My own backlog
#2585 (re-file whole-row read; hardening), #2576 (probe fault shown as denial), #2524, gate null-column hardening, SharePoint census, #2543, #2292, K-P1 for task 191; parked: 193 (C:/wt193), 197, #2427, 196a/196b; TASK-INDEX reconcile at 090.

## Peers
uac-r2 `-20` (this session) · ebilling `-c7` · field-mapping `-69` · ontology `-38` · provisioning `-b8` · word-add-in `-7b` · website `spaarke-website-cb` · SPA R4 `spaarke-wt-spa-external-access-platform-ad`.
