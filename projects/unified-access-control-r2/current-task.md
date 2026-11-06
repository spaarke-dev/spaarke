# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-06 ~17:35Z (checkpoint #99: 20669df6a9 deployed to dev and gate PASS; owner rulings on 046c and the root team; the root-team census is done and its removal waits on the owner's go).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge needs Router + "Client lint (type-aware ratchet)": `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; a peer's go is never the owner's. |
| **Active work** | None in code. **#2560 MERGED** (PR #2598, `b6dee2e590`; issue closed); C:/wt2560 and C:/wtfm removed. Waiting on: fm 049 round-3 SHA; ebilling 095d-2a-2 SHA and the 095a column-security pre-apply ping. |
| **Also in flight** | #2577 test-host fix MERGED (#2596, `972929fd69`). |
| **Dev state** | BFF = **20669df6a9** (since 2026-11-06 16:44:51Z) plus the `sprk_spaarkeai` bundle at the same SHA (16:54:04Z). G-8 revoke-on-change ON. 226 settings. |
| **Next action** | 1. **Root team "Spaarke": Console User REMOVED 18:11:46Z** (owner go; read-back shows only Basic User left on the team). Census confirmed: 4 findings at 18:45Z (was 5); the remaining finding is the Basic User grant that 046c fixes. Work-branch docs merged to master via PR #2603 (`38aec72923`). `# Spaarke Get Started` (getstarted@): the owner said it is a special-purpose marketing account; leave it as is and change nothing (no license removal, no environment security group; self-service joining is correct behaviour). 2. 046c: owner ruling "all five get Core User": DONE 17:30Z (role f8ce167c, BU cb15f587; read-back OK for 49f89867, bcebc56c, b0cd2d30, 4c9917c2, e44317a0). Tell fm and word-add-in; also tell them the root account 1d02f31c holds System Administrator. 046c's own apply is fm's, with the owner's go in their session; I review the PR after its fix round and asked for a step-order check. 3. Open: organic traffic for the fm and ontology route checks (ontology reads its own); the 02:30Z nightly (ontology). |

## Decisions this session (2026-11-06, owner here)
- W3 direction: named exceptions plus guards; no core redesign in R1; no given-id input in the core (fm recorded it at `5e4f4be3aa`).
- Deploy go for 20669df6a9 (done).
- 046c: all five add-on holders get Core User (done 17:30Z).
- The root team's Console User was removed (done 18:11Z). Get Started is left as is; no root-cause change (owner).
- ebilling EB-D-200 = option D (via ebilling).

## The next dev deploy (owner: wait for 049)
- Contents: master incl. #2560 (`b6dee2e590`), #2596 (`972929fd69`), `0f6618094b` (043), #2577 (`f34993dc78`), #2592 (046b, auto-merge), #2582 (audit flags), #2589 (docs), plus 049; code pages: LegalWorkspace, SpaarkeAi, CreateProjectWizard, AI.Widgets.
- **095a gate (2026-11-06):** no BFF that contains ebilling 095a goes to dev until I read `sprk_invoicesubmission.sprk_sourceemaildocument` IsSecured = true myself. It is false on dev today, with 0 rows populated. ebilling runs `Set-EbillingSourceEmailFieldSecurity.ps1` with the owner's go in their session. Readers membership: (a) Writers only (accepted; `-Verify` refuses a Readers row). Provisioning received the ordered prerequisite.
- **164a-2 / invoice-row inventory (2026-11-06):** reviewed with ebilling; the detail is held privately (peer channel). Recommendation sent via ebilling; the owner decides through ebilling (EB-D-200 = option D). External scope keys on the submission's firm (confirm D-70).
- **Pre-deploy check:** master must NOT carry ontology 174/175 (#2580/#2579; held unmerged until ebilling 112 is on dev). Stop if 174 is there.
- Gate: gate0f5.sh set + fm 043 gates; ontology: `table_unsupported` per table (signal/decisionrecord/budgetrevision) baseline 0, writer codes 0, Undetermined 0, census unchanged (**5** since 2026-11-06 00:30Z); ebilling: `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exits 0, `Ebilling__StoredFileReconcileEnabled` stays false.

## Reviews in flight / conditions sent
- **fm (`-69`):**
  - **049 `e8da405eb0` APPROVED 2026-11-06** (rounds 3 and 4: Basic not counted, tests inverted, wording clean). fm opens the PR and merges, then sends the master SHA, then I put the combined deploy to the owner. Rebuild order: BFF, then UI.Components → LegalWorkspace / SpaarkeAi / AI.Widgets (CreateProjectWizard optional). Recommended via fm that the owner delete remote `task/fm-049c` (the old private passage is in public commits).
  - 046b PR #2592 APPROVED (role diff matches dev exactly; auto-merge on).
  - G13 thread Deep withdrawn (private threads); thread Read stays Basic.
  - 049: rights gate option (b): Basic never counts for a team-owned target; invert the two 049-only tests.
  - 049: notes passage removed, and the branch history rewritten (squash + force-with-lease).
  - 046c (Basic User read-only): conditions 1–7 in its POML; inventory and dry run come to me privately before the owner.
- **ebilling (`-c7`):**
  - **096b (EB-D-201, after 164a):** `sprk_invoice` and `sprk_billingevent` leave `ChildRecordEndpoints.CreateTables`; the ribbon "New billing event" and `SecureChildNewCommandAgreementTests` change in the same change; `RefileTables` is untouched. Scope ACCEPTED. Pending from ebilling: the diff, then file:line proof that the 052 wizard and the 095 paths create app-only, BEFORE the role -WhatIf to drop user Create on `sprk_invoice` goes to the owner. The BFF and ribbon deploy in one window via me; #1973 closes once the build is on dev.
  - 095d-2b-1 `4f113d72e2` APPROVED 2026-11-06: the withhold is keyed on the app-only `sprk_firmhold`, the invoice is withheld while the re-run is pending, and I verified every external path myself. An outcome reply by email must respect the withhold if one is ever added.
  - 095d-2a-2 (`7316036f5c`, `741a4fccaf`, `0bd7d49f89`) APPROVED 2026-11-06 with no F findings. K1 (line attach between the re-check and the transaction) waits on their invoice row-version answer. K2: a refused-count in the job result in 2b. K3: the result's RerunState stays non-partner-visible.
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
- Dev: application users with production-like names (`# spaarke-bff-api-prod`, `# JobsServiceProd`) hold System Administrator in DEV (fm 044 finding). Ask the owner whether those identities belong in dev.
- ebilling: a hold-column item is fixed in ebilling task 165, ordered first and before AP export is turned on anywhere; ebilling tells the owner (detail held privately). 095d-2b-1 C1 and C2 resolved and approved (`4f113d72e2`).
- ebilling: a line-validation item is fixed in 164a-4a; I review the 164 diff against conditions sent privately. Does the owner want tracking beyond 164?
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
